using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AgoAnchor
{
    /// <summary>
    /// --headless &lt;秒&gt;：完全不显示设置面板，只创建覆盖层窗口并推送图层，
    /// 到时自动退出并输出诊断。用于验证 DWM 分层窗口在真实桌面上是否工作。
    /// </summary>
    public static class Headless
    {
        public static int Run(int seconds)
        {
            Diagnostics.Enabled = true;
            DpiHelper.EnablePerMonitorV2();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var cfg = Config.Load();
            if (cfg.TargetScreens.Count == 0)
                foreach (var s in Screen.AllScreens) cfg.TargetScreens.Add(s.DeviceName);
            

            
            Console.WriteLine("配置目录：" + Paths.AppDataDir + "  存在=" + System.IO.Directory.Exists(Paths.AppDataDir));

            var engine = new OverlayEngine();
            var sw = Stopwatch.StartNew();

            try
            {
                engine.Apply(cfg);
                Console.WriteLine("engine.Apply 完成，窗口数=" + Screen.AllScreens.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("创建覆盖层失败：" + ex);
                return 4;
            }

            // 用一个隐藏窗体承载消息循环，保证 WM_PAINT / WM_DISPLAYCHANGE 能被处理
            var host = new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                WindowState = FormWindowState.Minimized,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                Size = new Size(1, 1)
            };
            host.Load += (s, e) =>
            {
                var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, seconds) * 1000 };
                timer.Tick += (s2, e2) =>
                {
                    timer.Stop();
                    engine.Dispose();
                    host.Close();
                };
                timer.Start();
            };

            Application.Run(host);

            sw.Stop();
            Console.WriteLine("无界面运行完成，用时 " + sw.ElapsedMilliseconds + " ms");
            Console.WriteLine("屏幕：" + DpiHelper.DescribeScreens());
            Console.WriteLine("日志：" + Paths.LogFile);
            return 0;
        }
    }
}

