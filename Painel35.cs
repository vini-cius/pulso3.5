/* SPDX-License-Identifier: GPL-3.0-or-later
 * Copyright (C) 2026 Pulso 3.5 contributors
 * See LICENSE for the full license text.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace Controlador35 {
    public sealed class Metrics {
        public double? Cpu, Ram, Gpu, CpuTemperature, GpuTemperature;
        public double UsedGb, TotalGb;
        public DateTime At;
    }
    public sealed class Sensors {
        [StructLayout(LayoutKind.Sequential)] struct Memory {
            public uint Length, Load;
            public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref Memory value);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        long lastIdle, lastKernel, lastUser;
        bool primed;
        public Metrics Read() {
            var result = new Metrics { At = DateTime.Now };
            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user)) {
                long total = kernel - lastKernel + user - lastUser;
                if (primed && total > 0) result.Cpu = Math.Max(0, Math.Min(100, 100.0 * (total - (idle - lastIdle)) / total));
                lastIdle = idle; lastKernel = kernel; lastUser = user; primed = true;
            }
            var mem = new Memory { Length = (uint)Marshal.SizeOf(typeof(Memory)) };
            if (GlobalMemoryStatusEx(ref mem) && mem.Total > 0) {
                result.Ram = 100.0 * (mem.Total - mem.Available) / mem.Total;
                result.UsedGb = (mem.Total - mem.Available) / 1073741824.0; result.TotalGb = mem.Total / 1073741824.0;
            }
            try {
                var engines = new Dictionary<string, double>();
                using (var query = new ManagementObjectSearcher("SELECT Name,UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")) {
                    query.Options.Timeout = TimeSpan.FromSeconds(3);
                    using (var rows = query.Get()) foreach (ManagementObject row in rows) using (row) {
                        string name = Convert.ToString(row["Name"]);
                        int begin = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                        if (begin < 0) continue;
                        string engine = name.Substring(begin);
                        double value = Convert.ToDouble(row["UtilizationPercentage"], CultureInfo.InvariantCulture);
                        if (!engines.ContainsKey(engine)) engines[engine] = 0;
                        engines[engine] += value;
                    }
                }
                if (engines.Count > 0) result.Gpu = Math.Min(100, engines.Values.Max());
            } catch (ManagementException) { } catch (UnauthorizedAccessException) { } catch (COMException) { }
            double?[] temperatures = Temperatures.Read();
            result.CpuTemperature = temperatures[0]; result.GpuTemperature = temperatures[1];
            return result;
        }
    }
    public static class Temperatures {
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint="nvmlInit_v2")]
        static extern uint Init();
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint="nvmlShutdown")]
        static extern uint Shutdown();
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint="nvmlDeviceGetHandleByIndex_v2")]
        static extern uint DeviceByIndex(uint index, out IntPtr device);
        [System.Runtime.InteropServices.DllImport("nvml.dll", EntryPoint="nvmlDeviceGetTemperature")]
        static extern uint DeviceTemperature(IntPtr device, uint sensor, out uint temperature);
        public static double?[] Read() {
            double? gpu = ReadNvidia();
            double?[] sensors = ReadMonitorProviders();
            if (!gpu.HasValue) gpu = sensors[1];
            return new double?[] { sensors[0], gpu };
        }
        static double? ReadNvidia() {
            bool initialized = false;
            try {
                if (Init() != 0) return null;
                initialized = true; double hottest = -1;
                for (uint index = 0; index < 16; index++) {
                    IntPtr device; if (DeviceByIndex(index, out device) != 0) break;
                    uint value; if (DeviceTemperature(device, 0, out value) == 0 && value <= 150) hottest = Math.Max(hottest, value);
                }
                return hottest < 0 ? (double?)null : hottest;
            } catch (DllNotFoundException) { return null; }
              catch (EntryPointNotFoundException) { return null; }
              catch (BadImageFormatException) { return null; }
            finally { if (initialized) try { Shutdown(); } catch { } }
        }
        static double?[] ReadMonitorProviders() {
            double? cpuPackage=null,gpu=null; var cpuCores=new List<double>();
            foreach(string path in new[] {@"\\localhost\root\LibreHardwareMonitor",@"\\localhost\root\OpenHardwareMonitor"}) {
                try {
                    var scope=new ManagementScope(path); scope.Connect();
                    using(var query=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT Name,Identifier,SensorType,Value FROM Sensor"))) {
                        query.Options.Timeout=TimeSpan.FromSeconds(1);
                        using(var rows=query.Get()) foreach(ManagementObject item in rows) using(item) {
                            if(!String.Equals(Convert.ToString(item["SensorType"]),"Temperature",StringComparison.OrdinalIgnoreCase)) continue;
                            string id=Convert.ToString(item["Identifier"]).ToLowerInvariant(); string name=Convert.ToString(item["Name"]);
                            double value=Convert.ToDouble(item["Value"],CultureInfo.InvariantCulture);
                            if(value < -20 || value > 150) continue;
                            if(id.Contains("intelcpu")||id.Contains("amdcpu")) {
                                if(name.IndexOf("package",StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("die (average)",StringComparison.OrdinalIgnoreCase)>=0) cpuPackage=value;
                                else if(name.IndexOf("core",StringComparison.OrdinalIgnoreCase)>=0) cpuCores.Add(value);
                            }
                            if((id.Contains("nvidiagpu")||id.Contains("amdgpu")) && name.IndexOf("hot spot",StringComparison.OrdinalIgnoreCase)<0 && (name.IndexOf("gpu core",StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("gpu temperature",StringComparison.OrdinalIgnoreCase)>=0)) gpu=Math.Max(gpu ?? -100,value);
                        }
                    }
                    if(cpuPackage.HasValue && gpu.HasValue) break;
                } catch(ManagementException) { } catch(UnauthorizedAccessException) { } catch(COMException) { }
            }
            double? cpu=cpuPackage.HasValue?cpuPackage:(cpuCores.Count>0?(double?)cpuCores.Average():null);
            return new double?[] { cpu,gpu };
        }
    }
    public sealed class ThemeWidget {
        public string Id=Guid.NewGuid().ToString("N"), Type="Text", Name="Texto", Text="", Font="Segoe UI", Align="Center", Fit="Cover", AccentKey="";
        public string Shape="Rectangle";
        public int X=16,Y=16,Width=120,Height=40,FontSize=20,Color=0x00FFFFFF,FillColor=0x000F1D2B;
        public bool Visible=true,Bold=false,UseFill=false;
        public ThemeWidget Clone() { return (ThemeWidget)MemberwiseClone(); }
    }
    public sealed class ThemeCanvas {
        public int Background=0x000F1D2B;
        public string BackgroundImage="",BackgroundFit="Cover";
        public List<ThemeWidget> Widgets=new List<ThemeWidget>();
        public ThemeCanvas Clone() {
            var copy=(ThemeCanvas)MemberwiseClone();
            copy.Widgets=Widgets==null?new List<ThemeWidget>():Widgets.Select(item=>item==null?null:item.Clone()).ToList();
            return copy;
        }
    }
    public sealed class ThemeConfig {
        public string Name="Oceano",Font="Segoe UI",Orientation="Portrait";
        public int Background=0x000F1D2B,Foreground=0x00FFFFFF,Muted=0x00B4CADC,
            CpuAccent=0x0067CCE6,RamAccent=0x006FD7A8,GpuAccent=0x00F4BE68,WeatherAccent=0x0067CCE6,ClockSize=44;
        public bool ShowSeconds=true;
        public ThemeCanvas Portrait,Landscape;
        public ThemeConfig() { Portrait=ThemeDefaults.Create(this,false); Landscape=ThemeDefaults.Create(this,true); }
        public ThemeCanvas Canvas(bool landscape) { return landscape?Landscape:Portrait; }
        public int CanvasWidth { get { return String.Equals(Orientation,"Landscape",StringComparison.OrdinalIgnoreCase)?480:320; } }
        public int CanvasHeight { get { return String.Equals(Orientation,"Landscape",StringComparison.OrdinalIgnoreCase)?320:480; } }
        public ThemeConfig Clone() {
            var copy=(ThemeConfig)MemberwiseClone();
            copy.Portrait=Portrait==null?null:Portrait.Clone();copy.Landscape=Landscape==null?null:Landscape.Clone();
            return copy;
        }
    }
    public static class ThemeDefaults {
        public static ThemeCanvas Create(ThemeConfig theme,bool landscape) {
            var canvas=new ThemeCanvas { Background=theme==null?0x000F1D2B:theme.Background };
            if(landscape) {
                canvas.Widgets.Add(new ThemeWidget{Type="Clock",Name="Relógio",Text="HH:mm:ss",X=16,Y=8,Width=448,Height=58,FontSize=52,Bold=true});
                canvas.Widgets.Add(new ThemeWidget{Type="Date",Name="Data",X=18,Y=68,Width=444,Height=24,FontSize=16,Color=0x00B4CADC});
                canvas.Widgets.Add(new ThemeWidget{Type="CPU",Name="CPU",X=18,Y=108,Width=140,Height=58,FontSize=24,AccentKey="CpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="RAM",Name="RAM",X=170,Y=108,Width=140,Height=58,FontSize=24,AccentKey="RamAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="GPU",Name="GPU",X=322,Y=108,Width=140,Height=58,FontSize=24,AccentKey="GpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="CPU_TEMP",Name="Temp. CPU",X=30,Y=184,Width=190,Height=34,FontSize=18,AccentKey="CpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="GPU_TEMP",Name="Temp. GPU",X=260,Y=184,Width=190,Height=34,FontSize=18,AccentKey="GpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="Weather",Name="Clima",X=18,Y=232,Width=444,Height=70,FontSize=23,AccentKey="WeatherAccent"});
            } else {
                canvas.Widgets.Add(new ThemeWidget{Type="Clock",Name="Relógio",Text="HH:mm:ss",X=16,Y=12,Width=288,Height=56,FontSize=theme==null?44:Math.Max(12,Math.Min(100,theme.ClockSize)),Bold=true});
                canvas.Widgets.Add(new ThemeWidget{Type="Date",Name="Data",X=18,Y=72,Width=284,Height=22,FontSize=14,Color=0x00B4CADC});
                canvas.Widgets.Add(new ThemeWidget{Type="CPU",Name="CPU",X=18,Y=112,Width=284,Height=48,FontSize=21,AccentKey="CpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="RAM",Name="RAM",X=18,Y=170,Width=284,Height=48,FontSize=21,AccentKey="RamAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="GPU",Name="GPU",X=18,Y=228,Width=284,Height=48,FontSize=21,AccentKey="GpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="CPU_TEMP",Name="Temp. CPU",X=18,Y=294,Width=136,Height=34,FontSize=17,AccentKey="CpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="GPU_TEMP",Name="Temp. GPU",X=166,Y=294,Width=136,Height=34,FontSize=17,AccentKey="GpuAccent"});
                canvas.Widgets.Add(new ThemeWidget{Type="Weather",Name="Clima",X=18,Y=350,Width=284,Height=108,FontSize=20,AccentKey="WeatherAccent"});
            }
            return canvas;
        }
        public static void Ensure(ThemeConfig theme,bool legacy) {
            if(theme==null)return;
            if(theme.Portrait==null || legacy)theme.Portrait=Create(theme,false);
            if(theme.Landscape==null || legacy)theme.Landscape=Create(theme,true);
            if(theme.Portrait.Widgets==null)theme.Portrait.Widgets=Create(theme,false).Widgets;
            if(theme.Landscape.Widgets==null)theme.Landscape.Widgets=Create(theme,true).Widgets;
            NormalizeCanvas(theme.Portrait,320,480);NormalizeCanvas(theme.Landscape,480,320);
            if(!String.Equals(theme.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase))theme.Orientation="Portrait";
        }
        static void NormalizeCanvas(ThemeCanvas canvas,int width,int height) {
            canvas.Widgets=canvas.Widgets.Where(item=>item!=null).ToList();
            foreach(var item in canvas.Widgets) {
                if(String.IsNullOrWhiteSpace(item.Id))item.Id=Guid.NewGuid().ToString("N");
                if(String.IsNullOrWhiteSpace(item.Type))item.Type="Text";
                item.Width=Math.Max(12,Math.Min(width,item.Width));item.Height=Math.Max(12,Math.Min(height,item.Height));
                item.X=Math.Max(0,Math.Min(width-item.Width,item.X));item.Y=Math.Max(0,Math.Min(height-item.Height,item.Y));
                item.FontSize=Math.Max(7,Math.Min(160,item.FontSize));
            }
            if(String.IsNullOrWhiteSpace(canvas.BackgroundFit))canvas.BackgroundFit="Cover";
        }
    }
    public static class ThemeStore {
        static readonly object gate=new object(); static readonly JavaScriptSerializer json=new JavaScriptSerializer();
        public static ThemeConfig Load() {
            try { lock(gate) {
                string raw=File.ReadAllText(Paths.UserFile("tema.json"));var value=json.Deserialize<ThemeConfig>(raw);
                if(value==null||String.IsNullOrWhiteSpace(value.Font)||value.ClockSize<12||value.ClockSize>100)throw new InvalidDataException();
                ThemeDefaults.Ensure(value,raw.IndexOf("\"Portrait\"",StringComparison.OrdinalIgnoreCase)<0);return value;
            } }
            catch { var value=new ThemeConfig();ThemeDefaults.Ensure(value,false);return value; }
        }
        public static List<ThemeConfig> LoadSaved() {
            try { lock(gate) {
                string raw=File.ReadAllText(Paths.UserFile("temas.json"));var list=json.Deserialize<List<ThemeConfig>>(raw)??new List<ThemeConfig>();
                bool legacy=raw.IndexOf("\"Portrait\"",StringComparison.OrdinalIgnoreCase)<0;
                foreach(var item in list)ThemeDefaults.Ensure(item,legacy);return list;
            } }
            catch { return new List<ThemeConfig>(); }
        }
        public static string StoreImage(string source) {
            string extension=Path.GetExtension(source).ToLowerInvariant();
            if(!new[]{".png",".jpg",".jpeg",".bmp",".gif"}.Contains(extension))throw new InvalidDataException("Escolha uma imagem PNG, JPG, BMP ou GIF.");
            string folder=Paths.UserFile("assets");Directory.CreateDirectory(folder);
            string name=Guid.NewGuid().ToString("N")+extension;File.Copy(source,Path.Combine(folder,name),true);return "assets/"+name;
        }
        public static string ResolveAsset(string path) {
            if(String.IsNullOrWhiteSpace(path))return null;
            if(Path.IsPathRooted(path))return File.Exists(path)?path:null;
            string normalized=path.Replace('/',Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar);
            string full=Path.GetFullPath(Path.Combine(Paths.UserData,normalized));
            string root=Path.GetFullPath(Paths.UserData+Path.DirectorySeparatorChar);
            return full.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&File.Exists(full)?full:null;
        }
        static IEnumerable<string> Assets(ThemeConfig theme) {
            ThemeDefaults.Ensure(theme,false);
            foreach(var canvas in new[]{theme.Portrait,theme.Landscape}) {
                if(!String.IsNullOrWhiteSpace(canvas.BackgroundImage))yield return canvas.BackgroundImage;
                foreach(var widget in canvas.Widgets.Where(item=>item!=null&&item.Type=="Image"&&!String.IsNullOrWhiteSpace(item.Text)))yield return widget.Text;
            }
        }
        public static void Export(ThemeConfig theme,string destination) {
            ThemeDefaults.Ensure(theme,false);
            using(var stream=new FileStream(destination,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            using(var archive=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Create)) {
                var entry=archive.CreateEntry("tema.json",System.IO.Compression.CompressionLevel.Optimal);
                using(var writer=new StreamWriter(entry.Open()))writer.Write(json.Serialize(theme));
                foreach(string asset in Assets(theme).Distinct(StringComparer.OrdinalIgnoreCase)) {
                    string source=ResolveAsset(asset);if(source==null)continue;
                    string name="assets/"+Path.GetFileName(source);var imageEntry=archive.CreateEntry(name,System.IO.Compression.CompressionLevel.Optimal);
                    using(var input=File.OpenRead(source))using(var output=imageEntry.Open())input.CopyTo(output);
                }
            }
        }
        public static ThemeConfig Import(string source) {
            using(var stream=File.OpenRead(source))using(var archive=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Read)) {
                var entry=archive.GetEntry("tema.json");if(entry==null||entry.Length>2000000)throw new InvalidDataException("O arquivo não contém um tema válido.");
                ThemeConfig theme;using(var reader=new StreamReader(entry.Open()))theme=json.Deserialize<ThemeConfig>(reader.ReadToEnd());
                if(theme==null)throw new InvalidDataException("O tema está vazio.");ThemeDefaults.Ensure(theme,false);
                string folder=Paths.UserFile("assets");Directory.CreateDirectory(folder);var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(var asset in archive.Entries.Where(item=>item.FullName.StartsWith("assets/",StringComparison.OrdinalIgnoreCase))) {
                    string leaf=Path.GetFileName(asset.FullName);if(String.IsNullOrWhiteSpace(leaf)||asset.Length>25000000)continue;
                    string target=Guid.NewGuid().ToString("N")+Path.GetExtension(leaf).ToLowerInvariant();
                    using(var input=asset.Open())using(var output=File.Create(Path.Combine(folder,target)))input.CopyTo(output);
                    paths[asset.FullName]="assets/"+target;
                }
                foreach(var canvas in new[]{theme.Portrait,theme.Landscape}) {
                    if(!String.IsNullOrWhiteSpace(canvas.BackgroundImage)&&paths.ContainsKey(canvas.BackgroundImage))canvas.BackgroundImage=paths[canvas.BackgroundImage];
                    foreach(var widget in canvas.Widgets.Where(item=>item!=null&&item.Type=="Image"&&!String.IsNullOrWhiteSpace(item.Text)))if(paths.ContainsKey(widget.Text))widget.Text=paths[widget.Text];
                }
                return theme;
            }
        }
        public static void Save(ThemeConfig theme) {
            ThemeDefaults.Ensure(theme,false);
            lock(gate) {
                var list=LoadSaved();int found=list.FindIndex(item=>String.Equals(item.Name,theme.Name,StringComparison.OrdinalIgnoreCase));
                if(found>=0)list[found]=theme.Clone();else list.Add(theme.Clone());
                File.WriteAllText(Paths.UserFile("temas.json"),json.Serialize(list));File.WriteAllText(Paths.UserFile("tema.json"),json.Serialize(theme));
            }
        }
    }
    public sealed class Place {
        public string Name, Region, Country;
        public double Latitude, Longitude;
        public override string ToString() { return Name + ", " + Region + " • " + Country; }
    }
    public sealed class Weather {
        public string City = "Mauá, SP", Detail = "Buscando clima…";
        public double? Temperature, Feels, Humidity;
        public DateTime Updated;
        public bool Failed;
    }
    public static class Climate {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static readonly object gate = new object();
        public static Dictionary<string, object> Fetch(string url) {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 10000; request.ReadWriteTimeout = 10000; request.UserAgent = "Pulso35/1.0";
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) {
                string content = reader.ReadToEnd();
                lock (gate) return Json.Deserialize<Dictionary<string, object>>(content);
            }
        }
        public static List<Place> Search(string name) {
            var data = Fetch("https://geocoding-api.open-meteo.com/v1/search?count=10&language=pt&format=json&name=" + Uri.EscapeDataString(name));
            var results = new List<Place>();
            if (!data.ContainsKey("results")) return results;
            foreach (Dictionary<string, object> item in (IEnumerable)data["results"]) results.Add(new Place {
                Name = Convert.ToString(item["name"]), Region = item.ContainsKey("admin1") ? Convert.ToString(item["admin1"]) : "",
                Country = Convert.ToString(item["country_code"]), Latitude = Convert.ToDouble(item["latitude"], CultureInfo.InvariantCulture),
                Longitude = Convert.ToDouble(item["longitude"], CultureInfo.InvariantCulture)
            });
            return results;
        }
        public static Weather Read(Place place) {
            string url = String.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code&timezone=auto", place.Latitude, place.Longitude);
            var data = Fetch(url); var current = (Dictionary<string, object>)data["current"];
            return new Weather { City = place.Name + ", " + place.Region, Updated = DateTime.Now,
                Temperature = Convert.ToDouble(current["temperature_2m"], CultureInfo.InvariantCulture),
                Feels = Convert.ToDouble(current["apparent_temperature"], CultureInfo.InvariantCulture),
                Humidity = Convert.ToDouble(current["relative_humidity_2m"], CultureInfo.InvariantCulture),
                Detail = Description(Convert.ToInt32(current["weather_code"], CultureInfo.InvariantCulture)) };
        }
        public static string Description(int code) {
            if (code == 0) return "Céu limpo";
            if (code >= 1 && code <= 3) return code == 3 ? "Nublado" : "Parcialmente nublado";
            if (code == 45 || code == 48) return "Nevoeiro";
            if (code >= 51 && code <= 57) return "Garoa";
            if (code >= 61 && code <= 67) return "Chuva";
            if (code >= 71 && code <= 77) return "Neve";
            if (code >= 80 && code <= 82) return "Pancadas de chuva";
            if (code == 85 || code == 86) return "Pancadas de neve";
            if (code >= 95 && code <= 99) return "Trovoadas";
            return "Condição indisponível";
        }
        public static Place Load() {
            try {
                Place saved;
                lock (gate) saved = Json.Deserialize<Place>(File.ReadAllText(Paths.UserFile("cidade.json")));
                if (saved == null || String.IsNullOrWhiteSpace(saved.Name) || Double.IsNaN(saved.Latitude) || Double.IsNaN(saved.Longitude) ||
                    Math.Abs(saved.Latitude) > 90 || Math.Abs(saved.Longitude) > 180) throw new InvalidDataException("Cidade inválida.");
                return saved;
            } catch { return new Place { Name = "Mauá", Region = "SP", Country = "BR", Latitude = -23.6678, Longitude = -46.4614 }; }
        }
        public static void Save(Place value) {
            lock (gate) File.WriteAllText(Paths.UserFile("cidade.json"), Json.Serialize(value));
        }
    }
    public static class Panel {
        static Color C(int value) { return Color.FromArgb(unchecked((int)(0xFF000000u | (uint)value))); }
        public static readonly Rectangle[] Bands = { new Rectangle(0,0,320,104), new Rectangle(0,104,320,74), new Rectangle(0,178,320,74), new Rectangle(0,252,320,74), new Rectangle(0,326,320,154) };
        static readonly object imageGate=new object();
        static readonly Dictionary<string,Bitmap> imageCache=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
        public static int Width(ThemeConfig theme) { return theme!=null&&String.Equals(theme.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase)?480:320; }
        public static int Height(ThemeConfig theme) { return theme!=null&&String.Equals(theme.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase)?320:480; }
        public static Rectangle[] BandsFor(int width,int height) {
            var result=new Rectangle[5];for(int i=0;i<result.Length;i++){int top=height*i/result.Length;int bottom=height*(i+1)/result.Length;result[i]=new Rectangle(0,top,width,bottom-top);}return result;
        }
        static Color C(int value,Color fallback) { return value==0?fallback:C(value); }
        static string ColorHex(int value) { return ColorTranslator.ToHtml(C(value)); }
        static string MetricValue(string key,Metrics m,bool stale) {
            if(stale||m==null)return "—";
            if(key=="CPU"&&m.Cpu.HasValue)return m.Cpu.Value.ToString("0",CultureInfo.InvariantCulture)+"%";
            if(key=="RAM"&&m.Ram.HasValue)return m.Ram.Value.ToString("0",CultureInfo.InvariantCulture)+"%";
            if(key=="GPU"&&m.Gpu.HasValue)return m.Gpu.Value.ToString("0",CultureInfo.InvariantCulture)+"%";
            if(key=="CPU_TEMP"&&m.CpuTemperature.HasValue)return m.CpuTemperature.Value.ToString("0",CultureInfo.InvariantCulture)+"°C";
            if(key=="GPU_TEMP"&&m.GpuTemperature.HasValue)return m.GpuTemperature.Value.ToString("0",CultureInfo.InvariantCulture)+"°C";
            return "—";
        }
        static Color Accent(ThemeWidget item,ThemeConfig theme) {
            if(item.AccentKey=="CpuAccent")return C(theme.CpuAccent);
            if(item.AccentKey=="RamAccent")return C(theme.RamAccent);
            if(item.AccentKey=="GpuAccent")return C(theme.GpuAccent);
            if(item.AccentKey=="WeatherAccent")return C(theme.WeatherAccent);
            string type=(item.Type??"").ToUpperInvariant();
            if(type=="CPU"||type=="CPU_TEMP")return C(theme.CpuAccent);
            if(type=="RAM")return C(theme.RamAccent);
            if(type=="GPU"||type=="GPU_TEMP")return C(theme.GpuAccent);
            if(type=="WEATHER")return C(theme.WeatherAccent);
            return C(item.Color,C(theme.Foreground));
        }
        static StringAlignment Align(string value) { return value=="Right"?StringAlignment.Far:value=="Center"?StringAlignment.Center:StringAlignment.Near; }
        static void DrawText(Graphics g,string text,ThemeWidget item,Rectangle bounds,Color color,int size,bool bold) {
            if(String.IsNullOrEmpty(text)||bounds.Width<=0||bounds.Height<=0)return;
            string face=String.IsNullOrWhiteSpace(item.Font)?"Segoe UI":item.Font;
            using(var font=new Font(face,Math.Max(7,Math.Min(160,size)),bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(color))
            using(var format=new StringFormat { Alignment=Align(item.Align),LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })
                g.DrawString(text,font,brush,new RectangleF(bounds.X,bounds.Y,bounds.Width,bounds.Height),format);
        }
        static Bitmap ImageFor(string asset) {
            string path=ThemeStore.ResolveAsset(asset);if(path==null)return null;
            lock(imageGate) {
                Bitmap image;if(imageCache.TryGetValue(path,out image))return image;
                try { using(var source=Image.FromFile(path)){image=new Bitmap(source);imageCache[path]=image;return image;} }
                catch { return null; }
            }
        }
        static void DrawImage(Graphics g,string asset,Rectangle bounds,string fit) {
            Bitmap image=ImageFor(asset);if(image==null||bounds.Width<=0||bounds.Height<=0)return;
            lock(imageGate) {
                GraphicsState state=g.Save();g.SetClip(bounds);
                if(fit=="Stretch")g.DrawImage(image,bounds);
                else {
                    float scale=fit=="Contain"?Math.Min((float)bounds.Width/image.Width,(float)bounds.Height/image.Height):Math.Max((float)bounds.Width/image.Width,(float)bounds.Height/image.Height);
                    float w=image.Width*scale,h=image.Height*scale;
                    g.DrawImage(image,new RectangleF(bounds.X+(bounds.Width-w)/2,bounds.Y+(bounds.Height-h)/2,w,h));
                }
                g.Restore(state);
            }
        }
        static double? ValueFor(string key,Metrics m,bool stale) {
            if(stale||m==null)return null;
            if(key=="CPU")return m.Cpu;if(key=="RAM")return m.Ram;if(key=="GPU")return m.Gpu;
            if(key=="CPU_TEMP")return m.CpuTemperature;if(key=="GPU_TEMP")return m.GpuTemperature;return null;
        }
        static string Expand(string text,DateTime now,Metrics metrics,Weather weather,ThemeConfig theme,bool stale) {
            if(text==null)return "";
            return text.Replace("%CPU%",MetricValue("CPU",metrics,stale)).Replace("%RAM%",MetricValue("RAM",metrics,stale))
                .Replace("%GPU%",MetricValue("GPU",metrics,stale)).Replace("%CPU_TEMP%",MetricValue("CPU_TEMP",metrics,stale))
                .Replace("%GPU_TEMP%",MetricValue("GPU_TEMP",metrics,stale)).Replace("%TIME%",now.ToString(theme.ShowSeconds?"HH:mm:ss":"HH:mm"))
                .Replace("%DATE%",now.ToString("dd/MM/yyyy",Pt)).Replace("%CITY%",weather==null?"":weather.City)
                .Replace("%WEATHER%",weather==null?"":weather.Detail).Replace("%HEX%",ColorHex(theme.Foreground));
        }
        static void DrawMetric(Graphics g,ThemeWidget item,Rectangle b,Color accent,Metrics m,Weather weather,bool stale,ThemeConfig theme) {
            string type=(item.Type??"").ToUpperInvariant();int size=Math.Max(10,item.FontSize);Color foreground=C(theme.Foreground),muted=C(theme.Muted);
            if(type=="CPU_TEMP"||type=="GPU_TEMP") {
                string label=type=="CPU_TEMP"?"CPU":"GPU";string metricValue=MetricValue(type,m,stale);
                DrawText(g,(item.Name??label).ToUpper(Pt),item,new Rectangle(b.X,b.Y,b.Width/2,b.Height),accent,Math.Max(9,size*2/3),true);
                DrawText(g,metricValue,item,new Rectangle(b.X+b.Width/2,b.Y,b.Width-b.Width/2,b.Height),foreground,size,item.Bold);return;
            }
            double? ratio=ValueFor(type,m,stale);string value=MetricValue(type,m,stale);string detail="";
            if(type=="RAM"&&!stale&&m!=null&&m.TotalGb>0)detail=String.Format(Pt,"{0:0.0} / {1:0.0} GB",m.UsedGb,m.TotalGb);
            else if(type=="GAUGE") { string key=(item.Text??"CPU").ToUpperInvariant();ratio=ValueFor(key,m,stale);value=MetricValue(key,m,stale);detail=item.Name; }
            if(item.UseFill)using(var fill=new SolidBrush(C(item.FillColor,Color.FromArgb(35,255,255,255))))g.FillRectangle(fill,b);
            int labelHeight=Math.Max(12,size*3/4);
            bool hasDetail=b.Height>35&&!String.IsNullOrEmpty(detail);
            if(hasDetail) {
                DrawText(g,(item.Name??type).ToUpper(Pt),item,new Rectangle(b.X+2,b.Y,b.Width/2,labelHeight),accent,Math.Max(9,size*2/3),true);
                DrawText(g,value,item,new Rectangle(b.X+b.Width/2,b.Y,b.Width/2-2,Math.Max(labelHeight,b.Height-12)),foreground,size,true);
                DrawText(g,detail,item,new Rectangle(b.X+2,b.Bottom-Math.Max(14,size*2/3)-1,b.Width-4,Math.Max(12,size*2/3)),muted,Math.Max(9,size*2/3),false);
            } else {
                DrawText(g,(item.Name??type).ToUpper(Pt),item,new Rectangle(b.X+2,b.Y,b.Width/2,b.Height),accent,Math.Max(9,size*2/3),true);
                DrawText(g,value,item,new Rectangle(b.X+b.Width/2,b.Y,b.Width/2-2,b.Height),foreground,size,true);
            }
            if(type=="GAUGE"||item.UseFill) {
                int h=Math.Max(3,Math.Min(7,b.Height/8));int y=b.Bottom-h;
                using(var track=new SolidBrush(Color.FromArgb(55,accent)))g.FillRectangle(track,b.X,y,b.Width,h);
                if(ratio.HasValue)using(var bar=new SolidBrush(accent))g.FillRectangle(bar,b.X,y,(int)(b.Width*Math.Max(0,Math.Min(100,ratio.Value))/100),h);
            }
        }
        static void DrawWidget(Graphics g,ThemeWidget item,Rectangle bounds,DateTime now,Metrics metrics,Weather weather,ThemeConfig theme,bool stale) {
            if(!item.Visible||bounds.Width<=0||bounds.Height<=0)return;
            GraphicsState state=g.Save();g.SetClip(bounds);Color accent=Accent(item,theme);Color foreground=C(theme.Foreground);
            try {
                if(item.Type=="Image") { DrawImage(g,item.Text,bounds,item.Fit);return; }
                if(item.Type=="Shape") {
                    int inset=item.FontSize<2?0:item.FontSize/2;Rectangle shape=Rectangle.Inflate(bounds,-inset,-inset);
                    if(item.UseFill)using(var fill=new SolidBrush(C(item.FillColor))) {
                        if(item.Shape=="Ellipse")g.FillEllipse(fill,shape);else if(item.Shape=="Line"){}else g.FillRectangle(fill,shape);
                    }
                    using(var pen=new Pen(accent,Math.Max(1,item.FontSize/4))) {
                        if(item.Shape=="Ellipse")g.DrawEllipse(pen,shape);else if(item.Shape=="Line")g.DrawLine(pen,shape.Left,shape.Top,shape.Right,shape.Bottom);else g.DrawRectangle(pen,shape);
                    }
                    return;
                }
                if(item.Type=="Clock") { DrawText(g,now.ToString(theme.ShowSeconds?"HH:mm:ss":"HH:mm"),item,bounds,C(item.Color,foreground),item.FontSize,item.Bold);return; }
                if(item.Type=="Date") { DrawText(g,now.ToString(String.IsNullOrWhiteSpace(item.Text)?"ddd, dd MMM":item.Text,Pt),item,bounds,C(item.Color,C(theme.Muted)),item.FontSize,item.Bold);return; }
                if(item.Type=="Text") { DrawText(g,Expand(item.Text,now,metrics,weather,theme,stale),item,bounds,C(item.Color,foreground),item.FontSize,item.Bold);return; }
                if(item.Type=="Weather") {
                    string city=weather==null?"Mauá, SP":weather.City;string temperature=weather!=null&&weather.Temperature.HasValue?weather.Temperature.Value.ToString("0",CultureInfo.InvariantCulture)+"°C":"—°C";
                    int line=Math.Max(16,item.FontSize);int cityHeight=Math.Min(bounds.Height,line+2);DrawText(g,city.ToUpper(Pt),item,new Rectangle(bounds.X,bounds.Y,bounds.Width,cityHeight),accent,Math.Max(10,line*2/3),true);
                    string detail=weather==null?"Configure a cidade no aplicativo":weather.Detail;
                    if(weather!=null&&weather.Feels.HasValue)detail+=String.Format(Pt," • sens. {0:0}°",weather.Feels);
                    if(weather!=null&&weather.Humidity.HasValue)detail+=String.Format(Pt," • {0:0}% umid.",weather.Humidity);
                    if(bounds.Height>=line*3) {
                        int valueY=bounds.Y+cityHeight;int valueHeight=Math.Min(line+2,Math.Max(10,bounds.Bottom-valueY));int detailY=valueY+valueHeight;
                        DrawText(g,temperature,item,new Rectangle(bounds.X,valueY,bounds.Width,valueHeight),foreground,line,true);
                        DrawText(g,detail,item,new Rectangle(bounds.X,detailY,bounds.Width,Math.Max(10,bounds.Bottom-detailY)),C(theme.Muted),Math.Max(9,line*2/3),false);
                    } else {
                        int valueY=bounds.Y+line;DrawText(g,temperature,item,new Rectangle(bounds.X,valueY,bounds.Width/2,Math.Max(10,bounds.Height-line)),foreground,line,true);
                        DrawText(g,detail,item,new Rectangle(bounds.X+bounds.Width/2,valueY,bounds.Width/2,Math.Max(10,bounds.Height-line)),C(theme.Muted),Math.Max(9,line*2/3),false);
                    }
                    return;
                }
                if(item.Type=="CPU"||item.Type=="RAM"||item.Type=="GPU"||item.Type=="CPU_TEMP"||item.Type=="GPU_TEMP"||item.Type=="Gauge") {
                    DrawMetric(g,item,bounds,accent,metrics,weather,stale,theme);return;
                }
                DrawText(g,Expand(item.Text,now,metrics,weather,theme,stale),item,bounds,foreground,item.FontSize,item.Bold);
            } finally { g.Restore(state); }
        }
        public static Bitmap Render(DateTime now,Metrics metrics,Weather weather) { return Render(now,metrics,weather,new ThemeConfig()); }
        public static Bitmap Render(DateTime now,Metrics metrics,Weather weather,ThemeConfig theme) {
            if(theme==null)theme=new ThemeConfig();ThemeDefaults.Ensure(theme,false);bool landscape=String.Equals(theme.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase);
            int width=landscape?480:320,height=landscape?320:480;ThemeCanvas canvas=theme.Canvas(landscape);if(canvas==null)canvas=ThemeDefaults.Create(theme,landscape);
            var bitmap=new Bitmap(width,height);
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(C(canvas.Background));g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;g.SmoothingMode=SmoothingMode.AntiAlias;
                if(!String.IsNullOrWhiteSpace(canvas.BackgroundImage))DrawImage(g,canvas.BackgroundImage,new Rectangle(0,0,width,height),canvas.BackgroundFit);
                bool stale=metrics==null||metrics.At==default(DateTime)||(DateTime.Now-metrics.At).TotalSeconds>15;
                foreach(var item in canvas.Widgets)if(item!=null)DrawWidget(g,item,new Rectangle(item.X,item.Y,item.Width,item.Height),now,metrics,weather,theme,stale);
            }
            return bitmap;
        }
    }
}
