/* SPDX-License-Identifier: GPL-3.0-or-later
 * Copyright (C) 2026 Pulso 3.5 contributors
 * See LICENSE for the full license text.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Controlador35 {
    public sealed class UpdateRelease {
        public string version { get; set; }
        public string packageUrl { get; set; }
        public string sha256 { get; set; }
        public string notes { get; set; }
        public Uri ManifestUri { get; set; }
        public Uri PackageUri { get; set; }
        public bool IsNewer { get; set; }
    }

    public static class UpdateManager {
        const string LatestReleaseManifest = "https://github.com/vini-cius/pulso3.5/releases/latest/download/latest.json";
        const string FallbackVersion = "1.0.0";
        const int MaxManifestBytes = 128 * 1024;
        const int MaxPackageBytes = 120 * 1024 * 1024;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static string CurrentVersion {
            get {
                try {
                    string value = File.ReadAllText(Path.Combine(Paths.Root, "versao.txt"), Encoding.UTF8).Trim();
                    Version parsed;
                    if (Version.TryParse(value, out parsed)) return parsed.ToString();
                } catch { }
                return FallbackVersion;
            }
        }

        public static UpdateRelease CheckLatest() {
            Uri requested = new Uri(LatestReleaseManifest, UriKind.Absolute);
            byte[] bytes = Download(requested, MaxManifestBytes);
            UpdateRelease release = Json.Deserialize<UpdateRelease>(Encoding.UTF8.GetString(bytes));
            if (release == null) throw new InvalidDataException("O manifesto de atualização está vazio.");
            Version remote, local;
            if (!Version.TryParse(release.version, out remote)) throw new InvalidDataException("O manifesto contém uma versão inválida.");
            if (!Version.TryParse(CurrentVersion, out local)) local = new Version(FallbackVersion);
            if (String.IsNullOrWhiteSpace(release.packageUrl)) throw new InvalidDataException("O manifesto não informa o pacote da atualização.");
            release.ManifestUri = requested;
            release.PackageUri = new Uri(requested, release.packageUrl ?? "");
            if (!String.Equals(release.PackageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O pacote de atualização precisa estar em um endereço HTTPS.");
            if (String.IsNullOrWhiteSpace(release.sha256) || release.sha256.Length != 64 ||
                release.sha256.Any(ch => !Uri.IsHexDigit(ch)))
                throw new InvalidDataException("O manifesto não contém um SHA-256 válido para o pacote.");
            release.IsNewer = remote > local;
            return release;
        }

        public static string DownloadPackage(UpdateRelease release) {
            if (Paths.IsMsix) throw new InvalidOperationException("O Windows App Installer deve aplicar atualizações da instalação MSIX.");
            if (release == null || release.PackageUri == null) throw new ArgumentNullException("release");
            byte[] bytes = Download(release.PackageUri, MaxPackageBytes);
            string actual;
            using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            if (!String.Equals(actual, release.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A verificação SHA-256 falhou. O pacote baixado foi descartado.");
            ValidatePackage(bytes, release.version);
            string path = Path.Combine(Path.GetTempPath(), "Pulso35-atualizacao-" + Guid.NewGuid().ToString("N") + ".zip");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        static byte[] Download(Uri uri, int maxBytes) {
            if (uri == null || !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("As atualizações só podem ser baixadas por HTTPS.");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET"; request.UserAgent = "Pulso35-Updater/" + CurrentVersion;
            request.Timeout = 15000; request.ReadWriteTimeout = 60000; request.AllowAutoRedirect = true;
            using (var response = (HttpWebResponse)request.GetResponse()) {
                if (!String.Equals(response.ResponseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("O servidor redirecionou o download para uma conexão sem HTTPS.");
                if (response.ContentLength > maxBytes) throw new InvalidDataException("O arquivo de atualização excede o limite permitido.");
                using (Stream input = response.GetResponseStream())
                using (var output = new MemoryStream()) {
                    byte[] buffer = new byte[32768]; int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                        if (output.Length + read > maxBytes) throw new InvalidDataException("O arquivo de atualização excede o limite permitido.");
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
        }

        static Dictionary<string, byte[]> ReadPackage(byte[] bytes, string expectedVersion) {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var memory = new MemoryStream(bytes, false))
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Read, false, Encoding.UTF8)) {
                foreach (ZipArchiveEntry entry in archive.Entries) {
                    string name = entry.FullName;
                    if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 ||
                        (name != "Pulso35.exe" && name != "versao.txt" && name != "portable.flag"))
                        throw new InvalidDataException("O pacote contém um arquivo não permitido: " + name);
                    if (!entryNames.Add(name)) throw new InvalidDataException("O pacote contém arquivos duplicados.");
                    if (entry.Length > MaxPackageBytes) throw new InvalidDataException("Um arquivo do pacote é grande demais.");
                    if (name == "portable.flag") {
                        if (entry.Length != 0) throw new InvalidDataException("O marcador de versão portátil precisa estar vazio.");
                        continue;
                    }
                    using (Stream input = entry.Open())
                    using (var output = new MemoryStream()) { input.CopyTo(output); files.Add(name, output.ToArray()); }
                }
            }
            if (!files.ContainsKey("Pulso35.exe") || !files.ContainsKey("versao.txt"))
                throw new InvalidDataException("O pacote não contém o aplicativo e a versão.");
            string packagedVersion = Encoding.UTF8.GetString(files["versao.txt"]).Trim();
            if (!String.Equals(packagedVersion, expectedVersion, StringComparison.Ordinal))
                throw new InvalidDataException("A versão do pacote não corresponde ao manifesto.");
            return files;
        }

        static void ValidatePackage(byte[] bytes, string version) { ReadPackage(bytes, version); }

        static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        public static void LaunchApply(string packagePath, string version, string sha256, string appPath, int processId) {
            if (Paths.IsMsix) throw new InvalidOperationException("O Windows App Installer deve aplicar atualizações da instalação MSIX.");
            string helper = Path.Combine(Path.GetTempPath(), "Pulso35-Atualizador-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(appPath, helper, false);
            var start = new ProcessStartInfo {
                FileName = helper,
                Arguments = String.Join(" ", new string[] { "--apply-update", Quote(packagePath), Quote(processId.ToString(CultureInfo.InvariantCulture)), Quote(version), Quote(sha256), Quote(appPath) }),
                WorkingDirectory = Path.GetDirectoryName(appPath),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try { if (Process.Start(start) == null) throw new InvalidOperationException("Não foi possível iniciar o atualizador."); }
            catch { try { File.Delete(helper); } catch { } throw; }
        }

        public static int ApplyPackage(string packagePath, string processIdText, string version, string sha256, string appPath) {
            try {
                if (Paths.IsMsix) throw new InvalidOperationException("O Windows App Installer deve aplicar atualizações da instalação MSIX.");
                int processId;
                if (!Int32.TryParse(processIdText, out processId) || processId <= 0) throw new InvalidDataException("Identificador do aplicativo inválido.");
                WaitForProcess(processId, appPath);
                byte[] package = File.ReadAllBytes(packagePath);
                string actual;
                using (var hash = SHA256.Create()) actual = BitConverter.ToString(hash.ComputeHash(package)).Replace("-", "").ToLowerInvariant();
                if (!String.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O arquivo de atualização mudou depois do download.");
                Dictionary<string, byte[]> files = ReadPackage(package, version);
                string appDirectory = Path.GetDirectoryName(Path.GetFullPath(appPath));
                string expectedExe = Path.Combine(appDirectory, "Pulso35.exe");
                if (!String.Equals(Path.GetFullPath(appPath), expectedExe, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("O caminho de instalação não é válido.");
                string probe = Path.Combine(appDirectory, ".pulso35-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok", Encoding.ASCII); File.Delete(probe);
                string stage = Path.Combine(appDirectory, ".update-stage-" + Guid.NewGuid().ToString("N"));
                string backup = Path.Combine(appDirectory, ".update-backup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage); Directory.CreateDirectory(backup);
                var changed = new List<string>(); var existed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                try {
                    foreach (var pair in files) File.WriteAllBytes(Path.Combine(stage, pair.Key), pair.Value);
                    foreach (var pair in files) {
                        string destination = Path.Combine(appDirectory, pair.Key);
                        existed[pair.Key] = File.Exists(destination);
                        if (existed[pair.Key]) File.Copy(destination, Path.Combine(backup, pair.Key), true);
                        File.Copy(Path.Combine(stage, pair.Key), destination, true); changed.Add(pair.Key);
                    }
                    Process.Start(new ProcessStartInfo { FileName = expectedExe, WorkingDirectory = appDirectory, UseShellExecute = true });
                    RemoveLegacyDiagnostic(appDirectory);
                    try { Directory.Delete(stage, true); Directory.Delete(backup, true); File.Delete(packagePath); } catch { }
                    return 0;
                } catch {
                    foreach (string name in changed.AsEnumerable().Reverse()) {
                        string destination = Path.Combine(appDirectory, name), saved = Path.Combine(backup, name);
                        try { if (existed[name] && File.Exists(saved)) File.Copy(saved, destination, true); else if (File.Exists(destination)) File.Delete(destination); } catch { }
                    }
                    throw;
                } finally {
                    try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
                    try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
                }
            } catch (Exception ex) {
                Paths.Log("UPDATE APPLY " + ex);
                MessageBox.Show("A atualização não pôde ser instalada. O aplicativo anterior foi preservado.\n\n" + ex.Message,
                    "Atualização do Pulso 3.5", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { if (File.Exists(appPath)) Process.Start(new ProcessStartInfo { FileName = appPath, WorkingDirectory = Path.GetDirectoryName(appPath), UseShellExecute = true }); } catch { }
                return 1;
            }
        }

        static void WaitForProcess(int processId, string appPath) {
            try {
                using (Process process = Process.GetProcessById(processId)) {
                    string runningPath = process.MainModule == null ? "" : process.MainModule.FileName;
                    if (!String.Equals(Path.GetFullPath(runningPath), Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase)) return;
                    if (!process.WaitForExit(90000)) throw new TimeoutException("O controlador ainda está encerrando. A atualização foi cancelada.");
                }
            } catch (ArgumentException) { }
        }

        static void RemoveLegacyDiagnostic(string appDirectory) {
            try { File.Delete(Path.Combine(appDirectory, "Pulso35-Diagnostico.exe")); } catch { }
            try {
                string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "Pulso 3.5", "Diagnóstico da tela.lnk");
                File.Delete(shortcut);
            } catch { }
        }

    }
}
