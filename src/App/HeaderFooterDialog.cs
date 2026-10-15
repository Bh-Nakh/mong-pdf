using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal sealed class HeaderFooterDialog : ToolDialog
    {
        private static readonly StampAnchor[] Anchors =
        {
            StampAnchor.TopLeft, StampAnchor.TopCenter, StampAnchor.TopRight,
            StampAnchor.BottomLeft, StampAnchor.BottomCenter, StampAnchor.BottomRight,
        };

        private readonly PdfDocument doc;
        private readonly int currentPage;
        private readonly TextBox[] fields = new TextBox[6];
        private readonly FontCombo font;
        private readonly NumericUpDown size;
        private readonly ColorCombo color;
        private readonly NumericUpDown marginMm;
        private readonly NumericUpDown startNumber;
        private readonly PageRangePicker range;
        private readonly StampPreview preview;

        public HeaderFooterDialog(PdfDocument doc, int currentPage)
            : base("머리글·바닥글", new Size(Ui.S(780), Ui.S(500)), Ui.S(300))
        {
            this.doc = doc;
            this.currentPage = Math.Max(0, currentPage);
            for (int i = 0; i < 6; i++) fields[i] = new TextBox { Width = Ui.S(112), Margin = new Padding(0, 0, Ui.S(4), 0) };
            fields[4].Text = "{쪽} / {전체}";

            font = new FontCombo();
            size = Number(5, 72, 10);
            color = new ColorCombo(Ui.PaletteColors[8]);
            marginMm = Number(3, 60, 12);
            startNumber = Number(1, 99999, 1);
            range = new PageRangePicker(currentPage);

            AddRow("머리글", Triple(0));
            AddRow("바닥글", Triple(3));
            AddRow(null, Hint("칸 순서: 왼쪽 · 가운데 · 오른쪽.  넣을 수 있는 값: {쪽} 쪽 번호, {전체} 전체 쪽수, {날짜} 오늘 날짜, {파일} 파일 이름"));
            AddRow("글꼴", font);
            AddRow("크기(pt)", size);
            AddRow("색", color);
            AddRow("여백(mm)", marginMm);
            AddRow("시작 쪽 번호", startNumber);
            AddRow("적용 페이지", range);

            preview = new StampPreview(doc, this.currentPage) { Dock = DockStyle.Fill };
            PreviewHost.Controls.Add(preview);

            EventHandler update = delegate { UpdatePreview(); };
            foreach (TextBox f in fields) f.TextChanged += update;
            font.SelectedIndexChanged += update;
            size.ValueChanged += update;
            color.SelectedIndexChanged += update;
            marginMm.ValueChanged += update;
            startNumber.ValueChanged += update;
            UpdatePreview();
        }

        private Control Triple(int first)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            for (int i = first; i < first + 3; i++) row.Controls.Add(fields[i]);
            return row;
        }

        public int[] Pages { get; private set; }

        // 한 페이지에 넣을 도형들 (쪽 번호 등 값을 채운 뒤 도형으로 바꾼 것).
        public IList<KeyValuePair<VectorShape, StampStyle>> ItemsFor(int pageIndex)
        {
            var list = new List<KeyValuePair<VectorShape, StampStyle>>();
            FontFamily family = font.Family;
            float pt = (float)size.Value;
            for (int i = 0; i < 6; i++)
            {
                string text = Expand(fields[i].Text, pageIndex);
                if (text.Trim().Length == 0) continue;
                var style = new StampStyle
                {
                    Color = Ui.ToPdf(color.SelectedColor),
                    Anchor = Anchors[i],
                    MarginPt = (double)marginMm.Value * 72.0 / 25.4,
                };
                list.Add(new KeyValuePair<VectorShape, StampStyle>(VectorText.Build(text, family, FontStyle.Regular, pt, false), style));
            }
            return list;
        }

        private string Expand(string template, int pageIndex)
        {
            int number = (int)startNumber.Value + pageIndex;
            int total = (int)startNumber.Value + doc.PageCount - 1;
            string date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string file = Path.GetFileNameWithoutExtension(doc.FilePath);
            return template
                .Replace("{쪽}", number.ToString(CultureInfo.InvariantCulture)).Replace("{page}", number.ToString(CultureInfo.InvariantCulture))
                .Replace("{전체}", total.ToString(CultureInfo.InvariantCulture)).Replace("{total}", total.ToString(CultureInfo.InvariantCulture))
                .Replace("{날짜}", date).Replace("{date}", date)
                .Replace("{파일}", file).Replace("{file}", file);
        }

        private void UpdatePreview()
        {
            preview.SetItems(ItemsFor(currentPage));
        }

        protected override bool ValidateInput()
        {
            bool any = false;
            foreach (TextBox f in fields) any |= f.Text.Trim().Length > 0;
            if (!any) return Fail("머리글이나 바닥글에 넣을 내용을 한 칸 이상 입력하세요.");
            string error;
            Pages = range.GetPages(doc.PageCount, out error);
            return Pages != null || Fail(error);
        }
    }
}
