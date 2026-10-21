using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace AgoAnchor
{
    public enum CloseAction { Ask = 0, ToTray = 1, Exit = 2 }

    public enum Lang { Chinese = 0, English = 1, Japanese = 2, Korean = 3 }

    /// <summary>一条辅助线的样式参数（仅中央横线在用）。</summary>
    public class LineStyle
    {
        public int Length = 0;        // 0 = 整屏宽
        public int Thickness = 2;
        public uint ColorArgb = unchecked((uint)0xFFA8E6FF);
        public uint BorderArgb = unchecked((uint)0x00000000);
        public int BorderThickness = 0;
        public int RoundedOrder = 0;

        public Color Color
        {
            get { return Argb.ColorFromUInt(ColorArgb); }
            set { ColorArgb = Argb.UIntFromColor(value); }
        }
        public Color Border
        {
            get { return Argb.ColorFromUInt(BorderArgb); }
            set { BorderArgb = Argb.UIntFromColor(value); }
        }
    }

    /// <summary>
    /// 一个 profile = 一套完整的视觉样式。用户可建多个，玩不同游戏时切换。
    /// </summary>
    public class Profile
    {
        public string Name = "默认";

        // ---- 点阵框 ----
        public bool GridEnabled = true;
        public int FramePercent = 82;          // 50-90
        public int DotSpacing = 26;            // 点间距 px
        public int DotSize = 2;                // 点直径 px
        public uint DotColorArgb = unchecked((uint)0xFFA8E6FF);   // 点阵主色（黑白点共用）
        public int GridOpacityPercent = 65;

        // ---- 两层描边（点阵与横线共用；颜色/透明度/粗细各自独立） ----
        // 内边框：紧贴本体的细线，对应 COD 准星那圈黑色内圈
        public bool InnerBorderEnabled = true;
        public uint InnerBorderArgb = unchecked((uint)0xFF000000);
        public int InnerBorderOpacityPercent = 100;
        public int InnerBorderThickness = 1;
        // 外边框：套在内边框之外的浅灰圈
        public bool OuterBorderEnabled = true;
        public uint OuterBorderArgb = unchecked((uint)0xFFB4B4B4);
        public int OuterBorderOpacityPercent = 100;
        public int OuterBorderThickness = 1;

        // ---- 中央横线：中间断开，不遮准星 ----
        public bool CenterLineEnabled = true;
        public LineStyle CenterLine = new LineStyle
        {
            Length = 0,
            Thickness = 2,
            ColorArgb = unchecked((uint)0xFFA8E6FF)
        };
        public int CenterGapPx = 260;          // 中间留空宽度（像素）
        public int CenterOffsetY = 0;          // 相对中心的垂直偏移（像素）
        public int CenterOpacityPercent = 90;

        // ---- X Mode：两条对角线，同样在中心断开 ----
        // 参数含义与中央横线一一对应，只是方向换成 45° 对角线。
        public bool XModeEnabled = false;
        public LineStyle XLine = new LineStyle
        {
            Length = 0,
            Thickness = 2,
            ColorArgb = unchecked((uint)0xFFA8E6FF)
        };
        public int XGapPx = 260;               // 交叉点附近的留空宽度（像素）
        public int XOffsetX = 0;               // 交叉点水平偏移（像素）
        public int XOffsetY = 0;               // 交叉点垂直偏移（像素）
        public int XOpacityPercent = 90;

        // ---- 线与点的重叠处理 ----
        // true = 线优先：落在任何线段覆盖范围里的圆点不绘制。
        public bool LineOverDot = false;

        // ---- 全局 ----
        public int GlobalOpacityPercent = 100;

        public Profile Clone()
        {
            var p = new Profile();
            CopyTo(p);
            return p;
        }

        public void CopyTo(Profile d)
        {
            d.Name = Name;
            d.GridEnabled = GridEnabled;
            d.FramePercent = FramePercent;
            d.DotSpacing = DotSpacing;
            d.DotSize = DotSize;
            d.DotColorArgb = DotColorArgb;
            d.GridOpacityPercent = GridOpacityPercent;
            d.InnerBorderArgb = InnerBorderArgb;
            d.InnerBorderEnabled = InnerBorderEnabled;
            d.InnerBorderOpacityPercent = InnerBorderOpacityPercent;
            d.InnerBorderThickness = InnerBorderThickness;
            d.OuterBorderArgb = OuterBorderArgb;
            d.OuterBorderEnabled = OuterBorderEnabled;
            d.OuterBorderOpacityPercent = OuterBorderOpacityPercent;
            d.OuterBorderThickness = OuterBorderThickness;
            d.CenterLineEnabled = CenterLineEnabled;
            d.CenterLine = new LineStyle
            {
                Length = CenterLine.Length,
                Thickness = CenterLine.Thickness,
                ColorArgb = CenterLine.ColorArgb,
                BorderArgb = CenterLine.BorderArgb,
                BorderThickness = CenterLine.BorderThickness,
                RoundedOrder = CenterLine.RoundedOrder
            };
            d.CenterGapPx = CenterGapPx;
            d.CenterOffsetY = CenterOffsetY;
            d.CenterOpacityPercent = CenterOpacityPercent;

            d.XModeEnabled = XModeEnabled;
            d.XLine = new LineStyle
            {
                Length = XLine.Length,
                Thickness = XLine.Thickness,
                ColorArgb = XLine.ColorArgb,
                BorderArgb = XLine.BorderArgb,
                BorderThickness = XLine.BorderThickness,
                RoundedOrder = XLine.RoundedOrder
            };
            d.XGapPx = XGapPx;
            d.XOffsetX = XOffsetX;
            d.XOffsetY = XOffsetY;
            d.XOpacityPercent = XOpacityPercent;

            d.LineOverDot = LineOverDot;
            d.GlobalOpacityPercent = GlobalOpacityPercent;
        }
    }

    public class Config
    {
        public const int Version = 3;

        // ---- 应用级 ----
        public bool Enabled = true;
        public bool StartWithWindows = false;
        public CloseAction OnClose = CloseAction.ToTray;
        public Lang Language = Lang.Chinese;
        public List<string> TargetScreens = new List<string>();

        /// <summary>
        /// 覆盖层窗口的标题。OBS 的窗口列表按「进程名: 窗口标题」显示。
        /// （OBS 专用模式已移除：正常窗口本身就能被 WGC 采集，无需特殊处理）
        /// </summary>
        public string ObsCaptureTitle = "AgoAnchor Overlay";

        public bool ScaleWithDpi = true;

        // ---- Profile 列表与当前选中 ----
        public List<Profile> Profiles = new List<Profile>();
        public int ActiveProfile = 0;

        // ---- 窗口尺寸（自由缩放后记住） ----
        public int WindowWidth = 0;
        public int WindowHeight = 0;

        // ================= 便捷访问当前 profile =================
                public Profile P
        {
            get
            {
                EnsureProfile();
                if (ActiveProfile < 0 || ActiveProfile >= Profiles.Count) ActiveProfile = 0;
                return Profiles[ActiveProfile];
            }
        }

        public void EnsureProfile()
        {
            if (Profiles == null) Profiles = new List<Profile>();
            while (Profiles.Count < 3)
            {
                var p = new Profile();
                int idx = Profiles.Count;
                if (idx == 1) p.Name = "Profile 2";
                else if (idx == 2) p.Name = "Profile 3";
                Profiles.Add(p);
            }
            if (ActiveProfile < 0 || ActiveProfile >= Profiles.Count) ActiveProfile = 0;
            foreach (var p in Profiles) NormalizeProfile(p);
        }

        private static void NormalizeProfile(Profile p)
        {
            if (p == null) return;
            if (p.CenterLine == null) p.CenterLine = new LineStyle();
            if (p.Name == null || p.Name.Length == 0) p.Name = "Profile";
        }

        // 兼容旧调用：渲染层读的是这些代理属性
        // ================= 序列化 =================
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static Config Load()
        {
            try
            {
                string path = Paths.ConfigFile;
                if (!File.Exists(path))
                {
                    var fresh = new Config();
                    fresh.EnsureProfile();
                    // 首次运行：默认勾选全部屏幕（之后完全由用户控制，
                    // 用户全部取消勾选就是"不显示"，不再自动补全）
                    try
                    {
                        foreach (var sc in System.Windows.Forms.Screen.AllScreens)
                            fresh.TargetScreens.Add(sc.DeviceName);
                    }
                    catch { }
                    // 首次运行：若能在旧位置找到历史配置，迁移过来
                    MigrateLegacyConfig(fresh);
                    return fresh;
                }

                var cfg = Ser.Deserialize<Config>(File.ReadAllText(path, Encoding.UTF8));
                if (cfg == null) { cfg = new Config(); }
                cfg.EnsureProfile();
                return cfg;
            }
            catch (Exception ex)
            {
                Log.Warn("config load failed / 配置读取失败: " + ex);
                var c = new Config();
                c.EnsureProfile();
                return c;
            }
        }

        /// <summary>
        /// 从旧路径（%APPDATA%）迁移历史配置。用户从 v2/v3 升级时，
        /// 配置可能还躺在旧目录里；如果只是换了保存位置，必须把内容带过来，
        /// 否则表现就是"版本迭代后 profile 没了"。
        /// </summary>
        private static void MigrateLegacyConfig(Config target)
        {
            try
            {
                string legacyDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgoAnchor");
                string legacyFile = Path.Combine(legacyDir, "config.json");
                if (!File.Exists(legacyFile)) return;
                if (string.Equals(Path.GetFullPath(legacyFile), Path.GetFullPath(Paths.ConfigFile),
                    StringComparison.OrdinalIgnoreCase)) return;

                string json = File.ReadAllText(legacyFile, Encoding.UTF8);
                var old = Ser.Deserialize<Config>(json);
                if (old == null) return;

                old.EnsureProfile();
                old.Save();
                Diagnostics.Line("[Migrate] 已从旧路径迁移配置: " + legacyFile);
                Log.Info("迁移旧配置: " + legacyFile);

                // 把迁移结果复制到调用方的 fresh 实例上
                var reloaded = Load();
                CopyConfig(reloaded, target);
            }
            catch (Exception ex)
            {
                Diagnostics.Line("[Migrate] 迁移失败: " + ex.Message);
            }
        }

        private static void CopyConfig(Config src, Config dst)
        {
            dst.Enabled = src.Enabled;
            dst.StartWithWindows = src.StartWithWindows;
            dst.OnClose = src.OnClose;
            dst.Language = src.Language;
            dst.TargetScreens = new List<string>(src.TargetScreens);
            dst.ScaleWithDpi = src.ScaleWithDpi;
            dst.Profiles = src.Profiles;
            dst.ActiveProfile = src.ActiveProfile;
            dst.WindowWidth = src.WindowWidth;
            dst.WindowHeight = src.WindowHeight;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.AppDataDir);
                File.WriteAllText(Paths.ConfigFile, Ser.Serialize(this), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Warn("config save failed / 配置保存失败: " + ex);
                Diagnostics.Line("[Save] FAILED: " + ex.GetType().Name + " :: " + ex.Message);
            }
        }

        public Config Clone()
        {
            var c = Ser.Deserialize<Config>(Ser.Serialize(this));
            if (c != null) c.EnsureProfile();
            return c;
        }

        public bool IsScreenSelected(string deviceName)
        {
            for (int i = 0; i < TargetScreens.Count; i++)
                if (string.Equals(TargetScreens[i], deviceName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>新建一个 profile 并选中它。</summary>
        public int AddProfile(string name)
        {
            EnsureProfile();
            var p = P.Clone();
            p.Name = name;
            Profiles.Add(p);
            ActiveProfile = Profiles.Count - 1;
            return ActiveProfile;
        }

        public void RemoveProfile(int index)
        {
            EnsureProfile();
            if (Profiles.Count <= 1) return;
            if (index < 0 || index >= Profiles.Count) return;
            Profiles.RemoveAt(index);
            if (ActiveProfile >= Profiles.Count) ActiveProfile = Profiles.Count - 1;
        }
    }

    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AgoAnchor";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    var v = k.GetValue(ValueName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (enabled)
                        k.SetValue(ValueName, "\"" + Paths.ExePath + "\" --autostart", RegistryValueKind.String);
                    else if (k.GetValue(ValueName) != null)
                        k.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex) { Log.Warn("autostart failed: " + ex.Message); }
        }
    }

    public static class Argb
    {
        public static Color ColorFromUInt(uint v)
        {
            return Color.FromArgb((int)((v >> 24) & 0xFF), (int)((v >> 16) & 0xFF),
                                  (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
        }

        public static uint UIntFromColor(Color c)
        {
            return ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | (uint)c.B;
        }

        public static uint ScaleAlpha(uint argb, int percent)
        {
            int a = (int)((argb >> 24) & 0xFF);
            a = a * Math.Max(0, Math.Min(100, percent)) / 100;
            return (argb & 0x00FFFFFF) | ((uint)a << 24);
        }
    }

    public static class Log
    {
        private static readonly object Sync = new object();

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }

        private static void Write(string lvl, string msg)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Paths.AppDataDir);
                    var fi = new FileInfo(Paths.LogFile);
                    if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    File.AppendAllText(Paths.LogFile,
                        string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1} {2}{3}", DateTime.Now, lvl, msg, Environment.NewLine),
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}




