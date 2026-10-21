using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgoAnchor
{
    /// <summary>
    /// 一块屏幕对应一个覆盖窗口。
    /// 纯 DWM 合成层：不注入、不读写游戏、不挂钩子、不模拟输入。
    /// 正常模式下为点击穿透的全屏透明层；OBS 模式下取消穿透并成为可被
    /// 「窗口采集」单独列出的普通顶层窗口。
    /// </summary>
    public sealed class OverlayWindow : Form
    {
        // ---------------- Win32 ----------------
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        private const int GWL_EXSTYLE = -20;

        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_DPICHANGED = 0x02E0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetExStyle(IntPtr h)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(h, GWL_EXSTYLE) : GetWindowLong32(h, GWL_EXSTYLE);
        }

        private static void SetExStyle(IntPtr h, IntPtr style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, style);
            else SetWindowLong32(h, GWL_EXSTYLE, style);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // ---------------- 状态 ----------------
        // screen 不能是 readonly：分辨率/DPI 变化后需要换用最新的 Screen 实例。
        private Screen screen;
        private Config config;
        private bool visible;
        private double dpiScale = 1.0;
        private string deviceShort = "1";

        public string DeviceName { get { return screen.DeviceName; } }
        public double DpiScale { get { return dpiScale; } }

        /// <summary>
        /// 换用最新的 Screen 实例（屏幕分辨率、位置或 DPI 变化后调用）。
        /// 只改引用并重算 DPI，真正的重绘交给下次 Refresh_Overlay。
        /// </summary>
        public void ReattachScreen(Screen latest)
        {
            if (latest == null) return;
            if (latest.DeviceName != screen.DeviceName) return;

            bool changed = latest.Bounds != screen.Bounds;
            screen = latest;
            dpiScale = DpiHelper.GetScaleForScreen(screen);

            if (changed)
            {
                ApplyScreenBounds();
                Log.Info("屏幕几何变化 " + DeviceName + " -> " + latest.Bounds);
            }
        }

        public OverlayWindow(Screen s, Config cfg)
        {
            screen = s;
            config = cfg;
            dpiScale = DpiHelper.GetScaleForScreen(s);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            MinimizeBox = false;
            MaximizeBox = false;

            // 窗口标题必须**唯一且带 Overlay 字样**：OBS 的「窗口采集」就是靠
            // [进程名.exe]: 窗口标题 来区分的。之前标题叫 AntiNauseaOverlay（旧名），
            // 与设置窗口的进程名撞车，OBS 下拉框里只会列出设置窗口，覆盖层根本选不到。
            deviceShort = ShortDeviceName(s.DeviceName);
            Text = cfg.ObsCaptureTitle + " (" + deviceShort + ")";
            BackColor = Color.Black;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            ResizeRedraw = false;

            ApplyScreenBounds();
        }

        /// <summary>把 "\\.\DISPLAY1" 这类设备名压成 "DISPLAY1"，用于窗口标题区分多屏。</summary>
        private static string ShortDeviceName(string device)
        {
            if (string.IsNullOrEmpty(device)) return "1";
            int i = device.LastIndexOf('\\');
            return i >= 0 && i + 1 < device.Length ? device.Substring(i + 1) : device;
        }

        /// <summary>
        /// 覆盖范围取该屏完整边界（含任务栏区域）。
        /// 进程已声明 PerMonitorV2，因此 Screen.Bounds 就是物理像素，与 UpdateLayeredWindow 一致。
        /// </summary>
        private void ApplyScreenBounds()
        {
            var b = screen.Bounds;
            Bounds = new Rectangle(b.X, b.Y, b.Width, b.Height);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        /// <summary>强制不出现在任务栏（即便没有 WS_EX_TOOLWINDOW）。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ShowInTaskbar = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;

                // 与 Beam Eye Tracker 实机取证结果对齐（它的 exstyle = 0x8080028）：
                //   LAYERED + TRANSPARENT + TOPMOST + NOACTIVATE
                //
                // **刻意不加 WS_EX_TOOLWINDOW**：OBS 的窗口采集枚举窗口时会跳过工具窗口，
                // 加了之后覆盖层根本不会出现在 OBS 的「窗口」下拉框里（这就是之前只看到
                // 设置窗口、选不到 Overlay 的根因）。
                //
                // 用 UpdateLayeredWindow 做逐像素 alpha —— Beam 的 GetLayeredWindowAttributes
                // 返回 False，说明它也是这么做的，而且已验证能被 WGC 采集。
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT
                            | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyStyles();
        }

        /// <summary>
        /// 同步窗口扩展样式。两种模式样式完全相同（见 CreateParams 的说明），
        /// 保留这个方法是为了在句柄重建/切换屏幕后重新巩固样式。
        /// </summary>
        public void ApplyStyles()
        {
            if (!IsHandleCreated) return;
            IntPtr style = GetExStyle(Handle);
            long v = style.ToInt64();

            v |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            // 明确清掉 TOOLWINDOW（OBS 枚举时会跳过它）
            v &= ~(long)WS_EX_TOOLWINDOW;

            SetExStyle(Handle, new IntPtr(v));
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : 0));
        }

        /// <summary>重新渲染并推送到屏幕。静止时不会重复调用，因此空闲 CPU 为 0。</summary>
        public void Refresh_Overlay(Config cfg, bool show)
        {
            config = cfg;
            visible = show;

            var b = screen.Bounds;
            int w = b.Width, h = b.Height;
            bool sizeChanged = Width != w || Height != h || Left != b.X || Top != b.Y;
            if (sizeChanged) ApplyScreenBounds();

            if (!show)
            {
                if (IsHandleCreated && Visible) Hide();
                return;
            }

            // 关键：窗体句柄只有在窗口被创建后才存在，而覆盖层窗口从不真正显示，
            // 因此必须显式强制创建句柄，否则 UpdateLayeredWindow 无处可推。
            if (!IsHandleCreated)
            {
                var _ = Handle;
                Diagnostics.Line("  [hdl] " + DeviceName + " 强制创建句柄 → " + Handle);
            }

            if (!IsHandleCreated)
            {
                Log.Warn("句柄创建失败：" + DeviceName);
                return;
            }

            // 每屏按自己的 DPI 换算像素参数，保证 100% 屏与 150% 屏观感一致
            dpiScale = DpiHelper.GetScaleForScreen(screen);
            Profile scaled = DpiHelper.ForScreen(cfg.P, dpiScale, cfg.ScaleWithDpi);

            Bitmap bmp = Renderer.Render(w, h, scaled, cfg.P.GlobalOpacityPercent);

            // 转为 32bpp 预乘 ARGB，UpdateLayeredWindow 要求预乘
            Bitmap premul = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(premul))
                g.DrawImageUnscaled(bmp, 0, 0);

            PushToScreen(premul);

            premul.Dispose();
            bmp.Dispose();

            if (!Visible)
            {
                Show();
                Diagnostics.Line("  [show] " + DeviceName + " Visible=" + Visible);
            }
            ApplyStyles();
            long ex = GetExStyle(Handle).ToInt64();
            Diagnostics.Line("  [state] " + DeviceName + " Visible=" + Visible +
                             " WS_EX=0x" + ex.ToString("X") +
                             " LAYERED=" + ((ex & WS_EX_LAYERED) != 0) +
                             " TRANSPARENT=" + ((ex & WS_EX_TRANSPARENT) != 0) +
                             " Bounds=" + Bounds);
        }

        private void PushToScreen(Bitmap bmp)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                screenDc = GetDC(IntPtr.Zero);
                memDc = CreateCompatibleDC(screenDc);
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var srcPoint = new POINT { X = 0, Y = 0 };
                var topPos = new POINT { X = screen.Bounds.X, Y = screen.Bounds.Y };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                bool ok = UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size,
                    memDc, ref srcPoint, 0, ref blend, ULW_ALPHA);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    Log.Warn("UpdateLayeredWindow 失败：" + err);
                    if (Diagnostics.Enabled)
                        Console.WriteLine("  [push] " + DeviceName + " FAILED err=" + err);
                }
                else if (Diagnostics.Enabled)
                {
                    Console.WriteLine("  [push] " + DeviceName + " OK " + bmp.Width + "x" + bmp.Height +
                                      " at " + topPos.X + "," + topPos.Y +
                                      " dpi=" + Math.Round(dpiScale * 100) + "% visible=" + Visible);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("推送图层异常：" + ex.Message);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        // 保证任何情况下都不抢焦点、不吃鼠标
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCHITTEST:
                    // 两种模式都保持点击穿透：
                    //  · 正常模式靠 WS_EX_TRANSPARENT
                    //  · OBS 模式没有该位，但 WM_NCHITTEST 返回 HTTRANSPARENT 同样可穿透，
                    //    这样 OBS 模式下也不会挡住底下的游戏/桌面操作。
                    m.Result = new IntPtr(HTTRANSPARENT);
                    return;
                case WM_MOUSEACTIVATE:
                    m.Result = new IntPtr(MA_NOACTIVATE);
                    return;
                case WM_DISPLAYCHANGE:
                case WM_DPICHANGED:
                    // 分辨率 / 显示器 / DPI 变化后重新取 DPI 并整屏重绘
                    dpiScale = DpiHelper.GetScaleForScreen(screen);
                    Refresh_Overlay(config, visible);
                    break;
            }
            base.WndProc(ref m);
        }

        // 全部绘制由 UpdateLayeredWindow 接管（逐像素 alpha），GDI 的 OnPaint 不需要参与。
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }
}

