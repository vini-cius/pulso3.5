/* SPDX-License-Identifier: GPL-3.0-or-later
 * Copyright (C) 2026 Pulso 3.5 contributors
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Controlador35 {
    public sealed class CodexTelemetry {
        public double? FiveHoursRemaining, WeekRemaining;
        public DateTime? FiveHoursReset, WeekReset;
        public string Status="Aguardando Codex", Attention="Avisos indisponíveis";
        public DateTime Updated;
    }
    public static class CodexMonitor {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        static IDictionary<string,object> Dict(object value) { return value as IDictionary<string,object>; }
        static object Get(IDictionary<string,object> value,string key) { object item; return value!=null&&value.TryGetValue(key,out item)?item:null; }
        public static CodexTelemetry ParseLimits(string json) {
            var result=new CodexTelemetry { Updated=DateTime.Now, Status="Limites indisponíveis" };
            var root=Dict(Json.DeserializeObject(json));var body=Dict(Get(root,"result"));
            var bucket=Dict(Get(body,"rateLimits"));var buckets=Dict(Get(body,"rateLimitsByLimitId"));
            var codex=Dict(Get(buckets,"codex"));if(codex!=null)bucket=codex;
            foreach(string key in new[]{"primary","secondary"}) {
                var window=Dict(Get(bucket,key));object duration=Get(window,"windowDurationMins"),used=Get(window,"usedPercent");
                if(duration==null||used==null)continue;
                int minutes=Convert.ToInt32(duration);double remaining=Math.Max(0,Math.Min(100,100-Convert.ToDouble(used)));
                DateTime? reset=null;object seconds=Get(window,"resetsAt");
                if(seconds!=null)try { reset=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(seconds)).ToLocalTime(); } catch(ArgumentOutOfRangeException) { }
                if(minutes==300){result.FiveHoursRemaining=remaining;result.FiveHoursReset=reset;}
                else if(minutes==10080){result.WeekRemaining=remaining;result.WeekReset=reset;}
            }
            if(result.FiveHoursRemaining.HasValue||result.WeekRemaining.HasValue)result.Status="Conectado";
            return result;
        }
        static string FindExecutable() {
            string[] dirs=(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator);
            foreach(string dir in dirs)try { string file=Path.Combine(dir.Trim('"'),"codex.exe");if(File.Exists(file))return file; } catch { }
            string windowsApps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps");
            try { foreach(string dir in Directory.GetDirectories(windowsApps,"OpenAI.Codex_*_x64__*").OrderByDescending(x=>x)) {
                string file=Path.Combine(dir,"app","resources","codex.exe");if(File.Exists(file))return file;
            } } catch { }
            // AppX package directories may not be enumerable even when the package is installed.
            try {
                var info=new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1 -ExpandProperty InstallLocation)\"") {
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true
                };
                using(var query=Process.Start(info)) {
                    Task<string> output=query.StandardOutput.ReadToEndAsync();
                    if(!query.WaitForExit(5000)) { try { query.Kill(); } catch { } return null; }
                    if(output.Wait(1000)) {
                        string root=output.Result.Trim();
                        if(!String.IsNullOrWhiteSpace(root)) { string file=Path.Combine(root,"app","resources","codex.exe");if(File.Exists(file))return file; }
                    }
                }
            } catch { }
            return null;
        }
        public static CodexTelemetry Read(CancellationToken token) {
            string executable=FindExecutable();if(executable==null)return new CodexTelemetry { Status="Codex não encontrado" };
            try {
                var info=new ProcessStartInfo(executable,"app-server") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
                using(var process=new Process { StartInfo=info }) {
                    process.Start();process.BeginErrorReadLine();
                    try {
                        process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"pulso35_monitor\",\"version\":\"1.0\"}}}");
                        process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                        process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");process.StandardInput.Flush();
                        DateTime deadline=DateTime.UtcNow.AddSeconds(15);
                        while(!token.IsCancellationRequested && DateTime.UtcNow<deadline) {
                            Task<string> line=process.StandardOutput.ReadLineAsync();
                            while(!line.Wait(100) && !token.IsCancellationRequested && DateTime.UtcNow<deadline) { }
                            if(!line.IsCompleted||line.Result==null)break;
                            var obj=Dict(Json.DeserializeObject(line.Result));object id=Get(obj,"id");
                            if(id!=null&&Convert.ToString(id)=="2")return ParseLimits(line.Result);
                        }
                        return new CodexTelemetry { Status="Consulta indisponível" };
                    } finally { try { if(!process.HasExited)process.Kill(); } catch { } }
                }
            } catch(Exception ex) { Paths.Log("CODEX " + ex.GetType().Name);return new CodexTelemetry { Status="Consulta indisponível" }; }
        }
    }
}
