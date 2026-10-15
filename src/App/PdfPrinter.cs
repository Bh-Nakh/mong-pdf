using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal static class PdfPrinter
    {
        private const float MaxPrintDpi = 300f;

        public static void Print(IWin32Window owner, PdfDocument doc, string documentName, int currentPage)
        {
            var settings = new PrinterSettings();
            settings.MinimumPage = 1;
            settings.MaximumPage = doc.PageCount;
            settings.FromPage = 1;
            settings.ToPage = doc.PageCount;

            using (var dialog = new PrintDialog())
            {
                dialog.UseEXDialog = true;
                dialog.AllowSomePages = true;
                dialog.AllowCurrentPage = true;
                dialog.PrinterSettings = settings;
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
            }

            int first = 0, last = doc.PageCount - 1;
            if (settings.PrintRange == PrintRange.SomePages)
            {
                first = Math.Max(0, settings.FromPage - 1);
                last = Math.Min(doc.PageCount - 1, settings.ToPage - 1);
            }
            else if (settings.PrintRange == PrintRange.CurrentPage)
            {
                first = last = Math.Max(0, currentPage);
            }
            if (first > last) return;

            using (var pd = new PrintDocument())
            {
                pd.PrinterSettings = settings;
                pd.DocumentName = documentName;
                int page = first;
                pd.QueryPageSettings += delegate(object s, QueryPageSettingsEventArgs e)
                {
                    PdfSize size = doc.GetPageSize(page);
                    e.PageSettings.Landscape = size.Width > size.Height;
                };
                pd.PrintPage += delegate(object s, PrintPageEventArgs e)
                {
                    DrawPage(e, doc, page);
                    page++;
                    e.HasMorePages = page <= last;
                };
                pd.Print();
            }
        }

        private static void DrawPage(PrintPageEventArgs e, PdfDocument doc, int pageIndex)
        {
            // 단위: 1/100 인치. VisibleClipBounds는 용지 방향이 반영된 인쇄 가능 영역이다.
            RectangleF area = e.Graphics.VisibleClipBounds;
            PdfSize size = doc.GetPageSize(pageIndex);
            double pageW = size.Width / 72.0 * 100.0;
            double pageH = size.Height / 72.0 * 100.0;
            double k = Math.Min(1.0, Math.Min(area.Width / pageW, area.Height / pageH));
            float destW = (float)(pageW * k);
            float destH = (float)(pageH * k);
            float x = area.X + (area.Width - destW) / 2f;
            float y = area.Y + (area.Height - destH) / 2f;

            float dpi = Math.Min(MaxPrintDpi, e.Graphics.DpiX > 0 ? e.Graphics.DpiX : MaxPrintDpi);
            int pxW = Math.Max(1, (int)Math.Round(destW / 100f * dpi));
            int pxH = Math.Max(1, (int)Math.Round(destH / 100f * dpi));

            using (Bitmap bmp = Ui.RenderPage(doc, pageIndex, pxW, pxH, pxW, pxH, 0, 0, true))
            {
                e.Graphics.DrawImage(bmp, x, y, destW, destH);
            }
        }
    }
}
