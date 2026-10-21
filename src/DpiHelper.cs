using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgoAnchor
{
    /// <summary>
    /// 高 DPI 支持。
    ///
    /// 关键事实：WinForms 在 .NET Framework 下最多只能设置「系统级 DPI 感知」。
    /// 若不声明感知，Windows 会把整个进程位图拉伸，导致 125%/150% 下点阵发虚且整体偏移。
    /// 解决办法：
    ///   1. 进程启动前（Main 最早期）创建 DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2，
    ///      这样窗口坐标与物理像素一一对应，UpdateLayeredWindow 不会被二次缩放；
    ///   2. 逐屏用 GetDpiForMonitor 取各屏真实 DPI，渲染时按各自比例缩放"像素类"参数，
    ///      这样 100% 屏与 150% 屏上的点阵肉眼观感一致；
    ///   3. 显示器切换/DPI 变化（WM_DPICHANGED / WM_DISPLAYCHANGE）后整屏重绘。
    ///
    /// 用户可在设置里关掉「跟随 DPI 缩放」，此时数值即物理像素（1:1 锐利，不随缩放放大）。
    /// </summary>
    public static class DpiHelper
    {
        // ---------------- Win32 ----------------
        private const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitorShcore(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const int MONITOR_DEFAULTTONEAREST = 2;

        // 进程级感知上下文（Windows 10 1703+）
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        /// <summary>必须在创建任何窗口之前调用。</summary>
        public static void EnablePerMonitorV2()
        {
            bool ok = false;
            // 1) 首选 Win10 1703+ 的 PerMonitorV2
            try { ok = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
            catch (Exception) { ok = false; }

            if (ok) { Log.Info("DPI：已启用 PerMonitorV2"); return; }

            // 2) Win8.1+ 的 shcore 逐显示器感知
            try
            {
                // 2 = PROCESS_PER_MONITOR_DPI_AWARE
                if (SetProcessDpiAwareness(2) == 0) { Log.Info("DPI：已启用 PerMonitor(shcore)"); return; }
            }
            catch (Exception) { }

            // 3) 最后退回系统级感知
            try
            {
                if (SetProcessDPIAware()) { Log.Info("DPI：已启用 System DPI Aware（降级）"); return; }
            }
            catch (Exception) { }

            Log.Warn("DPI：未能声明感知，可能在非 100% 缩放下出现模糊或偏移");
        }

        /// <summary>取某块屏幕的缩放系数（1.0 = 100%）。取不到时返回 1.0。</summary>
        public static double GetScaleForScreen(Screen screen)
        {
            try
            {
                var center = new POINT
                {
                    X = screen.Bounds.X + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Y + screen.Bounds.Height / 2
                };
                IntPtr hMon = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
                if (hMon == IntPtr.Zero) return 1.0;

                uint dpiX, dpiY;

                // Win8.1+：shcore 的 GetDpiForMonitor
                if (GetDpiForMonitorShcore(hMon, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0 && dpiX > 0)
                    return dpiX / 96.0;
            }
            catch (Exception) { }

            return 1.0;
        }

        /// <summary>按屏幕 DPI 换算一个"逻辑像素"值。</summary>
        public static int Scale(int value, double dpiScale)
        {
            if (dpiScale <= 0 || Math.Abs(dpiScale - 1.0) < 0.001) return value;
            return (int)Math.Round(value * dpiScale);
        }

        /// <summary>
        /// 生成"已按该屏 DPI 换算"的 profile 副本。像素类参数（间距/点大小/粗细/留空/偏移）
        /// 乘缩放系数；百分比类参数（占比/圆角/透明度）本身就是无量纲的，保持不变。
        /// </summary>
        public static Profile ForScreen(Profile src, double dpiScale, bool scaleWithDpi)
        {
            if (src == null) return null;
            if (!scaleWithDpi || Math.Abs(dpiScale - 1.0) < 0.001) return src;

            var c = src.Clone();

            c.DotSpacing = Scaled(src.DotSpacing, dpiScale);
            c.DotSize = Scaled(src.DotSize, dpiScale);
            c.CenterGapPx = (int)Math.Round(src.CenterGapPx * dpiScale);
            c.CenterOffsetY = (int)Math.Round(src.CenterOffsetY * dpiScale);
            c.CenterLine.Thickness = Scaled(src.CenterLine.Thickness, dpiScale);
            c.CenterLine.BorderThickness = (int)Math.Round(src.CenterLine.BorderThickness * dpiScale);
            if (src.CenterLine.Length > 0)
                c.CenterLine.Length = Scaled(src.CenterLine.Length, dpiScale);

            // 两层边框粗细同样是像素量纲
            if (src.InnerBorderThickness > 0) c.InnerBorderThickness = Scaled(src.InnerBorderThickness, dpiScale);
            if (src.OuterBorderThickness > 0) c.OuterBorderThickness = Scaled(src.OuterBorderThickness, dpiScale);

            return c;
        }

        private static int Scaled(int v, double dpiScale)
        {
            return Math.Max(1, (int)Math.Round(v * dpiScale));
        }

        /// <summary>供设置面板显示用的一句话描述。</summary>
        public static string DescribeScreens()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in Screen.AllScreens)
            {
                if (sb.Length > 0) sb.Append("；");
                sb.AppendFormat("{0} {1}x{2} @{3}%",
                    s.DeviceName, s.Bounds.Width, s.Bounds.Height,
                    Math.Round(GetScaleForScreen(s) * 100));
            }
            return sb.ToString();
        }

        /// <summary>当前窗口所在屏幕的 DPI（用于设置面板自身缩放）。</summary>
        public static uint GetDpiForHwnd(IntPtr hwnd)
        {
            try { return GetDpiForWindow(hwnd); }
            catch (Exception) { return 96; }
        }
    }
}
