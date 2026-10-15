using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PdfViewer
{
    // 설정 대화상자의 공통 틀: 왼쪽 설정 표, (선택) 오른쪽 미리보기, 아래 확인·취소 버튼.
    internal class ToolDialog : Form
    {
        protected readonly TableLayoutPanel Grid;
        protected readonly Panel PreviewHost;
        protected readonly FlowLayoutPanel Buttons;
        protected readonly Button OkButton;

        // 시험 실행에서 대화상자가 포커스를 가져가지 않게 한다.
        public static bool NoActivateForTest;

        protected override bool ShowWithoutActivation
        {
            get { return NoActivateForTest; }
        }

        public ToolDialog(string title, Size clientSize, int previewWidth)
        {
            Text = title;
            Font = Ui.BaseFont();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = clientSize;

            Buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = Ui.S(48),
                Padding = new Padding(Ui.S(8)),
            };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Size = new Size(Ui.S(84), Ui.S(30)) };
            OkButton = new Button { Text = "확인", DialogResult = DialogResult.OK, Size = new Size(Ui.S(84), Ui.S(30)) };
            Buttons.Controls.Add(cancel);
            Buttons.Controls.Add(OkButton);
            AcceptButton = OkButton;
            CancelButton = cancel;

            // 위쪽에 붙여 내용 높이만큼만 차지하게 한다 (Fill이면 남는 높이가 마지막 줄에 몰려 빈칸이 생긴다).
            Grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(Ui.S(12), Ui.S(12), Ui.S(8), 0),
            };
            Grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            if (previewWidth > 0)
            {
                PreviewHost = new Panel { Dock = DockStyle.Right, Width = previewWidth, Padding = new Padding(Ui.S(4), Ui.S(12), Ui.S(12), 0) };
                Controls.Add(PreviewHost);
            }
            Controls.Add(Grid);
            Controls.Add(Buttons);
            Grid.BringToFront();
        }

        protected void AddRow(string label, Control control)
        {
            int row = Grid.RowCount++;
            Grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label != null)
            {
                Grid.Controls.Add(new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, Ui.S(6), Ui.S(10), Ui.S(6)),
                }, 0, row);
                control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                control.Margin = new Padding(0, Ui.S(3), 0, Ui.S(3));
                Grid.Controls.Add(control, 1, row);
            }
            else
            {
                control.Margin = new Padding(0, Ui.S(3), 0, Ui.S(3));
                Grid.Controls.Add(control, 0, row);
                Grid.SetColumnSpan(control, 2);
            }
        }

        protected static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(0x60, 0x60, 0x60), MaximumSize = new Size(Ui.S(420), 0) };
        }

        protected static NumericUpDown Number(decimal min, decimal max, decimal value)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = Ui.S(80), Anchor = AnchorStyles.Left };
        }

        protected bool Fail(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // 확인을 눌렀을 때 입력을 검사한다. false면 대화상자가 닫히지 않는다.
        protected virtual bool ValidateInput()
        {
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (DialogResult == DialogResult.OK && !ValidateInput()) e.Cancel = true;
        }
    }

    internal sealed class ColorCombo : ComboBox
    {
        public ColorCombo(Color initial)
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Ui.S(20);
            Width = Ui.S(130);
            foreach (string name in Ui.PaletteNames) Items.Add(name);
            SelectedIndex = 0;
            for (int i = 0; i < Ui.PaletteColors.Length; i++)
                if (Ui.PaletteColors[i].ToArgb() == initial.ToArgb()) SelectedIndex = i;
        }

        public Color SelectedColor
        {
            get { return Ui.PaletteColors[Math.Max(0, SelectedIndex)]; }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            int sw = e.Bounds.Height - Ui.S(6);
            var swatch = new Rectangle(e.Bounds.X + Ui.S(4), e.Bounds.Y + Ui.S(3), sw, sw);
            using (var b = new SolidBrush(Ui.PaletteColors[e.Index])) e.Graphics.FillRectangle(b, swatch);
            e.Graphics.DrawRectangle(Pens.Gray, swatch);
            TextRenderer.DrawText(e.Graphics, Ui.PaletteNames[e.Index], Font,
                new Rectangle(swatch.Right + Ui.S(6), e.Bounds.Y, e.Bounds.Width, e.Bounds.Height), e.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    internal sealed class FontCombo : ComboBox
    {
        public FontCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            Width = Ui.S(200);
            int select = 0;
            foreach (FontFamily f in FontFamily.Families)
            {
                if (!f.IsStyleAvailable(FontStyle.Regular) && !f.IsStyleAvailable(FontStyle.Bold)) continue;
                int i = Items.Add(f.Name);
                if (f.Name == "맑은 고딕" || f.Name == "Malgun Gothic") select = i;
            }
            if (Items.Count > 0) SelectedIndex = select;
        }

        public FontFamily Family
        {
            get
            {
                try
                {
                    return new FontFamily((string)SelectedItem);
                }
                catch (ArgumentException)
                {
                    return FontFamily.GenericSansSerif;
                }
            }
        }
    }

    // 적용할 페이지: 전체 / 현재 페이지 / 직접 지정("1-3, 5, 8-").
    internal sealed class PageRangePicker : FlowLayoutPanel
    {
        private readonly RadioButton all, current, custom;
        private readonly TextBox ranges;
        private readonly int currentPage;

        public event EventHandler Changed;

        public PageRangePicker(int currentPage)
        {
            this.currentPage = currentPage;
            AutoSize = true;
            WrapContents = true;
            Margin = Padding.Empty;
            all = new RadioButton { Text = "전체", AutoSize = true, Checked = true };
            current = new RadioButton { Text = "현재 페이지 (" + (currentPage + 1) + ")", AutoSize = true };
            custom = new RadioButton { Text = "지정:", AutoSize = true };
            ranges = new TextBox { Width = Ui.S(110), Enabled = false };
            Ui.SetCueBannerWhenCreated(ranges, "예: 1-3, 5");
            foreach (RadioButton r in new[] { all, current, custom })
                r.CheckedChanged += delegate
                {
                    ranges.Enabled = custom.Checked;
                    if (Changed != null) Changed(this, EventArgs.Empty);
                };
            Controls.AddRange(new Control[] { all, current, custom, ranges });
        }

        public int[] GetPages(int pageCount, out string error)
        {
            error = null;
            if (all.Checked)
            {
                var r = new int[pageCount];
                for (int i = 0; i < pageCount; i++) r[i] = i;
                return r;
            }
            if (current.Checked) return new[] { currentPage };
            return PageRanges.Parse(ranges.Text, pageCount, out error);
        }
    }

    internal static class PageRanges
    {
        public static int[] Parse(string text, int pageCount, out string error)
        {
            error = null;
            var set = new SortedSet<int>();
            foreach (string raw in (text ?? "").Split(','))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int from, to;
                int dash = part.IndexOf('-');
                if (dash < 0)
                {
                    if (!TryPage(part, out from)) { error = "'" + part + "'은(는) 올바른 페이지 번호가 아닙니다."; return null; }
                    to = from;
                }
                else
                {
                    string a = part.Substring(0, dash).Trim(), b = part.Substring(dash + 1).Trim();
                    if (a.Length == 0) from = 1;
                    else if (!TryPage(a, out from)) { error = "'" + part + "'은(는) 올바른 범위가 아닙니다."; return null; }
                    if (b.Length == 0) to = pageCount;
                    else if (!TryPage(b, out to)) { error = "'" + part + "'은(는) 올바른 범위가 아닙니다."; return null; }
                }
                if (from < 1 || to > pageCount || from > to)
                {
                    error = "'" + part + "'은(는) 1부터 " + pageCount + " 사이여야 합니다.";
                    return null;
                }
                for (int p = from; p <= to; p++) set.Add(p - 1);
            }
            if (set.Count == 0)
            {
                error = "적용할 페이지를 입력하세요.";
                return null;
            }
            return new List<int>(set).ToArray();
        }

        private static bool TryPage(string s, out int page)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
        }
    }
}
