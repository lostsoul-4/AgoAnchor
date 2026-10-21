using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AgoAnchor
{
    /// <summary>按配置为每块选中的屏幕维护一个覆盖窗口。</summary>
    public sealed class OverlayEngine : IDisposable
    {
        private readonly Dictionary<string, OverlayWindow> windows =
            new Dictionary<string, OverlayWindow>(StringComparer.OrdinalIgnoreCase);

        private Config config = new Config();
        private bool running = true;

        public void Apply(Config cfg)
        {
            config = cfg;
            SyncWindows();
            Render();
        }

        public void SetRunning(bool on)
        {
            running = on;
            Render();
        }

        public bool IsRunning { get { return running; } }

        public void Rebuild()
        {
            foreach (var kv in windows) kv.Value.Dispose();
            windows.Clear();
            SyncWindows();
            Render();
        }

        private void SyncWindows()
        {
            var screens = Screen.AllScreens;

            // 屏幕对象是按 DeviceName 缓存的，分辨率/DPI 变化后 Screen.AllScreens
            // 返回的 Bounds 会更新，但 OverlayWindow 里缓存的 screen 引用可能是旧快照。
            // 因此把最新的 Screen 实例回灌给已存在的窗口。
            var latest = new Dictionary<string, Screen>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in screens) latest[s.DeviceName] = s;

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in screens)
            {
                // 用户没勾选任何屏幕 → 不显示（不再默认全部屏幕）
                bool selected = config.IsScreenSelected(s.DeviceName);
                if (!selected) continue;
                wanted.Add(s.DeviceName);

                if (!windows.ContainsKey(s.DeviceName))
                {
                    var w = new OverlayWindow(s, config);
                    windows[s.DeviceName] = w;
                }
                else
                {
                    windows[s.DeviceName].ReattachScreen(s);
                }
            }

            var remove = new List<string>();
            foreach (var kv in windows)
                if (!wanted.Contains(kv.Key)) remove.Add(kv.Key);

            foreach (var key in remove)
            {
                windows[key].Close();
                windows[key].Dispose();
                windows.Remove(key);
            }
        }

        /// <summary>
        /// 强制按当前屏幕拓扑重建全部覆盖窗口。
        /// 分辨率或 DPI 改变后必须走这里 —— 只重绘不够，窗口尺寸/位置都是旧的。
        /// </summary>
        public void ForceRebuild()
        {
            foreach (var kv in windows) kv.Value.Dispose();
            windows.Clear();
            SyncWindows();
            Render();
        }

        public void Render()
        {
            foreach (var kv in windows)
            {
                try
                {
                    
                    kv.Value.Refresh_Overlay(config, running && config.Enabled);
                }
                catch (Exception ex)
                {
                    Log.Warn("渲染失败 " + kv.Key + "：" + ex.Message);
                }
            }
        }

        /// <summary>显示/隐藏切换或屏幕拓扑变化后调用。</summary>
        public void RebuildAndRender()
        {
            Rebuild();
        }

        public void Dispose()
        {
            foreach (var kv in windows) kv.Value.Dispose();
            windows.Clear();
        }
    }
}

