using System;

namespace AgoAnchor
{
    /// <summary>诊断开关：--headless 时打开控制台输出，正常运行时关闭。</summary>
    public static class Diagnostics
    {
        public static bool Enabled = false;

        public static void Line(string msg)
        {
            if (!Enabled) return;
            Console.WriteLine(msg);
        }
    }
}
