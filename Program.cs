// HotspotTray - liga o Hotspot Movel do Windows automaticamente e mostra o estado na bandeja.
// Compilado com o csc do .NET Framework 4.x (C# 5) - veja build.ps1.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace HotspotTray
{
    enum HotspotState { On, Off, Connecting, NoInternet, Error }

    sealed class HotspotStatus
    {
        public HotspotState State;
        public string Ssid;
        public int Clients;
        public string Message;
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static bool IsElevated()
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        public static int RunElevated(string args)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath, args)
            {
                UseShellExecute = true, Verb = "runas",
                WorkingDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath)
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log.Write("elevacao cancelada ou falhou: " + ex.Message);
                return 2;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            // Registra a tarefa de logon. Exige administrador: se nao estiver elevado, pede o UAC
            // e repassa o usuario atual, para a tarefa ser criada para ele (e nao para a conta admin).
            int reg = Array.IndexOf(args, "/register-task");
            if (reg >= 0)
            {
                string user = reg + 1 < args.Length ? args[reg + 1]
                    : Environment.UserDomainName + "\\" + Environment.UserName;
                if (!IsElevated()) return RunElevated("/register-task \"" + user + "\"");
                string err = Settings.CreateLogonTask(user);
                Log.Write(err == null ? "tarefa de logon registrada para " + user : "falha ao registrar tarefa: " + err);
                return err == null ? 0 : 1;
            }
            // Iniciado automaticamente, mas o usuario desativou "Iniciar com o Windows".
            if (args.Contains("/startup") && !Settings.StartWithWindows) return 0;

            bool created;
            using (var mutex = new Mutex(true, "Local\\HotspotTray_SingleInstance", out created))
            {
                if (!created) return 0;
                // Prioridade alta ate o hotspot ligar pela primeira vez (volta ao normal depois).
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }
                Log.Write("iniciado (" + Application.ExecutablePath + ", " + (Environment.TickCount / 1000) + "s desde o boot)");
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("ERRO fatal: " + e.ExceptionObject);
                Application.ThreadException += (s, e) => Log.Write("ERRO: " + e.Exception);
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
            return 0;
        }
    }

    static class Settings
    {
        const string KeyPath = @"Software\HotspotTray";
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "HotspotTray";

        static bool GetBool(string name, bool def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (k == null) return def;
                object v = k.GetValue(name);
                return v == null ? def : Convert.ToInt32(v) != 0;
            }
        }

        static void SetBool(string name, bool value)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                k.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }

        public static bool KeepOn
        {
            get { return GetBool("KeepOn", true); }
            set { SetBool("KeepOn", value); }
        }

        public static bool StartWithWindows
        {
            get { return GetBool("StartWithWindows", true); }
            set { SetBool("StartWithWindows", value); ApplyStartup(); }
        }

        // Inicializacao principal: tarefa agendada no logon (sem o atraso da chave Run e com prioridade alta),
        // registrada pelo instalador com permissao de administrador. A chave "Run" fica como reserva;
        // o mutex impede duas instancias. Os dois passam "/startup", que respeita a opcao do menu.
        public static void ApplyStartup()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunPath))
            {
                if (StartWithWindows)
                    k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" /startup");
                else if (k.GetValue(RunName) != null)
                    k.DeleteValue(RunName);
            }
            if (!LogonTaskExists())
                Log.Write("aviso: tarefa de logon nao encontrada - rode \"Ativar inicializacao rapida.cmd\" para registrar");
        }

        public static bool LogonTaskExists()
        {
            return RunSchtasks("/Query /TN " + RunName) == null;
        }

        public static string CreateLogonTask(string user)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>Liga o Hotspot Movel ao entrar no Windows</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement(user) + "</UserId></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + SecurityElement(user) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
                "<Priority>1</Priority><AllowStartOnDemand>true</AllowStartOnDemand></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + SecurityElement(Application.ExecutablePath) + "</Command>" +
                "<Arguments>/startup</Arguments></Exec></Actions>" +
                "</Task>";
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HotspotTray_task.xml");
            System.IO.File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            try { return RunSchtasks("/Create /TN " + RunName + " /XML \"" + file + "\" /F"); }
            finally { try { System.IO.File.Delete(file); } catch { } }
        }

        static string SecurityElement(string s) { return System.Security.SecurityElement.Escape(s); }

        // Retorna null em caso de sucesso, ou a saida de erro.
        static string RunSchtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? null : output.Trim();
            }
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        // O log fica ao lado do executavel (AppData pode ser virtualizado quando iniciado por apps empacotados).
        static readonly string file = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.ExecutablePath), "log.txt");

        public static void Write(string msg)
        {
            lock (gate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                    var fi = new System.IO.FileInfo(file);
                    if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    System.IO.File.AppendAllText(file,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
                catch { }
            }
        }
    }

    // Conversa com a API WinRT do Hotspot Movel (NetworkOperatorTetheringManager).
    static class Hotspot
    {
        static ConnectionProfile FindInternetProfile()
        {
            ConnectionProfile p = NetworkInformation.GetInternetConnectionProfile();
            if (p != null) return p;
            return NetworkInformation.GetConnectionProfiles().FirstOrDefault(
                x => x.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
        }

        static NetworkOperatorTetheringManager GetManager(out string error)
        {
            error = null;
            ConnectionProfile profile = FindInternetProfile();
            if (profile == null) { error = "sem conexão com a internet"; return null; }
            try
            {
                return NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            }
            catch (Exception ex)
            {
                error = "não suportado nesta conexão (" + ex.Message.Trim() + ")";
                return null;
            }
        }

        public static HotspotStatus Query()
        {
            var s = new HotspotStatus();
            try
            {
                string error;
                var mgr = GetManager(out error);
                if (mgr == null)
                {
                    s.State = error.StartsWith("sem") ? HotspotState.NoInternet : HotspotState.Error;
                    s.Message = error;
                    return s;
                }
                switch (mgr.TetheringOperationalState)
                {
                    case TetheringOperationalState.On: s.State = HotspotState.On; break;
                    case TetheringOperationalState.InTransition: s.State = HotspotState.Connecting; break;
                    default: s.State = HotspotState.Off; break;
                }
                try { s.Ssid = mgr.GetCurrentAccessPointConfiguration().Ssid; } catch { }
                if (s.State == HotspotState.On)
                {
                    try { s.Clients = (int)mgr.ClientCount; } catch { }
                }
            }
            catch (Exception ex)
            {
                s.State = HotspotState.Error;
                s.Message = ex.Message.Trim();
            }
            return s;
        }

        // Retorna null em caso de sucesso, ou a mensagem de erro.
        public static string SetEnabled(bool on)
        {
            try
            {
                string error;
                var mgr = GetManager(out error);
                if (mgr == null) return error;
                var op = on ? mgr.StartTetheringAsync() : mgr.StopTetheringAsync();
                var deadline = DateTime.Now.AddSeconds(60);
                while (op.Status == AsyncStatus.Started)
                {
                    if (DateTime.Now > deadline) { op.Cancel(); return "tempo esgotado"; }
                    Thread.Sleep(200);
                }
                if (op.Status != AsyncStatus.Completed)
                    return op.ErrorCode != null ? op.ErrorCode.Message.Trim() : op.Status.ToString();
                var r = op.GetResults();
                if (r.Status == TetheringOperationStatus.Success) return null;
                return string.IsNullOrEmpty(r.AdditionalErrorMessage)
                    ? r.Status.ToString()
                    : r.AdditionalErrorMessage;
            }
            catch (Exception ex)
            {
                return ex.Message.Trim();
            }
        }
    }

    // Desenha os icones da bandeja no estilo dos icones de rede do Windows.
    static class IconPainter
    {
        public const int SpinnerFrames = 12;

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public static bool LightTaskbar()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v != null && Convert.ToInt32(v) != 0;
                }
            }
            catch { return false; }
        }

        // kind: 0 = ligado, 1 = desligado (corte), 2 = conectando (spinner, frame = posicao)
        public static Icon Make(int kind, int frame)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            Color fg = LightTaskbar() ? Color.FromArgb(26, 26, 26) : Color.White;
            float u = size / 16f; // unidades de desenho em uma grade 16x16

            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.ScaleTransform(u, u);

                    // Area recortada do simbolo (para o corte ou o spinner ficarem "vazados")
                    using (var cut = new GraphicsPath())
                    {
                        if (kind == 1)
                        {
                            using (var wide = new Pen(Color.Black, 4.3f))
                            {
                                cut.AddLine(1.5f, 1.5f, 14.5f, 14.5f);
                                cut.Widen(wide);
                            }
                        }
                        else if (kind == 2)
                        {
                            cut.AddEllipse(6.4f, 6.4f, 11f, 11f);
                        }
                        if (cut.PointCount > 0) g.SetClip(cut, CombineMode.Exclude);

                        Color glyph = kind == 2 ? Color.FromArgb(200, fg) : fg;
                        DrawHotspotGlyph(g, glyph);
                        g.ResetClip();
                    }

                    if (kind == 1)
                    {
                        using (var pen = new Pen(fg, 1.65f))
                        {
                            pen.StartCap = pen.EndCap = LineCap.Round;
                            g.DrawLine(pen, 2f, 2f, 14f, 14f);
                        }
                    }
                    else if (kind == 2)
                    {
                        using (var pen = new Pen(fg, 1.75f))
                        {
                            pen.StartCap = pen.EndCap = LineCap.Round;
                            float start = frame * (360f / SpinnerFrames);
                            g.DrawArc(pen, 8.4f, 8.4f, 6.8f, 6.8f, start, 270f);
                        }
                    }
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        // Simbolo de hotspot: um ponto central com ondas de transmissao dos dois lados  ((•))
        static void DrawHotspotGlyph(Graphics g, Color c)
        {
            const float cx = 8f, cy = 8f;
            using (var b = new SolidBrush(c))
                g.FillEllipse(b, cx - 1.75f, cy - 1.75f, 3.5f, 3.5f);
            using (var pen = new Pen(c, 1.6f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                foreach (float r in new[] { 4.0f, 6.8f })
                {
                    var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                    g.DrawArc(pen, rect, -48f, 96f);  // direita
                    g.DrawArc(pen, rect, 132f, 96f);  // esquerda
                }
            }
        }
    }

    sealed class TrayContext : ApplicationContext
    {
        const string SettingsUri = "ms-settings:network-mobilehotspot";

        readonly NotifyIcon tray = new NotifyIcon();
        readonly Control ui = new Control();
        readonly System.Windows.Forms.Timer spinTimer = new System.Windows.Forms.Timer();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread worker;
        volatile bool running = true;

        Icon iconOn, iconOff;
        Icon[] iconSpin;
        int spinFrame;

        // Estado compartilhado com a thread de trabalho
        volatile bool keepOn = Settings.KeepOn;
        volatile bool paused;          // "Desligar" pelo menu pausa o religamento automatico
        int pendingCommand;            // 0 = nada, 1 = ligar, 2 = desligar

        ToolStripMenuItem statusItem, keepOnItem, startupItem, fastStartItem;
        HotspotStatus last;

        public TrayContext()
        {
            IntPtr force = ui.Handle; // cria o handle para poder usar BeginInvoke

            BuildIcons();
            BuildMenu();
            tray.Icon = iconSpin[0];
            tray.Text = "Hotspot: iniciando…";
            tray.Visible = true;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

            spinTimer.Interval = 80;
            spinTimer.Tick += (s, e) =>
            {
                spinFrame = (spinFrame + 1) % IconPainter.SpinnerFrames;
                tray.Icon = iconSpin[spinFrame];
            };
            SetSpinning(true);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Settings.ApplyStartup();
                    bool missing = !Settings.LogonTaskExists();
                    ui.BeginInvoke((Action)(() => fastStartItem.Visible = missing));
                }
                catch (Exception ex) { Log.Write("falha na inicializacao automatica: " + ex.Message); }
            });

            SystemEvents.UserPreferenceChanged += OnThemeChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "HotspotWorker" };
            worker.Start();
        }

        void BuildIcons()
        {
            iconOn = IconPainter.Make(0, 0);
            iconOff = IconPainter.Make(1, 0);
            iconSpin = new Icon[IconPainter.SpinnerFrames];
            for (int i = 0; i < iconSpin.Length; i++) iconSpin[i] = IconPainter.Make(2, i);
        }

        void BuildMenu()
        {
            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Verificando…") { Enabled = false };
            var openItem = new ToolStripMenuItem("Abrir configurações do Hotspot Móvel", null, (s, e) => OpenSettings());
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            var onItem = new ToolStripMenuItem("Ligar hotspot agora", null, (s, e) => Command(1));
            var offItem = new ToolStripMenuItem("Desligar hotspot (pausa até religar)", null, (s, e) => Command(2));
            keepOnItem = new ToolStripMenuItem("Manter sempre ligado", null, (s, e) =>
            {
                keepOn = !keepOn;
                Settings.KeepOn = keepOn;
                keepOnItem.Checked = keepOn;
                if (keepOn) paused = false;
                wake.Set();
            }) { Checked = keepOn };
            startupItem = new ToolStripMenuItem("Iniciar com o Windows", null, (s, e) =>
            {
                try { Settings.StartWithWindows = !Settings.StartWithWindows; } catch { }
                startupItem.Checked = Settings.StartWithWindows;
            }) { Checked = Settings.StartWithWindows };
            fastStartItem = new ToolStripMenuItem("Ativar inicialização rápida (pede admin)…", null, (s, e) =>
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Program.RunElevated("/register-task \"" + Environment.UserDomainName + "\\" + Environment.UserName + "\"");
                    bool ok = Settings.LogonTaskExists();
                    ui.BeginInvoke((Action)(() =>
                    {
                        fastStartItem.Visible = !ok;
                        tray.ShowBalloonTip(4000, "HotspotTray", ok
                            ? "Pronto! O hotspot vai ligar logo ao entrar no Windows."
                            : "Não foi possível ativar a inicialização rápida.", ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
                    }));
                })) { Visible = false };
            var exitItem = new ToolStripMenuItem("Sair", null, (s, e) => Exit());

            menu.Items.AddRange(new ToolStripItem[] {
                statusItem, new ToolStripSeparator(),
                openItem, new ToolStripSeparator(),
                onItem, offItem, new ToolStripSeparator(),
                keepOnItem, startupItem, fastStartItem, new ToolStripSeparator(),
                exitItem });
            tray.ContextMenuStrip = menu;
        }

        void Command(int cmd)
        {
            Interlocked.Exchange(ref pendingCommand, cmd);
            if (cmd == 1) Show(new HotspotStatus { State = HotspotState.Connecting });
            wake.Set();
        }

        static void OpenSettings()
        {
            try { Process.Start(SettingsUri); } catch { }
        }

        void OnNetworkChanged(object sender, EventArgs e) { wake.Set(); }

        void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;
            ui.BeginInvoke((Action)(() =>
            {
                Icon[] old = { iconOn, iconOff };
                Icon[] oldSpin = iconSpin;
                BuildIcons();
                if (last != null) Show(last);
                foreach (var i in old) i.Dispose();
                foreach (var i in oldSpin) i.Dispose();
            }));
        }

        void WorkerLoop()
        {
            DateTime nextAttempt = DateTime.MinValue;
            int failures = 0;
            HotspotState lastLogged = (HotspotState)(-1);
            bool priorityLowered = false;

            while (running)
            {
                int cmd = Interlocked.Exchange(ref pendingCommand, 0);
                string error = null;

                if (cmd == 2)
                {
                    paused = true;
                    Report(new HotspotStatus { State = HotspotState.Connecting });
                    error = Hotspot.SetEnabled(false);
                }
                else if (cmd == 1)
                {
                    paused = false;
                    failures = 0;
                    nextAttempt = DateTime.MinValue;
                }

                HotspotStatus s = Hotspot.Query();

                bool wantOn = cmd == 1 || (keepOn && !paused);
                if (wantOn && s.State == HotspotState.Off && DateTime.Now >= nextAttempt)
                {
                    s.State = HotspotState.Connecting;
                    Report(s);
                    Log.Write("ligando o hotspot...");
                    error = Hotspot.SetEnabled(true);
                    if (error != null)
                    {
                        failures++;
                        // Logo apos o boot tenta de novo rapido; depois espaca as tentativas.
                        int wait = Environment.TickCount < 3 * 60 * 1000 ? 3 : Math.Min(60, 10 * failures);
                        nextAttempt = DateTime.Now.AddSeconds(wait);
                        Log.Write("falha ao ligar: " + error + " (nova tentativa em " + wait + "s)");
                    }
                    else failures = 0;
                    s = Hotspot.Query();
                }
                if (s.State != lastLogged)
                {
                    lastLogged = s.State;
                    Log.Write("estado: " + s.State + (string.IsNullOrEmpty(s.Message) ? "" : " - " + s.Message));
                    if (s.State == HotspotState.On && !priorityLowered)
                    {
                        priorityLowered = true;
                        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }
                    }
                }
                // Logo apos o boot a internet pode ainda nao estar pronta: mostra "conectando".
                if (s.State == HotspotState.NoInternet && wantOn && Environment.TickCount < 3 * 60 * 1000)
                {
                    s.State = HotspotState.Connecting;
                    s.Message = "aguardando conexão com a internet";
                }
                if (error != null && s.State != HotspotState.On) s.Message = error;

                Report(s);
                wake.WaitOne(s.State == HotspotState.Connecting ? 1500 : 5000);
            }
        }

        void Report(HotspotStatus s)
        {
            if (!running) return;
            try { ui.BeginInvoke((Action)(() => Show(s))); } catch { }
        }

        void Show(HotspotStatus s)
        {
            last = s;
            SetSpinning(s.State == HotspotState.Connecting);
            if (s.State == HotspotState.On) tray.Icon = iconOn;
            else if (s.State != HotspotState.Connecting) tray.Icon = iconOff;

            string text;
            switch (s.State)
            {
                case HotspotState.On:
                    text = "Hotspot ligado";
                    if (!string.IsNullOrEmpty(s.Ssid)) text += " • " + s.Ssid;
                    text += "\n" + (s.Clients == 1 ? "1 dispositivo conectado" : s.Clients + " dispositivos conectados");
                    break;
                case HotspotState.Connecting:
                    text = "Hotspot ligando…";
                    break;
                default:
                    text = paused && s.State == HotspotState.Off ? "Hotspot desligado (pausado)" : "Hotspot desligado";
                    break;
            }
            if (!string.IsNullOrEmpty(s.Message) && s.State != HotspotState.On) text += "\n" + s.Message;

            statusItem.Text = text.Replace("\n", " — ");
            tray.Text = text.Length > 63 ? text.Substring(0, 62) + "…" : text; // limite do Windows
        }

        void SetSpinning(bool on)
        {
            if (on && !spinTimer.Enabled) spinTimer.Start();
            else if (!on && spinTimer.Enabled) spinTimer.Stop();
        }

        void Exit()
        {
            running = false;
            wake.Set();
            SystemEvents.UserPreferenceChanged -= OnThemeChanged;
            spinTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }
}
