/* SPDX-License-Identifier: GPL-3.0-or-later
 * Copyright (C) 2026 Pulso 3.5 contributors
 * See LICENSE for the full license text.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Controlador35 {
    public static class Paths {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Controlador35");
        public static readonly string Logs = Path.Combine(UserData, "logs");
        public static string UserFile(string name) { Directory.CreateDirectory(UserData); return Path.Combine(UserData,name); }
        public static void Log(string text) {
            Directory.CreateDirectory(Logs);
            File.AppendAllText(Path.Combine(Logs, "controlador.log"), DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine, Encoding.UTF8);
        }
    }
    public sealed class Device {
        public string Port, Id;
        public override string ToString() { return Port + " — Tela USB 3,5\""; }
        public static List<Device> Find() {
            var result = new List<Device>();
            using (var search = new ManagementObjectSearcher("SELECT DeviceID,PNPDeviceID FROM Win32_SerialPort"))
            using (var rows = search.Get()) {
                foreach (ManagementObject row in rows) using (row) {
                    string id = Convert.ToString(row["PNPDeviceID"]);
                    if (id.IndexOf("VID_1A86&PID_5722", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.Add(new Device { Port = Convert.ToString(row["DeviceID"]), Id = id });
                }
            }
            return result;
        }
    }
    public static class Protocol {
        public const int Width = 320, Height = 480;
        public static readonly Rectangle ClockArea = new Rectangle(16, 282, 288, 96);
        public static byte[] Command(byte cmd, int x, int y, int endX, int endY) {
            foreach (int coordinate in new int[] { x, y, endX, endY })
                if (coordinate < 0 || coordinate > 1023) throw new ArgumentOutOfRangeException("coordinate");
            return new byte[] { (byte)(x >> 2), (byte)(((x & 3) << 6) | (y >> 4)),
                (byte)(((y & 15) << 4) | (endX >> 6)), (byte)(((endX & 63) << 2) | (endY >> 8)),
                (byte)endY, cmd };
        }
        public static byte[] RegionHeader(Rectangle r) {
            return RegionHeader(r,Width,Height);
        }
        public static byte[] RegionHeader(Rectangle r,int width,int height) {
            if (r.Width <= 0 || r.Height <= 0 || r.X < 0 || r.Y < 0 ||
                (long)r.X + r.Width > width || (long)r.Y + r.Height > height)
                throw new ArgumentOutOfRangeException("r", "A região deve caber na orientação selecionada.");
            return Command(0xC5, r.X, r.Y, r.Right - 1, r.Bottom - 1);
        }
        public static byte[] Orientation(bool inverted) {
            return Orientation(inverted,false);
        }
        public static byte[] Orientation(bool inverted,bool landscape) {
            int orientation=landscape?(inverted?3:2):(inverted?1:0);
            int width=landscape?480:320,height=landscape?320:480;
            byte[] data = new byte[16]; data[5] = 121; data[6] = (byte)(100+orientation);
            data[7] = (byte)(width>>8);data[8]=(byte)width;data[9]=(byte)(height>>8);data[10]=(byte)height;
            return data;
        }
        public static byte[] Pixels(Bitmap source) {
            using (var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb)) {
                using (Graphics g = Graphics.FromImage(bmp)) g.DrawImageUnscaled(source, 0, 0);
                BitmapData bits = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try {
                    var row = new byte[Math.Abs(bits.Stride)]; var pixels = new byte[bmp.Width * bmp.Height * 2];
                    int output = 0;
                    for (int y = 0; y < bmp.Height; y++) {
                        Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                        for (int x = 0; x < bmp.Width; x++) {
                            int i = x * 3; int rgb = ((row[i + 2] & 248) << 8) | ((row[i + 1] & 252) << 3) | (row[i] >> 3);
                            pixels[output++] = (byte)rgb; pixels[output++] = (byte)(rgb >> 8);
                        }
                    }
                    return pixels;
                } finally { bmp.UnlockBits(bits); }
            }
        }
    }
    public sealed class Measurement {
        public string Kind; public int Bytes; public double Milliseconds;
        public override string ToString() {
            return String.Format(CultureInfo.InvariantCulture, "{0}: {1:N1} ms | {2:N0} bytes | {3:N1} KiB/s",
                Kind, Milliseconds, Bytes, Bytes / Math.Max(Milliseconds, 0.01) * 1000 / 1024);
        }
    }
    public sealed class DisplayLink : IDisposable {
        private SerialPort port;
        private int width=Protocol.Width,height=Protocol.Height;
        public string Model { get; private set; }
        public void Open(Device device, bool inverted) {
            Open(device,inverted,false);
        }
        public void Open(Device device,bool inverted,bool landscape) {
            width=landscape?480:320;height=landscape?320:480;
            // Re-enumerate before opening: never send screen commands to another serial device.
            if (!Device.Find().Any(d => d.Port == device.Port && d.Id == device.Id))
                throw new IOException("A tela selecionada foi desconectada. Conecte o cabo e tente novamente.");
            port = new SerialPort(device.Port, 115200, Parity.None, 8, StopBits.One) {
                DtrEnable = true, RtsEnable = true, Handshake = Handshake.None,
                ReadTimeout = 800, WriteTimeout = 5000, WriteBufferSize = 32768
            };
            try {
                port.Open(); Thread.Sleep(150); port.DiscardInBuffer();
                byte[] hello = Enumerable.Repeat((byte)69, 6).ToArray();
                port.Write(hello, 0, hello.Length); Drain();
                var response = new List<byte>(); var sw = Stopwatch.StartNew();
                while (response.Count < 6 && sw.ElapsedMilliseconds < 850) {
                    if (port.BytesToRead > 0) response.Add((byte)port.ReadByte()); else Thread.Sleep(5);
                }
                string answer = BitConverter.ToString(response.ToArray());
                if (response.Count == 6 && response.All(b => b == 1)) Model = "3,5 polegadas confirmadas";
                else if (response.Count == 0) Model = "Sem resposta HELLO; perfil serial 3,5 polegadas";
                else throw new IOException("Resposta de identificação inesperada: " + answer + ". Envio interrompido.");
                Paths.Log("OPEN " + device.Port + " " + device.Id + " HELLO=" + answer);
                port.DiscardInBuffer();
                Write(Protocol.Orientation(inverted,landscape)); Thread.Sleep(20);
                Write(Protocol.Command(109, 0, 0, 0, 0)); Thread.Sleep(20);
                // About 60% brightness, fixed for consistent visual comparisons.
                Write(Protocol.Command(110, 102, 0, 0, 0)); Thread.Sleep(20);
            } catch { Dispose(); throw; }
        }
        private void Write(byte[] bytes) { port.Write(bytes, 0, bytes.Length); Drain(); }
        private void Drain() {
            var timer = Stopwatch.StartNew();
            while (port.BytesToWrite > 0) {
                if (timer.ElapsedMilliseconds > 5000) throw new TimeoutException("A fila de envio não esvaziou em 5 segundos. Reconecte a tela.");
                Thread.Sleep(1);
            }
        }
        public Measurement Send(Bitmap image, Rectangle region, string kind) {
            if (image.Width != region.Width || image.Height != region.Height) throw new ArgumentException("Imagem e região têm tamanhos diferentes.");
            byte[] header = Protocol.RegionHeader(region,width,height); byte[] pixels = Protocol.Pixels(image);
            var timer = Stopwatch.StartNew();
            port.Write(header, 0, header.Length);
            for (int start = 0; start < pixels.Length; start += 2560)
                port.Write(pixels, start, Math.Min(2560, pixels.Length - start));
            Drain(); timer.Stop();
            var result = new Measurement { Kind = kind, Bytes = header.Length + pixels.Length, Milliseconds = timer.Elapsed.TotalMilliseconds };
            Paths.Log("SEND " + result); return result;
        }
        public void Dispose() {
            if (port == null) return;
            try { port.Dispose(); } finally { port = null; }
        }
    }
    public static class Pattern {
        static readonly Color Background = Color.FromArgb(15, 29, 43);
        static void Text(Graphics g, string text, int size, Color color, RectangleF box, bool bold) {
            using (var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, brush, box, format);
        }
        public static Bitmap Clock(DateTime now) {
            var bitmap = new Bitmap(288, 96);
            using (var g = Graphics.FromImage(bitmap)) {
                g.Clear(Background); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Text(g, now.ToString("HH:mm:ss"), 54, Color.White, new RectangleF(0, 0, 288, 65), true);
                Text(g, now.ToString("dd/MM/yyyy"), 17, Color.FromArgb(180, 202, 220), new RectangleF(0, 65, 288, 29), false);
            }
            return bitmap;
        }
        public static Bitmap Full(DateTime now) {
            var bitmap = new Bitmap(320, 480);
            using (var g = Graphics.FromImage(bitmap)) {
                g.Clear(Background); g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var pen = new Pen(Color.White)) g.DrawRectangle(pen, 0, 0, 319, 479);
                Text(g, "TESTE DA TELA", 23, Color.White, new RectangleF(16, 28, 288, 36), true);
                Text(g, "320 × 480  /  CORES E POSIÇÃO", 12, Color.FromArgb(180, 202, 220), new RectangleF(16, 61, 288, 24), false);
                var colors = new Color[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Cyan, Color.Magenta };
                var labels = new string[] { "R", "G", "B", "Y", "C", "M" };
                for (int i = 0; i < colors.Length; i++) {
                    using (var brush = new SolidBrush(colors[i])) g.FillRectangle(brush, 16 + i * 48, 100, 48, 72);
                    Text(g, labels[i], 14, Color.White, new RectangleF(16 + i * 48, 175, 48, 23), true);
                }
                for (int x = 0; x < 288; x++) {
                    int value = x * 255 / 287;
                    using (var pen = new Pen(Color.FromArgb(value, value, value))) g.DrawLine(pen, 16 + x, 211, 16 + x, 235);
                }
                using (var black = new SolidBrush(Color.Black)) g.FillRectangle(black, 16, 246, 144, 18);
                g.FillRectangle(Brushes.White, 160, 246, 144, 18);
                using (var clock = Clock(now)) g.DrawImageUnscaled(clock, Protocol.ClockArea.Location);
                Text(g, "Relógio atualizado por região", 14, Color.FromArgb(180, 202, 220), new RectangleF(16, 395, 288, 25), false);
                Text(g, "USB  /  RGB565", 13, Color.White, new RectangleF(16, 424, 288, 22), true);
                Text(g, "0,0", 11, Color.White, new RectangleF(6, 3, 42, 18), false);
                Text(g, "319,0", 11, Color.White, new RectangleF(263, 3, 52, 18), false);
                Text(g, "0,479", 11, Color.White, new RectangleF(5, 457, 52, 18), false);
                Text(g, "319,479", 11, Color.White, new RectangleF(249, 457, 66, 18), false);
            }
            return bitmap;
        }
    }
    public sealed class ControllerForm : Form {
        const int PanelRefreshIntervalMs = 1000;
        readonly ComboBox devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Button connect = new Button { Text = "Conectar e mostrar" }, stop = new Button { Text = "Parar", Enabled = false },
            resend = new Button { Text = "Reenviar painel", Enabled = false }, refresh = new Button { Text = "Buscar tela" },
            editTheme = new Button { Text = "Editar tema…" }, search = new Button { Text = "Buscar cidade" }, apply = new Button { Text = "Usar cidade" },
            updates = new Button { Text = "Atualizações…" };
        readonly TextBox city = new TextBox { Text = "Mauá" };
        readonly ComboBox places = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox invert = new CheckBox { Text = "Girar tela em 180°", AutoSize = true, Checked = true };
        readonly Label status = new Label(), timing = new Label(), climateStatus = new Label(),caption = new Label();
        readonly PictureBox preview = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom };
        readonly System.Windows.Forms.Timer previewTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly CancellationTokenSource services = new CancellationTokenSource();
        readonly NotifyIcon trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Pulso 3.5 — painel USB", Visible = true };
        readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        volatile Metrics latest = new Metrics();
        volatile Weather weather = new Weather();
        volatile ThemeConfig theme = ThemeStore.Load();
        volatile Place place = Climate.Load();
        Task sensorsTask, weatherTask, running;
        CancellationTokenSource cancel;
        volatile bool fullRequested;
        bool closing, rendering, shuttingDown, exitRequested, trayNoticeShown, checkingUpdate;
        public ControllerForm(bool auto) {
            rendering=Environment.GetCommandLineArgs().Contains("--render-ui");
            trayIcon.Visible=!rendering;
            weather = new Weather { City = place.Name + ", " + place.Region };
            Text = "Pulso 3.5 — Painel open source para telas USB"; BackColor = Color.FromArgb(238,242,246);
            Font = new Font("Segoe UI",10); ClientSize = new Size(870,630); MinimumSize = new Size(886,669);
            StartPosition = FormStartPosition.CenterScreen;
            preview.SetBounds(22,25,320,480);
            var title = new Label { Text = "Computador e clima", Font = new Font("Segoe UI",22,FontStyle.Bold) };
            title.SetBounds(365,26,290,46); updates.SetBounds(665,31,175,34);
            var subtitle = new Label { Text = "CPU, memória, GPU e o tempo na sua cidade." };
            subtitle.SetBounds(368,79,475,32);
            devices.SetBounds(368,119,285,32); refresh.SetBounds(665,118,175,32);
            connect.SetBounds(368,164,285,40); stop.SetBounds(665,164,175,40);
            invert.SetBounds(369,219,265,28); resend.SetBounds(665,215,175,36);
            var cityLabel = new Label { Text = "Clima • cidade" }; cityLabel.SetBounds(368,268,450,24);
            editTheme.SetBounds(368,260,472,34);
            cityLabel.SetBounds(368,307,450,24);
            city.SetBounds(368,335,285,30); search.SetBounds(665,334,175,32);
            places.SetBounds(368,376,285,32); apply.SetBounds(665,375,175,32);
            places.Items.Add(place); places.SelectedIndex = 0; city.Text = place.Name;
            climateStatus.SetBounds(368,416,472,40); climateStatus.Text = "Clima: " + place;
            status.SetBounds(368,465,472,57); timing.SetBounds(368,529,472,73);
            status.Text = "Prévia ao vivo. Conecte para mostrar na tela USB.";
            caption.Text = Panel.Width(theme)+" × "+Panel.Height(theme)+" • relógio a cada 1 s\nIndicadores a cada 2 s • clima a cada 10 min\nTemperaturas: LibreHardwareMonitor com WMI";
            caption.ForeColor=Color.FromArgb(70,84,100);caption.SetBounds(23,520,325,76);
            Controls.AddRange(new Control[] { preview,title,subtitle,devices,refresh,connect,stop,invert,resend,editTheme,updates,cityLabel,city,search,places,apply,climateStatus,status,timing,caption });
            trayMenu.Items.Add("Abrir Pulso 3.5",null,delegate { RestoreFromTray(); });
            trayMenu.Items.Add("Sair e parar a tela",null,delegate { exitRequested=true; Close(); });
            trayIcon.ContextMenuStrip=trayMenu;
            trayIcon.DoubleClick+=delegate { RestoreFromTray(); };
            Resize+=delegate { if(WindowState==FormWindowState.Minimized) HideToTray(); };
            updates.Click+=async delegate { await CheckForUpdates(); };
            connect.Click += delegate { Start(); }; stop.Click += delegate { Stop(); };
            resend.Click += delegate { fullRequested = true; }; refresh.Click += delegate { Scan(); };
            editTheme.Click += delegate {
                using(var editor=new ThemeDesignerForm(theme,latest,weather,delegate(ThemeConfig updated){theme=updated;caption.Text=Panel.Width(theme)+" × "+Panel.Height(theme)+" • relógio a cada 1 s\nIndicadores a cada 2 s • clima a cada 10 min\nTemperaturas: LibreHardwareMonitor com WMI";SetPreview(Panel.Render(DateTime.Now,latest,weather,theme));fullRequested=true;})) editor.ShowDialog(this);
            };
            search.Click += async delegate {
                if (city.Text.Trim().Length < 2) { climateStatus.Text = "Digite pelo menos duas letras da cidade."; return; }
                search.Enabled = false; climateStatus.Text = "Buscando cidades…";
                try {
                    string name = city.Text.Trim(); var results = await Task.Run(() => Climate.Search(name));
                    if (IsDisposed) return;
                    places.Items.Clear(); foreach (var item in results) places.Items.Add(item);
                    if (results.Count > 0) places.SelectedIndex = 0;
                    climateStatus.Text = results.Count > 0 ? "Escolha cidade e estado; clique em Usar cidade." : "Cidade não encontrada. Tente apenas o nome.";
                } catch (Exception ex) { if (!IsDisposed) climateStatus.Text = "Busca indisponível: " + ex.Message; }
                finally { if (!IsDisposed) search.Enabled = true; }
            };
            apply.Click += delegate {
                var selected = places.SelectedItem as Place; if (selected == null) return;
                try { Climate.Save(selected);
                    weather = new Weather { City = selected.Name + ", " + selected.Region };
                    place = new Place { Name = selected.Name, Region = selected.Region, Country = selected.Country, Latitude = selected.Latitude, Longitude = selected.Longitude }; climateStatus.Text = "Cidade salva: " + selected; }
                catch (Exception ex) { climateStatus.Text = "Não foi possível salvar: " + ex.Message; }
            };
            previewTimer.Tick += delegate { SetPreview(Panel.Render(DateTime.Now,latest,weather,theme)); };
            Shown += delegate {
                Scan(); SetPreview(Panel.Render(DateTime.Now,latest,weather,theme));
                if (!rendering) { StartServices(); previewTimer.Start(); if (auto && devices.Items.Count == 1) Start(); }
            };
            FormClosing += OnClosing;
        }
        void HideToTray() {
            if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
            ShowInTaskbar=false; Hide();
            if(!trayNoticeShown) {
                trayIcon.ShowBalloonTip(2200,"Pulso 3.5","O painel continuará ativo na bandeja e manterá a tela USB atualizada.",ToolTipIcon.Info);
                trayNoticeShown=true;
            }
        }
        void RestoreFromTray() {
            ShowInTaskbar=true; Show(); WindowState=FormWindowState.Normal; Activate(); BringToFront();
        }
        async Task CheckForUpdates() {
            if(checkingUpdate || shuttingDown) return;
            string currentUrl=UpdateManager.LoadManifestUrl();
            using(var dialog=new UpdateSourceForm(currentUrl)) {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try { UpdateManager.SaveManifestUrl(dialog.ManifestUrl); }
                catch(Exception ex) { MessageBox.Show(this,ex.Message,"Endereço de atualização",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
            }
            checkingUpdate=true; updates.Enabled=false; status.Text="Verificando atualizações…";
            try {
                string url=UpdateManager.LoadManifestUrl();
                UpdateRelease release=await Task.Run(()=>UpdateManager.Check(url));
                if(!release.IsNewer) {
                    status.Text="O Pulso 3.5 já está atualizado ("+UpdateManager.CurrentVersion+").";
                    MessageBox.Show(this,"Você já está usando a versão mais recente.","Atualizações",MessageBoxButtons.OK,MessageBoxIcon.Information); return;
                }
                string message="A versão "+release.version+" está disponível.\n\n"+(String.IsNullOrWhiteSpace(release.notes)?"Deseja baixar e instalar agora?":release.notes+"\n\nDeseja baixar e instalar agora?");
                if(MessageBox.Show(this,message,"Atualização disponível",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes) return;
                status.Text="Baixando e validando a atualização…";
                string package=await Task.Run(()=>UpdateManager.DownloadPackage(release));
                UpdateManager.LaunchApply(package,release.version,release.sha256,Application.ExecutablePath,Process.GetCurrentProcess().Id);
                exitRequested=true; Close();
            } catch(Exception ex) {
                if(!IsDisposed) { status.Text="Não foi possível atualizar: "+ex.Message; MessageBox.Show(this,ex.Message,"Falha na atualização",MessageBoxButtons.OK,MessageBoxIcon.Error); }
            } finally {
                checkingUpdate=false;
                if(!IsDisposed && !shuttingDown) updates.Enabled=true;
            }
        }
        void StartServices() {
            var token = services.Token;
            sensorsTask = Task.Run(delegate {
                var sensors = new Sensors();
                while (!token.IsCancellationRequested) {
                    try { latest = sensors.Read(); }
                    catch (Exception ex) { Paths.Log("SENSORS " + ex.Message); }
                    token.WaitHandle.WaitOne(2000);
                }
            });
            weatherTask = Task.Run(delegate {
                Place previous = null; DateTime next = DateTime.MinValue;
                while (!token.IsCancellationRequested) {
                    Place selected = place;
                    if (!Object.ReferenceEquals(previous,selected) || DateTime.UtcNow >= next) {
                        previous = selected;
                        try { var value = Climate.Read(selected); if (Object.ReferenceEquals(place,selected)) weather = value; next = DateTime.UtcNow.AddMinutes(10); }
                        catch (Exception ex) {
                            if (Object.ReferenceEquals(place,selected)) {
                                var old = weather;
                                weather = new Weather { City = old.City, Temperature = old.Temperature, Feels = old.Feels, Humidity = old.Humidity,
                                    Updated = old.Updated, Detail = old.Temperature.HasValue ? old.Detail + (old.Failed ? "" : " • sem conexão") : "Clima indisponível", Failed = true };
                            }
                            Paths.Log("WEATHER " + ex.Message); next = DateTime.UtcNow.AddMinutes(1);
                        }
                    }
                    token.WaitHandle.WaitOne(1000);
                }
            });
        }
        public void SaveUiPreview() {
            rendering = true; Show(); Application.DoEvents();
            SetPreview(Panel.Render(DateTime.Now,latest,weather,theme));
            using (var image = new Bitmap(Width,Height)) { DrawToBitmap(image,new Rectangle(0,0,Width,Height)); image.Save(Paths.UserFile("previa-controlador.png"),ImageFormat.Png); }
        }
        void Scan() {
            try { devices.Items.Clear(); foreach (var d in Device.Find()) devices.Items.Add(d); if (devices.Items.Count>0) devices.SelectedIndex=0; else status.Text="Tela não encontrada. Confira o USB e clique em Buscar tela."; }
            catch (Exception ex) { status.Text="Não foi possível buscar a tela: " + ex.Message; }
        }
        void SetPreview(Bitmap image) { var old=preview.Image; preview.Image=image; if(old!=null) old.Dispose(); }
        void Ui(Action action) { if(IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch(InvalidOperationException) { } }
        void Busy(bool busy) { connect.Enabled=!busy; devices.Enabled=!busy; refresh.Enabled=!busy; invert.Enabled=!busy; editTheme.Enabled=!busy; stop.Enabled=busy; resend.Enabled=busy; }
        void Stop() { if(cancel!=null) { cancel.Cancel(); status.Text="Parando após o envio atual…"; stop.Enabled=false; } }
        async void Start() {
            var selected=devices.SelectedItem as Device; if(selected==null) { status.Text="Selecione uma tela USB compatível."; return; }
            bool rotation=invert.Checked,landscape=String.Equals(theme.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase); cancel=new CancellationTokenSource(); var token=cancel.Token;
            Busy(true); status.Text="Conectando à " + selected.Port + "…";
            running=Task.Run(delegate {
                try {
                    using(var link=new DisplayLink()) {
                        link.Open(selected,rotation,landscape); fullRequested=true;
                        int width=landscape?480:320,height=landscape?320:480;var bands=Panel.BandsFor(width,height);
                        byte[][] previous = new byte[bands.Length][];
                        Ui(delegate { status.Text="Conectado à " + selected.Port + ". " + link.Model; });
                        while(!token.IsCancellationRequested) {
                            var tick=Stopwatch.StartNew(); int bytes=0, regions=0; double elapsed=0;
                            using(var image=Panel.Render(DateTime.Now,latest,weather,theme)) {
                                bool full=fullRequested; fullRequested=false;
                                if(full) {
                                    var measurement=link.Send(image,new Rectangle(0,0,width,height),"Painel completo");
                                    bytes+=measurement.Bytes; elapsed+=measurement.Milliseconds; regions++;
                                }
                                for(int i=0;i<bands.Length;i++) {
                                    var area=bands[i];
                                    using(var band=image.Clone(area,PixelFormat.Format24bppRgb)) {
                                        byte[] pixels=Protocol.Pixels(band);
                                        if(!full && (previous[i]==null || !pixels.SequenceEqual(previous[i]))) {
                                            if(token.IsCancellationRequested) break;
                                            var measurement=link.Send(band,area,"Painel parcial");
                                            bytes+=measurement.Bytes; elapsed+=measurement.Milliseconds; regions++;
                                        }
                                        previous[i]=pixels;
                                    }
                                }
                            }
                            string info=String.Format(CultureInfo.GetCultureInfo("pt-BR"),"Último ciclo: {0} regiões • {1:N0} bytes\nEnvio: {2:N0} ms • meta: 1 s",regions,bytes,elapsed);
                            Ui(delegate { timing.Text=info; });
                            token.WaitHandle.WaitOne(Math.Max(0,PanelRefreshIntervalMs-(int)tick.ElapsedMilliseconds));
                        }
                    }
                    Ui(delegate { status.Text="Parado. A última imagem permanece na tela."; });
                } catch(UnauthorizedAccessException) { Ui(delegate { status.Text="Porta ocupada. Feche o UsbMonitor ou outro controlador e tente novamente."; }); }
                catch(Exception ex) { Paths.Log("PANEL ERROR " + ex); Ui(delegate { status.Text="Envio interrompido: " + ex.Message; }); }
            });
            await running; cancel.Dispose(); cancel=null; Busy(false);
        }
        void DisposeAfterServices(Task background) {
            background.ContinueWith(delegate { services.Dispose(); },TaskScheduler.Default);
        }
        async void OnClosing(object sender,FormClosingEventArgs e) {
            if(closing) return;
            if(!exitRequested && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; HideToTray(); return; }
            e.Cancel=true; if(shuttingDown) return; shuttingDown=true; previewTimer.Stop(); services.Cancel(); Stop(); Enabled=false;
            if(running!=null) await running;
            var background = Task.WhenAll(new Task[] { sensorsTask,weatherTask }.Where(task => task != null));
            await Task.WhenAny(background,Task.Delay(1500));
            if(background.IsCompleted) services.Dispose();
            else DisposeAfterServices(background);
            closing=true; if(preview.Image!=null) { preview.Image.Dispose(); preview.Image=null; }
            previewTimer.Dispose(); trayIcon.Visible=false; trayIcon.Dispose(); trayMenu.Dispose(); Close();
        }
    }
    public static class Program {
        static void Equal(byte[] actual, params byte[] expected) {
            if (!actual.SequenceEqual(expected)) throw new Exception("Teste falhou: " + BitConverter.ToString(actual));
        }
        static void SelfTest() {
            Equal(Protocol.RegionHeader(new Rectangle(0, 0, 320, 480)), 0, 0, 4, 253, 223, 197);
            Equal(Protocol.RegionHeader(new Rectangle(16, 282, 288, 96)), 4, 17, 164, 189, 121, 197);
            Equal(Protocol.RegionHeader(new Rectangle(319, 479, 1, 1)), 79, 221, 244, 253, 223, 197);
            using (var colors = new Bitmap(7, 2)) {
                Color[] values = { Color.Red, Color.Lime, Color.Blue, Color.White, Color.Black, Color.Cyan, Color.Magenta };
                for (int x = 0; x < 7; x++) for (int y = 0; y < 2; y++) colors.SetPixel(x, y, values[x]);
                byte[] row = { 0, 248, 224, 7, 31, 0, 255, 255, 0, 0, 255, 7, 31, 248 };
                Equal(Protocol.Pixels(colors), row.Concat(row).ToArray());
            }
            bool rejected = false;
            try { Protocol.RegionHeader(new Rectangle(319, 479, 2, 1)); } catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected) throw new Exception("Região fora da tela foi aceita.");
            rejected = false;
            try { Protocol.RegionHeader(new Rectangle(0, 0, 0, 1)); } catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected) throw new Exception("Região vazia foi aceita.");
            using (var image = Pattern.Full(new DateTime(2026, 10, 22, 12, 34, 56))) {
                if (Protocol.Pixels(image).Length != 307200) throw new Exception("Tamanho de quadro inválido.");
                image.Save(Paths.UserFile("imagem-teste.png"), ImageFormat.Png);
            }
            Equal(Protocol.Orientation(false), 0, 0, 0, 0, 0, 121, 100, 1, 64, 1, 224, 0, 0, 0, 0, 0);
            Equal(Protocol.Orientation(true), 0, 0, 0, 0, 0, 121, 101, 1, 64, 1, 224, 0, 0, 0, 0, 0);
            Paths.Log("SELFTEST PASS: headers, boundary validation, RGB565 colors/stride, image length and orientation.");
            Console.WriteLine("SELFTEST PASS");
        }
        static int Probe(int seconds, string port) {
            var candidates = Device.Find().Where(d => port == null || d.Port.Equals(port, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1) throw new IOException("É necessário exatamente um dispositivo de tela compatível.");
            var measurements = new List<Measurement>();
            using (var link = new DisplayLink()) {
                link.Open(candidates[0], true);
                using (var image = Pattern.Full(DateTime.Now)) measurements.Add(link.Send(image, new Rectangle(0, 0, 320, 480), "full"));
                for (int i = 0; i < seconds; i++) {
                    Thread.Sleep(1000);
                    using (var image = Pattern.Clock(DateTime.Now)) measurements.Add(link.Send(image, Protocol.ClockArea, "clock"));
                }
                var json = new StringBuilder("{\n  \"port\":\"").Append(candidates[0].Port).Append("\",\n  \"hello\":\"")
                    .Append(link.Model).Append("\",\n  \"timestamp\":\"").Append(DateTimeOffset.Now.ToString("o"))
                    .Append("\",\n  \"measurement_scope\":\"serial_write_and_windows_queue_drain_not_physical_refresh\",\n  \"samples\":[\n");
                for (int i = 0; i < measurements.Count; i++) {
                    var m = measurements[i];
                    json.AppendFormat(CultureInfo.InvariantCulture, "    {{\"kind\":\"{0}\",\"bytes\":{1},\"milliseconds\":{2}}}{3}\n", m.Kind, m.Bytes, m.Milliseconds.ToString("F3", CultureInfo.InvariantCulture), i == measurements.Count - 1 ? "" : ",");
                }
                json.Append("  ]\n}\n");
                File.WriteAllText(Paths.UserFile("resultado-teste.json"), json.ToString(), Encoding.UTF8);
                foreach (var m in measurements) Console.WriteLine(m);
            }
            return 0;
        }
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Length>=6 && args[0]=="--apply-update")
                    return UpdateManager.ApplyPackage(args[1],args[2],args[3],args[4],args[5]);
                if (args.Contains("--self-test")) { SelfTest(); return 0; }
                if (args.Contains("--panel-check")) {
                    var sensor = new Sensors(); sensor.Read(); Thread.Sleep(1200); var sample = sensor.Read();
                    if (!sample.Cpu.HasValue || !sample.Ram.HasValue || sample.TotalGb <= 0) throw new Exception("Falha na leitura de CPU/RAM.");
                    var climate = Climate.Read(Climate.Load());
                    if (!climate.Temperature.HasValue) throw new Exception("Clima sem temperatura.");
                    using(var image = Panel.Render(DateTime.Now,sample,climate)) {
                        image.Save(Paths.UserFile("previa-painel.png"),ImageFormat.Png);
                        foreach(var band in Panel.Bands) using(var crop=image.Clone(band,PixelFormat.Format24bppRgb)) {
                            Protocol.RegionHeader(band);
                            if(Protocol.Pixels(crop).Length!=band.Width*band.Height*2) throw new Exception("Faixa inválida.");
                        }
                    }
                    Console.WriteLine(String.Format(CultureInfo.InvariantCulture,"PANEL PASS CPU={0:0.0}% RAM={1:0.0}/{2:0.0}GB GPU={3} WEATHER={4} {5}C",sample.Cpu,sample.UsedGb,sample.TotalGb,sample.Gpu.HasValue?sample.Gpu.Value.ToString("0.0"):"indisponível",climate.City,climate.Temperature));
                    return 0;
                }
                if (args.Contains("--render-ui")) {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using (var form = new ControllerForm(false)) form.SaveUiPreview();
                    return 0;
                }
                bool created;
                using (var mutex = new Mutex(true, "Local\\Controlador35.SerialOwner", out created)) {
                    if (!created) throw new IOException("O controlador já está aberto. Use a janela existente.");
                    try {
                        if (args.Contains("--probe")) {
                            int seconds = 8; string port = null;
                            for (int i = 0; i < args.Length; i++) {
                                if (args[i] == "--seconds" && i + 1 < args.Length) seconds = Int32.Parse(args[++i]);
                                if (args[i] == "--port" && i + 1 < args.Length) port = args[++i];
                            }
                            if (seconds < 1 || seconds > 60) throw new ArgumentOutOfRangeException("seconds");
                            return Probe(seconds, port);
                        }
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        Application.Run(new ControllerForm(args.Contains("--connect"))); return 0;
                    } finally { mutex.ReleaseMutex(); }
                }
            } catch (Exception ex) {
                Paths.Log("FATAL " + ex); Console.Error.WriteLine(ex.Message);
                if (!args.Contains("--probe") && !args.Contains("--self-test") && !args.Contains("--panel-check")) MessageBox.Show(ex.Message, "Pulso 3.5");
                return 1;
            }
        }
    }
}
