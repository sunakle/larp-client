using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class LaunchConfig
{
    public string title { get; set; }
    public string version { get; set; }
    public string java { get; set; }
    public string[] classpath { get; set; }
    public string[] jvmArgs { get; set; }
    public string mainClass { get; set; }
    public string[] gameArgs { get; set; }
}

internal sealed class ClientSettings
{
    public string username { get; set; }
    public int memoryMb { get; set; }
}

internal static class Client
{
    internal static readonly string Root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    [STAThread]
    private static int Main(string[] args)
    {
        bool check = args.Length > 0 && args[0] == "--check";
        bool autoLaunch = args.Length > 0 && args[0] == "--launch";
        try
        {
            if(args.Length == 2 && args[0] == "--export-assets") { Brand.ExportAssets(args[1]); return 0; }
            if(args.Length == 2 && args[0] == "--preview") {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using(var preview=new LauncherForm(new LaunchConfig {title="LARP Client",version="1.21.11"},null,4096)) {
                    preview.ShowInTaskbar=false; preview.StartPosition=FormStartPosition.Manual; preview.Location=new Point(-30000,-30000); preview.Show(); Application.DoEvents();
                    using(var image=new Bitmap(preview.Width,preview.Height)) {preview.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));using(var clientImage=image.Clone(new Rectangle(preview.PointToScreen(Point.Empty).X-preview.Left,preview.PointToScreen(Point.Empty).Y-preview.Top,preview.ClientSize.Width,preview.ClientSize.Height),System.Drawing.Imaging.PixelFormat.Format32bppArgb)) clientImage.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}
                } return 0;
            }
            LaunchConfig config = LoadConfig();
            if (check)
            {
                Validate(config);
                WriteJson("logs/check-result.json", new { ok = true, version = config.version, checkedAt = DateTime.UtcNow.ToString("o") });
                return 0;
            }
            string autoName = autoLaunch && args.Length > 1 ? args[1] : null;
            int memory = 4096;
            if (autoLaunch && args.Length > 2 && !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out memory))
                throw new InvalidDataException("Память указывается числом в МиБ.");
            if (autoLaunch && autoName == null)
                throw new InvalidDataException("Использование: --launch Player [4096]");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            LauncherForm form = new LauncherForm(config, autoName, memory);
            Application.Run(form);
            return form.ResultCode;
        }
        catch (Exception error)
        {
            try { WriteJson(check ? "logs/check-result.json" : "logs/launcher-error.json", new { ok = false, error = error.Message }); }
            catch { }
            if (!check && !autoLaunch)
                MessageBox.Show(error.Message, "LARP Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string LocalPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
            throw new InvalidDataException("В конфигурации разрешены только пути внутри клиента: " + relative);
        string path = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Путь выходит за пределы папки клиента: " + relative);
        return path;
    }

    internal static LaunchConfig LoadConfig()
    {
        LaunchConfig config = Json.Deserialize<LaunchConfig>(File.ReadAllText(LocalPath("launch-config.json"), Encoding.UTF8));
        if (config == null || config.classpath == null || config.jvmArgs == null || config.gameArgs == null ||
            !Regex.IsMatch(config.mainClass ?? "", @"^[A-Za-z_$][A-Za-z0-9_$.]+$"))
            throw new InvalidDataException("Повреждена конфигурация запуска launch-config.json.");
        return config;
    }

    internal static void Validate(LaunchConfig config)
    {
        string javaPath = LocalPath(config.java);
        if (!File.Exists(javaPath)) throw new FileNotFoundException("Не найдена встроенная Java. Распакуйте всю папку клиента.", javaPath);
        if (config.classpath.Length == 0) throw new InvalidDataException("Список библиотек пуст.");
        foreach (string entry in config.classpath)
            if (!File.Exists(LocalPath(entry))) throw new FileNotFoundException("Не найден игровой файл: " + entry);
        foreach (string directory in new[] { "game", "game/assets", "game/natives" })
            if (!Directory.Exists(LocalPath(directory))) throw new DirectoryNotFoundException("Не найдена папка: " + directory);
    }

    internal static void WriteJson(string relative, object value)
    {
        string path = LocalPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, Json.Serialize(value), Utf8);
    }

    internal static string OfflineUuid(string username)
    {
        byte[] hash;
        using (MD5 md5 = MD5.Create()) hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        StringBuilder result = new StringBuilder(32);
        foreach (byte value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return result.ToString();
    }

    // Java argument files use double-quoted tokens and backslash escapes, not Windows argv quoting.
    internal static string ArgFileQuote(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new InvalidDataException("Недопустимый символ в аргументе запуска.");
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\f", "\\f") + "\"";
    }

    internal static string Expand(string value, IDictionary<string, string> replacements)
    {
        foreach (KeyValuePair<string, string> replacement in replacements)
            value = value.Replace("${" + replacement.Key + "}", replacement.Value);
        if (value.IndexOf("${", StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("Неизвестная переменная в конфигурации: " + value);
        return value;
    }
}

internal sealed class LauncherForm : Form
{
    private readonly LaunchConfig config;
    private readonly TextBox username;
    private readonly NumericUpDown memory;
    private readonly Button start;
    private readonly Label status;
    private readonly string autoName;
    private readonly int autoMemory;
    private Process game;
    private StreamWriter log;
    private readonly object logLock = new object();
    private bool waitingForExit;
    private bool hiddenWhilePlaying;
    private Mutex launchMutex;
    private bool mutexHeld;
    internal int ResultCode;

    internal LauncherForm(LaunchConfig config, string autoName, int autoMemory)
    {
        this.config = config;
        this.autoName = autoName;
        this.autoMemory = autoMemory;
        Text = config.title ?? "LARP Client";
        AutoScaleDimensions = new SizeF(96,96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(920, 590);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Brand.Background;
        ForeColor = Color.FromArgb(239,243,247);
        Font = new Font("Segoe UI",10);
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Controls.Add(new BrandView { Left=36,Top=30,Width=30,Height=34,TabStop=false });
        AddLabel("L A R P",78,32,200,30,13,ForeColor).Font=new Font("Segoe UI Semibold",13);
        AddLabel("CLIENT",78,61,160,20,8,Brand.Muted);
        Controls.Add(new BrandView { Left=65,Top=145,Width=206,Height=232,TabStop=false });
        AddLabel("Твой мир. Твои правила.",36,422,270,32,14,ForeColor);
        AddLabel("Minecraft "+config.version+"  /  Fabric",36,461,270,24,10,Brand.Muted);
        AddLabel("LARP  /  ЛОКАЛЬНЫЙ ПРОФИЛЬ",36,542,280,22,8,Brand.Muted);
        AddLabel("ЛАУНЧЕР",380,35,260,24,9,Brand.Accent);
        AddLabel("С возвращением",376,80,500,48,27,ForeColor).Font=new Font("Segoe UI Semibold",27);
        AddLabel("Настрой профиль — и возвращайся в игру.",380,139,480,28,10,Brand.Muted);
        AddLabel("Имя игрока",380,196,290,25,10,ForeColor);
        username = new TextBox {Left=16,Top=15,Width=448,MaxLength=16,Font=new Font("Segoe UI",12),BackColor=Brand.Surface,ForeColor=ForeColor,BorderStyle=BorderStyle.None,TabIndex=0,AccessibleName="Имя игрока"};
        Controls.Add(new FieldPanel(username){Left=380,Top=227,Width=484,Height=54,TabIndex=0});
        AddLabel("3–16 символов: латиница, цифры и _",380,289,484,22,9,Brand.Muted);
        AddLabel("Оперативная память",380,333,260,24,10,ForeColor);
        memory = new NumericUpDown {Left=16,Top=14,Width=108,Minimum=2,Maximum=12,Value=4,Font=new Font("Segoe UI",12),BackColor=Brand.Surface,ForeColor=ForeColor,BorderStyle=BorderStyle.None,TabIndex=1,AccessibleName="Память в гигабайтах"};
        Controls.Add(new FieldPanel(memory){Left=380,Top=367,Width=140,Height=52,TabIndex=1});
        AddLabel("ГБ  /  от 2 до 12",540,380,285,24,10,Brand.Muted);
        start = new ModernButton {Primary=true,Text="ИГРАТЬ  →",Left=380,Top=449,Width=304,Height=56,Font=new Font("Segoe UI Semibold",12),TabIndex=2};
        start.Click += delegate { StartGame(username.Text.Trim(), Decimal.ToInt32(memory.Value) * 1024); };
        Controls.Add(start);
        Button folder = new ModernButton {Text="Папка игры",Left=696,Top=449,Width=168,Height=56,Font=new Font("Segoe UI",10),TabIndex=3};
        folder.Click += delegate {try {Process.Start(new ProcessStartInfo("explorer.exe","\""+Client.LocalPath("game")+"\""){UseShellExecute=true});}catch(Exception error){status.Text=error.Message;}};
        Controls.Add(folder);
        status = AddLabel("Готов к запуску",380,519,484,26,9,Brand.Accent);
        AddLabel("Серверы с проверкой лицензии требуют вход Microsoft.",380,550,490,24,8,Brand.Muted);
        Paint += delegate(object sender,PaintEventArgs e) {
            using(var pen=new Pen(Color.FromArgb(39,46,55))) {e.Graphics.DrawLine(pen,334,30,334,558);e.Graphics.DrawLine(pen,36,515,296,515);}
        };
        status.AutoEllipsis=true;
        ToolTip statusTip=new ToolTip(); statusTip.SetToolTip(status,status.Text);
        status.TextChanged+=delegate {statusTip.SetToolTip(status,status.Text);};
        FormClosed+=delegate {statusTip.Dispose();};
        AcceptButton = start;
        ReadSettings();
        Shown += delegate
        {
            if (autoName != null)
            {
                WindowState = FormWindowState.Minimized;
                StartGame(autoName, autoMemory);
            }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (waitingForExit)
            {
                e.Cancel = true;
                hiddenWhilePlaying = true;
                Hide();
            }
        };
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        try {int enabled=1;DwmSetWindowAttribute(Handle,20,ref enabled,4);} catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
    }
    private Label AddLabel(string text, int left, int top, int width, int height, float size, Color color)
    {
        Label label = new Label { Text = text, Left = left, Top = top, Width = width, Height = height, ForeColor = color, Font = new Font("Segoe UI", size), AutoSize = false };
        Controls.Add(label);
        return label;
    }

    private void ReadSettings()
    {
        username.Text = "Player";
        string path = Client.LocalPath("client-settings.json");
        if (!File.Exists(path)) return;
        try
        {
            ClientSettings settings = Client.Json.Deserialize<ClientSettings>(File.ReadAllText(path, Encoding.UTF8));
            if (settings != null)
            {
                if (Regex.IsMatch(settings.username ?? "", "^[A-Za-z0-9_]{3,16}$")) username.Text = settings.username;
                if (settings.memoryMb >= 2048 && settings.memoryMb <= 12288) memory.Value = Math.Max(2, Math.Min(12, settings.memoryMb / 1024));
            }
        }
        catch { status.Text = "Настройки сброшены. Укажите имя игрока."; }
    }

    private void StartGame(string name, int memoryMb)
    {
        if (waitingForExit) return;
        try
        {
            if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$")) throw new InvalidDataException("Имя: 3–16 латинских букв, цифр или символов _.");
            if (memoryMb < 2048 || memoryMb > 12288) throw new InvalidDataException("Память: от 2048 до 12288 МиБ.");
            Client.Validate(config);
            string mutexName;
            using (MD5 md5 = MD5.Create()) mutexName = "Local\\LarpClient-" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(Client.Root.ToLowerInvariant()))).Replace("-", "");
            launchMutex = new Mutex(false, mutexName);
            try { mutexHeld = launchMutex.WaitOne(0); }
            catch (AbandonedMutexException) { mutexHeld = true; }
            if (!mutexHeld) throw new InvalidOperationException("Этот клиент уже запущен. Закройте его перед повторным запуском.");
            Client.WriteJson("client-settings.json", new ClientSettings { username = name, memoryMb = memoryMb });
            string[] paths = new string[config.classpath.Length];
            for (int i = 0; i < paths.Length; i++) paths[i] = Client.LocalPath(config.classpath[i]);
            Dictionary<string, string> replacements = new Dictionary<string, string>
            {
                { "root", Client.Root },
                { "game_directory", Client.LocalPath("game") },
                { "natives_directory", Client.LocalPath("game/natives") },
                { "assets_root", Client.LocalPath("game/assets") },
                { "classpath", string.Join(";", paths) },
                { "auth_player_name", name },
                { "auth_uuid", Client.OfflineUuid(name) },
                { "memory_mb", memoryMb.ToString(CultureInfo.InvariantCulture) }
            };
            List<string> arguments = new List<string> { "-Xms512M", "-Xmx" + memoryMb.ToString(CultureInfo.InvariantCulture) + "M" };
            foreach (string item in config.jvmArgs) arguments.Add(Client.Expand(item, replacements));
            arguments.Add(config.mainClass);
            foreach (string item in config.gameArgs) arguments.Add(Client.Expand(item, replacements));
            Directory.CreateDirectory(Client.LocalPath("logs"));
            string suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string argFile = Client.LocalPath("logs/launch-" + suffix + ".args");
            StringBuilder argumentText = new StringBuilder();
            foreach (string argument in arguments) argumentText.AppendLine(Client.ArgFileQuote(argument));
            File.WriteAllText(argFile, argumentText.ToString(), Client.Utf8);
            string logPath = Client.LocalPath("logs/game-" + suffix + ".log");
            log = new StreamWriter(logPath, false, Client.Utf8) { AutoFlush = true };
            LogLine("LARP Client / Minecraft " + config.version + " / " + DateTime.Now.ToString("s"));
            game = new Process();
            game.StartInfo = new ProcessStartInfo(Client.LocalPath(config.java), "@\"" + argFile + "\"")
            {
                WorkingDirectory = Client.LocalPath("game"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string variable in new[] { "_JAVA_OPTIONS", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH" })
                game.StartInfo.EnvironmentVariables.Remove(variable);
            game.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) LogLine(e.Data); };
            game.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) LogLine(e.Data); };
            if (!game.Start()) throw new InvalidOperationException("Не удалось запустить встроенную Java.");
            waitingForExit = true;
            game.BeginOutputReadLine();
            game.BeginErrorReadLine();
            Client.WriteJson("logs/last-launch.json", new { pid = game.Id, javaPID = game.Id, launcherPID = Process.GetCurrentProcess().Id, version = config.version, username = name, memoryMb = memoryMb, log = logPath, startedAt = DateTime.UtcNow.ToString("o") });
            start.Enabled = false;
            username.Enabled = false;
            memory.Enabled = false;
            status.Text = "Игра запущена. Ожидаем закрытия Minecraft…";
            WindowState = FormWindowState.Minimized;
            ThreadPool.QueueUserWorkItem(delegate
            {
                game.WaitForExit();
                int exit = game.ExitCode;
                LogLine("Minecraft завершён. Код: " + exit.ToString(CultureInfo.InvariantCulture));
                lock (logLock) { log.Dispose(); log = null; }
                try
                {
                    BeginInvoke((Action)delegate { GameExited(exit, logPath); });
                }
                catch (InvalidOperationException) { }
            });
        }
        catch (Exception error)
        {
            ResultCode = 1;
            ReleaseLaunchMutex();
            lock (logLock) { if (log != null) { log.WriteLine(error.ToString()); log.Dispose(); log = null; } }
            status.Text = error.Message;
            try { Client.WriteJson("logs/launcher-error.json", new { error = error.Message, at = DateTime.UtcNow.ToString("o") }); }
            catch { }
            if (autoName != null) { waitingForExit = false; Close(); }
            else MessageBox.Show(this, error.Message, "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LogLine(string message)
    {
        lock (logLock) { if (log != null) log.WriteLine(message); }
    }

    private void GameExited(int exitCode, string logPath)
    {
        waitingForExit = false;
        ReleaseLaunchMutex();
        ResultCode = exitCode == 0 ? 0 : 1;
        try { Client.WriteJson("logs/last-exit.json", new { exitCode = exitCode, log = logPath, exitedAt = DateTime.UtcNow.ToString("o") }); }
        catch { }
        game.Dispose();
        game = null;
        start.Enabled = true;
        username.Enabled = true;
        memory.Enabled = true;
        status.Text = exitCode == 0 ? "Игра закрыта. Можно запустить снова." : "Игра завершилась с ошибкой. Журнал: " + Path.GetFileName(logPath);
        if (autoName != null || hiddenWhilePlaying) { Close(); return; }
        WindowState = FormWindowState.Normal;
        Activate();
        if (exitCode != 0) MessageBox.Show(this, "Minecraft завершился с кодом " + exitCode.ToString(CultureInfo.InvariantCulture) + ".\n\nЖурнал:\n" + logPath, "Ошибка Minecraft", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void ReleaseLaunchMutex()
    {
        if (launchMutex == null) return;
        if (mutexHeld) launchMutex.ReleaseMutex();
        launchMutex.Dispose();
        launchMutex = null;
        mutexHeld = false;
    }
}

internal static class Brand
{
    internal static readonly Color Background = Color.FromArgb(15, 18, 23);
    internal static readonly Color Surface = Color.FromArgb(25, 30, 37);
    internal static readonly Color Muted = Color.FromArgb(145, 156, 170);
    internal static readonly Color Accent = Color.FromArgb(145, 237, 194);
    internal static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath(); float d = radius * 2;
        p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
    internal static void Logo(Graphics g, RectangleF bounds, Color color)
    {
        float[][] data = {
            new float[]{768,95,646,307,709,307,767,207,951,525,984,471},
            new float[]{381,319,499,524,531,470,473,375,843,375,812,320},
            new float[]{381,754,620,754,589,700,476,700,662,391,599,391},
            new float[]{553,607,767,967,887,770,824,770,766,862,587,550},
            new float[]{693,699,724,754,1150,754,1037,557,1003,610,1056,699},
            new float[]{918,319,949,375,1065,375,876,681,940,681,1158,319}
        };
        float scale = Math.Min(bounds.Width/777f,bounds.Height/872f);
        float x = bounds.X+(bounds.Width-777*scale)/2, y = bounds.Y+(bounds.Height-872*scale)/2;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var brush = new SolidBrush(color)) foreach(var a in data) {
            PointF[] points = new PointF[a.Length/2];
            for(int i=0;i<points.Length;i++) points[i]=new PointF(x+(a[i*2]-381)*scale,y+(a[i*2+1]-95)*scale);
            g.FillPolygon(brush,points);
        }
    }
    internal static Bitmap Image(int width, int height, bool solid)
    {
        var b = new Bitmap(width,height); using(var g=Graphics.FromImage(b)) {
            g.Clear(solid ? Color.FromArgb(10,12,16) : Color.Transparent);
            float pad=Math.Min(width,height)*0.12f;
            Logo(g,new RectangleF(pad,pad,width-pad*2,height-pad*2),Color.White);
        } return b;
    }
    internal static void ExportAssets(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach(int size in new[]{16,24,32,48,64,128,256,512}) using(var b=Image(size,size,false)) b.Save(Path.Combine(directory,"logo-"+size+".png"),System.Drawing.Imaging.ImageFormat.Png);
        using(var stream=new FileStream(Path.Combine(directory,"larp.ico"),FileMode.Create)) using(var writer=new BinaryWriter(stream)) {
            int[] sizes={16,24,32,48,64,128,256}; var images=new List<byte[]>();
            foreach(int size in sizes) using(var b=Image(size,size,true)) using(var m=new MemoryStream()) {b.Save(m,System.Drawing.Imaging.ImageFormat.Png);images.Add(m.ToArray());}
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
            foreach(var bytes in images) writer.Write(bytes);
        }
    }
}
internal sealed class BrandView : Control
{
    internal BrandView(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Brand.Background;}
    protected override void OnPaint(PaintEventArgs e){Brand.Logo(e.Graphics,ClientRectangle,Color.White);}
}
internal sealed class ModernButton : Button
{
    internal bool Primary; private bool hovered;
    internal ModernButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color fill=Primary?(hovered?Color.FromArgb(177,250,217):Brand.Accent):(hovered?Color.FromArgb(42,49,59):Brand.Surface);
        if(!Enabled) fill=Color.FromArgb(45,58,54);
        using(var p=Brand.Round(new RectangleF(1,1,Width-3,Height-3),12)) using(var b=new SolidBrush(fill)) e.Graphics.FillPath(b,p);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?(Primary?Color.FromArgb(14,31,24):Color.White):Brand.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        if(Focused) using(var p=Brand.Round(new RectangleF(5,5,Width-11,Height-11),9)) using(var pen=new Pen(Primary?Color.FromArgb(30,88,64):Brand.Accent)) {pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;e.Graphics.DrawPath(pen,p);}
    }
}
internal sealed class FieldPanel : Panel
{
    private Control input;
    internal FieldPanel(Control child){input=child;BackColor=Brand.Background;Controls.Add(child);child.Enter+=delegate{Invalidate();};child.Leave+=delegate{Invalidate();};SetStyle(ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var p=Brand.Round(new RectangleF(1,1,Width-3,Height-3),10)){using(var b=new SolidBrush(Brand.Surface))e.Graphics.FillPath(b,p);using(var pen=new Pen(input.ContainsFocus?Brand.Accent:Color.FromArgb(49,58,70)))e.Graphics.DrawPath(pen,p);}}
}
