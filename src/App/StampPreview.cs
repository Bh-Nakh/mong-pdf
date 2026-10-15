using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    // 워터마크·머리글 대화상자의 미리보기. 현재 페이지 위에 실제 삽입과 같은 배치 계산으로 도형을 그린다.
    internal sealed class StampPreview : Control
    {
        private readonly PdfDocument doc;
        private readonly int pageIndex;
        private Bitmap pageImage;
        private Size imageSizeFor;
        private IList<KeyValuePair<VectorShape, StampStyle>> items = new List<KeyValuePair<VectorShape, StampStyle>>();

        public StampPreview(PdfDocument doc, int pageIndex)
        {
            this.doc = doc;
            this.pageIndex = Math.Max(0, pageIndex);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(0xE6, 0xE6, 0xE6);
        }

        public void SetItems(IList<KeyValuePair<VectorShape, StampStyle>> list)
        {
            items = list;
            Invalidate();
        }

        private Rectangle PageRect()
        {
            PdfSize size = doc.GetPageSize(pageIndex);
            int pad = Ui.S(10);
            double k = Math.Min((Width - 2 * pad) / size.Width, (Height - 2 * pad) / size.Height);
            int w = Math.Max(1, (int)(size.Width * k)), h = Math.Max(1, (int)(size.Height * k));
            return new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = PageRect();
            if (pageImage == null || imageSizeFor != r.Size)
            {
                if (pageImage != null) pageImage.Dispose();
                pageImage = null;
                try
                {
                    pageImage = Ui.RenderPage(doc, pageIndex, r.Width, r.Height, r.Width, r.Height, 0, 0, false);
                }
                catch (Exception)
                {
                    pageImage = null;
                }
                imageSizeFor = r.Size;
            }
            g.FillRectangle(Brushes.White, r);
            if (pageImage != null) g.DrawImage(pageImage, r);
            using (var border = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0))) g.DrawRectangle(border, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);

            PdfSize size = doc.GetPageSize(pageIndex);
            float k = (float)(r.Width / size.Width);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(r);
            foreach (KeyValuePair<VectorShape, StampStyle> item in items)
            {
                if (item.Key.IsEmpty) continue;
                double[] m = StampLayout.Compute(item.Key, item.Value, size.Width, size.Height);
                using (GraphicsPath gp = VectorText.ToPath(item.Key))
                using (var place = new Matrix((float)m[0], (float)m[1], (float)m[2], (float)m[3], (float)m[4], (float)m[5]))
                using (var view = new Matrix(k, 0, 0, k, r.X, r.Y))
                {
                    gp.Transform(place);
                    gp.Transform(view);
                    int alpha = (int)Math.Round(Math.Max(0, Math.Min(1, item.Value.Opacity)) * 255);
                    PdfColor c = item.Value.Color;
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, c.R, c.G, c.B))) g.FillPath(brush, gp);
                }
            }
            g.ResetClip();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pageImage != null) pageImage.Dispose();
            base.Dispose(disposing);
        }
    }
}
