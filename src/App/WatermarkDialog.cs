using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal sealed class WatermarkDialog : ToolDialog
    {
        private const float BaseSizePt = 72f;

        private readonly PdfDocument doc;
        private readonly TextBox text;
        private readonly FontCombo font;
        private readonly CheckBox bold;
        private readonly ColorCombo color;
        private readonly NumericUpDown opacity;
        private readonly NumericUpDown angle;
        private readonly RadioButton fit;
        private readonly RadioButton fixedSize;
        private readonly NumericUpDown size;
        private readonly PageRangePicker range;
        private readonly StampPreview preview;

        public WatermarkDialog(PdfDocument doc, int currentPage)
            : base("워터마크", new Size(Ui.S(720), Ui.S(470)), Ui.S(300))
        {
            this.doc = doc;
            text = new TextBox { Text = "대외비" };
            font = new FontCombo();
            bold = new CheckBox { Text = "굵게", AutoSize = true, Checked = true };
            color = new ColorCombo(Ui.PaletteColors[5]);
            opacity = Number(5, 100, 30);
            angle = Number(-180, 180, 45);
            fit = new RadioButton { Text = "페이지에 맞춤", AutoSize = true, Checked = true };
            fixedSize = new RadioButton { Text = "글자 크기(pt):", AutoSize = true };
            size = Number(6, 400, 72);
            size.Enabled = false;
            range = new PageRangePicker(currentPage);

            var sizeRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            sizeRow.Controls.AddRange(new Control[] { fit, fixedSize, size });

            AddRow("문구", text);
            AddRow("글꼴", font);
            AddRow(null, bold);
            AddRow("색", color);
            AddRow("불투명도(%)", opacity);
            AddRow("각도(°)", angle);
            AddRow("크기", sizeRow);
            AddRow("적용 페이지", range);
            AddRow(null, Hint("글자를 도형으로 넣기 때문에 파일 용량이 거의 늘지 않습니다. 대신 이 글자는 검색·선택되지 않습니다. 잘못 넣었으면 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다."));

            preview = new StampPreview(doc, currentPage) { Dock = DockStyle.Fill };
            PreviewHost.Controls.Add(preview);

            EventHandler update = delegate { UpdatePreview(); };
            text.TextChanged += update;
            font.SelectedIndexChanged += update;
            bold.CheckedChanged += update;
            color.SelectedIndexChanged += update;
            opacity.ValueChanged += update;
            angle.ValueChanged += update;
            fit.CheckedChanged += delegate
            {
                size.Enabled = fixedSize.Checked;
                UpdatePreview();
            };
            size.ValueChanged += update;
            UpdatePreview();
        }

        public VectorShape BuildShape()
        {
            float pt = fit.Checked ? BaseSizePt : (float)size.Value;
            return VectorText.Build(text.Text.Trim(), font.Family, bold.Checked ? FontStyle.Bold : FontStyle.Regular, pt, true);
        }

        public StampStyle BuildStyle()
        {
            return new StampStyle
            {
                Color = Ui.ToPdf(color.SelectedColor),
                Opacity = (double)opacity.Value / 100.0,
                RotationDeg = (double)angle.Value,
                Anchor = StampAnchor.Center,
                FitFraction = fit.Checked ? 0.8 : 0,
            };
        }

        public int[] Pages { get; private set; }

        private void UpdatePreview()
        {
            preview.SetItems(new List<KeyValuePair<VectorShape, StampStyle>>
            {
                new KeyValuePair<VectorShape, StampStyle>(BuildShape(), BuildStyle()),
            });
        }

        protected override bool ValidateInput()
        {
            if (text.Text.Trim().Length == 0) return Fail("워터마크 문구를 입력하세요.");
            string error;
            Pages = range.GetPages(doc.PageCount, out error);
            return Pages != null || Fail(error);
        }
    }
}
