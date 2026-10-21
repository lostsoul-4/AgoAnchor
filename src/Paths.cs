using System;
using System.IO;
using System.Text;

namespace AgoAnchor
{
    /// <summary>
    /// 配置与日志的落盘位置。
    ///
    /// 现实中遇到过：本机某些安全组件/沙盒会拒绝进程写入 %APPDATA%\Roaming 下的**新建目录**，
    /// 此时 Save() 会抛 UnauthorizedAccessException。为保证配置一定能存下来，
    /// 这里按顺序尝试多个候选位置，第一个可写的就固定下来：
    ///   1. %LOCALAPPDATA%\AgoAnchor   （Local 通常比 Roaming 更少被同步/拦截）
    ///   2. 程序所在目录 \data                  （绿色便携，程序放在可写盘时最稳）
    ///   3. %APPDATA%\AgoAnchor         （传统位置，放最后）
    /// 选中的位置会写入一个标记文件，后续启动直接复用，避免来回跳。
    /// </summary>
    public static class Paths
    {
        private const string FolderName = "AgoAnchor";
        private static string resolved;

        private static readonly object Sync = new object();

        public static string AppDataDir
        {
            get
            {
                lock (Sync)
                {
                    if (resolved != null) return resolved;
                    resolved = Resolve();
                    return resolved;
                }
            }
        }

        public static string ConfigFile { get { return Path.Combine(AppDataDir, "config.json"); } }
        public static string LogFile { get { return Path.Combine(AppDataDir, "overlay.log"); } }

        /// <summary>当前配置所在目录（供界面展示与排查）。</summary>
        public static string CurrentDir { get { return AppDataDir; } }

        private static string Resolve()
        {
            var candidates = new System.Collections.Generic.List<string>();

            // 1) 先看上次选定的位置。指针文件放在程序目录（用户目录实测可能被拦截），
            //    若程序目录不可写，则退化为每次都重新探测（开销很小）。
            try
            {
                string pointer = PointerFile();
                if (pointer != null && File.Exists(pointer))
                {
                    string p = File.ReadAllText(pointer, Encoding.UTF8).Trim();
                    if (p.Length > 0 && Directory.Exists(p) && ProbeWritable(p)) return p;
                }
            }
            catch { }

            // 顺序说明（本机实测结论）：
            // 某些安全组件会拦截进程对用户配置文件目录 (%APPDATA% / %LOCALAPPDATA%) 的新建写入，
            // 连指针文件都写不进去，导致配置永远存不下来。
            // 因此优先走"便携模式"：配置放在 exe 同级的 data\，exe 放哪配置跟哪。
            // Program Files 不行——普通权限对该目录只读，写入需要 UAC 提权。
            string portable = SafeCombineExe("data");
            string desktop = SafeCombine(Environment.SpecialFolder.DesktopDirectory, FolderName);
            string local = SafeCombine(Environment.SpecialFolder.LocalApplicationData, FolderName);
            string roaming = SafeCombine(Environment.SpecialFolder.ApplicationData, FolderName);
            string temp = SafeCombineTemp(FolderName);

            if (portable != null) candidates.Add(portable);
            if (desktop != null) candidates.Add(desktop);
            if (temp != null) candidates.Add(temp);
            if (local != null) candidates.Add(local);
            if (roaming != null) candidates.Add(roaming);

            foreach (var dir in candidates)
            {
                if (TryEnsureWritable(dir))
                {
                    RememberPath(dir);
                    Diagnostics.Line("[Paths] 使用目录: " + dir);
                    return dir;
                }
            }

            string fallback = Path.Combine(Path.GetTempPath(), FolderName);
            Diagnostics.Line("[Paths] 全部候选不可写，回退: " + fallback);
            return fallback;
        }

        private static string SafeCombineTemp(string child)
        {
            try { return Path.Combine(Path.GetTempPath(), child); }
            catch { return null; }
        }

        private static string SafeCombine(Environment.SpecialFolder root, string child)
        {
            try
            {
                string baseDir = Environment.GetFolderPath(root);
                if (string.IsNullOrEmpty(baseDir)) return null;
                return Path.Combine(baseDir, child);
            }
            catch { return null; }
        }

        private static string SafeCombineExe(string child)
        {
            try
            {
                string dir = Path.GetDirectoryName(ExePath);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, "data");
            }
            catch { return null; }
        }

        /// <summary>尝试建目录并实际写一个探测文件，确认真的可写。</summary>
        private static bool TryEnsureWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                return ProbeWritable(dir);
            }
            catch
            {
                return false;
            }
        }

        private static bool ProbeWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".writetest");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>指针文件存放在程序目录，记录上次选定的配置目录。</summary>
        private static string PointerFile()
        {
            try
            {
                string dir = Path.GetDirectoryName(ExePath);
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(dir, ".datapath");
            }
            catch { return null; }
        }

        private static void RememberPath(string dir)
        {
            try
            {
                string pointer = PointerFile();
                if (pointer == null) return;
                File.WriteAllText(pointer, dir, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string ExePath
        {
            get
            {
                try
                {
                    var p = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    if (!string.IsNullOrEmpty(p)) return p;
                }
                catch { }
                return System.Reflection.Assembly.GetEntryAssembly().Location;
            }
        }
    }
}

