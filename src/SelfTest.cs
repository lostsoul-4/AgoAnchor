using System;
using System.Drawing;

namespace AgoAnchor
{
    /// <summary>
    /// --selftest：不显示窗口、不碰屏幕，只验证几何与渲染是否自洽。
    /// </summary>
    public static class SelfTest
    {
        private static int failures;

        public static int Run()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== AgoAnchor SelfTest ===");

            TestAspect();
            TestXModeAngle();
            TestEvenness();
            TestPercentBounds();
            TestCenterGapPixels();
            TestCenterPixelClear();
            TestOpacityZero();
            TestRenderSmoke();
            TestProfileIsolation();
            TestBorders();

            Console.WriteLine();
            if (failures == 0) { Console.WriteLine("ALL PASS"); return 0; }
            Console.WriteLine("FAILURES: " + failures);
            return 2;
        }

        private static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (detail == null ? "" : "  -> " + detail));
            if (!ok) failures++;
        }

        private static Profile P() { return new Profile(); }

        /// <summary>框的宽高比必须等于屏幕宽高比。</summary>
        /// <summary>
        /// 验证 X Mode 的两条线确实沿屏幕对角线方向：
        /// 16:9 → 29.36°，16:10 → 32.01°，4:3 → 36.87°，正方形 → 45°。
        /// </summary>
        private static void TestXModeAngle()
        {
            var p = P();
            int[] ws = { 1920, 2560, 1680, 1920, 1024, 1600, 1000, 2560, 5120 };
            int[] hs = { 1080, 1440, 1050, 1200,  768, 1200, 1000, 1080, 1440 };
            string[] labels = { "16:9", "16:9", "16:10", "16:10", "4:3", "4:3", "1:1", "21:9", "32:9" };

            for (int i = 0; i < ws.Length; i++)
            {
                int w = ws[i], h = hs[i];
                string label = labels[i];

                int cx, cy, half;
                double dx, dy;
                Renderer.ComputeXMode(w, h, p, out cx, out cy, out dx, out dy, out half);

                double gotDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                double wantDeg = Math.Atan2(h, w) * 180.0 / Math.PI;

                Check(string.Format("X Mode 对角线方向 {0} ({1}x{2})", label, w, h),
                    Math.Abs(gotDeg - wantDeg) < 0.01,
                    string.Format("{0:F2}° (期望 {1:F2}°)", gotDeg, wantDeg));
            }
        }

        private static void TestAspect()
        {
            var p = P();
            int[][] sizes = new int[][]
            {
                new[] { 2560, 1440 },  // 16:9
                new[] { 1680, 1050 },  // 16:10（常见笔记本/办公屏）
                new[] { 1920, 1080 },  // 16:9
                new[] { 1920, 1200 },  // 16:10
                new[] { 1024, 768 },   // 4:3
                new[] { 1600, 1200 },  // 4:3
                new[] { 2560, 1080 },  // 21:9
                new[] { 3440, 1440 },  // 21:9
                new[] { 5120, 1440 },  // 32:9
            };

            foreach (var s in sizes)
            {
                int w = s[0], h = s[1];
                foreach (int pct in new[] { 50, 70, 82, 90 })
                {
                    p.FramePercent = pct;
                    var geo = Renderer.ComputeFrame(w, h, p);

                    int leftGap = geo.Left, rightGap = w - geo.Right;
                    int topGap = geo.Top, bottomGap = h - geo.Bottom;

                    bool centered = Math.Abs(leftGap - rightGap) <= 1 && Math.Abs(topGap - bottomGap) <= 1;
                    double screenAspect = (double)w / h;
                    double frameAspect = (double)geo.Width / geo.Height;
                    bool aspectOk = Math.Abs(frameAspect - screenAspect) / screenAspect < 0.015;

                    Check(string.Format("比例跟随 {0}x{1} @{2}%", w, h, pct),
                        centered && aspectOk,
                        string.Format("frame={0}x{1} aspect={2:F3} screen={3:F3}",
                            geo.Width, geo.Height, frameAspect, screenAspect));
                }
            }
        }

        private static void TestEvenness()
        {
            var p = P();
            foreach (int pct in new[] { 50, 75, 90 })
                foreach (int space in new[] { 7, 13, 28, 33, 64, 101 })
                {
                    p.FramePercent = pct;
                    p.DotSpacing = space;
                    var geo = Renderer.ComputeFrame(2560, 1440, p);
                    bool exactH = geo.Step * geo.SegH == geo.Width;
                    bool exactV = geo.Step * geo.SegV == geo.Height;
                    bool evenH = geo.SegH % 2 == 0;
                    bool evenV = geo.SegV % 2 == 0;
                    Check("横竖边整除 且 偶数段 pct=" + pct + " space=" + space,
                        exactH && exactV && evenH && evenV,
                        string.Format("W={0} step={1} segH={2} | H={3} segV={4}",
                            geo.Width, geo.Step, geo.SegH, geo.Height, geo.SegV));
                }
        }

        private static void TestPercentBounds()
        {
            var p = P();
            p.FramePercent = 10;
            var lo = Renderer.ComputeFrame(2560, 1440, p);
            p.FramePercent = 200;
            var hi = Renderer.ComputeFrame(2560, 1440, p);

            double loW = (double)lo.Width / 2560;
            double hiW = (double)hi.Width / 2560;
            Check("占比下限夹到 50%", Math.Abs(loW - 0.50) < 0.02, loW.ToString("F3"));
            Check("占比上限夹到 90%", Math.Abs(hiW - 0.90) < 0.02, hiW.ToString("F3"));
        }

        /// <summary>中间留空以像素为单位，且左右对称、不越界。</summary>
        private static void TestCenterGapPixels()
        {
            var p = P();
            p.CenterGapPx = 300;

            int w = 2560, h = 1440;
            int y, l0, l1, r0, r1;
            Renderer.ComputeCenterLine(w, h, p, out y, out l0, out l1, out r0, out r1);

            int cx = w / 2;
            int gap = r0 - l1;
            Check("留空 = 设定像素值", Math.Abs(gap - 300) <= 1, "gap=" + gap);
            Check("留空左右对称", Math.Abs((cx - l1) - (r0 - cx)) <= 1,
                string.Format("L={0} R={1}", cx - l1, r0 - cx));
            Check("留空不越过中心", l1 <= cx && r0 >= cx, "l1=" + l1 + " r0=" + r0 + " cx=" + cx);
            Check("两段都在屏幕内", l0 >= 0 && r1 <= w, "l0=" + l0 + " r1=" + r1);

            // 换一个分辨率，留空像素值必须保持不变（这正是改成像素的目的）
            Renderer.ComputeCenterLine(1920, 1080, p, out y, out l0, out l1, out r0, out r1);
            int gap2 = r0 - l1;
            Check("换分辨率留空像素不变", Math.Abs(gap2 - 300) <= 1, "1920x1080 gap=" + gap2);

            // 留空超过整条线时自动夹紧，不产生负数段
            p.CenterGapPx = 99999;
            Renderer.ComputeCenterLine(1280, 720, p, out y, out l0, out l1, out r0, out r1);
            Check("留空超限自动夹紧", l1 >= l0 && r1 >= r0 && (r0 - l1) < 1280,
                string.Format("l={0}..{1} r={2}..{3}", l0, l1, r0, r1));
        }

        private static void TestCenterPixelClear()
        {
            var p = P();
            p.CenterLineEnabled = true;
            p.CenterGapPx = 260;
            p.GridEnabled = true;
            p.DotColorArgb = 0xFFFFFFFF;
            p.CenterLine.ColorArgb = 0xFFFFFFFF;
            p.GlobalOpacityPercent = 100;
            p.GridOpacityPercent = 100;
            p.CenterOpacityPercent = 100;

            using (var bmp = Renderer.Render(1280, 720, p, 100))
            {
                var c = bmp.GetPixel(640, 360);
                Check("准星位置像素透明", c.A < 16, "A=" + c.A);
                var left = bmp.GetPixel(400, 360);
                Check("中心左侧确有横线", left.A > 16, "A=" + left.A);
            }
        }

        private static void TestOpacityZero()
        {
            var p = P();
            using (var bmp = Renderer.Render(320, 180, p, 0))
            {
                bool allTransparent = true;
                for (int y = 0; y < bmp.Height && allTransparent; y += 9)
                    for (int x = 0; x < bmp.Width && allTransparent; x += 9)
                        if (bmp.GetPixel(x, y).A != 0) allTransparent = false;
                Check("全局透明度 0% 时整图全透明", allTransparent, null);
            }
        }

        private static void TestRenderSmoke()
        {
            var p = P();
            p.FramePercent = 80;
            p.DotSpacing = 24;
            p.DotSize = 3;

            using (var bmp = Renderer.Render(1280, 720, p, 100))
            {
                Check("渲染尺寸正确", bmp.Width == 1280 && bmp.Height == 720, bmp.Width + "x" + bmp.Height);

                int opaque = 0;
                for (int y = 0; y < bmp.Height; y += 2)
                    for (int x = 0; x < bmp.Width; x += 2)
                        if (bmp.GetPixel(x, y).A > 16) opaque++;
                Check("画面有内容", opaque > 100, "采样点=" + opaque);

                var geo = Renderer.ComputeFrame(1280, 720, p);
                var edge = bmp.GetPixel(geo.Left, geo.Top);
                Check("框角有像素", edge.A > 16, "A=" + edge.A);
            }

            // 点是圆的：圆心实、外接正方形角部空。
            // 必须关掉两层边框，否则边框把直径撑大、外接角部自然会有像素，测的就不是圆度了。
            var circ = P();
            circ.DotSize = 15;
            circ.DotSpacing = 200;
            circ.CenterLineEnabled = false;
            circ.InnerBorderEnabled = false;
            circ.OuterBorderEnabled = false;
            using (var bmp = Renderer.Render(600, 600, circ, 100))
            {
                var geo = Renderer.ComputeFrame(600, 600, circ);
                int cx = geo.Left, cy = FindDotCenterY(bmp, geo.Left);
                var centerPx = bmp.GetPixel(cx, cy);
                var cornerPx = bmp.GetPixel(cx + 6, cy + 6);
                Check("点中心不透明", centerPx.A > 100, "A=" + centerPx.A);
                Check("点外接正方形角部透明（圆点）", cornerPx.A < 60, "cornerA=" + cornerPx.A);
            }
        }

        private static int FindDotCenterY(Bitmap bmp, int x)
        {
            int first = -1;
            for (int y = 0; y < bmp.Height; y++)
                if (bmp.GetPixel(x, y).A > 100) { first = y; break; }
            if (first < 0) return bmp.Height / 2;
            int last = first;
            for (int y = first; y < bmp.Height; y++)
            {
                if (bmp.GetPixel(x, y).A > 100) last = y; else break;
            }
            return (first + last) / 2;
        }

        /// <summary>Profile 之间必须互不影响（切换/编辑不会串数据）。</summary>
        private static void TestProfileIsolation()
        {
            var cfg = new Config();
            cfg.EnsureProfile();
            Check("默认至少 3 个 profile", cfg.Profiles.Count >= 3, "count=" + cfg.Profiles.Count);

            cfg.ActiveProfile = 0;
            cfg.P.FramePercent = 60;
            cfg.P.DotSpacing = 11;

            cfg.ActiveProfile = 1;
            bool independent = cfg.P.FramePercent != 60 && cfg.P.DotSpacing != 11;
            Check("profile 间互不干扰", independent,
                string.Format("p1: frame={0} spacing={1}", cfg.P.FramePercent, cfg.P.DotSpacing));

            // 克隆后修改不回流
            var clone = cfg.P.Clone();
            clone.FramePercent = 50;
            Check("Clone 不回流", cfg.P.FramePercent != 50, "orig=" + cfg.P.FramePercent);

            // 新增 profile 继承当前样式但独立
            cfg.ActiveProfile = 0;
            int newIdx = cfg.AddProfile("测试");
            Check("新建 profile 会立即选中", cfg.ActiveProfile == newIdx, "idx=" + cfg.ActiveProfile);
            cfg.P.FramePercent = 90;
            cfg.ActiveProfile = 0;
            Check("新建 profile 修改不影响原方案", cfg.P.FramePercent == 60, "orig=" + cfg.P.FramePercent);
        }

        /// <summary>
        /// 两层边框：内框贴本体、外框套在内框之外；颜色各取各的；关掉任一层不留下空洞。
        /// </summary>
        private static void TestBorders()
        {
            var p = new Profile();
            p.GridEnabled = false;          // 只看横线，便于精确定位像素
            p.CenterLineEnabled = true;
            p.CenterLine.Length = 0;
            p.CenterGapPx = 0;
            p.CenterLine.Thickness = 4;
            p.CenterLine.RoundedOrder = 0;
            p.CenterLine.ColorArgb = 0xFFFF0000;   // 本体 红
            p.InnerBorderEnabled = true;
            p.InnerBorderArgb = 0xFF00FF00;        // 内框 绿
            p.InnerBorderThickness = 2;
            p.InnerBorderOpacityPercent = 100;
            p.OuterBorderEnabled = true;
            p.OuterBorderArgb = 0xFF0000FF;        // 外框 蓝
            p.OuterBorderThickness = 3;
            p.OuterBorderOpacityPercent = 100;
            p.CenterOpacityPercent = 100;

            int w = 600, h = 200;
            using (var bmp = Renderer.Render(w, h, p, 100))
            {
                int cx = 300;
                int cy = h / 2;          // 100

                // 本体半高 2 → 占据 y 99..101；内框 2px → y 97..96 与 102..103；外框 3px → 更外侧
                var body = bmp.GetPixel(cx, cy);
                Check("本体颜色正确（红）", body.A > 200 && body.R > 200 && body.G < 60 && body.B < 60,
                    string.Format("RGBA={0},{1},{2},{3}", body.R, body.G, body.B, body.A));

                var inner = bmp.GetPixel(cx, cy - 3);   // 本体上缘外 1px，应落在内框
                Check("内边框颜色正确（绿）", inner.A > 200 && inner.G > 200 && inner.R < 60,
                    string.Format("RGBA={0},{1},{2},{3}", inner.R, inner.G, inner.B, inner.A));

                var outer = bmp.GetPixel(cx, cy - 6);   // 再往外，应落在内框之外的层
                Check("外边框颜色正确（蓝）", outer.A > 200 && outer.B > 200 && outer.R < 60,
                    string.Format("RGBA={0},{1},{2},{3}", outer.R, outer.G, outer.B, outer.A));
            }

            // 关掉外框不应留下透明空洞
            var q = p.Clone();
            q.OuterBorderEnabled = false;
            using (var bmp = Renderer.Render(w, h, q, 100))
            {
                var inner2 = bmp.GetPixel(300, 100 - 3);
                Check("关闭外框后内框仍存在", inner2.A > 200 && inner2.G > 200,
                    string.Format("RGBA={0},{1},{2},{3}", inner2.R, inner2.G, inner2.B, inner2.A));
            }

            // 两层都关：只有本体
            var r = p.Clone();
            r.InnerBorderEnabled = false;
            r.OuterBorderEnabled = false;
            using (var bmp = Renderer.Render(w, h, r, 100))
            {
                var inner3 = bmp.GetPixel(300, 100 - 3);
                Check("两层都关时无描边", inner3.A < 16, "A=" + inner3.A);
                var body3 = bmp.GetPixel(300, 100);
                Check("两层都关时本体仍在", body3.A > 200, "A=" + body3.A);
            }

            // 点阵也吃到同样的两层边框
            var g = new Profile();
            g.GridEnabled = true;
            g.CenterLineEnabled = false;
            g.DotSize = 6;
            g.DotSpacing = 200;
            g.InnerBorderEnabled = true;
            g.InnerBorderThickness = 2;
            g.InnerBorderArgb = 0xFF00FF00;
            g.OuterBorderEnabled = true;
            g.OuterBorderThickness = 2;
            g.OuterBorderArgb = 0xFF0000FF;
            g.DotColorArgb = 0xFFFFFFFF;
            g.GridOpacityPercent = 100;

            using (var bmp = Renderer.Render(600, 600, g, 100))
            {
                var geo = Renderer.ComputeFrame(600, 600, g);
                int cy = FindDotCenterY(bmp, geo.Left);
                var dot0 = bmp.GetPixel(geo.Left, cy);
                Check("点阵本体为白", dot0.A > 200 && dot0.R > 200 && dot0.G > 200 && dot0.B > 200,
                    string.Format("RGBA={0},{1},{2},{3}", dot0.R, dot0.G, dot0.B, dot0.A));

                // 点半径 3，内框 2 → 内框外缘半径 5，因此在 4px 处应是绿色
                var ring = bmp.GetPixel(geo.Left, cy - 4);
                Check("点阵带内边框（绿）", ring.A > 150 && ring.G > 120,
                    string.Format("RGBA={0},{1},{2},{3}", ring.R, ring.G, ring.B, ring.A));
            }
        }
    }
}







