using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgoAnchor
{
    public static class Program
    {
        private const string MutexName = @"Local\AgoAnchor.SingleInstance";
        private const string EventName = @"Local\AgoAnchor.ShowSettings";

        private static Mutex singleInstanceMutex;
        internal static MainContext Context;

        [STAThread]
        public static int Main(string[] args)
        {
            // ---- 单实例检查（必须最先做）----
            // 放在所有初始化和日志之前：被拒绝的实例要立刻退出，
            // 不能先声明 DPI、写日志甚至建窗口再退出，否则连点会看到窗口闪一下。
            bool isDiagnostic = HasFlag(args, "--selftest")
                || HasFlag(args, "--savetest")
                || HasFlag(args, "--resizecheck")
                || !string.IsNullOrEmpty(GetFlagValue(args, "--gallery"))
                || !string.IsNullOrEmpty(GetFlagValue(args, "--headless"))
                || !string.IsNullOrEmpty(GetFlagValue(args, "--trayshot"))
                || !string.IsNullOrEmpty(GetFlagValue(args, "--uishot"))
                || !string.IsNullOrEmpty(GetFlagValue(args, "--uishot-live"));

            if (!isDiagnostic)
            {
                bool firstInstance;
                try
                {
                    singleInstanceMutex = new Mutex(true, MutexName, out firstInstance);
                }
                catch
                {
                    // 互斥体不可用时宁可启动，也不要静默失败
                    firstInstance = true;
                }

                if (!firstInstance)
                {
                    // 已有实例在跑：通知它把设置面板唤到前台，然后立刻退出。
                    // 此处不初始化 DPI、不写日志、不创建任何窗口。
                    SignalExisting();
                    return 0;
                }
            }

            // ---- 诊断模式（仅在控制台宿主下有意义） ----
            if (HasFlag(args, "--selftest")) return SelfTest.Run();

            string gallery = GetFlagValue(args, "--gallery");
            if (!string.IsNullOrEmpty(gallery)) return Gallery.Dump(gallery);

            string headless = GetFlagValue(args, "--headless");
            if (!string.IsNullOrEmpty(headless))
            {
                AttachConsoleIfPossible();
                return Headless.Run(int.Parse(headless));
            }

            if (HasFlag(args, "--savetest"))
            {
                AttachConsoleIfPossible();
                return SaveTest.Run();
            }

            // ---- 正常启动 ----
            // 单实例已在上方 Main 开头处理完毕，这里一定是"第一个实例"。
            // DPI 感知必须在创建任何窗口之前声明
            DpiHelper.EnablePerMonitorV2();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Warn("UI thread exception: " + e.Exception);

            bool startHidden = HasFlag(args, "--autostart");

            int exitCode = 0;
            try
            {
                Context = new MainContext(startHidden);
                Application.Run(Context);
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Log.Warn("fatal: " + ex);
                MessageBox.Show(
                    FatalTitle() + ex.Message +
                    "\r\n\r\n" + Paths.LogFile,
                    "AgoAnchor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try { if (singleInstanceMutex != null) singleInstanceMutex.ReleaseMutex(); }
                catch { }
                try { if (singleInstanceMutex != null) singleInstanceMutex.Dispose(); }
                catch { }
            }
            return exitCode;
        }

        /// <summary>诊断模式下若宿主不是控制台，则尝试连上父进程控制台以输出文字。</summary>
        private static void AttachConsoleIfPossible()
        {
            try
            {
                if (GetConsoleWindow() == IntPtr.Zero)
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                }
            }
            catch { }
        }

        private const int ATTACH_PARENT_PROCESS = -1;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>致命错误的标题（跟随当前界面语言）。</summary>
        private static string FatalTitle()
        {
            switch (L.Current)
            {
                case Lang.English: return "Failed to start:\r\n";
                case Lang.Japanese: return "起動に失敗しました：\r\n";
                case Lang.Korean: return "시작하지 못했습니다:\r\n";
                default: return "程序启动失败：\r\n";
            }
        }

        /// <summary>转发到 MainContext.BuildIcon，供设置窗口等外部调用。</summary>
        internal static Icon BuildIcon()
        {
            return MainContext.BuildIcon();
        }

        private static string GetFlagValue(string[] args, string flag)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1];
            return null;
        }

        private static void SignalExisting()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(EventName))
                    ev.Set();
            }
            catch { }
        }

        internal static void HandleCloseRequest()
        {
            if (Context != null) Context.HandleCloseRequest();
        }

        internal static void RequestExit()
        {
            if (Context != null) Context.ExitApplication();
        }
    }

    /// <summary>程序主体：配置、覆盖层引擎、托盘、设置窗口、关闭策略。</summary>
    public sealed class MainContext : ApplicationContext
    {
        private readonly Config config;
        private readonly OverlayEngine engine;
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private SettingsForm settings;
        private readonly bool startHidden;
        private bool exiting;

        private readonly ToolStripMenuItem menuToggle;
        private readonly ToolStripMenuItem menuSettings;
        private readonly ToolStripMenuItem menuHide;
        private readonly ToolStripMenuItem menuExit;

        public MainContext(bool hidden)
        {
            startHidden = hidden;
            config = Config.Load();
            L.Current = config.Language;

            if (config.TargetScreens.Count == 0)
                foreach (var s in Screen.AllScreens) config.TargetScreens.Add(s.DeviceName);

            engine = new OverlayEngine();
            engine.Apply(config);
            engine.SetRunning(true);

            menuToggle = new ToolStripMenuItem(L.T("tray.toggle"), null, (s, e) => TogglePause());
            menuSettings = new ToolStripMenuItem(L.T("tray.settings"), null, (s, e) => ShowSettings());
            menuHide = new ToolStripMenuItem(L.T("tray.hide"), null, (s, e) => HideSettings());
            menuExit = new ToolStripMenuItem(L.T("tray.exit"), null, (s, e) => ExitApplication());

            var menu = new ContextMenuStrip();
            menu.Items.Add(menuToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(menuSettings);
            menu.Items.Add(menuHide);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(menuExit);

            trayIcon = BuildIcon();
            tray = new NotifyIcon
            {
                Icon = trayIcon,
                Text = L.T("tray.tip"),
                Visible = true,
                ContextMenuStrip = menu
            };

            // 托盘注册诊断：把每一步结果写进日志，用于定位"图标不出现"。
            // 关键：NotifyIcon 依赖一个隐藏的宿主窗口，在 Application.Run 之前创建时
            // 消息循环尚未启动，某些环境下 shell 通知会失败，因此这里记录并延后重试。
            Log.Info(string.Format("tray: icon={0} text={1}len visible={2} dataDir={3}",
                trayIcon != null, L.T("tray.tip").Length, tray.Visible, Paths.CurrentDir));
            Diagnostics.Line("[tray] Visible=" + tray.Visible + " iconHandle=" +
                (trayIcon != null ? trayIcon.Handle.ToString() : "null") + " textLen=" + L.T("tray.tip").Length);

            // 单击/双击都弹出设置。注意：一旦设置了 ContextMenuStrip，
            // 右键由它接管；左键的 MouseClick 在部分系统上会被吞掉，
            // 因此这里用 MouseUp 兜底，并且不分左右键都唤出设置。
            tray.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) return;   // 右键交给菜单
                ShowSettings();
            };
            tray.BalloonTipClicked += (s, e) => ShowSettings();

            // 关键兜底：托盘图标在某些系统上会被折叠进"隐藏的图标"区，用户找不到。
            // 这里暴露一个全局错误对话框钩子之外的最小捷径——主窗体若不可见，
            // 用户从开始菜单再次启动会走单实例逻辑唤起设置（见 SignalExisting）。

            // 监听二次启动
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program_EventName);
            ThreadPool.RegisterWaitForSingleObject(ev,
                (state, timedOut) => ShowSettingsSafe(), null, Timeout.Infinite, false);

            // 首次运行弹一次托盘提示，帮助用户找到图标
            try
            {
                string marker = Path.Combine(Paths.AppDataDir, ".tray_notified");
                if (!File.Exists(marker))
                {
                    Directory.CreateDirectory(Paths.AppDataDir);
                    File.WriteAllText(marker, DateTime.Now.ToString("o"));
                    Notify(L.T("tray.balloon.tray"));
                }
            }
            catch { }

            if (!startHidden)
            {
                // 延后一帧再显示，确保托盘先注册完成
                var timer = new System.Windows.Forms.Timer { Interval = 60 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    ShowSettings();
                };
                timer.Start();
            }

            // 托盘注册必须**校验成功**，不能设完 Visible=true 就当作成功。
            //
            // 实测教训：Shell_NotifyIcon(NIM_ADD) 会返回 False（托盘图标根本不出现），
            // 但 WinForms 的 NotifyIcon 不检查这个返回值，于是"看起来注册了、实际没有"。
            // 常见诱因是程序启动时 explorer 的托盘窗口尚未就绪。
            //
            // 因此这里起一个定时器，每 500ms 用 Shell_NotifyIconGetRect 探测一次，
            // 确认真的拿到图标矩形才停手；最多重试 60 次（30 秒）。
            trayRetry = new System.Windows.Forms.Timer { Interval = 500 };
            trayRetry.Tick += (s, e) =>
            {
                trayRetryTries++;

                if (TrayIconAlive())
                {
                    trayRetry.Stop();
                    trayRetry.Dispose();
                    trayRetry = null;
                    Log.Info("tray: 已确认注册成功（第 " + trayRetryTries + " 次检查）");
                    return;
                }

                if (trayRetryTries > 60)
                {
                    trayRetry.Stop();
                    trayRetry.Dispose();
                    trayRetry = null;
                    Log.Warn("tray: 重试 60 次仍未注册成功，可能是 explorer/任务栏异常");
                    return;
                }

                // 重新注册：先隐藏再显示，强制 shell 重建图标
                try
                {
                    tray.Visible = false;
                    tray.Visible = true;
                    Log.Info("tray: 第 " + trayRetryTries + " 次重试注册");
                }
                catch (Exception ex) { Log.Warn("tray retry failed: " + ex.Message); }
            };
            trayRetry.Start();

            Log.Info("started. screens=" + Screen.AllScreens.Length + " hidden=" + startHidden +
                     " explorer=" + CountExplorer());
        }

        private System.Windows.Forms.Timer trayRetry;
        private int trayRetryTries;

        private static int CountExplorer()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("explorer").Length; }
            catch { return -1; }
        }

        /// <summary>用 Shell_NotifyIconGetRect 判断托盘图标是否真的存在。</summary>
        private bool TrayIconAlive()
        {
            try
            {
                IntPtr hwnd = TrayHostHwnd();
                if (hwnd == IntPtr.Zero) return false;

                for (uint uid = 0; uid < 8; uid++)
                {
                    var id = new NOTIFYICONIDENTIFIER
                    {
                        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)),
                        hWnd = hwnd,
                        uID = uid
                    };
                    RECT r;
                    if (Shell_NotifyIconGetRect(ref id, out r) == 0) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>取 NotifyIcon 内部隐藏宿主窗口的句柄。</summary>
        private IntPtr TrayHostHwnd()
        {
            try
            {
                var f = typeof(NotifyIcon).GetField("window",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null)
                {
                    var w = f.GetValue(tray) as NativeWindow;
                    if (w != null) return w.Handle;
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct NOTIFYICONIDENTIFIER
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);


        internal const string Program_EventName = @"Local\AgoAnchor.ShowSettings";

        /// <summary>
        /// 弹出托盘气泡提示。部分系统/专注助手下气泡会被吞掉，
        /// 因此这里同时把托盘 ToolTip 文本更新为提示内容，确保用户有可见反馈。
        /// </summary>
        private void Notify(string message)
        {
            try
            {
                if (tray == null) return;
                // 托盘气泡对文本长度敏感，截断到 60 字符避免被系统忽略
                string t = message == null ? "" : message;
                if (t.Length > 60) t = t.Substring(0, 60);
                tray.ShowBalloonTip(2500, L.T("tray.balloon.title"), t, ToolTipIcon.Info);
            }
            catch { }
        }

        // ------------------------------------------------------------ 设置窗
        private void ShowSettingsSafe()
        {
            try
            {
                if (settings != null && !settings.IsDisposed && settings.InvokeRequired)
                    settings.BeginInvoke(new Action(ShowSettings));
                else
                    ShowSettings();
            }
            catch { }
        }

        public void ShowSettings()
        {
            try
            {
                if (settings != null && !settings.IsDisposed)
                {
                    if (!settings.Visible) settings.Show();
                    if (settings.WindowState == FormWindowState.Minimized)
                        settings.WindowState = FormWindowState.Normal;

                    EnsureOnScreen(settings);

                    settings.BringToFront();
                    settings.Activate();
                    settings.Invalidate(true);
                    return;
                }

                settings = new SettingsForm(config, ApplyChanged, ForceRebuildOverlays, TogglePause);
            SyncPauseToSettings();
                EnsureOnScreen(settings);
                settings.Show();
                settings.Activate();
            }
            catch (Exception ex)
            {
                Log.Warn("ShowSettings failed: " + ex);
            }
        }

        /// <summary>
        /// 保证窗口落在某块可见屏幕内。窗口曾在某屏上的坐标被保存，
        /// 换了显示器后可能整体落在屏幕外，表现为"点了没反应"。
        /// </summary>
        private static void EnsureOnScreen(Form f)
        {
            Rectangle b = f.Bounds;
            bool visibleAnywhere = false;
            foreach (var s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(b)) { visibleAnywhere = true; break; }
            }
            if (visibleAnywhere) return;

            var host = Screen.PrimaryScreen.WorkingArea;
            int x = host.X + (host.Width - f.Width) / 2;
            int y = host.Y + (host.Height - f.Height) / 2;
            f.Location = new Point(Math.Max(host.X, x), Math.Max(host.Y, y));
        }

        private void ApplyChanged(Config cfg)
        {
            try
            {
                engine.Apply(cfg);
                cfg.Save();
                RefreshTrayText();
            }
            catch (Exception ex) { Log.Warn("ApplyChanged failed: " + ex.Message); }
        }

        /// <summary>
        /// 分辨率 / DPI 变化后由设置面板的「刷新」按钮调用：
        /// 丢弃全部覆盖窗口，按最新的屏幕拓扑重新创建并绘制。
        /// 只调 Apply() 是不够的 —— 已存在窗口的尺寸与位置都还是旧的。
        /// </summary>
        private void ForceRebuildOverlays()
        {
            try
            {
                Log.Info("ForceRebuild overlays: " + Screen.AllScreens.Length + " 屏");
                engine.ForceRebuild();
                RefreshTrayText();
            }
            catch (Exception ex) { Log.Warn("ForceRebuild failed: " + ex.Message); }
        }

        // ------------------------------------------------------------ 托盘动作
        /// <summary>
        /// 切换暂停/继续。托盘菜单和设置面板里的「暂停」按钮都走这里，
        /// 两个入口共享同一状态。
        /// </summary>
        private void TogglePause()
        {
            bool running = !engine.IsRunning;
            engine.SetRunning(running);
            RefreshTrayText();
            SyncPauseToSettings();
            Notify(L.T(running ? "tray.balloon.resumed" : "tray.balloon.paused"));
        }

        /// <summary>把引擎的暂停状态回写到设置面板的按钮上。</summary>
        private void SyncPauseToSettings()
        {
            try
            {
                if (settings != null && !settings.IsDisposed)
                    settings.SetPaused(!engine.IsRunning);
            }
            catch { }
        }

        private void RefreshTrayText()
        {
            menuToggle.Text = L.T(engine.IsRunning ? "tray.toggle" : "tray.toggle.resume");
            menuSettings.Text = L.T("tray.settings");
            menuHide.Text = L.T("tray.hide");
            menuExit.Text = L.T("tray.exit");
            // NotifyIcon.Text 上限 63 字符，超出会被系统丢弃导致图标"消失"
            string t = L.T("tray.tip") + (engine.IsRunning ? "" : L.T("tray.paused"));
            if (t.Length > 63) t = t.Substring(0, 63);
            tray.Text = t;
        }

        /// <summary>隐藏设置面板（等价于"最小化到托盘"）。覆盖层不受影响。</summary>
        private void HideSettings()
        {
            try
            {
                if (settings != null && !settings.IsDisposed && settings.Visible)
                    settings.Hide();

                Notify(L.T("tray.balloon.tray"));
            }
            catch (Exception ex) { Log.Warn("HideSettings failed: " + ex.Message); }
        }

        // ------------------------------------------------------------ 关闭策略
        public void HandleCloseRequest()
        {
            switch (config.OnClose)
            {
                case CloseAction.ToTray:
                    HideSettings();
                    break;
                case CloseAction.Exit:
                    ExitApplication();
                    break;
                default:
                    AskClose();
                    break;
            }
        }

        private void AskClose()
        {
            using (var dlg = new CloseChoiceForm())
            {
                if (dlg.ShowDialog() != DialogResult.OK || dlg.Choice == null) return;

                if (dlg.Remember)
                {
                    config.OnClose = dlg.Choice.Value;
                    config.Save();
                }

                if (dlg.Choice.Value == CloseAction.Exit) ExitApplication();
                else HideSettings();
            }
        }

        public void ExitApplication()
        {
            if (exiting) return;
            exiting = true;
            try
            {
                config.Save();
                engine.Dispose();
                if (settings != null && !settings.IsDisposed) settings.CloseForReal();
                tray.Visible = false;
                tray.Dispose();
            }
            catch (Exception ex) { Log.Warn("exit failed: " + ex.Message); }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (engine != null) engine.Dispose(); } catch { }
                try { if (tray != null) tray.Dispose(); } catch { }
                try { if (trayIcon != null) trayIcon.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------ 托盘图标
        /// <summary>
        /// 取托盘/窗口图标。
        ///
        /// 优先级：
        ///   1. **exe 自身内嵌的图标资源**（由 /win32icon 写入）—— 这样 exe 可以单独分发，
        ///      旁边不需要放任何 .ico 文件。
        ///   2. 运行时绘制的兜底图标（理论上不会走到）。
        ///
        /// 注意：返回的 Icon 是独立对象（通过 Icon 构造器复制），
        /// 不能直接把 Bitmap.GetHicon() 的句柄交给 NotifyIcon —— 源位图释放后
        /// GDI 句柄失效，系统会把图标画成空白。
        /// </summary>
        internal static Icon BuildIcon()
        {
            // 1) 从 exe 内嵌资源提取
            try
            {
                var extracted = Icon.ExtractAssociatedIcon(Paths.ExePath);
                if (extracted != null)
                {
                    var copy = new Icon(extracted, extracted.Width, extracted.Height);
                    extracted.Dispose();
                    return copy;
                }
            }
            catch (Exception ex) { Log.Warn("extract embedded icon failed: " + ex.Message); }

            // 2) 兜底：运行时绘制
            try
            {
                using (var bmp = RenderTrayBitmap(32))
                {
                    IntPtr h = bmp.GetHicon();
                    try
                    {
                        using (var tmp = Icon.FromHandle(h))
                            return new Icon(tmp, 32, 32);
                    }
                    finally { DestroyIcon(h); }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("BuildIcon fallback failed: " + ex.Message);
                return SystemIcons.Application;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static Bitmap RenderTrayBitmap(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float s = size / 32f;
                var cyan = Color.FromArgb(255, 168, 230, 255);

                using (var br = new SolidBrush(cyan))
                {
                    // 四个角点 + 四条边中点，示意"点阵框"
                    for (int i = 0; i < 5; i++)
                    {
                        float p = (7 + i * 4) * s;
                        float r = 1.6f * s;
                        g.FillEllipse(br, p - r, 6 * s - r, r * 2, r * 2);
                        g.FillEllipse(br, p - r, 24 * s - r, r * 2, r * 2);
                        g.FillEllipse(br, 6 * s - r, p - r, r * 2, r * 2);
                        g.FillEllipse(br, 24 * s - r, p - r, r * 2, r * 2);
                    }
                    // 中心点
                    g.FillEllipse(br, 14 * s, 14 * s, 4 * s, 4 * s);
                }
            }
            return bmp;
        }
    }

    /// <summary>关闭时的三选一对话框。</summary>
    public sealed class CloseChoiceForm : Form
    {
        public CloseAction? Choice { get; private set; }
        public bool Remember { get { return chkRemember.Checked; } }
        private readonly CheckBox chkRemember;

        public CloseChoiceForm()
        {
            Text = L.T("close.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 196);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(30, 32, 38);
            ForeColor = Color.FromArgb(232, 235, 242);

            var lbl = new Label
            {
                Text = L.T("close.text"),
                Left = 18,
                Top = 18,
                Width = 364,
                Height = 26,
                ForeColor = Color.FromArgb(210, 216, 228)
            };
            Controls.Add(lbl);

            var btnTray = Btn(L.T("close.tray"), 18, 60, 150);
            btnTray.Click += (s, e) => { Choice = CloseAction.ToTray; DialogResult = DialogResult.OK; };
            Controls.Add(btnTray);

            var btnExit = Btn(L.T("close.exit"), 176, 60, 110);
            btnExit.Click += (s, e) => { Choice = CloseAction.Exit; DialogResult = DialogResult.OK; };
            Controls.Add(btnExit);

            var btnCancel = Btn(L.T("close.cancel"), 294, 60, 88);
            btnCancel.Click += (s, e) => { Choice = null; DialogResult = DialogResult.Cancel; };
            Controls.Add(btnCancel);

            chkRemember = new CheckBox
            {
                Text = L.T("close.remember"),
                Left = 20,
                Top = 108,
                Width = 240,
                Height = 24,
                ForeColor = Color.FromArgb(180, 188, 200)
            };
            Controls.Add(chkRemember);

            CancelButton = btnCancel;
        }

        private Button Btn(string text, int x, int y, int w)
        {
            var b = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = w,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 66),
                ForeColor = Color.FromArgb(232, 235, 242)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(84, 92, 106);
            return b;
        }
    }
}









