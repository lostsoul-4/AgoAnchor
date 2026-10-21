using System;
using System.IO;

namespace AgoAnchor
{
    /// <summary>--savetest：直接验证配置能否写盘并原样读回。</summary>
    public static class SaveTest
    {
        public static int Run()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Diagnostics.Enabled = true;
            Console.WriteLine("dir      : " + Paths.AppDataDir);
            Console.WriteLine("dirExists: " + Directory.Exists(Paths.AppDataDir));

            var cfg = new Config();
            cfg.EnsureProfile();
            cfg.P.Name = "测试方案";
            cfg.P.FramePercent = 77;
            cfg.P.InnerBorderThickness = 3;

            cfg.Save();

            Console.WriteLine("fileExists: " + File.Exists(Paths.ConfigFile));
            if (!File.Exists(Paths.ConfigFile)) { Console.WriteLine("SAVE FAILED"); return 1; }

            var fi = new FileInfo(Paths.ConfigFile);
            Console.WriteLine("size      : " + fi.Length);

            var back = Config.Load();
            Console.WriteLine("profiles  : " + back.Profiles.Count);
            Console.WriteLine("p0.name   : " + back.P.Name);
            Console.WriteLine("p0.frame  : " + back.P.FramePercent);
            Console.WriteLine("p0.inner  : " + back.P.InnerBorderThickness);

            bool ok = back.Profiles.Count >= 3 && back.P.Name == "测试方案" && back.P.FramePercent == 77;
            Console.WriteLine(ok ? "ROUNDTRIP OK" : "ROUNDTRIP MISMATCH");

            // 打印 JSON 前 400 字符，便于确认结构
            string json = File.ReadAllText(Paths.ConfigFile);
            Console.WriteLine("--- json head ---");
            Console.WriteLine(json.Length > 400 ? json.Substring(0, 400) : json);

            return ok ? 0 : 2;
        }
    }
}

