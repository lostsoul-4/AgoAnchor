using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgoAnchor
{
    /// <summary>
    /// 设置面板。
    /// 布局策略：绝对定位 + 顶部锚定，窗口可自由缩放（Sizable），
    /// 底部按钮栏与右侧预览区随窗口拉伸，左侧参数区高度自适应并保持不重叠。
    ///
    /// 交互要点：每个数值都可双击进入编辑（Enter 确认 / Esc 取消），
    /// 因为滑杆很难精确调到 260px 这种具体数值。
    /// </summary>
    public sealed class SettingsForm : Form
    {
        private readonly Config cfg;
        private readonly Action<Config> onChanged;
        /// <summary>可选：要求引擎按当前屏幕拓扑强制重建覆盖窗口（分辨率/DPI 变化后使用）。</summary>
        private readonly Action onForceRebuild;
        /// <summary>点击「暂停」时通知宿主切换暂停状态（与托盘菜单共用同一状态）。</summary>
        private readonly Action onTogglePause;
        /// <summary>当前是否处于暂停（由宿主同步过来）。</summary>
        private bool paused;
        private bool loading = true;
        private readonly float ui;

        private readonly Dictionary<string, Control> refs = new Dictionary<string, Control>();
        private readonly List<NumRow> rows = new List<NumRow>();
        private readonly Dictionary<Control, string> labelKeys = new Dictionary<Control, string>();
        private readonly Dictionary<Button, string> buttonKeys = new Dictionary<Button, string>();
        private readonly Dictionary<GroupBox, string> groupKeys = new Dictionary<GroupBox, string>();

        private Panel previewHost;
        private FlowLayoutPanel footer;
        private Button btnReset, btnExit, btnPause;
        private Label footerHint;
        private GroupBox gColors, gPrev, gScreens, gSys, gProfile;
        private TabControl tabs;
        private TabPage tpGrid, tpCenter, tpX;
        private readonly Dictionary<TabPage, string> tabPageKeys = new Dictionary<TabPage, string>();

        /// <summary>一行数值控件：滑杆 + 可双击编辑的数值框。</summary>
        private class NumRow
        {
            public string Key;
            public TrackBar Bar;
            public TextBox Edit;
            public bool Editing;
            public int Min;
            public int Max;
            public Func<int, string> Format;   // 显示用，例如 "260 px"
            public Action<int> Apply;
        }

        public SettingsForm(Config c, Action<Config> changed) : this(c, changed, null, null) { }

        public SettingsForm(Config c, Action<Config> changed, Action forceRebuild)
            : this(c, changed, forceRebuild, null) { }

        public SettingsForm(Config c, Action<Config> changed, Action forceRebuild, Action togglePause)
        {
            cfg = c;
            onChanged = changed;
            onForceRebuild = forceRebuild;
            onTogglePause = togglePause;
            L.Current = cfg.Language;
            ui = GetUiScale();

            Text = L.T("app.title");
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            // 关键兜底：即使托盘图标失效，用户仍能从任务栏/Alt+Tab 找回设置窗口。
            // 隐藏到托盘时本程序不会出现在任务栏，但最小化（而非隐藏）时仍然可见。
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(30, 32, 38);
            ForeColor = Color.FromArgb(232, 235, 242);
            DoubleBuffered = true;
            MinimumSize = new Size(S(940), S(680));

            // 还原上次窗口尺寸；没有记录则用默认
            int w = cfg.WindowWidth > 0 ? cfg.WindowWidth : S(1060);
            int h = cfg.WindowHeight > 0 ? cfg.WindowHeight : S(800);
            ClientSize = new Size(w, h);

            // 窗口图标（任务栏 / Alt+Tab）：从 exe 内嵌资源取，不依赖外部 .ico 文件
            try { Icon = Program.BuildIcon(); }
            catch { }

            BuildUi();
            LoadFromConfig();
            loading = false;
            ApplyLanguage();

            // 最小尺寸按**实际内容**算出：正好能让最长的 Tab 页（X Mode）不出现滚动条。
            // 之前写死 680 会导致缩小时 Tab 页内容被压出竖直滚动条，很难看。
            //
            // 顺序很关键：先 LayoutAll() 完成一次摆放（子控件才有真实坐标），
            // 再据此算最小尺寸；算完若当前窗口比最小值还小，就撑到最小值。
            LayoutAll();
            Size min = ComputeMinSize();
            MinimumSize = min;

            int needW = min.Width - Math.Max(0, Width - ClientSize.Width);
            int needH = min.Height - Math.Max(0, Height - ClientSize.Height);
            if (ClientSize.Width < needW || ClientSize.Height < needH)
                ClientSize = new Size(Math.Max(ClientSize.Width, needW),
                                      Math.Max(ClientSize.Height, needH));
            LayoutAll();
        }

        private static float GetUiScale()
        {
            try
            {
                var s = DpiHelper.GetScaleForScreen(Screen.PrimaryScreen);
                if (s < 1.0) s = 1.0;
                if (s > 2.5) s = 2.5;
                return (float)s;
            }
            catch { return 1f; }
        }

        private int S(int v) { return (int)Math.Round(v * ui); }
        private Font F(float pt) { return new Font("Microsoft YaHei UI", pt * Math.Min(ui, 1.4f), FontStyle.Regular, GraphicsUnit.Point); }
        private Font FB(float pt) { return new Font("Microsoft YaHei UI", pt * Math.Min(ui, 1.4f), FontStyle.Bold, GraphicsUnit.Point); }

        // ============================================================ 构建
        private void BuildUi()
        {
            gProfile = MakeGroup("grp.profile", S(14), S(8), S(500), S(58));
            BuildProfileBar(gProfile);

            // ---- 左侧模式区：Tab 分页（点阵 / 中央线 / X Mode）----
            tabs = new TabControl
            {
                Left = S(14),
                Top = S(74),
                Width = S(500),
                Height = S(430),
                Font = FB(9f),
                Appearance = TabAppearance.Normal,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom
            };
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.ItemSize = new Size(S(96), S(26));
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.DrawItem += OnDrawTab;
            Controls.Add(tabs);

            tpGrid = MakeTabPage("tab.grid");
            BuildGridGroup(tpGrid);

            tpCenter = MakeTabPage("tab.center");
            BuildCenterGroup(tpCenter);

            tpX = MakeTabPage("tab.xmode");
            BuildXGroup(tpX);

            // ---- 左下：颜色板块（点阵色 / 内边框 / 外边框）----
            gColors = MakeGroup("grp.colors", S(14), S(514), S(500), S(268));
            BuildColorsGroup(gColors);

            gPrev = MakeGroup("grp.preview", S(526), S(74), S(500), S(300));
            BuildPreview(gPrev);

            gScreens = MakeGroup("grp.screens", S(526), S(382), S(500), S(150));
            BuildScreens(gScreens);

            gSys = MakeGroup("grp.system", S(526), S(540), S(500), S(230));
            BuildSystem(gSys);

            BuildFooter();
        }

        private TabPage MakeTabPage(string key)
        {
            var tp = new TabPage
            {
                BackColor = Color.FromArgb(30, 32, 38),
                ForeColor = Color.FromArgb(232, 235, 242),
                Text = L.T(key),
                // 窗口缩到最小尺寸时内容可能超出，允许页内滚动而不是被裁掉
                AutoScroll = true
            };
            tabPageKeys[tp] = key;
            tabs.TabPages.Add(tp);
            return tp;
        }

        private void OnDrawTab(object sender, DrawItemEventArgs e)
        {
            var tc = (TabControl)sender;
            bool selected = e.Index == tc.SelectedIndex;
            var title = tc.TabPages.Count > e.Index
                ? tabPageKeys[tc.TabPages[e.Index]].Length > 0 ? L.T(tabPageKeys[tc.TabPages[e.Index]]) : tc.TabPages[e.Index].Text
                : "";

            using (var bg = new SolidBrush(selected
                ? Color.FromArgb(44, 48, 58)
                : Color.FromArgb(24, 26, 31)))
                e.Graphics.FillRectangle(bg, e.Bounds);

            if (selected)
            {
                using (var line = new Pen(Color.FromArgb(120, 200, 232), S(2)))
                    e.Graphics.DrawLine(line, e.Bounds.Left + S(4), e.Bounds.Bottom - S(2),
                                              e.Bounds.Right - S(4), e.Bounds.Bottom - S(2));
            }

            TextRenderer.DrawText(e.Graphics, title, F(9f), e.Bounds,
                selected ? Color.FromArgb(120, 200, 232) : Color.FromArgb(150, 158, 172),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private GroupBox MakeGroup(string key, int x, int y, int w, int h)
        {
            var g = new GroupBox
            {
                Left = x,
                Top = y,
                Width = w,
                Height = h,
                ForeColor = Color.FromArgb(120, 200, 232),
                Font = FB(9f),
                BackColor = Color.FromArgb(30, 32, 38),
                Text = L.T(key),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            groupKeys[g] = key;
            Controls.Add(g);
            return g;
        }

        private Label AddLabel(Control parent, string key, int x, int y, int w)
        {
            var l = new Label
            {
                Left = x,
                Top = y,
                Width = w,
                Height = S(20),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(198, 205, 218),
                Font = F(9f),
                Text = L.T(key)
            };
            parent.Controls.Add(l);
            labelKeys[l] = key;
            return l;
        }

        // ---------------------------------------------------------- Profile 栏
        private ComboBox cbProfile;

        private void BuildProfileBar(GroupBox g)
        {
            int y = S(22);
            AddLabel(g, "profile.current", S(12), y + S(3), S(76));

            cbProfile = new ComboBox
            {
                Left = S(92),
                Top = y,
                Width = S(180),
                Height = S(24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            cbProfile.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                int idx = cbProfile.SelectedIndex;
                if (idx < 0 || idx == cfg.ActiveProfile) return;
                cfg.ActiveProfile = idx;
                LoadFromConfig();
                Commit();
            };
            refs["profile.combo"] = cbProfile;
            g.Controls.Add(cbProfile);

            var btnRename = MakeBtn("profile.rename", S(280), y - S(2), S(70));
            btnRename.Click += (s, e) => RenameProfile();
            g.Controls.Add(btnRename);

            var btnAdd = MakeBtn("profile.add", S(356), y - S(2), S(70));
            btnAdd.Click += (s, e) =>
            {
                cfg.AddProfile(L.T("profile.newname"));
                LoadFromConfig();
                Commit();
            };
            g.Controls.Add(btnAdd);

            var btnDel = MakeBtn("profile.delete", S(432), y - S(2), S(60));
            btnDel.Click += (s, e) =>
            {
                if (cfg.Profiles.Count <= 1) return;
                cfg.RemoveProfile(cfg.ActiveProfile);
                LoadFromConfig();
                Commit();
            };
            g.Controls.Add(btnDel);
        }

        private void RenameProfile()
        {
            using (var dlg = new InputDialog(L.T("profile.rename.title"), L.T("profile.name"), cfg.P.Name))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Value.Trim().Length > 0)
                {
                    cfg.P.Name = dlg.Value.Trim();
                    LoadProfileCombo();
                    Commit();
                }
            }
        }

        /// <summary>
        /// 把 profile 名显示给用户。内置的默认名字（"默认"/"Profile 2"…）随界面语言本地化，
        /// 用户自己起的名字原样显示。
        /// </summary>
        private static string DisplayProfileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name == "默认") return L.T("profile.default");
            if (name == "Profile 2") return L.T("profile.p2");
            if (name == "Profile 3") return L.T("profile.p3");
            if (name == "新方案") return L.T("profile.newname");
            if (name == "New profile") return L.T("profile.newname");
            return name;
        }

        private void LoadProfileCombo()
        {
            bool was = loading;
            loading = true;
            int sel = cfg.ActiveProfile;
            cbProfile.BeginUpdate();
            cbProfile.Items.Clear();
            for (int i = 0; i < cfg.Profiles.Count; i++) cbProfile.Items.Add(DisplayProfileName(cfg.Profiles[i].Name));
            cbProfile.EndUpdate();
            if (sel >= 0 && sel < cbProfile.Items.Count) cbProfile.SelectedIndex = sel;
            loading = was;
        }

        // ---------------------------------------------------------- 点阵
        private CheckBox chkGrid, chkLineOverDot;

        private void BuildGridGroup(TabPage g)
        {
            int y = S(24);

            chkGrid = MakeCheck(g, "grid.enabled", S(14), y, S(180));
            chkGrid.CheckedChanged += (s, e) => { if (loading) { } else { cfg.P.GridEnabled = chkGrid.Checked; Commit(); } };
            y += S(30);

            AddNumRow(g, "grid.size", y, 50, 90, 5, v => v + " %", v => cfg.P.FramePercent = v, false); y += S(36);
            AddNumRow(g, "grid.density", y, 4, 200, 10, v => v + " px", v => cfg.P.DotSpacing = v, false); y += S(36);
            AddNumRow(g, "grid.dotsize", y, 1, 32, 2, v => v + " px", v => cfg.P.DotSize = v, false); y += S(36);
            AddNumRow(g, "grid.opacity", y, 0, 100, 10, v => v + " %", v => cfg.P.GridOpacityPercent = v, false); y += S(42);

            var hint = AddLabel(g, "grid.hint2", S(14), y, S(420));
            hint.ForeColor = Color.FromArgb(140, 150, 165);
            hint.Font = F(8.2f);
        }

        // ---------------------------------------------------------- 颜色板块
        private CheckBox chkInner, chkOuter;

        private void BuildColorsGroup(GroupBox g)
        {
            int y = S(24);

            AddLabel(g, "color.palette", S(14), y, S(200)).ForeColor = Color.FromArgb(120, 200, 232);
            y += S(24);

            // 主色：点阵 / 中央线 / X Mode **三者共用同一个颜色**，改一处三处同步
            var btnDot = MakeBtn("color.dot", S(14), y, S(158));
            btnDot.Click += (s, e) =>
            {
                using (var dlg = new ColorPicker(cfg.P.DotColorArgb, L.T("color.dot")))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    ApplyMainColor(dlg.ResultValue);
                    RefreshSwatches();
                    Commit();
                }
            };
            refs["swatch.main"] = btnDot;
            g.Controls.Add(btnDot);

            // 内边框颜色
            var btnInnerColor = MakeBtn("border.inner.color", S(178), y, S(158));
            btnInnerColor.Click += (s, e) => PickBorderColor(true, btnInnerColor);
            refs["swatch.inner"] = btnInnerColor;
            g.Controls.Add(btnInnerColor);

            // 外边框颜色
            var btnOuterColor = MakeBtn("border.outer.color", S(342), y, S(146));
            btnOuterColor.Click += (s, e) => PickBorderColor(false, btnOuterColor);
            refs["swatch.outer"] = btnOuterColor;
            g.Controls.Add(btnOuterColor);
            y += S(34);

            // 一道分隔
            var sep = new Label { Left = S(14), Top = y, Width = S(474), Height = 1, BackColor = Color.FromArgb(52, 58, 70) };
            g.Controls.Add(sep);
            y += S(10);

            // ---- 线与点不重叠（跨越所有元素的绘制规则，放在此处分隔区） ----
            chkLineOverDot = MakeCheck(g, "grid.lineover", S(14), y, S(400));
            chkLineOverDot.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                cfg.P.LineOverDot = chkLineOverDot.Checked;
                Commit();
            };
            y += S(26);

            var lh = AddLabel(g, "grid.hint", S(34), y, S(420));
            lh.ForeColor = Color.FromArgb(140, 150, 165);
            lh.Font = F(8.2f);
            y += S(26);

            // ---- 内边框 ----
            chkInner = MakeCheck(g, "border.inner.enabled", S(14), y, S(160));
            chkInner.CheckedChanged += (s, e) => { if (loading) { } else { cfg.P.InnerBorderEnabled = chkInner.Checked; Commit(); } };
            y += S(30);

            AddNumRow(g, "border.inner.thickness", y, 0, 16, 1, v => v + " px",
                v => cfg.P.InnerBorderThickness = v, false); y += S(34);
            AddNumRow(g, "border.inner.opacity", y, 0, 100, 10, v => v + " %",
                v => cfg.P.InnerBorderOpacityPercent = v, false); y += S(38);

            // ---- 外边框 ----
            chkOuter = MakeCheck(g, "border.outer.enabled", S(14), y, S(160));
            chkOuter.CheckedChanged += (s, e) => { if (loading) { } else { cfg.P.OuterBorderEnabled = chkOuter.Checked; Commit(); } };
            y += S(30);

            AddNumRow(g, "border.outer.thickness", y, 0, 16, 1, v => v + " px",
                v => cfg.P.OuterBorderThickness = v, false); y += S(34);
            AddNumRow(g, "border.outer.opacity", y, 0, 100, 10, v => v + " %",
                v => cfg.P.OuterBorderOpacityPercent = v, false); y += S(34);

            var hint = AddLabel(g, "border.hint", S(14), y, S(470));
            hint.ForeColor = Color.FromArgb(140, 150, 165);
            hint.Font = F(8.2f);
        }

        // ---------------------------------------------------------- X Mode
        private CheckBox chkX;

        private void BuildXGroup(TabPage g)
        {
            int y = S(24);

            chkX = MakeCheck(g, "x.enabled", S(14), y, S(200));
            chkX.CheckedChanged += (s, e) => { if (loading) { } else { cfg.P.XModeEnabled = chkX.Checked; Commit(); } };
            y += S(30);

            // 交叉点水平偏移（对角线的"线"以交叉点定位，因此横竖两个方向都可调）
            AddNumRow(g, "x.offsetx", y, -800, 800, 20, v => (v > 0 ? "+" : "") + v + " px",
                v => cfg.P.XOffsetX = v, true); y += S(36);
            AddNumRow(g, "x.offsety", y, -800, 800, 20, v => (v > 0 ? "+" : "") + v + " px",
                v => cfg.P.XOffsetY = v, true); y += S(36);
            AddNumRow(g, "x.gap", y, 0, 1600, 20, v => v + " px", v => cfg.P.XGapPx = v, true); y += S(36);
            AddNumRow(g, "x.length", y, 0, 8000, 100,
                v => v == 0 ? L.T("center.length.full") : v + " px",
                v => cfg.P.XLine.Length = v, false); y += S(36);
            AddNumRow(g, "x.thickness", y, 1, 32, 2, v => v + " px", v => cfg.P.XLine.Thickness = v, false); y += S(36);
            AddNumRow(g, "x.radius", y, 0, 100, 10, v => v + " %", v => cfg.P.XLine.RoundedOrder = v, false); y += S(36);
            AddNumRow(g, "x.opacity", y, 0, 100, 10, v => v + " %", v => cfg.P.XOpacityPercent = v, false); y += S(36);

            var hint = AddLabel(g, "x.hint", S(14), y, S(420));
            hint.ForeColor = Color.FromArgb(140, 150, 165);
            hint.Font = F(8.2f);
        }

        private void PickBorderColor(bool inner, Button btn)
        {
            uint cur = inner ? cfg.P.InnerBorderArgb : cfg.P.OuterBorderArgb;
            string title = L.T(inner ? "border.inner.color" : "border.outer.color");
            using (var dlg = new ColorPicker(cur, title))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (inner) cfg.P.InnerBorderArgb = dlg.ResultValue;
                else cfg.P.OuterBorderArgb = dlg.ResultValue;
                RefreshSwatches();
                Commit();
            }
        }

        // ---------------------------------------------------------- 中央横线
        private CheckBox chkCenter;

        private void BuildCenterGroup(TabPage g)
        {
            int y = S(24);

            chkCenter = MakeCheck(g, "center.enabled", S(14), y, S(200));
            chkCenter.CheckedChanged += (s, e) => { if (loading) return; cfg.P.CenterLineEnabled = chkCenter.Checked; Commit(); };
            y += S(30);

            AddNumRow(g, "center.gap", y, 0, 1600, 20, v => v + " px", v => cfg.P.CenterGapPx = v, true); y += S(36);
            AddNumRow(g, "center.offset", y, -800, 800, 20, v => (v > 0 ? "+" : "") + v + " px", v => cfg.P.CenterOffsetY = v, true); y += S(36);
            AddNumRow(g, "center.length", y, 0, 4000, 100,
                v => v == 0 ? L.T("center.length.full") : v + " px",
                v => cfg.P.CenterLine.Length = v, false); y += S(36);
            AddNumRow(g, "center.thickness", y, 1, 32, 2, v => v + " px", v => cfg.P.CenterLine.Thickness = v, false); y += S(36);
            AddNumRow(g, "center.radius", y, 0, 100, 10, v => v + " %", v => cfg.P.CenterLine.RoundedOrder = v, false); y += S(36);
            AddNumRow(g, "center.opacity", y, 0, 100, 10, v => v + " %", v => cfg.P.CenterOpacityPercent = v, false); y += S(36);

            var hint = AddLabel(g, "center.hint", S(14), y, S(470));
            hint.ForeColor = Color.FromArgb(140, 150, 165);
            hint.Font = F(8.2f);
        }

        // ---------------------------------------------------------- 数值行
        /// <summary>
        /// 滑杆 + 可编辑数值框。双击数值（或直接点进文本框）即可键入精确值，
        /// Enter 提交，Esc 取消，离焦也提交。
        /// </summary>
        private void AddNumRow(Control parent, string key, int y, int min, int max, int tick,
                               Func<int, string> fmt, Action<int> apply, bool allowDirect)
        {
            // 标签列按语言自动加宽：日文/韩文普遍比中文长（"ドットの不透明度" vs "点阵透明度"），
            // 固定 90px 会截断成"ドットの不透"。这里按当前语言给一档额外宽度。
            int labelW = L.Current == Lang.Chinese ? S(90) : S(124);
            int lx = S(12), barX = lx + labelW + S(4);
            int barW = S(250) - (labelW - S(90));      // 挤出来的宽度从滑条上让
            if (barW < S(170)) barW = S(170);
            int valX = barX + barW + S(8), valW = S(122);

            AddLabel(parent, key, lx, y + S(6), labelW);

            var bar = new TrackBar
            {
                Left = barX,
                Top = y,
                Width = barW,
                Height = S(28),
                Minimum = min,
                Maximum = max,
                TickFrequency = Math.Max(1, tick),
                SmallChange = 1,
                LargeChange = Math.Max(1, Math.Abs(max - min) / 10),
                AutoSize = false,
                BackColor = Color.FromArgb(30, 32, 38)
            };

            var edit = new TextBox
            {
                Left = valX,
                Top = y + S(4),
                Width = valW,
                Height = S(22),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(30, 32, 38),
                ForeColor = Color.FromArgb(170, 210, 240),
                Font = F(8.8f),
                TextAlign = HorizontalAlignment.Left,
                ReadOnly = false
            };

            var row = new NumRow
            {
                Key = key, Bar = bar, Edit = edit, Min = min, Max = max, Format = fmt, Apply = apply
            };
            rows.Add(row);

            // 滑杆拖动 -> 立即应用
            bar.ValueChanged += (s, e) =>
            {
                if (row.Editing) return;
                row.Edit.Text = fmt(bar.Value);
                if (loading) return;
                apply(bar.Value);
                Commit();
            };

            // 数值框：获得焦点即可直接输入；Enter 提交，Esc 还原
            edit.Enter += (s, e) =>
            {
                row.Editing = true;
                edit.Text = bar.Value.ToString();
                edit.SelectAll();
            };
            edit.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    int v;
                    if (int.TryParse(edit.Text.Trim(), out v))
                    {
                        v = Math.Max(min, Math.Min(max, v));
                        bar.Value = v;
                        edit.Text = fmt(v);
                        if (!loading) { apply(v); Commit(); }
                    }
                    else edit.Text = fmt(bar.Value);
                    parent.Focus();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    edit.Text = fmt(bar.Value);
                    parent.Focus();
                }
            };
            edit.Leave += (s, e) =>
            {
                if (!row.Editing) return;
                row.Editing = false;
                int v;
                if (int.TryParse(edit.Text.Trim(), out v))
                {
                    v = Math.Max(min, Math.Min(max, v));
                    bar.Value = v;
                    edit.Text = fmt(v);
                    if (!loading) { apply(v); Commit(); }
                }
                else edit.Text = fmt(bar.Value);
            };

            edit.Text = fmt(bar.Value);
            parent.Controls.Add(bar);
            parent.Controls.Add(edit);
            refs["num." + key] = edit;
        }

        // ---------------------------------------------------------- 预览
        private void BuildPreview(GroupBox g)
        {
            previewHost = new Panel
            {
                Left = S(10),
                Top = S(24),
                Width = g.Width - S(20),
                Height = g.Height - S(34),
                BackColor = Color.FromArgb(20, 22, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            previewHost.Paint += (s, e) => DrawPreview(e.Graphics, previewHost.ClientSize);
            previewHost.Resize += (s, e) => previewHost.Invalidate();
            g.Controls.Add(previewHost);
        }

        /// <summary>用真实渲染器在缩略尺寸上预览，所见即所得。</summary>
        private void DrawPreview(Graphics graphics, Size size)
        {
            if (size.Width < 16 || size.Height < 16) return;

            int wz = 320;

            // 把像素类参数按预览缩放，避免缩略图上点全糊在一起
            int w = size.Width, h = size.Height;
            double k = (double)w / wz;

            var p = cfg.P.Clone();
            p.DotSpacing = Math.Max(3, (int)Math.Round(p.DotSpacing * k));
            p.DotSize = Math.Max(1, (int)Math.Round(p.DotSize * k));
            p.CenterGapPx = (int)Math.Round(p.CenterGapPx * (double)w / 2560.0 * 2.4);
            p.CenterOffsetY = (int)Math.Round(p.CenterOffsetY * k);
            if (p.CenterLine.Length > 0) p.CenterLine.Length = Math.Max(8, (int)Math.Round(p.CenterLine.Length * k));
            p.CenterLine.Thickness = Math.Max(1, (int)Math.Round(p.CenterLine.Thickness * k));

            using (var bg = new Bitmap(w, h))
            {
                using (var bmg = Graphics.FromImage(bg))
                {
                    using (var br = new LinearGradientBrush(
                        new Point(0, 0), new Point(w, h),
                        Color.FromArgb(255, 62, 72, 86), Color.FromArgb(255, 22, 26, 32)))
                        bmg.FillRectangle(br, 0, 0, w, h);

                    // 准星区域示意：用来确认中央横线确实没有遮挡
                    using (var br2 = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                        bmg.FillEllipse(br2, w / 2 - S(3), h / 2 - S(3), S(6), S(6));

                    using (var top = Renderer.Render(w, h, p, cfg.P.GlobalOpacityPercent))
                        bmg.DrawImageUnscaled(top, 0, 0);
                }
                graphics.DrawImageUnscaled(bg, 0, 0);
            }
        }

        // ---------------------------------------------------------- 屏幕
        private CheckedListBox clbScreens;
        private readonly List<string> screenMap = new List<string>();

        private void BuildScreens(GroupBox g)
        {
            // 顶部一行：刷新按钮 + 说明。分辨率/DPI 变更后点它重新读取并立即重绘覆盖层。
            btnRefreshScreens = MakeBtn("screens.refresh", S(10), S(22), S(110));
            btnRefreshScreens.Height = S(24);
            buttonKeys[btnRefreshScreens] = "screens.refresh";
            btnRefreshScreens.Click += (s, e) => RefreshScreensNow();
            g.Controls.Add(btnRefreshScreens);

            lblScreenHint = new Label
            {
                Left = S(128),
                Top = S(26),
                Width = Math.Max(S(120), g.Width - S(140)),
                Height = S(18),
                AutoSize = false,
                ForeColor = Color.FromArgb(140, 150, 165),
                Font = F(8.2f),
                Text = L.T("screens.refresh.hint"),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            labelKeys[lblScreenHint] = "screens.refresh.hint";
            g.Controls.Add(lblScreenHint);

            clbScreens = new CheckedListBox
            {
                Left = S(10),
                Top = S(52),
                Width = g.Width - S(20),
                Height = Math.Max(S(60), g.Height - S(62)),
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                BackColor = Color.FromArgb(24, 26, 31),
                ForeColor = Color.FromArgb(225, 230, 240),
                Font = F(8.8f),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            clbScreens.ItemCheck += (s, e) => { if (!loading) BeginInvoke(new Action(PullScreens)); };
            refs["screens"] = clbScreens;
            g.Controls.Add(clbScreens);
            LoadScreens();
        }

        private Button btnRefreshScreens;
        private Label lblScreenHint;

        /// <summary>
        /// 立即重新读取当前屏幕分辨率与 DPI，并让覆盖层按新尺寸重绘。
        ///
        /// 用途：CS 等游戏从 16:9 切到 4:3（或改 DPI 缩放）时，系统可能不会及时触发
        /// WM_DISPLAYCHANGE，覆盖层的几何会沿用旧尺寸。点这个按钮强制重新采样。
        /// </summary>
        private void RefreshScreensNow()
        {
            try
            {
                // 1) 重新枚举屏幕，刷新列表（保留用户已勾选的项）
                var prevChecked = new List<string>();
                for (int i = 0; i < clbScreens.Items.Count && i < screenMap.Count; i++)
                    if (clbScreens.GetItemChecked(i)) prevChecked.Add(screenMap[i]);

                LoadScreens();

                // 恢复勾选状态
                if (prevChecked.Count > 0)
                {
                    bool was = loading;
                    loading = true;
                    for (int i = 0; i < screenMap.Count && i < clbScreens.Items.Count; i++)
                        clbScreens.SetItemChecked(i, prevChecked.Contains(screenMap[i]));
                    loading = was;
                }

                // 2) 让覆盖层**按新屏幕拓扑重建**（只重绘不够：窗口尺寸/位置都是旧的）
                if (onForceRebuild != null) onForceRebuild();
                if (onChanged != null) onChanged(cfg);

                lblScreenHint.Text = L.T("screens.refresh.done");
                Log.Info("screens refreshed: " + Screen.AllScreens.Length + " 屏");
            }
            catch (Exception ex)
            {
                Log.Warn("refresh screens failed: " + ex.Message);
            }
        }

        private void LoadScreens()
        {
            if (clbScreens == null) return;
            bool was = loading;
            loading = true;
            clbScreens.Items.Clear();
            screenMap.Clear();
            foreach (var s in Screen.AllScreens)
            {
                double scale = DpiHelper.GetScaleForScreen(s);
                clbScreens.Items.Add(string.Format("{0}   {1}x{2}   {3} {4}%{5}",
                    s.DeviceName, s.Bounds.Width, s.Bounds.Height,
                    L.T("screen.dpi"), Math.Round(scale * 100),
                    s.Primary ? "   [" + L.T("screen.primary") + "]" : ""));
                screenMap.Add(s.DeviceName);
                // 勾选状态严格跟随配置：配置里有的才勾。
                // 旧代码写成 "Count == 0 || IsSelected"，空列表会把所有屏幕都勾上，
                // 这就是"重新勾选时状态反着来"的根因。
                bool on = cfg.IsScreenSelected(s.DeviceName);
                clbScreens.SetItemChecked(clbScreens.Items.Count - 1, on);
            }
            loading = was;
        }

        private void PullScreens()
        {
            if (clbScreens == null) return;
            cfg.TargetScreens.Clear();
            for (int i = 0; i < clbScreens.Items.Count && i < screenMap.Count; i++)
                if (clbScreens.GetItemChecked(i)) cfg.TargetScreens.Add(screenMap[i]);

            // 注意：**不再**在"一个都没勾选"时自动填充全部屏幕。
            // 用户明确要求：没勾选 = 不显示任何屏幕。
            // 旧逻辑会自动补全，导致重新勾选时界面状态与实际相反。
            Commit();
        }

        // ---------------------------------------------------------- 系统
        private CheckBox chkAuto, chkDpi;
        private ComboBox cbClose, cbLang;

        private void BuildSystem(GroupBox g)
        {
            int y = S(24);

            AddNumRow(g, "sys.opacity", y, 0, 100, 10, v => v + " %", v => cfg.P.GlobalOpacityPercent = v, false);
            y += S(38);

            chkAuto = MakeCheck(g, "sys.autostart", S(14), y, S(440));
            chkAuto.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                AutoStart.Set(chkAuto.Checked);
                cfg.StartWithWindows = chkAuto.Checked;
                Commit();
            };
            y += S(26);

            chkDpi = MakeCheck(g, "sys.dpi", S(14), y, S(440));
            chkDpi.CheckedChanged += (s, e) => { if (!loading) { cfg.ScaleWithDpi = chkDpi.Checked; Commit(); } };
            y += S(32);

            AddLabel(g, "sys.close", S(14), y + S(3), S(78));
            cbClose = new ComboBox
            {
                Left = S(96), Top = y, Width = S(300), Height = S(24),
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat
            };
            cbClose.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                cfg.OnClose = (CloseAction)cbClose.SelectedIndex;
                Commit();
            };
            refs["sys.close"] = cbClose;
            g.Controls.Add(cbClose);
            y += S(30);

            AddLabel(g, "sys.language", S(14), y + S(3), S(78));
            cbLang = new ComboBox
            {
                Left = S(96), Top = y, Width = S(300), Height = S(24),
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat
            };
            cbLang.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                int idx = cbLang.SelectedIndex;
                if (idx < 0 || idx >= L.LangCount) idx = 0;
                cfg.Language = (Lang)idx;
                L.Current = cfg.Language;
                ApplyLanguage();
                Commit();
            };
            refs["sys.lang"] = cbLang;
            g.Controls.Add(cbLang);
            y += S(30);

            // 配置保存位置（排查用：确认配置到底写到哪了）
            var dirLabel = new Label
            {
                Left = S(14), Top = y, Width = g.Width - S(28), Height = S(18),
                ForeColor = Color.FromArgb(120, 130, 145),
                Font = F(7.8f),
                Text = L.T("sys.datadir") + " " + Paths.CurrentDir,
                AutoEllipsis = true
            };
            refs["sys.datadir"] = dirLabel;
            g.Controls.Add(dirLabel);
        }

        private CheckBox MakeCheck(Control g, string key, int x, int y, int w)
        {
            var chk = new CheckBox
            {
                Left = x, Top = y, Width = w, Height = S(22),
                Text = L.T(key), Font = F(9f), ForeColor = Color.FromArgb(230, 235, 242)
            };
            labelKeys[chk] = key;
            g.Controls.Add(chk);
            refs[key] = chk;
            return chk;
        }

        // ---------------------------------------------------------- 底部
        private void BuildFooter()
        {
            // 「隐藏设置」按钮已移除：点窗口的 × 依配置最小化到托盘，本身就是隐藏 GUI，
            // 单独再放一个隐藏按钮语义重复。
            btnReset = MakeBtn("btn.reset", 0, 0, S(112));
            btnReset.Click += (s, e) => ResetProfile();

            // 暂停 / 继续：与托盘菜单的「暂停覆盖层」是同一个状态，
            // 在面板里暂停后不必再去托盘恢复。
            btnPause = MakeBtn("btn.pause", 0, 0, S(112));
            btnPause.Click += (s, e) =>
            {
                if (onTogglePause != null) onTogglePause();
                // 状态由宿主回写（见 SetPaused），这里只触发动作
            };

            btnExit = MakeBtn("btn.exit", 0, 0, S(100));
            btnExit.BackColor = Color.FromArgb(110, 50, 58);
            btnExit.Click += (s, e) => Program.RequestExit();

            footerHint = new Label
            {
                Top = 0, Width = S(400), Height = S(20),
                ForeColor = Color.FromArgb(130, 140, 155),
                Font = F(8f),
                Text = L.T("footer.hint"),
                TextAlign = ContentAlignment.MiddleLeft
            };
            labelKeys[footerHint] = "footer.hint";

            footer = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.FromArgb(30, 32, 38),
                Padding = new Padding(0)
            };
            footer.Controls.Add(btnReset);
            footer.Controls.Add(btnPause);
            footer.Controls.Add(btnExit);
            footer.Controls.Add(footerHint);
            Controls.Add(footer);
        }

        private Button MakeBtn(string key, int x, int y, int w)
        {
            var b = new Button
            {
                Left = x, Top = y, Width = w, Height = S(28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 66),
                ForeColor = Color.FromArgb(232, 235, 242),
                Font = F(8.8f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(84, 92, 106);
            buttonKeys[b] = key;
            b.Text = L.T(key);
            return b;
        }

        /// <summary>
        /// 由宿主同步暂停状态（托盘菜单点暂停时，面板里的按钮文字也要跟着变）。
        /// </summary>
        public void SetPaused(bool isPaused)
        {
            paused = isPaused;
            if (btnPause == null) return;

            // 换 key 让多语言生效
            buttonKeys[btnPause] = paused ? "btn.resume" : "btn.pause";
            btnPause.Text = L.T(buttonKeys[btnPause]);
            btnPause.BackColor = paused
                ? Color.FromArgb(96, 78, 44)      // 暂停中：暖色，提示当前是暂停状态
                : Color.FromArgb(52, 56, 66);
        }

        // ============================================================ 自适应布局
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (loading) return;

            // 兜底：点最小化按钮时不要缩成"看不见"。
            // 若托盘图标因系统原因没显示出来，最小化后用户就再也找不到窗口了，
            // 因此这里还原成普通状态并走与 × 相同的关闭策略。
            if (WindowState == FormWindowState.Minimized && !forceClose)
            {
                WindowState = FormWindowState.Normal;
                Program.HandleCloseRequest();
                return;
            }

            LayoutAll();
        }

        /// <summary>
        /// 窗口缩放后重新摆放：右侧三块贴右边并纵向拉伸，底部按钮栏贴底，
        /// 左侧三块保持列宽、高度按可用空间均分，避免互相重叠。
        /// </summary>
        private void LayoutAll()
        {
            if (footer == null) return;

            int pad = S(14);
            int gap = S(10);
            int footerH = S(38);
            int w = ClientSize.Width, h = ClientSize.Height;

            int leftW = S(500);
            int rightX = pad + leftW + S(12);
            int rightW = Math.Max(S(340), w - rightX - pad);

            // 左侧：Profile 条 + Tab 模式区 + 颜色板块
            int topY = S(8);
            int blockTop = topY;
            int avail = h - footerH - pad - S(8);

            int hProfile = S(58);
            int rest = avail - hProfile - S(6) * 2;

            // Tab 区优先拿够「最高页面所需高度」，剩下的全部给颜色板块。
            // 只要窗口不小于 ComputeMinSize 算出的最小值就不会出现滚动条。
            int tabsNeed = TabsNaturalHeight();
            int hTabs = Math.Min(tabsNeed, rest);
            int hColors = Math.Max(S(200), rest - hTabs);
            // 若两者之和超过可用空间（空间不足时），优先保 tabs，颜色板块压缩
            if (hTabs + hColors > rest) hColors = Math.Max(S(160), rest - hTabs);

            gProfile.SetBounds(pad, blockTop, leftW, hProfile);
            blockTop += hProfile + S(6);

            tabs.SetBounds(pad, blockTop, leftW, hTabs);
            blockTop += hTabs + S(6);

            // 颜色板块用刚算好的 hColors，并夹在剩余空间内，避免溢出到底部按钮栏
            int colorBottomLimit = pad + avail - blockTop + topY;
            int colorH = Math.Min(hColors, Math.Max(S(160), colorBottomLimit));
            gColors.SetBounds(pad, blockTop, leftW, colorH);

            // 右侧三块
            int ry = S(8);
            int rPrevH = Math.Max(S(200), (int)((h - footerH - pad - S(8)) * 0.40));
            gPrev.SetBounds(rightX, ry, rightW, rPrevH);
            ry += rPrevH + S(8);

            int rScreenH = Math.Max(S(110), (int)((h - footerH - pad - S(8)) * 0.20));
            gScreens.SetBounds(rightX, ry, rightW, rScreenH);
            ry += rScreenH + S(8);

            int rSysH = Math.Max(S(210), h - footerH - pad - S(8) - ry);
            gSys.SetBounds(rightX, ry, rightW, rSysH);

            // 底部
            footer.SetBounds(pad, h - footerH + S(6), w - pad * 2, footerH - S(6));
            footerHint.Width = Math.Max(S(160), w - pad * 2 - S(360));

            Invalidate(true);
        }

        // ============================================================ 最小尺寸
        /// <summary>
        /// 按当前实际内容算出窗口的最小尺寸。
        ///
        /// 关键：必须和 LayoutAll 使用**同一套分配公式**反向求解，否则算出的值对不上。
        /// LayoutAll 里左侧是「Profile 条 + Tab + 颜色板块」按比例分配，
        /// 所以要保证的是 tabs 分到的高度 >= 最高那一页的内容高度。
        /// </summary>
        private Size ComputeMinSize()
        {
            int pad = S(14);
            int footerH = S(38);
            int hProfile = S(58);
            int chrome = hProfile + S(6) * 2;          // Profile 条 + 两处间隙

            // ---- 1) 最高的 Tab 页需要多高（与 LayoutAll 共用同一口径）----
            int tabsNeed = TabsNaturalHeight();

            // ---- 2) 颜色板块需要多高 ----
            // 按内容行数硬算，比读控件几何更可靠（首次调用时板块可能还没被布局过）：
            //   标题24 + 颜色按钮行34 + 分隔10
            // + 线与点不重叠(开关26 + 说明26)
            // + 内边框(开关30 + 粗细34 + 透明度38)
            // + 外边框(开关30 + 粗细34 + 透明度34)
            // + 提示26 + 余量20
            int colorNeed = S(24) + S(34) + S(10)
                          + S(26) + S(26)
                          + S(30) + S(34) + S(38)
                          + S(30) + S(34) + S(34)
                          + S(26) + S(20);
            if (colorNeed < S(320)) colorNeed = S(320);

            // ---- 3) 右侧一列需要多高 ----
            int rightNeed = S(200) + S(8) + S(170) + S(8) + S(280);

            // ---- 4) 反解：LayoutAll 中 avail = h - footerH - pad - 8，
            //         rest = avail - hProfile - 12，
            //         hTabs = min(tabsNeed, rest)，hColors = max(200, rest - hTabs)
            //         要同时满足 hTabs >= tabsNeed 且 hColors >= colorNeed
            double restNeed = Math.Max(tabsNeed + colorNeed, Math.Max(tabsNeed, S(240)));
            double availNeed = restNeed + hProfile + S(6) * 2;
            double leftNeed = availNeed + footerH + pad + S(8);

            // ---- 5) 右侧：avail*0.40 + avail*0.20 + 系统块 >= rightNeed ----
            //        简化：系统块取 210 下限，其余按比例
            double availForRight = (rightNeed - S(210)) / 0.60;
            double rightTotalNeed = availForRight + footerH + pad + S(8);

            int clientH = (int)Math.Ceiling(Math.Max(leftNeed, rightTotalNeed));

            // ---- 6) 宽度 ----
            int leftW = S(500);
            int rightW = Math.Max(S(340), RightColumnMinWidth());
            int clientW = pad + leftW + S(12) + rightW + pad;

            // MinimumSize 是「整个窗口」尺寸，需加上非客户区（边框 + 标题栏）。
            int ncW = Math.Max(0, Width - ClientSize.Width);
            int ncH = Math.Max(0, Height - ClientSize.Height);
            return new Size(clientW + ncW, clientH + ncH);
        }

        /// <summary>
        /// Tab 区自然需要的高度：三个页面里最底部控件的位置 + 标签栏高度。
        /// LayoutAll 与 ComputeMinSize 都用它，保证两者口径一致。
        ///
        /// 用 Top + Height 而不是 Bottom：TabPage 高度被压小时，
        /// 子控件的 Bottom 会被容器裁剪而失真，Top + Height 才是真实内容高度。
        /// </summary>
        private int TabsNaturalHeight()
        {
            int pageBottom = 0;
            foreach (var tp in new[] { tpGrid, tpCenter, tpX })
            {
                if (tp == null) continue;
                int bottom = 0;
                foreach (Control c in tp.Controls)
                {
                    int hh = c.Height > 0 ? c.Height : S(20);
                    int b = c.Top + hh;
                    if (b > bottom) bottom = b;
                }
                if (bottom > pageBottom) pageBottom = bottom;
            }
            return pageBottom + S(30) + S(10);   // 标签栏 + 余量
        }

        /// <summary>取容器内所有可见子控件的最底部坐标（用 Top+Height，不受容器裁剪影响）。</summary>
        private int ContentBottom(Control parent)
        {
            int bottom = 0;
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                int hh = c.Height > 0 ? c.Height : S(20);
                int b = c.Top + hh;
                if (b > bottom) bottom = b;
            }
            return bottom;
        }

        /// <summary>右侧一列（预览/屏幕/系统）需要的最小宽度，按最长标签文字估算。</summary>
        private int RightColumnMinWidth()
        {
            int w = S(340);

            // 关闭方式 / 界面语言 两个下拉 + 标签
            if (cbClose != null && cbClose.Parent != null)
            {
                int need = cbClose.Left + cbClose.Width + S(20);
                if (need > w) w = need;
            }
            if (cbLang != null && cbLang.Parent != null)
            {
                int need = cbLang.Left + cbLang.Width + S(20);
                if (need > w) w = need;
            }

            // 屏幕列表里最长的条目（设备名 + 分辨率 + DPI）
            if (clbScreens != null)
            {
                foreach (var item in clbScreens.Items)
                {
                    var s = item as string;
                    if (s == null) continue;
                    int need = TextRenderer.MeasureText(s, clbScreens.Font).Width + S(40);
                    if (need > w) w = need;
                }
            }

            return w;
        }

        // ============================================================ 数据流
        private void LoadFromConfig()
        {
            loading = true;
            cfg.EnsureProfile();
            LoadProfileCombo();

            var p = cfg.P;
            if (chkGrid != null) chkGrid.Checked = p.GridEnabled;
            if (chkLineOverDot != null) chkLineOverDot.Checked = p.LineOverDot;
            if (chkCenter != null) chkCenter.Checked = p.CenterLineEnabled;
            if (chkX != null) chkX.Checked = p.XModeEnabled;
            if (chkInner != null) chkInner.Checked = p.InnerBorderEnabled;
            if (chkOuter != null) chkOuter.Checked = p.OuterBorderEnabled;
            if (chkAuto != null) chkAuto.Checked = AutoStart.IsEnabled();
            if (chkDpi != null) chkDpi.Checked = cfg.ScaleWithDpi;

            SetRow("grid.size", p.FramePercent);
            SetRow("grid.density", p.DotSpacing);
            SetRow("grid.dotsize", p.DotSize);
            SetRow("grid.opacity", p.GridOpacityPercent);
            SetRow("border.inner.thickness", p.InnerBorderThickness);
            SetRow("border.inner.opacity", p.InnerBorderOpacityPercent);
            SetRow("border.outer.thickness", p.OuterBorderThickness);
            SetRow("border.outer.opacity", p.OuterBorderOpacityPercent);
            SetRow("center.gap", p.CenterGapPx);
            SetRow("center.offset", p.CenterOffsetY);
            SetRow("center.length", p.CenterLine.Length);
            SetRow("center.thickness", p.CenterLine.Thickness);
            SetRow("center.radius", p.CenterLine.RoundedOrder);
            SetRow("center.opacity", p.CenterOpacityPercent);
            SetRow("x.offsetx", p.XOffsetX);
            SetRow("x.offsety", p.XOffsetY);
            SetRow("x.gap", p.XGapPx);
            SetRow("x.length", p.XLine.Length);
            SetRow("x.thickness", p.XLine.Thickness);
            SetRow("x.radius", p.XLine.RoundedOrder);
            SetRow("x.opacity", p.XOpacityPercent);
            SetRow("sys.opacity", p.GlobalOpacityPercent);

            SetCombo(cbClose, (int)cfg.OnClose);
            SetCombo(cbLang, (int)cfg.Language);

            RefreshSwatches();
            LoadScreens();
            loading = false;

            if (previewHost != null) previewHost.Invalidate();
        }

        private void SetRow(string key, int v)
        {
            foreach (var r in rows)
            {
                if (!string.Equals(r.Key, key, StringComparison.Ordinal)) continue;
                int cv = Math.Max(r.Min, Math.Min(r.Max, v));
                r.Bar.Value = cv;
                r.Edit.Text = r.Format(cv);
                return;
            }
        }

        private void SetCombo(ComboBox cb, int idx)
        {
            if (cb == null) return;
            if (idx >= 0 && idx < cb.Items.Count) cb.SelectedIndex = idx;
        }

        private void Commit()
        {
            if (loading) return;
            if (previewHost != null) previewHost.Invalidate();
            if (onChanged != null) onChanged(cfg);
        }

        private void RefreshSwatches()
        {
            UpdateSwatch("swatch.main", L.T("color.mainshared"), cfg.P.DotColorArgb);
            UpdateSwatch("swatch.inner", L.T("border.inner.color"), cfg.P.InnerBorderArgb);
            UpdateSwatch("swatch.outer", L.T("border.outer.color"), cfg.P.OuterBorderArgb);
        }

        /// <summary>
        /// 主色同时写入点阵 / 中央线 / X Mode —— 三者共用同一个颜色。
        /// </summary>
        private void ApplyMainColor(uint argb)
        {
            cfg.P.DotColorArgb = argb;
            cfg.P.CenterLine.ColorArgb = argb;
            cfg.P.XLine.ColorArgb = argb;
        }

        private void UpdateSwatch(string refKey, string label, uint argb)
        {
            Control c;
            if (!refs.TryGetValue(refKey, out c)) return;
            var b = c as Button;
            if (b == null) return;
            var col = Argb.ColorFromUInt(argb);
            b.ForeColor = col.A < 40 ? Color.FromArgb(150, 158, 172) : Color.FromArgb(240, 244, 250);
            b.Text = label + "  ■";
        }

        private void ResetProfile()
        {
            var d = new Profile();
            d.Name = cfg.P.Name;
            d.CopyTo(cfg.P);
            LoadFromConfig();
            Commit();
        }

        // ============================================================ 语言
        private void ApplyLanguage()
        {
            Text = L.T("app.title");

            foreach (var kv in groupKeys) kv.Key.Text = L.T(kv.Value);
            foreach (var kv in labelKeys) kv.Key.Text = L.T(kv.Value);
            foreach (var kv in buttonKeys) kv.Key.Text = L.T(kv.Value);
            // 暂停按钮的文字取决于当前暂停状态，单独同步一次
            if (btnPause != null) { buttonKeys[btnPause] = paused ? "btn.resume" : "btn.pause"; btnPause.Text = L.T(buttonKeys[btnPause]); }
            RefreshSwatches();

            FillCombo(cbClose, new[] { "sys.close.ask", "sys.close.tray", "sys.close.exit" }, (int)cfg.OnClose);
            FillCombo(cbLang, new[] { "lang.chinese", "lang.english", "lang.japanese", "lang.korean" },
                (int)cfg.Language);

            foreach (var r in rows) r.Edit.Text = r.Format(r.Bar.Value);
            LoadProfileCombo();
            LoadScreens();

            if (previewHost != null) previewHost.Invalidate();
            Invalidate(true);
        }

        private void FillCombo(ComboBox cb, string[] keys, int sel)
        {
            if (cb == null) return;
            bool was = loading;
            loading = true;
            cb.BeginUpdate();
            cb.Items.Clear();
            foreach (var k in keys) cb.Items.Add(L.T(k));
            cb.EndUpdate();
            if (sel >= 0 && sel < cb.Items.Count) cb.SelectedIndex = sel;
            loading = was;
        }

        // ============================================================ 关闭行为
        private bool forceClose = false;

        public void CloseForReal()
        {
            forceClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 记住窗口尺寸
            try
            {
                if (WindowState == FormWindowState.Normal)
                {
                    cfg.WindowWidth = ClientSize.Width;
                    cfg.WindowHeight = ClientSize.Height;
                }
            }
            catch { }

            // 无论最终是隐藏还是退出，都先把配置落盘，避免改动丢失
            try { cfg.Save(); } catch { }

            if (!forceClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Program.HandleCloseRequest();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 隐藏时也立即保存一次（"最小化到托盘"不经过 FormClosing 的场景）
            if (!Visible)
            {
                try { cfg.Save(); } catch { }
            }
        }
    }

    /// <summary>简易文本输入对话框（用于重命名 profile）。</summary>
    public sealed class InputDialog : Form
    {
        public string Value { get { return txt.Text; } }
        private readonly TextBox txt;

        public InputDialog(string title, string label, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 130);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(30, 32, 38);
            ForeColor = Color.FromArgb(232, 235, 242);

            Controls.Add(new Label { Text = label, Left = 16, Top = 16, Width = 320, ForeColor = Color.FromArgb(200, 206, 218) });

            txt = new TextBox { Left = 16, Top = 42, Width = 326, Text = initial };
            Controls.Add(txt);

            var ok = new Button
            {
                Text = L.T("ok"), Left = 190, Top = 82, Width = 78, Height = 30,
                DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(58, 118, 158), ForeColor = Color.White
            };
            Controls.Add(ok);

            var cancel = new Button
            {
                Text = L.T("cancel"), Left = 274, Top = 82, Width = 68, Height = 30,
                DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 66), ForeColor = Color.FromArgb(232, 235, 242)
            };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            txt.SelectAll();
        }
    }
}































