using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace AgoAnchor
{
    /// <summary>--gallery &lt;目录&gt;：把几组典型参数渲染成对照图。纯离线。</summary>
    public static class Gallery
    {
        private const int W = 1280;
        private const int H = 720;

        public static int Dump(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);

                Save(Path.Combine(dir, "01-default.png"), Build(p => { }));
                Save(Path.Combine(dir, "02-dense.png"), Build(p => { p.DotSpacing = 10; p.DotSize = 3; }));
                Save(Path.Combine(dir, "03-sparse.png"), Build(p => { p.DotSpacing = 48; p.DotSize = 4; }));
                Save(Path.Combine(dir, "04-big-dots.png"), Build(p => { p.DotSpacing = 32; p.DotSize = 7; }));
                Save(Path.Combine(dir, "05-small-frame.png"), Build(p => { p.FramePercent = 50; p.DotSpacing = 16; }));
                Save(Path.Combine(dir, "06-warm-color.png"), Build(p =>
                {
                    p.DotColorArgb = unchecked((uint)0xFF8FFFC4);
                    p.CenterLine.ColorArgb = unchecked((uint)0xFF8FFFC4);
                }));
                Save(Path.Combine(dir, "07-gap-small.png"), Build(p => { p.CenterGapPx = 80; }));
                Save(Path.Combine(dir, "08-gap-large.png"), Build(p => { p.CenterGapPx = 700; }));
                Save(Path.Combine(dir, "09-thick-line.png"), Build(p =>
                {
                    p.CenterLine.Thickness = 4;
                    p.CenterLine.RoundedOrder = 100;
                }));
                Save(Path.Combine(dir, "10-line-short.png"), Build(p =>
                {
                    p.CenterLine.Length = 900;
                    p.CenterLine.RoundedOrder = 100;
                    p.CenterLine.BorderArgb = unchecked((uint)0x80000000);
                    p.CenterLine.BorderThickness = 2;
                }));
                Save(Path.Combine(dir, "11-opacity-40.png"), Build(p => { p.GlobalOpacityPercent = 40; }));
                Save(Path.Combine(dir, "12-offset.png"), Build(p => { p.CenterOffsetY = 260; }));

                // 不同屏幕比例下的框形状
                SaveSize(Path.Combine(dir, "13-4x3-1024x768.png"), 1024, 768, Build(p => { }));
                SaveSize(Path.Combine(dir, "14-21x9-2560x1080.png"), 2560, 1080, Build(p => { }));

                // 局部放大：看清内外两层边框
                SaveZoom(Path.Combine(dir, "15-zoom-borders.png"), Build(p =>
                {
                    p.DotSize = 6;
                    p.DotSpacing = 24;
                    p.InnerBorderThickness = 2;
                    p.OuterBorderThickness = 2;
                    p.CenterLine.Thickness = 6;
                    p.CenterGapPx = 120;
                }), 8);

                // 只开内框 / 只开外框，对比
                SaveZoom(Path.Combine(dir, "16-zoom-inner-only.png"), Build(p =>
                {
                    p.DotSize = 6; p.DotSpacing = 24;
                    p.InnerBorderThickness = 2; p.OuterBorderEnabled = false;
                    p.CenterLine.Thickness = 6; p.CenterGapPx = 120;
                }), 8);
                SaveZoom(Path.Combine(dir, "17-zoom-outer-only.png"), Build(p =>
                {
                    p.DotSize = 6; p.DotSpacing = 24;
                    p.InnerBorderEnabled = false; p.OuterBorderThickness = 2;
                    p.CenterLine.Thickness = 6; p.CenterGapPx = 120;
                }), 8);

                // ---- X Mode ----
                Save(Path.Combine(dir, "18-xmode.png"), Build(p =>
                {
                    p.XModeEnabled = true;
                    p.XLine.ColorArgb = unchecked((uint)0xFFFF9E64);
                    p.XLine.Thickness = 3;
                    p.XGapPx = 320;
                }));
                Save(Path.Combine(dir, "19-xmode-only.png"), Build(p =>
                {
                    p.GridEnabled = false;
                    p.CenterLineEnabled = false;
                    p.XModeEnabled = true;
                    p.XLine.Thickness = 2;
                    p.XGapPx = 400;
                }));

                // ---- 线优先：圆点在线段处消失 ----
                Save(Path.Combine(dir, "20-lineover-off.png"), Build(p =>
                {
                    p.DotSpacing = 40; p.DotSize = 5;
                    p.FramePercent = 90;
                    p.CenterGapPx = 200;
                    p.LineOverDot = false;
                }));
                Save(Path.Combine(dir, "21-lineover-on.png"), Build(p =>
                {
                    p.DotSpacing = 40; p.DotSize = 5;
                    p.FramePercent = 90;
                    p.CenterGapPx = 200;
                    p.LineOverDot = true;
                }));

                // ---- 大间距（验证 200px 上限） ----
                Save(Path.Combine(dir, "22-spacing-200.png"), Build(p =>
                {
                    p.DotSpacing = 200; p.DotSize = 6; p.FramePercent = 90;
                }));

                // ---- X Mode + 线与点不重叠 ----
                Save(Path.Combine(dir, "23-xmode-lineover-on.png"), Build(p =>
                {
                    p.DotSpacing = 60; p.DotSize = 5; p.FramePercent = 90;
                    p.XModeEnabled = true; p.XGapPx = 200;
                    p.LineOverDot = true;
                }));

                // ---- X Mode 在不同宽高比下的方向（应为屏幕对角线） ----
                SaveSize(Path.Combine(dir, "24-xmode-4x3.png"), 1024, 768, Build(p =>
                {
                    p.XModeEnabled = true; p.CenterLineEnabled = false;
                    p.XLine.Thickness = 2;
                }));
                SaveSize(Path.Combine(dir, "25-xmode-21x9.png"), 2560, 1080, Build(p =>
                {
                    p.XModeEnabled = true; p.CenterLineEnabled = false;
                    p.XLine.Thickness = 2;
                }));

                Console.WriteLine("gallery -> " + dir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("gallery failed: " + ex);
                return 3;
            }
        }

        private static Profile Build(Action<Profile> tune)
        {
            var p = new Profile();   // 直接用默认值，保证对照图与程序默认观感一致
            p.FramePercent = 82;
            p.CenterGapPx = 260;
            tune(p);
            return p;
        }

        private static void Save(string path, Profile p)
        {
            SaveSize(path, W, H, p);
        }

        /// <summary>截取屏幕中心一块区域并做最近邻放大，便于检查边框像素。</summary>
        private static void SaveZoom(string path, Profile p, int zoom)
        {
            int w = W, h = H;
            using (var full = Renderer.Render(w, h, p, p.GlobalOpacityPercent))
            {
                int cw = 160, ch = 100;
                int sx = w / 2 - cw / 2, sy = h / 2 - ch / 2;

                using (var crop = new Bitmap(cw, ch))
                {
                    using (var g = Graphics.FromImage(crop))
                    {
                        // 深色底，方便看清浅灰外框
                        g.Clear(Color.FromArgb(255, 30, 34, 40));
                        g.DrawImage(full, new Rectangle(0, 0, cw, ch), new Rectangle(sx, sy, cw, ch), GraphicsUnit.Pixel);
                    }

                    using (var big = new Bitmap(cw * zoom, ch * zoom))
                    {
                        using (var g = Graphics.FromImage(big))
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(crop, new Rectangle(0, 0, cw * zoom, ch * zoom),
                                new Rectangle(0, 0, cw, ch), GraphicsUnit.Pixel);
                        }
                        big.Save(path, ImageFormat.Png);
                    }
                }
            }
        }
        private static void SaveSize(string path, int w, int h, Profile p)
        {
            using (var top = Renderer.Render(w, h, p, p.GlobalOpacityPercent))
            {
                top.Save(path, ImageFormat.Png);

                using (var bg = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bg))
                    {
                        using (var br = new LinearGradientBrush(
                            new Point(0, 0), new Point(w, h),
                            Color.FromArgb(255, 62, 72, 86), Color.FromArgb(255, 22, 26, 32)))
                            g.FillRectangle(br, 0, 0, w, h);
                        using (var br2 = new SolidBrush(Color.FromArgb(255, 34, 40, 48)))
                            g.FillRectangle(br2, w / 2 - w / 6, h / 2 - h / 8, w / 3, h / 4);
                        g.DrawImageUnscaled(top, 0, 0);
                    }
                    string onBg = path.Substring(0, path.Length - 4) + "_on_bg.png";
                    bg.Save(onBg, ImageFormat.Png);
                }
            }
        }
    }
}


