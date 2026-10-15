using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal static class Ui
    {
        public static float Dpi = 96f;

        public static readonly string[] PaletteNames = { "노랑", "초록", "하늘", "분홍", "주황", "빨강", "파랑", "회색", "검정" };
        public static readonly Color[] PaletteColors =
        {
            Color.FromArgb(255, 212, 0), Color.FromArgb(0, 200, 83), Color.FromArgb(0, 176, 255), Color.FromArgb(255, 64, 160),
            Color.FromArgb(255, 145, 0), Color.FromArgb(229, 57, 53), Color.FromArgb(30, 99, 233), Color.FromArgb(128, 128, 128),
            Color.FromArgb(33, 33, 33),
        };

        public static int S(int value)
        {
            return (int)Math.Round(value * Dpi / 96f);
        }

        public static double PointToPixel
        {
            get { return Dpi / 72.0; }
        }

        public static Font BaseFont()
        {
            return new Font("Malgun Gothic", 9f);
        }

        public static Bitmap RenderPage(PdfDocument doc, int pageIndex, int bufferWidth, int bufferHeight,
            int pageWidthPx, int pageHeightPx, int offsetX, int offsetY, bool forPrinting)
        {
            var bmp = new Bitmap(bufferWidth, bufferHeight, PixelFormat.Format32bppPArgb);
            try
            {
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, bufferWidth, bufferHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    doc.RenderPage(pageIndex, data.Scan0, data.Stride, bufferWidth, bufferHeight,
                        pageWidthPx, pageHeightPx, offsetX, offsetY, forPrinting);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }

        private static string glyphFont;

        public static Bitmap Glyph(char glyph, int size, Color color)
        {
            if (glyphFont == null)
            {
                glyphFont = "";
                foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                {
                    using (var probe = new Font(name, 10f))
                    {
                        if (probe.Name == name) { glyphFont = name; break; }
                    }
                }
            }

            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            if (glyphFont.Length == 0) return bmp;
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new Font(glyphFont, size * 0.8f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
            }
            return bmp;
        }

        public static Bitmap IBeamIcon(int size, Color color)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(color, Math.Max(1f, size / 12f)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = size / 2f, top = size * 0.12f, bottom = size * 0.88f, arm = size * 0.2f;
                g.DrawLine(pen, cx, top, cx, bottom);
                g.DrawLine(pen, cx - arm, top, cx + arm, top);
                g.DrawLine(pen, cx - arm, bottom, cx + arm, bottom);
            }
            return bmp;
        }

        public static Bitmap StrikeIcon(int size, Color color)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", size * 0.8f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(color, Math.Max(1f, size / 12f)))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString("S", font, brush, new RectangleF(0, 0, size, size), format);
                g.DrawLine(pen, size * 0.1f, size * 0.55f, size * 0.9f, size * 0.55f);
            }
            return bmp;
        }

        // 한 화면에 쪽 두 장을 나란히 보여 주는 아이콘
        public static Bitmap TwoPageIcon(int size, Color color)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(color, Math.Max(1f, size / 12f)))
            {
                int w = Math.Max(3, size * 5 / 16), h = size * 12 / 16;
                int top = (size - h) / 2, gap = Math.Max(1, size / 12);
                int left = (size - 2 * w - gap) / 2;
                g.DrawRectangle(pen, left, top, w, h);
                g.DrawRectangle(pen, left + w + gap, top, w, h);
            }
            return bmp;
        }

        public static Bitmap Swatch(Color color, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(Color.FromArgb(0x60, 0x60, 0x60)))
            {
                int m = Math.Max(1, size / 8);
                g.FillRectangle(brush, m, m, size - 2 * m - 1, size - 2 * m - 1);
                g.DrawRectangle(pen, m, m, size - 2 * m - 1, size - 2 * m - 1);
            }
            return bmp;
        }

        public static PdfColor ToPdf(Color c)
        {
            return new PdfColor(c.R, c.G, c.B);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void SetCueBanner(TextBox box, string text)
        {
            const int EM_SETCUEBANNER = 0x1501;
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        public static void SetCueBannerWhenCreated(TextBox box, string text)
        {
            if (box.IsHandleCreated) SetCueBanner(box, text);
            else box.HandleCreated += delegate { SetCueBanner(box, text); };
        }
    }

    internal sealed class FlatToolStripRenderer : ToolStripSystemRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }

        // 도구 모음은 지정한 배경색으로 칠한다 (탭 아래 영역이 기본 회색과 구분되도록). 메뉴 막대는 기본 모양 그대로.
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is MenuStrip || e.ToolStrip.BackColor == SystemColors.Control)
            {
                base.OnRenderToolStripBackground(e);
                return;
            }
            using (var b = new SolidBrush(e.ToolStrip.BackColor))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
    }
}
