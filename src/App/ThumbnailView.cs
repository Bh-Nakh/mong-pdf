using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal sealed class ThumbnailView : ScrollableControl
    {
        private const int KeepMargin = 20;

        private readonly RenderWorker worker;
        private readonly int thumbWidth;
        private readonly int pad;
        private readonly int labelHeight;
        private readonly Dictionary<int, Bitmap> cache = new Dictionary<int, Bitmap>();
        private readonly Dictionary<int, int> versions = new Dictionary<int, int>();
        private readonly HashSet<int> selection = new HashSet<int>();

        private PdfDocument doc;
        private volatile int generation;
        private volatile int keepFirst = -1;
        private volatile int keepLast = -1;
        private int[] tops = new int[0];
        private Size[] sizes = new Size[0];
        private int current = -1;
        private int anchor = -1;

        private bool mouseDown;
        private bool dragging;
        private int pressedIndex = -1;
        private bool deferredSingleSelect;
        private Point downPoint;
        private int insertIndex = -1;

        public event Action<int> PageClicked;
        public event Action<int[], int> MoveRequested;
        public event Action DeleteRequested;

        public ThumbnailView(RenderWorker worker)
        {
            this.worker = worker;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(0xF4, 0xF4, 0xF4);
            thumbWidth = Ui.S(110);
            pad = Ui.S(12);
            labelHeight = Ui.S(20);
        }

        public void SetDocument(PdfDocument document, bool keepScroll)
        {
            int oldScroll = ScrollY;
            doc = document;
            generation++;
            foreach (Bitmap b in cache.Values) b.Dispose();
            cache.Clear();
            versions.Clear();
            selection.Clear();
            current = -1;
            anchor = -1;
            dragging = false;
            mouseDown = false;

            int n = doc == null ? 0 : doc.PageCount;
            tops = new int[n];
            sizes = new Size[n];
            int y = pad;
            for (int i = 0; i < n; i++)
            {
                PdfSize p = doc.GetPageSize(i);
                double ratio = p.Height / p.Width;
                int w = thumbWidth;
                int h = (int)Math.Round(thumbWidth * ratio);
                int maxH = (int)(thumbWidth * 1.5);
                if (h > maxH)
                {
                    h = maxH;
                    w = (int)Math.Round(maxH / ratio);
                }
                sizes[i] = new Size(Math.Max(1, w), Math.Max(1, h));
                tops[i] = y;
                y += h + labelHeight + pad;
            }
            AutoScrollMinSize = new Size(0, n == 0 ? 0 : y);
            AutoScrollPosition = new Point(0, keepScroll ? oldScroll : 0);
            Invalidate();
        }

        public void RefreshPage(int page)
        {
            int v;
            versions.TryGetValue(page, out v);
            versions[page] = v + 1;
            Bitmap old;
            if (cache.TryGetValue(page, out old))
            {
                old.Dispose();
                cache.Remove(page);
            }
            Invalidate();
        }

        // 본문에서 보고 있는 페이지. 여러 장을 선택해 둔 상태가 아니면 선택도 이 페이지로 따라간다.
        public int CurrentPage
        {
            get { return current; }
            set
            {
                if (value == current) return;
                current = value;
                if (selection.Count <= 1)
                {
                    selection.Clear();
                    if (value >= 0) selection.Add(value);
                    anchor = value;
                }
                EnsureVisible(value);
                Invalidate();
            }
        }

        public int[] SelectedPages
        {
            get
            {
                var list = new List<int>(selection);
                list.Sort();
                return list.ToArray();
            }
        }

        public void SelectPages(IEnumerable<int> pages)
        {
            selection.Clear();
            foreach (int p in pages)
                if (p >= 0 && p < tops.Length) selection.Add(p);
            Invalidate();
        }

        private int ScrollY
        {
            get { return -AutoScrollPosition.Y; }
        }

        private void EnsureVisible(int index)
        {
            if (index < 0 || index >= tops.Length) return;
            int top = tops[index] - pad / 2;
            int bottom = tops[index] + sizes[index].Height + labelHeight;
            if (top < ScrollY) AutoScrollPosition = new Point(0, top);
            else if (bottom > ScrollY + ClientSize.Height) AutoScrollPosition = new Point(0, bottom - ClientSize.Height);
        }

        private int IndexAtY(int y)
        {
            int lo = 0, hi = tops.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (tops[mid] <= y) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private Rectangle ThumbRect(int i, int scrollY)
        {
            int x = (ClientSize.Width - sizes[i].Width) / 2;
            return new Rectangle(x, tops[i] - scrollY, sizes[i].Width, sizes[i].Height);
        }

        private Rectangle ItemRect(int i, int scrollY)
        {
            Rectangle r = ThumbRect(i, scrollY);
            r.Inflate(Ui.S(5), Ui.S(5));
            r.Height += labelHeight;
            return r;
        }

        private int HitItem(Point p)
        {
            if (tops.Length == 0) return -1;
            int sy = ScrollY;
            int i = IndexAtY(p.Y + sy);
            return ItemRect(i, sy).Contains(p) ? i : -1;
        }

        // 끌어 놓을 위치: 이 번호의 페이지 앞에 넣는다 (페이지 수와 같으면 맨 끝).
        private int InsertIndexAt(int y)
        {
            int docY = y + ScrollY;
            for (int i = 0; i < tops.Length; i++)
                if (docY < tops[i] + sizes[i].Height / 2) return i;
            return tops.Length;
        }

        // ---------- 그리기 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            if (doc == null || tops.Length == 0) return;
            Graphics g = e.Graphics;
            int sy = ScrollY;
            int first = IndexAtY(Math.Max(0, sy - pad - labelHeight));
            int last = first;
            while (last + 1 < tops.Length && tops[last + 1] < sy + ClientSize.Height) last++;
            keepFirst = Math.Max(0, first - KeepMargin);
            keepLast = Math.Min(tops.Length - 1, last + KeepMargin);

            Color accent = Color.FromArgb(0x1A, 0x73, 0xE8);
            using (var border = new Pen(Color.FromArgb(0xC8, 0xC8, 0xC8)))
            using (var selBrush = new SolidBrush(Focused ? Color.FromArgb(0xCC, 0xE4, 0xFF) : Color.FromArgb(0xDD, 0xE8, 0xF5)))
            using (var curPen = new Pen(accent, Ui.S(2)))
            {
                for (int i = first; i <= last; i++)
                {
                    Rectangle r = ThumbRect(i, sy);
                    if (selection.Contains(i)) g.FillRectangle(selBrush, ItemRect(i, sy));

                    g.FillRectangle(Brushes.White, r);
                    Bitmap bmp;
                    if (cache.TryGetValue(i, out bmp))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(bmp, r, new Rectangle(0, 0, bmp.Width, bmp.Height), GraphicsUnit.Pixel);
                        g.PixelOffsetMode = PixelOffsetMode.Default;
                    }
                    else
                    {
                        RequestThumb(i);
                    }
                    g.DrawRectangle(i == current ? curPen : border, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);

                    var label = new Rectangle(0, r.Bottom + Ui.S(2), ClientSize.Width, labelHeight);
                    TextRenderer.DrawText(g, (i + 1).ToString(), Font, label,
                        i == current ? accent : Color.FromArgb(0x55, 0x55, 0x55),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                }

                if (dragging && insertIndex >= 0)
                {
                    int y = insertIndex < tops.Length
                        ? tops[insertIndex] - pad / 2 - Ui.S(3)
                        : tops[tops.Length - 1] + sizes[tops.Length - 1].Height + labelHeight + pad / 2;
                    y -= sy;
                    using (var p = new Pen(accent, Ui.S(3)))
                    {
                        g.DrawLine(p, Ui.S(8), y, ClientSize.Width - Ui.S(8), y);
                    }
                }
            }
            Evict();
        }

        private void RequestThumb(int page)
        {
            int gen = generation;
            int version;
            versions.TryGetValue(page, out version);
            PdfDocument d = doc;
            int w = sizes[page].Width;
            int h = sizes[page].Height;
            worker.Enqueue("t" + gen + ":" + page + "v" + version, false,
                delegate { return generation == gen && page >= keepFirst && page <= keepLast; },
                delegate { return Ui.RenderPage(d, page, w, h, w, h, 0, 0, false); },
                delegate(object result)
                {
                    var bmp = (Bitmap)result;
                    int nowVersion;
                    versions.TryGetValue(page, out nowVersion);
                    if (gen != generation || nowVersion != version || cache.ContainsKey(page))
                    {
                        bmp.Dispose();
                        return;
                    }
                    cache[page] = bmp;
                    Invalidate();
                });
        }

        private void Evict()
        {
            List<int> drop = null;
            foreach (int page in cache.Keys)
            {
                if (page < keepFirst || page > keepLast)
                {
                    if (drop == null) drop = new List<int>();
                    drop.Add(page);
                }
            }
            if (drop == null) return;
            foreach (int page in drop)
            {
                cache[page].Dispose();
                cache.Remove(page);
            }
        }

        // ---------- 마우스·키보드 ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = HitItem(e.Location);
            if (i < 0) return;

            if (e.Button == MouseButtons.Right)
            {
                if (!selection.Contains(i))
                {
                    selection.Clear();
                    selection.Add(i);
                    anchor = i;
                    Invalidate();
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            mouseDown = true;
            pressedIndex = i;
            downPoint = e.Location;
            deferredSingleSelect = false;

            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!selection.Remove(i)) selection.Add(i);
                anchor = i;
            }
            else if ((ModifierKeys & Keys.Shift) != 0 && anchor >= 0)
            {
                selection.Clear();
                for (int k = Math.Min(anchor, i); k <= Math.Max(anchor, i); k++) selection.Add(k);
            }
            else if (selection.Contains(i) && selection.Count > 1)
            {
                deferredSingleSelect = true;
            }
            else
            {
                selection.Clear();
                selection.Add(i);
                anchor = i;
                if (PageClicked != null) PageClicked(i);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown || selection.Count == 0) return;
            if (!dragging)
            {
                if (Math.Abs(e.X - downPoint.X) < SystemInformation.DragSize.Width &&
                    Math.Abs(e.Y - downPoint.Y) < SystemInformation.DragSize.Height) return;
                dragging = true;
                deferredSingleSelect = false;
            }

            int edge = Ui.S(30);
            if (e.Y < edge) AutoScrollPosition = new Point(0, Math.Max(0, ScrollY - Ui.S(20)));
            else if (e.Y > ClientSize.Height - edge) AutoScrollPosition = new Point(0, ScrollY + Ui.S(20));
            insertIndex = InsertIndexAt(e.Y);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!mouseDown) return;
            mouseDown = false;

            if (dragging)
            {
                dragging = false;
                int target = insertIndex;
                insertIndex = -1;
                Invalidate();
                int[] pages = SelectedPages;
                if (target < 0 || pages.Length == 0) return;
                int dest = target;
                foreach (int p in pages)
                    if (p < target) dest--;
                bool contiguous = pages[pages.Length - 1] - pages[0] == pages.Length - 1;
                if (contiguous && dest == pages[0]) return;
                if (MoveRequested != null) MoveRequested(pages, dest);
                return;
            }

            if (deferredSingleSelect && pressedIndex >= 0)
            {
                selection.Clear();
                selection.Add(pressedIndex);
                anchor = pressedIndex;
                Invalidate();
                if (PageClicked != null) PageClicked(pressedIndex);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete && selection.Count > 0 && DeleteRequested != null)
            {
                DeleteRequested();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                selection.Clear();
                for (int i = 0; i < tops.Length; i++) selection.Add(i);
                Invalidate();
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Bitmap b in cache.Values) b.Dispose();
                cache.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
