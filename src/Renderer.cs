using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AgoAnchor
{
    /// <summary>
    /// 纯渲染层：不碰窗口句柄，给定 (宽, 高, 配置) 就产出像素。
    /// 可脱离显示器做几何验证（见 --selftest）。
    /// 只绘制两种元素：屏幕等比例的点阵框、中间断开的中央横线。
    /// </summary>
    public static class Renderer
    {
        public struct FrameGeometry
        {
            public int Width;
            public int Height;
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int SegH;
            public int SegV;
            public int Step;
        }

        /// <summary>
        /// 全屏比例框：宽高各自按屏幕比例缩放，保证框的比例 = 屏幕比例。
        ///
        /// 关键约束：横边与竖边必须共用**同一个整数步长**（点与点的物理间距一致），
        /// 否则两边各自取整会让比例漂移 1~2%。
        /// 在期望步长 ±40% 内搜索 (step, segH, segV)，使 step*segH : step*segV 最接近屏幕比例，
        /// 且分段数为偶数（保证四边中点都是点、四角共享）。
        /// </summary>
        public static FrameGeometry ComputeFrame(int width, int height, Profile p)
        {
            int pct = Clamp(p.FramePercent, 50, 90);
            int targetW = (int)Math.Round(width * pct / 100.0);
            int targetH = (int)Math.Round(height * pct / 100.0);
            if (targetW > width) targetW = width;
            if (targetH > height) targetH = height;

            int desired = Clamp(p.DotSpacing, 4, 400);

            int bestStep = desired, bestSegH = 2, bestSegV = 2;
            double bestErr = double.MaxValue;
            double targetAspect = (double)width / height;

            int lo = Math.Max(2, (int)(desired * 0.60));
            int hi = Math.Max(lo + 1, (int)(desired * 1.40));

            for (int step = lo; step <= hi; step++)
            {
                int segH = (int)Math.Round((double)targetW / step);
                int segV = (int)Math.Round((double)targetH / step);
                if (segH < 2) segH = 2;
                if (segV < 2) segV = 2;
                if (segH % 2 != 0) segH++;
                if (segV % 2 != 0) segV++;

                int fw = segH * step;
                int fh = segV * step;
                if (fw > width || fh > height) continue;

                double aspectErr = Math.Abs((double)fw / fh - targetAspect) / targetAspect;
                double sizeErr = Math.Abs((double)fw / targetW - 1.0) + Math.Abs((double)fh / targetH - 1.0);
                double err = aspectErr + sizeErr * 0.35;

                if (err < bestErr)
                {
                    bestErr = err;
                    bestStep = step;
                    bestSegH = segH;
                    bestSegV = segV;
                }
            }

            int frameW = bestSegH * bestStep;
            int frameH = bestSegV * bestStep;

            return new FrameGeometry
            {
                Width = frameW,
                Height = frameH,
                Left = (width - frameW) / 2,
                Top = (height - frameH) / 2,
                Right = (width - frameW) / 2 + frameW,
                Bottom = (height - frameH) / 2 + frameH,
                SegH = bestSegH,
                SegV = bestSegV,
                Step = bestStep
            };
        }

        /// <summary>
        /// 中央横线的两段几何。中间留空以**像素**为单位，不随分辨率变化。
        /// </summary>
        public static void ComputeCenterLine(int width, int height, Profile p,
                                             out int y, out int leftStart, out int leftEnd,
                                             out int rightStart, out int rightEnd)
        {
            var st = p.CenterLine;
            int fullLen = st.Length <= 0 ? width : Clamp(st.Length, 1, width);
            int x0 = (width - fullLen) / 2;
            int x1 = x0 + fullLen;

            // 留空宽度为像素；不允许超过整条线，保证左右各至少 2px
            int gap = Clamp(p.CenterGapPx, 0, Math.Max(0, fullLen - 4));
            int halfGap = gap / 2;

            int cx = width / 2;
            y = height / 2 + p.CenterOffsetY;

            leftStart = x0;
            leftEnd = cx - halfGap;
            rightStart = cx + (gap - halfGap);
            rightEnd = x1;

            if (leftEnd < leftStart) leftEnd = leftStart;
            if (rightEnd < rightStart) rightEnd = rightStart;
        }

        public static Bitmap Render(int width, int height, Profile p, int globalOpacityPct)
        {
            var bmp = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                int global = Clamp(globalOpacityPct, 0, 100);
                if (global <= 0) return bmp;

                int gridPct = global * Clamp(p.GridOpacityPercent, 0, 100) / 100;
                int centerPct = global * Clamp(p.CenterOpacityPercent, 0, 100) / 100;
                int xPct = global * Clamp(p.XOpacityPercent, 0, 100) / 100;

                // 「线与点不重叠」：先求出所有线段的几何，画圆点时把与线段相交的圆点整个跳过。
                // 注意这**不是**图层叠放顺序问题，而是重叠检测后直接不绘制该圆点。
                var lineSegs = p.LineOverDot
                    ? BuildLineSegs(width, height, p, centerPct, xPct)
                    : null;

                if (p.GridEnabled && gridPct > 0) DrawGridFrame(g, width, height, p, gridPct, lineSegs);
                if (p.CenterLineEnabled && centerPct > 0) DrawCenterLine(g, width, height, p, centerPct);
                if (p.XModeEnabled && xPct > 0) DrawXMode(g, width, height, p, xPct);
            }
            return bmp;
        }

        // ============================================================ 线与点重叠判断

        /// <summary>一条线段的几何描述（用于判断圆点是否与它重叠）。</summary>
        private struct LineSeg
        {
            public float X0, Y0, X1, Y1;
            public float HalfThick;   // 线段半宽（含内外描边）
        }

        /// <summary>
        /// 收集所有线段（中央横线的两段 + X Mode 的四段对角线）。
        /// 只做几何判断，不涉及任何图层/绘制顺序概念。
        /// </summary>
        private static List<LineSeg> BuildLineSegs(int width, int height, Profile p, int centerPct, int xPct)
        {
            var list = new List<LineSeg>();
            float bodyHalf = LineBodyPad(p);

            if (p.CenterLineEnabled && centerPct > 0)
            {
                int y, l0, l1, r0, r1;
                ComputeCenterLine(width, height, p, out y, out l0, out l1, out r0, out r1);
                float half = Clamp(p.CenterLine.Thickness, 1, 32) / 2f + bodyHalf;
                if (l1 > l0) list.Add(Seg(l0, y, l1, y, half));
                if (r1 > r0) list.Add(Seg(r0, y, r1, y, half));
            }

            if (p.XModeEnabled && xPct > 0)
            {
                int cx, cy, halfLen;
                double dirX, dirY;
                ComputeXMode(width, height, p, out cx, out cy, out dirX, out dirY, out halfLen);

                float thick = Clamp(p.XLine.Thickness, 1, 32);
                float half = thick / 2f + bodyHalf;
                float gapHalf = Clamp(p.XGapPx, 0, Math.Max(0, halfLen * 2 - 4)) / 2f;
                if (halfLen - gapHalf > 0)
                {
                    double[][] axes =
                    {
                        new double[] { dirX, dirY },
                        new double[] { dirX, -dirY }
                    };
                    int[] signs = { 1, -1 };
                    for (int a = 0; a < axes.Length; a++)
                    {
                        double ax = axes[a][0], ay = axes[a][1];
                        for (int s = 0; s < signs.Length; s++)
                        {
                            double sg = signs[s];
                            float sx = (float)(cx + ax * gapHalf * sg);
                            float sy = (float)(cy + ay * gapHalf * sg);
                            float ex = (float)(cx + ax * halfLen * sg);
                            float ey = (float)(cy + ay * halfLen * sg);
                            list.Add(Seg(sx, sy, ex, ey, half));
                        }
                    }
                }
            }

            return list;
        }

        private static LineSeg Seg(float x0, float y0, float x1, float y1, float half)
        {
            return new LineSeg { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, HalfThick = half };
        }

        /// <summary>
        /// 判断一个圆（圆心 + 半径）是否与任何线段相交。
        /// 用「点到线段的最短距离」精确判定：距离 <= 半径 + 线段半宽 即视为重叠。
        /// </summary>
        private static bool OverlapsAnyLine(List<LineSeg> segs, float cx, float cy, float r)
        {
            if (segs == null || segs.Count == 0) return false;
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                float d = PointSegDistance(cx, cy, s.X0, s.Y0, s.X1, s.Y1);
                if (d <= r + s.HalfThick) return true;
            }
            return false;
        }

        /// <summary>点到线段的最短距离。</summary>
        private static float PointSegDistance(float px, float py, float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len2 = dx * dx + dy * dy;

            if (len2 <= 1e-6f)
            {
                float ax = px - x0, ay = py - y0;
                return (float)Math.Sqrt(ax * ax + ay * ay);
            }

            // 投影参数 t 夹到 [0,1]，得到线段上最近点
            float t = ((px - x0) * dx + (py - y0) * dy) / len2;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;

            float qx = x0 + t * dx, qy = y0 + t * dy;
            float vx = px - qx, vy = py - qy;
            return (float)Math.Sqrt(vx * vx + vy * vy);
        }

        /// <summary>线段本体之外的描边膨胀量（内外圈厚度之和）。</summary>
        private static float LineBodyPad(Profile p)
        {
            int innerW = p.InnerBorderEnabled ? Clamp(p.InnerBorderThickness, 0, 16) : 0;
            int outerW = p.OuterBorderEnabled ? Clamp(p.OuterBorderThickness, 0, 16) : 0;
            return innerW + outerW;
        }

        // ============================================================ 点阵框
        private static void DrawGridFrame(Graphics g, int width, int height, Profile p, int alphaPct,
                                          List<LineSeg> lineSegs)
        {
            var geo = ComputeFrame(width, height, p);
            int dot = Clamp(p.DotSize, 1, 32);
            uint main = Argb.ScaleAlpha(p.DotColorArgb, alphaPct);

            DrawSharpFrame(g, geo, dot, main, p, alphaPct, lineSegs);
        }

        /// <summary>
        /// 画点阵框。每个圆点可叠加两层描边：
        /// 外层（Outer，浅灰）套在内层（Inner，黑）之外，两层各有颜色/透明度/粗细。
        ///
        /// 半径约定（全部以"可见直径"计）：
        ///   本体直径 = DotSize
        ///   内圈可见宽 = InnerBorderThickness，其外缘半径 = DotSize/2 + InnerBorderThickness
        ///   外圈可见宽 = OuterBorderThickness，其外缘半径 = 上面 + OuterBorderThickness
        /// 绘制顺序由外向内覆盖，因此每层最终可见宽度精确等于配置值。
        /// </summary>
        private static void DrawSharpFrame(Graphics g, FrameGeometry geo, int dot, uint main,
                                           Profile p, int alphaPct, List<LineSeg> lineSegs)
        {
            int innerW = p.InnerBorderEnabled ? Clamp(p.InnerBorderThickness, 0, 16) : 0;
            int outerW = p.OuterBorderEnabled ? Clamp(p.OuterBorderThickness, 0, 16) : 0;

            uint innerCol = 0, outerCol = 0;
            bool hasInner = false, hasOuter = false;

            if (innerW > 0)
            {
                innerCol = Argb.ScaleAlpha(p.InnerBorderArgb,
                    p.InnerBorderOpacityPercent * alphaPct / 100);
                hasInner = Argb.ColorFromUInt(innerCol).A > 0;
            }
            if (outerW > 0)
            {
                outerCol = Argb.ScaleAlpha(p.OuterBorderArgb,
                    p.OuterBorderOpacityPercent * alphaPct / 100);
                hasOuter = Argb.ColorFromUInt(outerCol).A > 0;
            }

            // 实际参与绘制的宽度（未启用的层宽度按 0 计，避免留下空洞）
            int effInner = hasInner ? innerW : 0;
            int effOuter = hasOuter ? outerW : 0;

            float rBody = dot / 2f;
            float rInnerOuter = rBody + effInner;              // 内圈外缘
            float rOuterOuter = rInnerOuter + effOuter;        // 外圈外缘

            // 「线优先」时按最外层半径判定，保证描边也不压线
            float hitR = rOuterOuter;

            for (int i = 0; i <= geo.SegH; i++)
            {
                int x = geo.Left + i * geo.Step;
                if (!OverlapsAnyLine(lineSegs, x, geo.Top, hitR))
                    DrawDotStack(g, x, geo.Top, rBody, rInnerOuter, rOuterOuter,
                        main, hasInner, innerCol, hasOuter, outerCol);
                if (!OverlapsAnyLine(lineSegs, x, geo.Bottom, hitR))
                    DrawDotStack(g, x, geo.Bottom, rBody, rInnerOuter, rOuterOuter,
                        main, hasInner, innerCol, hasOuter, outerCol);
            }
            for (int j = 1; j < geo.SegV; j++)
            {
                int yy = geo.Top + j * geo.Step;
                if (!OverlapsAnyLine(lineSegs, geo.Left, yy, hitR))
                    DrawDotStack(g, geo.Left, yy, rBody, rInnerOuter, rOuterOuter,
                        main, hasInner, innerCol, hasOuter, outerCol);
                if (!OverlapsAnyLine(lineSegs, geo.Right, yy, hitR))
                    DrawDotStack(g, geo.Right, yy, rBody, rInnerOuter, rOuterOuter,
                        main, hasInner, innerCol, hasOuter, outerCol);
            }
        }

        /// <summary>由外向内三连画：外圈 → 内圈 → 本体，后画的盖住先画的中心部分。</summary>
        private static void DrawDotStack(Graphics g, int x, int y,
                                         float rBody, float rInnerOuter, float rOuterOuter,
                                         uint main,
                                         bool hasInner, uint innerCol,
                                         bool hasOuter, uint outerCol)
        {
            if (hasOuter) FillCircle(g, x, y, rOuterOuter, outerCol);
            if (hasInner) FillCircle(g, x, y, rInnerOuter, innerCol);
            FillCircle(g, x, y, rBody, main);
        }

        private static void FillCircle(Graphics g, int x, int y, float radius, uint argb)
        {
            if (radius <= 0) return;
            Color c = Argb.ColorFromUInt(argb);
            if (c.A == 0) return;
            using (var br = new SolidBrush(c))
                g.FillEllipse(br, x - radius, y - radius, radius * 2, radius * 2);
        }

        /// <summary>圆点，固定形状。</summary>
        private static void DrawDot(Graphics g, int x, int y, int size, uint argb)
        {
            Color c = Argb.ColorFromUInt(argb);
            if (c.A == 0) return;
            float half = size / 2f;
            using (var br = new SolidBrush(c))
                g.FillEllipse(br, x - half, y - half, size, size);
        }

        // ============================================================ X Mode（对角线）
        /// <summary>
        /// X Mode 的几何：交叉点 (cx, cy) 与单条对角线的半长 halfLen。
        ///
        /// 对角线取 45°，两条线的端点落在以交叉点为中心、边长 2*halfLen/√2 的正方形的四个角上，
        /// 这样两条对角线等长。Length=0 时自动取能填满整屏的值。
        /// </summary>
        /// <summary>
        /// X Mode 的两条线**沿屏幕自身对角线方向**（不是固定 45°）。
        ///
        /// 屏幕对角线方向由宽高比决定：16:9 是 atan(9/16)≈29.4°，4:3 是 36.9°，
        /// 正方形才是 45°。这样两条线正好落在屏幕上从角到角的对角线上，
        /// 与「跟随屏幕比例的点阵框」保持同一套几何语义。
        ///
        /// 输出：
        ///   cx, cy     交叉点（屏幕中心 + 用户偏移）
        ///   dirX, dirY 第一条对角线的单位方向向量（另一条取其镜像）
        ///   halfLen    单条对角线从交叉点到端点的长度
        /// </summary>
        public static void ComputeXMode(int width, int height, Profile p,
                                        out int cx, out int cy,
                                        out double dirX, out double dirY,
                                        out int halfLen)
        {
            var st = p.XLine;

            cx = width / 2 + p.XOffsetX;
            cy = height / 2 + p.XOffsetY;

            // 屏幕对角线方向：从左上角指向右下角
            dirX = width;
            dirY = height;
            double n = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (n <= 0) { dirX = 1; dirY = 0; n = 1; }
            dirX /= n;
            dirY /= n;

            // 沿对角线方向铺满整屏所需半长：
            // 求交叉点到屏幕四边在 (dirX, dirY) 方向上能走多远，取最小正值。
            int autoHalf = (int)Math.Ceiling(MaxReachAlong(cx, cy, width, height, dirX, dirY));

            halfLen = st.Length <= 0
                ? autoHalf
                : Clamp(st.Length / 2, 1, Math.Max(autoHalf * 2, 4));
        }

        /// <summary>
        /// 从点 (cx,cy) 沿单位方向 (dx,dy) 出发，在矩形 [0,w]×[0,h] 内能走多远。
        /// 同时取反方向，返回两者较大值（保证两条对角线等长且都能铺满）。
        /// </summary>
        private static double MaxReachAlong(double cx, double cy, int w, int h, double dx, double dy)
        {
            double fwd = RayHitBox(cx, cy, dx, dy, w, h);
            double back = RayHitBox(cx, cy, -dx, -dy, w, h);
            return Math.Max(fwd, back);
        }

        /// <summary>射线与矩形边界的交点距离（t>=0）。</summary>
        private static double RayHitBox(double px, double py, double dx, double dy, int w, int h)
        {
            double tMax = double.MaxValue;

            if (Math.Abs(dx) > 1e-9)
            {
                double t1 = (0 - px) / dx;
                double t2 = (w - px) / dx;
                if (t1 > 0) tMax = Math.Min(tMax, t1);
                if (t2 > 0) tMax = Math.Min(tMax, t2);
            }
            if (Math.Abs(dy) > 1e-9)
            {
                double t1 = (0 - py) / dy;
                double t2 = (h - py) / dy;
                if (t1 > 0) tMax = Math.Min(tMax, t1);
                if (t2 > 0) tMax = Math.Min(tMax, t2);
            }

            return tMax == double.MaxValue ? 0 : tMax;
        }

        private static void DrawXMode(Graphics g, int width, int height, Profile p, int alphaPct)
        {
            int cx, cy, halfLen;
            double dirX, dirY;
            ComputeXMode(width, height, p, out cx, out cy, out dirX, out dirY, out halfLen);

            var st = p.XLine;
            int thick = Clamp(st.Thickness, 1, 32);
            uint col = Argb.ScaleAlpha(st.ColorArgb, alphaPct);
            int rad = (int)Math.Round(thick / 2.0 * Clamp(st.RoundedOrder, 0, 100) / 100.0);

            int innerW = p.InnerBorderEnabled ? Clamp(p.InnerBorderThickness, 0, 16) : 0;
            int outerW = p.OuterBorderEnabled ? Clamp(p.OuterBorderThickness, 0, 16) : 0;

            uint innerCol = 0, outerCol = 0;
            bool hasInner = false, hasOuter = false;
            if (innerW > 0)
            {
                innerCol = Argb.ScaleAlpha(p.InnerBorderArgb, p.InnerBorderOpacityPercent * alphaPct / 100);
                hasInner = Argb.ColorFromUInt(innerCol).A > 0;
            }
            if (outerW > 0)
            {
                outerCol = Argb.ScaleAlpha(p.OuterBorderArgb, p.OuterBorderOpacityPercent * alphaPct / 100);
                hasOuter = Argb.ColorFromUInt(outerCol).A > 0;
            }

            int effInner = hasInner ? innerW : 0;
            int effOuter = hasOuter ? outerW : 0;

            float gapHalf = Clamp(p.XGapPx, 0, Math.Max(0, halfLen * 2 - 4)) / 2f;
            if (halfLen - gapHalf <= 0) return;

            // 两条线：沿屏幕对角线 (dirX,dirY) 与其镜像 (dirX,-dirY)。
            // 每条线各向两端延伸，共四段。
            double[][] axes =
            {
                new double[] { dirX, dirY },
                new double[] { dirX, -dirY }
            };
            int[] signs = { 1, -1 };

            for (int a = 0; a < axes.Length; a++)
            {
                double ax = axes[a][0], ay = axes[a][1];
                for (int s = 0; s < signs.Length; s++)
                {
                    double sg = signs[s];
                    float sx = (float)(cx + ax * gapHalf * sg);
                    float sy = (float)(cy + ay * gapHalf * sg);
                    float ex = (float)(cx + ax * halfLen * sg);
                    float ey = (float)(cy + ay * halfLen * sg);

                    DrawDiagonalSegment(g, sx, sy, ex, ey, thick, rad, col,
                        hasInner, innerCol, effInner, hasOuter, outerCol, effOuter);
                }
            }
        }

        /// <summary>
        /// 画一段任意方向的分段（用旋转坐标系把"倾斜的粗线段"当矩形处理）。
        /// 由外向内三层：外圈 → 内圈 → 本体。
        /// </summary>
        private static void DrawDiagonalSegment(Graphics g, float x0, float y0, float x1, float y1,
                                                int thick, int rad, uint col,
                                                bool hasInner, uint innerCol, int effInner,
                                                bool hasOuter, uint outerCol, int effOuter)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0.5f) return;

            var state = g.Save();
            try
            {
                // 以起点为原点、沿线段方向建立坐标系
                g.TranslateTransform(x0, y0);
                g.RotateTransform((float)(Math.Atan2(dy, dx) * 180.0 / Math.PI));

                var body = new RectangleF(0, -thick / 2f, len, thick);

                if (hasOuter)
                {
                    int pad = effInner + effOuter;
                    var outer = new RectangleF(body.X - pad, body.Y - pad,
                        body.Width + pad * 2, body.Height + pad * 2);
                    FillRoundedF(g, outer, outerCol, rad + pad);
                }
                if (hasInner)
                {
                    var inner = new RectangleF(body.X - effInner, body.Y - effInner,
                        body.Width + effInner * 2, body.Height + effInner * 2);
                    FillRoundedF(g, inner, innerCol, rad + effInner);
                }
                FillRoundedF(g, body, col, rad);
            }
            finally { g.Restore(state); }
        }

        private static void FillRoundedF(Graphics g, RectangleF r, uint argb, int radius)
        {
            Color c = Argb.ColorFromUInt(argb);
            if (c.A == 0) return;
            using (var br = new SolidBrush(c))
            {
                if (radius <= 0) { g.FillRectangle(br, r); return; }
                using (var path = RoundRectF(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2)))
                    g.FillPath(br, path);
            }
        }

        private static GraphicsPath RoundRectF(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ============================================================ 中央横线（断开）
        private static void DrawCenterLine(Graphics g, int width, int height, Profile p, int alphaPct)
        {
            int y, l0, l1, r0, r1;
            ComputeCenterLine(width, height, p, out y, out l0, out l1, out r0, out r1);

            var st = p.CenterLine;
            int thick = Clamp(st.Thickness, 1, 32);
            uint col = Argb.ScaleAlpha(st.ColorArgb, alphaPct);
            int rad = (int)Math.Round(thick / 2.0 * Clamp(st.RoundedOrder, 0, 100) / 100.0);

            // 与点阵共用同一套两层描边参数（颜色/透明度/粗细各自独立）
            int innerW = p.InnerBorderEnabled ? Clamp(p.InnerBorderThickness, 0, 16) : 0;
            int outerW = p.OuterBorderEnabled ? Clamp(p.OuterBorderThickness, 0, 16) : 0;

            uint innerCol = 0, outerCol = 0;
            bool hasInner = false, hasOuter = false;
            if (innerW > 0)
            {
                innerCol = Argb.ScaleAlpha(p.InnerBorderArgb, p.InnerBorderOpacityPercent * alphaPct / 100);
                hasInner = Argb.ColorFromUInt(innerCol).A > 0;
            }
            if (outerW > 0)
            {
                outerCol = Argb.ScaleAlpha(p.OuterBorderArgb, p.OuterBorderOpacityPercent * alphaPct / 100);
                hasOuter = Argb.ColorFromUInt(outerCol).A > 0;
            }

            int effInner = hasInner ? innerW : 0;
            int effOuter = hasOuter ? outerW : 0;

            DrawHorizontalSegment(g, l0, y, Math.Max(0, l1 - l0), thick, rad, col,
                hasInner, innerCol, effInner, hasOuter, outerCol, effOuter);
            DrawHorizontalSegment(g, r0, y, Math.Max(0, r1 - r0), thick, rad, col,
                hasInner, innerCol, effInner, hasOuter, outerCol, effOuter);
        }

        /// <summary>由外向内画三块圆角矩形：外圈 → 内圈 → 本体。</summary>
        private static void DrawHorizontalSegment(Graphics g, int x, int cy, int len, int thick, int rad,
                                                  uint col,
                                                  bool hasInner, uint innerCol, int effInner,
                                                  bool hasOuter, uint outerCol, int effOuter)
        {
            if (len <= 0) return;

            var body = new Rectangle(x, cy - thick / 2, len, thick);

            if (hasOuter)
            {
                int pad = effInner + effOuter;
                var outer = new Rectangle(body.X - pad, body.Y - pad, body.Width + pad * 2, body.Height + pad * 2);
                FillRounded(g, outer, outerCol, rad + pad);
            }
            if (hasInner)
            {
                var inner = new Rectangle(body.X - effInner, body.Y - effInner,
                                          body.Width + effInner * 2, body.Height + effInner * 2);
                FillRounded(g, inner, innerCol, rad + effInner);
            }
            FillRounded(g, body, col, rad);
        }

        // ============================================================ 工具
        private static void FillRounded(Graphics g, Rectangle r, uint argb, int radius)
        {
            Color c = Argb.ColorFromUInt(argb);
            if (c.A == 0) return;
            using (var br = new SolidBrush(c))
            {
                if (radius <= 0) { g.FillRectangle(br, r); return; }
                using (var path = RoundRect(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2)))
                    g.FillPath(br, path);
            }
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}


