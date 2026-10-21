using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgoAnchor
{
    /// <summary>
    /// 取色对话框：原生 ColorDialog 选 RGB，再叠 alpha 滑杆
    /// （ColorDialog 本身不带透明度，而覆盖层全部需要半透明）。
    /// 带实时预览色块与十六进制显示。
    /// </summary>
    public sealed class ColorPicker : Form
    {
        private readonly NumericUpDown nudA, nudR, nudG, nudB;
        private readonly Panel swatch;
        private readonly Label hex;
        public uint ResultValue { get; private set; }

        private readonly float ui;

        public ColorPicker(uint initial, string title)
        {
            ui = 1f;
            try
            {
                var s = DpiHelper.GetScaleForScreen(Screen.PrimaryScreen);
                if (s >= 1.0 && s <= 2.5) ui = (float)s;
            }
            catch { }

            var c0 = Argb.ColorFromUInt(initial);
            if (c0.A == 0) c0 = Color.FromArgb(240, 255, 232, 176);

            Text = string.IsNullOrEmpty(title) ? L.T("color.title") : title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(392), S(230));
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(30, 32, 38);
            ForeColor = Color.FromArgb(232, 235, 242);

            swatch = new Panel
            {
                Left = S(16),
                Top = S(16),
                Width = S(120),
                Height = S(96),
                BackColor = c0,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(swatch);

            hex = new Label
            {
                Left = S(16),
                Top = S(118),
                Width = S(120),
                Height = S(20),
                ForeColor = Color.FromArgb(160, 200, 230),
                Font = new Font("Consolas", 9f)
            };
            Controls.Add(hex);

            int rx = S(152);
            nudR = Channel("R", rx, S(16));
            nudG = Channel("G", rx, S(48));
            nudB = Channel("B", rx, S(80));
            nudA = Channel("A", rx, S(112));
            nudA.Maximum = 255;

            nudR.Value = c0.R;
            nudG.Value = c0.G;
            nudB.Value = c0.B;
            nudA.Value = c0.A;

            nudR.ValueChanged += (s, e) => Sync();
            nudG.ValueChanged += (s, e) => Sync();
            nudB.ValueChanged += (s, e) => Sync();
            nudA.ValueChanged += (s, e) => Sync();

            var btnNative = new Button
            {
                Text = L.T("color.system"),
                Left = S(152),
                Top = S(148),
                Width = S(120),
                Height = S(32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 66),
                ForeColor = Color.FromArgb(225, 230, 240)
            };
            btnNative.FlatAppearance.BorderColor = Color.FromArgb(84, 92, 106);
            btnNative.Click += (s, e) =>
            {
                using (var cd = new ColorDialog { FullOpen = true, Color = swatch.BackColor })
                {
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        nudR.Value = cd.Color.R;
                        nudG.Value = cd.Color.G;
                        nudB.Value = cd.Color.B;
                        Sync();
                    }
                }
            };
            Controls.Add(btnNative);

            var btnOk = new Button
            {
                Text = L.T("color.ok"),
                Left = S(206),
                Top = S(184),
                Width = S(82),
                Height = S(32),
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(58, 118, 158),
                ForeColor = Color.White
            };
            btnOk.FlatAppearance.BorderColor = Color.FromArgb(90, 150, 190);
            btnOk.Click += (s, e) =>
            {
                ResultValue = Argb.UIntFromColor(Color.FromArgb(
                    (int)nudA.Value, (int)nudR.Value, (int)nudG.Value, (int)nudB.Value));
            };
            Controls.Add(btnOk);

            var btnCancel = new Button
            {
                Text = L.T("color.cancel"),
                Left = S(294),
                Top = S(184),
                Width = S(82),
                Height = S(32),
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 56, 66),
                ForeColor = Color.FromArgb(225, 230, 240)
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(84, 92, 106);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Sync();
        }

        private int S(int v) { return (int)Math.Round(v * ui); }

        private NumericUpDown Channel(string name, int x, int y)
        {
            var lbl = new Label
            {
                Text = name,
                Left = x - S(20),
                Top = y + S(3),
                Width = S(18),
                ForeColor = Color.FromArgb(198, 205, 218),
                Font = new Font("Consolas", 9f)
            };
            Controls.Add(lbl);

            var n = new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = S(62),
                Maximum = 255,
                Minimum = 0,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(n);
            return n;
        }

        private void Sync()
        {
            var c = Color.FromArgb(255, (int)nudR.Value, (int)nudG.Value, (int)nudB.Value);
            swatch.BackColor = Color.FromArgb(255, c);
            hex.Text = string.Format("#{0:X2}{1:X2}{2:X2}  A{3}", c.R, c.G, c.B, (int)nudA.Value);
        }
    }
}
