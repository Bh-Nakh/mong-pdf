using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal enum ZoomMode
    {
        FitPage,
        FitWidth,
        Custom,
    }

    internal enum ToolMode
    {
        Select,
        Hand,
        Highlight,
        Underline,
        StrikeOut,
        Note,
        Pen,
    }

    internal sealed class TextRange
    {
        public int Page;
        public int Start;
        public int Count;
    }

    internal sealed class PageView : ScrollableControl
    {
        public static readonly double[] ZoomSteps = { 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0 };
        public const double MinZoom = 0.1;
        public const double MaxZoom = 8.0;

        // 이보다 큰 페이지는 화면에 보이는 영역만 렌더링한다 (약 24MB).
        private const long MaxFullPagePixels = 6000000;

        private sealed class CacheEntry : IDisposable
        {
            public double Scale;
            public Rectangle Region;
            public Bitmap Bitmap;
            public bool Stale;

            public void Dispose()
            {
                Bitmap.Dispose();
            }
        }

        private sealed class PendingRequest
        {
            public int Id;
            public int Version;
            public Rectangle Region;
        }

        private struct Caret
        {
            public int Page;
            public int Index;

            public int CompareTo(Caret other)
            {
                return Page != other.Page ? Page.CompareTo(other.Page) : Index.CompareTo(other.Index);
            }
        }

        private enum DragKind
        {
            None,
            Pan,
            PendingSelect,
            Select,
            Pen,
            Note,
        }

        private readonly RenderWorker worker;
        private readonly int gap;
        private readonly Dictionary<int, CacheEntry> cache = new Dictionary<int, CacheEntry>();
        private readonly Dictionary<int, PendingRequest> pending = new Dictionary<int, PendingRequest>();
        private readonly ConcurrentDictionary<int, int> latestRequest = new ConcurrentDictionary<int, int>();
        private readonly Dictionary<int, int> pageVersions = new Dictionary<int, int>();
        private readonly Dictionary<int, List<PageRect>> highlights = new Dictionary<int, List<PageRect>>();
        private readonly Dictionary<int, PageRect[]> charBoxes = new Dictionary<int, PageRect[]>();
        private readonly Dictionary<int, IList<PdfAnnotationInfo>> annotations = new Dictionary<int, IList<PdfAnnotationInfo>>();
        private readonly Dictionary<int, List<PageRect>> selectionRects = new Dictionary<int, List<PageRect>>();
        private readonly ToolTip noteTip = new ToolTip();

        private PdfDocument doc;
        private volatile int generation;
        private volatile int keepFirst = -1;
        private volatile int keepLast = -1;
        private int requestCounter;

        private ZoomMode zoomMode = ZoomMode.FitWidth;
        private double customZoom = 1.0;
        private double scale = 1.0;
        private int[] tops = new int[0];
        private int[] lefts = new int[0];
        private bool twoPage;
        private Size[] sizes = new Size[0];
        private int contentWidth;

        private PdfSearchMatch currentMatch;
        private int currentPage = -1;
        private int fitPageIndex;
        private int forcedPageScrollY = -1;
        private bool inResize;

        private ToolMode tool = ToolMode.Select;
        private DragKind drag;
        private Point dragStart;
        private Point dragScrollStart;
        private PdfAnnotationInfo downAnnotation;
        private int downAnnotationPage;

        private bool hasSelection;
        private Caret selAnchor;
        private Caret selFocus;

        private PdfAnnotationInfo selectedAnnotation;
        private int selectedAnnotationPage = -1;
        private PdfAnnotationInfo hoverNote;

        private int penPage = -1;
        private readonly List<PagePoint> penPoints = new List<PagePoint>();
        private int notePage;
        private PagePoint notePoint;

        public event EventHandler CurrentPageChanged;
        public event EventHandler ZoomChanged;
        public event EventHandler SelectionChanged;
        public event Action MarkupRequested;
        public event Action<int, PagePoint> NoteRequested;
        public event Action<int, IList<PagePoint>> InkCreated;
        public event Action<int, PdfAnnotationInfo> AnnotationActivated;
        public event Action<Point> ContextMenuRequested;

        public PageView(RenderWorker worker)
        {
            this.worker = worker;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(0xE6, 0xE6, 0xE6);
            gap = Ui.S(12);
            PenColor = Color.Red;
            PenWidthPt = 2f;
        }

        public PdfDocument Document { get { return doc; } }
        // 한 줄에 놓이는 쪽 수 (한 화면에 2장씩 볼 때 2)
        public int PageStep { get { return twoPage ? 2 : 1; } }

        // 한 화면에 2장씩 나란히 보기. 켜면 (1,2), (3,4)... 로 짝지어 보여 준다.
        public bool TwoPage
        {
            get { return twoPage; }
            set
            {
                if (twoPage == value) return;
                int keep = Math.Max(0, currentPage);
                twoPage = value;
                if (doc == null || doc.PageCount == 0) return;
                keep = Math.Min(keep, doc.PageCount - 1);
                fitPageIndex = RowFirst(Math.Min(fitPageIndex, doc.PageCount - 1));
                UpdateLayout();
                ScrollPos = new Point(Math.Max(0, CenteredScrollX(keep)), tops[keep] - gap);
                forcedPageScrollY = ScrollPos.Y;
                Invalidate();
                UpdateCurrentPage();
                if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
            }
        }
        public int CurrentPage { get { return currentPage; } }
        public ZoomMode Mode { get { return zoomMode; } }
        public double Zoom { get { return scale / Ui.PointToPixel; } }
        public Color PenColor { get; set; }
        public float PenWidthPt { get; set; }
        public bool HasSelection { get { return hasSelection && selAnchor.CompareTo(selFocus) != 0; } }
        public PdfAnnotationInfo SelectedAnnotation { get { return selectedAnnotation; } }
        public int SelectedAnnotationPage { get { return selectedAnnotationPage; } }

        public ToolMode Tool
        {
            get { return tool; }
            set
            {
                if (tool == value) return;
                CancelDrag();
                tool = value;
                Cursor = DefaultCursorForTool();
                Invalidate();
            }
        }

        private Point ScrollPos
        {
            get { return new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y); }
            set { AutoScrollPosition = value; }
        }

        // ---------- 문서 교체 ----------

        public void SetDocument(PdfDocument document)
        {
            doc = document;
            ResetDocumentState();
            fitPageIndex = 0;
            UpdateLayout();
            ScrollPos = tops.Length > 0 ? new Point(Math.Max(0, CenteredScrollX(0)), 0) : Point.Empty;
            Invalidate();
            UpdateCurrentPage();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        // 탭을 바꿀 때 보던 쪽·확대율·스크롤 위치를 기억해 두었다가 되돌린다.
        public sealed class ViewState
        {
            public ZoomMode Mode = ZoomMode.FitWidth;
            public double CustomZoom = 1.0;
            public Point Scroll;
            public int Page;
        }

        public ViewState CaptureView()
        {
            return new ViewState { Mode = zoomMode, CustomZoom = customZoom, Scroll = ScrollPos, Page = Math.Max(0, currentPage) };
        }

        public void SetDocument(PdfDocument document, ViewState state)
        {
            if (state == null || document == null || document.PageCount == 0)
            {
                SetDocument(document);
                return;
            }
            doc = document;
            ResetDocumentState();
            zoomMode = state.Mode;
            customZoom = state.CustomZoom;
            int page = Math.Min(state.Page, doc.PageCount - 1);
            fitPageIndex = RowFirst(page);
            UpdateLayout();
            if (zoomMode == ZoomMode.Custom)
            {
                ScrollPos = state.Scroll;
            }
            else
            {
                ScrollPos = new Point(Math.Max(0, CenteredScrollX(page)), tops[page] - gap);
                forcedPageScrollY = ScrollPos.Y;
                SetCurrentPage(page);
            }
            Invalidate();
            UpdateCurrentPage();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        // 편집·실행 취소로 문서가 바뀌었을 때, 확대율과 보던 위치를 최대한 유지한 채 다시 불러온다.
        public void ReloadDocument(PdfDocument document)
        {
            int keepPage = Math.Max(0, currentPage);
            double frac = 0;
            Point sc = ScrollPos;
            if (keepPage < tops.Length) frac = (sc.Y - tops[keepPage]) / (double)Math.Max(1, sizes[keepPage].Height);

            doc = document;
            ResetDocumentState();
            if (doc.PageCount == 0)
            {
                UpdateLayout();
                Invalidate();
                return;
            }
            keepPage = Math.Min(keepPage, doc.PageCount - 1);
            fitPageIndex = Math.Min(fitPageIndex, doc.PageCount - 1);
            UpdateLayout();
            ScrollPos = new Point(sc.X, Math.Max(0, tops[keepPage] + (int)Math.Round(frac * sizes[keepPage].Height)));
            Invalidate();
            UpdateCurrentPage();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        private void ResetDocumentState()
        {
            generation++;
            ClearCache();
            highlights.Clear();
            charBoxes.Clear();
            annotations.Clear();
            pageVersions.Clear();
            currentMatch = null;
            currentPage = -1;
            forcedPageScrollY = -1;
            CancelDrag();
            ClearSelectionSilently();
            selectedAnnotation = null;
            selectedAnnotationPage = -1;
            HideNoteTip();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        // 주석을 추가·삭제한 페이지만 다시 그린다. 새 그림이 나올 때까지 이전 그림을 보여 준다.
        public void RefreshPage(int page)
        {
            int v;
            pageVersions.TryGetValue(page, out v);
            pageVersions[page] = v + 1;
            annotations.Remove(page);
            CacheEntry ce;
            if (cache.TryGetValue(page, out ce)) ce.Stale = true;
            pending.Remove(page);
            if (selectedAnnotationPage == page)
            {
                selectedAnnotation = null;
                selectedAnnotationPage = -1;
            }
            Invalidate();
        }

        private void ClearCache()
        {
            foreach (CacheEntry e in cache.Values) e.Dispose();
            cache.Clear();
            pending.Clear();
            latestRequest.Clear();
        }

        // ---------- 배치·확대 ----------

        private void UpdateLayout()
        {
            if (doc == null || doc.PageCount == 0)
            {
                tops = new int[0];
                lefts = new int[0];
                sizes = new Size[0];
                contentWidth = 0;
                AutoScrollMinSize = Size.Empty;
                return;
            }

            double s = ComputeScale();
            if (s != scale)
            {
                scale = s;
                generation++;
                pending.Clear();
                latestRequest.Clear();
            }

            int n = doc.PageCount;
            var newTops = new int[n];
            var newLefts = new int[n];
            var newSizes = new Size[n];
            int maxW = 0;
            for (int i = 0; i < n; i++)
            {
                PdfSize p = doc.GetPageSize(i);
                newSizes[i] = new Size(Math.Max(1, (int)Math.Round(p.Width * scale)), Math.Max(1, (int)Math.Round(p.Height * scale)));
                maxW = Math.Max(maxW, newSizes[i].Width);
            }

            int step = PageStep;
            int y = gap;
            for (int row = 0; row < n; row += step)
            {
                int rowH = 0;
                for (int k = row; k < Math.Min(n, row + step); k++)
                {
                    newTops[k] = y;
                    rowH = Math.Max(rowH, newSizes[k].Height);
                    // 한 장씩: 가운데. 두 장씩: 왼쪽 쪽은 가운데 틈 쪽(오른쪽)으로, 오른쪽 쪽은 왼쪽으로 붙인다.
                    if (step == 1) newLefts[k] = gap + (maxW - newSizes[k].Width) / 2;
                    else if (k == row) newLefts[k] = gap + maxW - newSizes[k].Width;
                    else newLefts[k] = gap + maxW + gap;
                }
                y += rowH + gap;
            }
            tops = newTops;
            lefts = newLefts;
            sizes = newSizes;
            contentWidth = step == 1 ? maxW + 2 * gap : 2 * maxW + 3 * gap;
            AutoScrollMinSize = new Size(contentWidth, y);
        }

        private double ComputeScale()
        {
            double ptp = Ui.PointToPixel;
            double s;
            if (zoomMode == ZoomMode.Custom)
            {
                s = customZoom * ptp;
            }
            else
            {
                // 반올림으로 1px 넘쳐 가로 스크롤바가 생기지 않도록 1px 여유를 둔다.
                int availW = Math.Max(Ui.S(50), Width - SystemInformation.VerticalScrollBarWidth - 2 * gap - 1);
                int availH = Math.Max(Ui.S(50), Height - 2 * gap);
                int first = RowFirst(Math.Min(fitPageIndex, doc.PageCount - 1));
                PdfSize p = doc.GetPageSize(first);
                double rowW = p.Width, rowH = p.Height;
                if (twoPage)
                {
                    // 짝지은 두 쪽이 함께 들어가도록 폭은 두 쪽의 합, 높이는 큰 쪽 기준
                    PdfSize q = first + 1 < doc.PageCount ? doc.GetPageSize(first + 1) : p;
                    rowW = p.Width + q.Width;
                    rowH = Math.Max(p.Height, q.Height);
                    availW = Math.Max(Ui.S(50), availW - gap);
                }
                s = zoomMode == ZoomMode.FitWidth ? availW / rowW : Math.Min(availW / rowW, availH / rowH);
            }
            return Math.Max(MinZoom * ptp, Math.Min(MaxZoom * ptp, s));
        }

        public void SetZoom(ZoomMode mode, double zoom)
        {
            SetZoom(mode, zoom, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void SetZoom(ZoomMode mode, double zoom, Point anchor)
        {
            zoomMode = mode;
            if (mode == ZoomMode.Custom) customZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            if (mode != ZoomMode.Custom && currentPage >= 0 && !inResize) fitPageIndex = currentPage;

            if (doc == null || tops.Length == 0)
            {
                if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
                return;
            }

            Point sc = ScrollPos;
            int docX = sc.X + anchor.X;
            int docY = sc.Y + anchor.Y;
            int p = PageAtDoc(docX, docY);
            double fy = (docY - tops[p]) / (double)sizes[p].Height;
            double fx = (docX - PageLeft(p)) / (double)sizes[p].Width;

            UpdateLayout();

            // 맞춤 모드에서는 기준 페이지가 가로 가운데 오게 한다 (더 넓은 가로 페이지가 섞여 있어도 기준 페이지가 잘리지 않도록).
            int nx = mode == ZoomMode.Custom
                ? PageLeft(p) + (int)Math.Round(fx * sizes[p].Width) - anchor.X
                : CenteredScrollX(fitPageIndex);
            int ny = tops[p] + (int)Math.Round(fy * sizes[p].Height) - anchor.Y;
            ScrollPos = new Point(Math.Max(0, nx), Math.Max(0, ny));
            Invalidate();
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        public void ZoomStep(int direction)
        {
            ZoomStep(direction, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        public void ZoomStep(int direction, Point anchor)
        {
            double cur = Zoom;
            double next;
            if (direction > 0)
            {
                next = ZoomSteps[ZoomSteps.Length - 1];
                foreach (double z in ZoomSteps)
                    if (z > cur * 1.001) { next = z; break; }
            }
            else
            {
                next = ZoomSteps[0];
                for (int i = ZoomSteps.Length - 1; i >= 0; i--)
                    if (ZoomSteps[i] < cur * 0.999) { next = ZoomSteps[i]; break; }
            }
            SetZoom(ZoomMode.Custom, next, anchor);
        }

        private int CenteredScrollX(int index)
        {
            if (twoPage) return contentWidth / 2 - ClientSize.Width / 2;
            return PageLeft(index) + sizes[index].Width / 2 - ClientSize.Width / 2;
        }

        private int PageLeft(int index)
        {
            int area = Math.Max(ClientSize.Width, contentWidth);
            return (area - contentWidth) / 2 + lefts[index];
        }

        // 그 쪽이 놓인 줄의 첫 쪽 번호
        private int RowFirst(int index)
        {
            return twoPage ? index - index % 2 : index;
        }

        // 높이 y에 놓인 줄의 첫 쪽
        private int PageAtY(int y)
        {
            int lo = 0, hi = tops.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (tops[mid] <= y) lo = mid;
                else hi = mid - 1;
            }
            return RowFirst(lo);
        }

        private int RowHeight(int first)
        {
            int h = sizes[first].Height;
            if (twoPage && first + 1 < sizes.Length) h = Math.Max(h, sizes[first + 1].Height);
            return h;
        }

        // 문서 좌표(x, y)에 가장 가까운 쪽 (두 장씩 볼 때는 줄의 왼쪽/오른쪽 쪽 중에서 고른다)
        private int PageAtDoc(int docX, int docY)
        {
            int p = PageAtY(docY);
            if (docY > tops[p] + RowHeight(p) + gap / 2 && RowFirst(p) + PageStep < tops.Length) p = RowFirst(p) + PageStep;
            if (twoPage && p + 1 < tops.Length)
            {
                int mid = PageLeft(p) + sizes[p].Width + gap / 2;
                if (docX >= mid) p++;
            }
            return p;
        }

        private Rectangle ScreenRectOf(int page)
        {
            Point sc = ScrollPos;
            return new Rectangle(PageLeft(page) - sc.X, tops[page] - sc.Y, sizes[page].Width, sizes[page].Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (inResize || doc == null || zoomMode == ZoomMode.Custom) return;
            inResize = true;
            try
            {
                SetZoom(zoomMode, customZoom, Point.Empty);
            }
            finally
            {
                inResize = false;
            }
        }

        // ---------- 이동 ----------

        public void GoToPage(int index)
        {
            if (doc == null || index < 0 || index >= tops.Length) return;
            int x = ScrollPos.X;
            if (zoomMode == ZoomMode.FitPage && index != fitPageIndex)
            {
                fitPageIndex = index;
                UpdateLayout();
                x = CenteredScrollX(index);
            }
            ScrollPos = new Point(Math.Max(0, x), tops[index] - gap);
            forcedPageScrollY = ScrollPos.Y;
            SetCurrentPage(index);
            Invalidate();
        }

        public void ShowMatch(PdfSearchMatch match)
        {
            currentMatch = match;
            if (match == null || doc == null || match.PageIndex >= tops.Length)
            {
                Invalidate();
                return;
            }

            Rectangle bounds = Rectangle.Empty;
            foreach (PageRect r in match.Rects)
            {
                Rectangle rr = ToPagePixels(match.PageIndex, r);
                bounds = bounds.IsEmpty ? rr : Rectangle.Union(bounds, rr);
            }
            bounds.Offset(PageLeft(match.PageIndex), tops[match.PageIndex]);

            Point sc = ScrollPos;
            var view = new Rectangle(sc, ClientSize);
            int x = sc.X, y = sc.Y;
            if (bounds.Top < view.Top || bounds.Bottom > view.Bottom) y = bounds.Top - ClientSize.Height / 3;
            if (bounds.Left < view.Left || bounds.Right > view.Right) x = bounds.Left - (ClientSize.Width - bounds.Width) / 2;
            ScrollPos = new Point(Math.Max(0, x), Math.Max(0, y));
            Invalidate();
        }

        public void AddHighlights(int pageIndex, IEnumerable<PageRect> rects)
        {
            List<PageRect> list;
            if (!highlights.TryGetValue(pageIndex, out list))
            {
                list = new List<PageRect>();
                highlights[pageIndex] = list;
            }
            list.AddRange(rects);
            Invalidate();
        }

        public void ClearHighlights()
        {
            highlights.Clear();
            currentMatch = null;
            Invalidate();
        }

        private void ScrollBy(int dx, int dy)
        {
            Point sc = ScrollPos;
            ScrollPos = new Point(Math.Max(0, sc.X + dx), Math.Max(0, sc.Y + dy));
            Invalidate();
        }

        private void UpdateCurrentPage()
        {
            if (doc == null || tops.Length == 0)
            {
                SetCurrentPage(-1);
                return;
            }
            int sy = ScrollPos.Y;
            if (sy == forcedPageScrollY) return;
            forcedPageScrollY = -1;

            int page;
            if (sy + ClientSize.Height >= AutoScrollMinSize.Height - 1 && sy > 0) page = tops.Length - 1;
            else page = PageAtY(sy + ClientSize.Height / 3);
            SetCurrentPage(page);
        }

        private void SetCurrentPage(int page)
        {
            if (page == currentPage) return;
            currentPage = page;
            if (CurrentPageChanged != null) CurrentPageChanged(this, EventArgs.Empty);
        }

        // ---------- 텍스트 선택 ----------

        private PageRect[] CharBoxes(int page)
        {
            PageRect[] boxes;
            if (!charBoxes.TryGetValue(page, out boxes))
            {
                try
                {
                    boxes = doc.GetCharBoxes(page);
                }
                catch (PdfException)
                {
                    boxes = new PageRect[0];
                }
                charBoxes[page] = boxes;
            }
            return boxes;
        }

        private bool IsOverText(int page, PagePoint pp)
        {
            PageRect[] boxes = CharBoxes(page);
            double tx = Ui.S(2) / (double)sizes[page].Width;
            double ty = Ui.S(2) / (double)sizes[page].Height;
            foreach (PageRect b in boxes)
            {
                if (b.Width <= 0) continue;
                if (pp.X >= b.X - tx && pp.X <= b.X + b.Width + tx && pp.Y >= b.Y - ty && pp.Y <= b.Y + b.Height + ty) return true;
            }
            return false;
        }

        // 점에서 가장 가까운 글자 사이 위치. 같은 줄의 글자를 먼저 고른다.
        private int CaretAt(int page, PagePoint pp)
        {
            PageRect[] boxes = CharBoxes(page);
            double w = sizes[page].Width, h = sizes[page].Height;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < boxes.Length; i++)
            {
                PageRect b = boxes[i];
                if (b.Width <= 0) continue;
                double dx = pp.X < b.X ? b.X - pp.X : pp.X > b.X + b.Width ? pp.X - (b.X + b.Width) : 0;
                double dy = pp.Y < b.Y ? b.Y - pp.Y : pp.Y > b.Y + b.Height ? pp.Y - (b.Y + b.Height) : 0;
                double d = dy * h * 1000 + dx * w;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best < 0) return 0;
            PageRect bb = boxes[best];
            return pp.X > bb.X + bb.Width / 2 ? best + 1 : best;
        }

        public IList<TextRange> GetSelectionRanges()
        {
            var result = new List<TextRange>();
            if (!HasSelection || doc == null) return result;
            Caret s = selAnchor, e = selFocus;
            if (s.CompareTo(e) > 0)
            {
                Caret t = s;
                s = e;
                e = t;
            }
            for (int p = s.Page; p <= e.Page && p < tops.Length; p++)
            {
                int from = p == s.Page ? s.Index : 0;
                int to = p == e.Page ? e.Index : CharBoxes(p).Length;
                if (to > from) result.Add(new TextRange { Page = p, Start = from, Count = to - from });
            }
            return result;
        }

        public string GetSelectedText()
        {
            var sb = new StringBuilder();
            foreach (TextRange r in GetSelectionRanges())
            {
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(doc.GetText(r.Page, r.Start, r.Count));
            }
            return sb.ToString();
        }

        public void SelectAll()
        {
            if (doc == null || tops.Length == 0) return;
            int last = tops.Length - 1;
            hasSelection = true;
            selAnchor = new Caret { Page = 0, Index = 0 };
            selFocus = new Caret { Page = last, Index = CharBoxes(last).Length };
            UpdateSelectionRects();
        }

        public void ClearSelection()
        {
            if (!hasSelection) return;
            ClearSelectionSilently();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private void ClearSelectionSilently()
        {
            hasSelection = false;
            selectionRects.Clear();
        }

        private void UpdateSelectionRects()
        {
            selectionRects.Clear();
            foreach (TextRange r in GetSelectionRanges())
            {
                PageRect[] boxes = CharBoxes(r.Page);
                var rects = new List<PageRect>();
                double l = 0, t = 0, rr = 0, b = 0;
                bool open = false;
                for (int i = r.Start; i < r.Start + r.Count && i < boxes.Length; i++)
                {
                    PageRect c = boxes[i];
                    if (c.Width <= 0) continue;
                    double cb = c.Y + c.Height;
                    bool sameLine = open && c.X >= l - c.Height && Math.Min(b, cb) - Math.Max(t, c.Y) > 0.5 * Math.Min(b - t, c.Height);
                    if (sameLine)
                    {
                        l = Math.Min(l, c.X);
                        rr = Math.Max(rr, c.X + c.Width);
                        t = Math.Min(t, c.Y);
                        b = Math.Max(b, cb);
                    }
                    else
                    {
                        if (open) rects.Add(new PageRect(l, t, rr - l, b - t));
                        l = c.X;
                        t = c.Y;
                        rr = c.X + c.Width;
                        b = cb;
                        open = true;
                    }
                }
                if (open) rects.Add(new PageRect(l, t, rr - l, b - t));
                selectionRects[r.Page] = rects;
            }
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        // ---------- 주석 ----------

        private IList<PdfAnnotationInfo> Annotations(int page)
        {
            IList<PdfAnnotationInfo> list;
            if (!annotations.TryGetValue(page, out list))
            {
                try
                {
                    list = doc.GetAnnotations(page);
                }
                catch (PdfException)
                {
                    list = new List<PdfAnnotationInfo>();
                }
                annotations[page] = list;
            }
            return list;
        }

        private PdfAnnotationInfo AnnotationAt(int page, PagePoint pp)
        {
            IList<PdfAnnotationInfo> list = Annotations(page);
            double tx = Ui.S(3) / (double)sizes[page].Width;
            double ty = Ui.S(3) / (double)sizes[page].Height;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                PageRect b = list[i].Bounds;
                if (pp.X >= b.X - tx && pp.X <= b.X + b.Width + tx && pp.Y >= b.Y - ty && pp.Y <= b.Y + b.Height + ty) return list[i];
            }
            return null;
        }

        private void SelectAnnotation(int page, PdfAnnotationInfo info)
        {
            selectedAnnotation = info;
            selectedAnnotationPage = info == null ? -1 : page;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void ClearAnnotationSelection()
        {
            if (selectedAnnotation == null) return;
            SelectAnnotation(-1, null);
        }

        private void HideNoteTip()
        {
            if (hoverNote == null) return;
            hoverNote = null;
            noteTip.Hide(this);
        }

        // ---------- 좌표 ----------

        // 화면 좌표가 가리키는 페이지와 그 안의 비율 좌표. clamp면 가장 가까운 페이지 안으로 맞춘다.
        private bool HitPage(Point client, bool clamp, out int page, out PagePoint pp)
        {
            page = -1;
            pp = new PagePoint();
            if (doc == null || tops.Length == 0) return false;
            Point sc = ScrollPos;
            int docX = client.X + sc.X;
            int docY = client.Y + sc.Y;
            int p = PageAtDoc(docX, docY);
            var r = new Rectangle(PageLeft(p), tops[p], sizes[p].Width, sizes[p].Height);
            if (!clamp && !r.Contains(docX, docY)) return false;
            double x = Math.Max(0, Math.Min(1, (docX - r.X) / (double)r.Width));
            double y = Math.Max(0, Math.Min(1, (docY - r.Y) / (double)r.Height));
            page = p;
            pp = new PagePoint(x, y);
            return true;
        }

        private PagePoint ClampToPage(int page, Point client)
        {
            Rectangle r = ScreenRectOf(page);
            double x = Math.Max(0, Math.Min(1, (client.X - r.X) / (double)r.Width));
            double y = Math.Max(0, Math.Min(1, (client.Y - r.Y) / (double)r.Height));
            return new PagePoint(x, y);
        }

        // ---------- 그리기 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (doc == null || tops.Length == 0)
            {
                TextRenderer.DrawText(g, "PDF 파일을 열거나 이 창으로 끌어다 놓으세요.", Font, ClientRectangle,
                    Color.FromArgb(0x70, 0x70, 0x70), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            Point sc = ScrollPos;
            int viewBottom = sc.Y + ClientSize.Height;
            int first = PageAtY(sc.Y);
            int last = first;
            while (last + 1 < tops.Length && tops[last + 1] < viewBottom) last++;
            keepFirst = Math.Max(0, first - 1);
            keepLast = Math.Min(tops.Length - 1, last + 1);

            using (var border = new Pen(Color.FromArgb(0xC0, 0xC0, 0xC0)))
            using (var shadow = new SolidBrush(Color.FromArgb(0xD0, 0xD0, 0xD0)))
            {
                for (int i = first; i <= last; i++)
                {
                    Rectangle pr = ScreenRectOf(i);
                    Rectangle vis = Rectangle.Intersect(pr, ClientRectangle);
                    if (vis.IsEmpty) continue;
                    vis.Offset(-pr.X, -pr.Y);

                    CacheEntry ce;
                    cache.TryGetValue(i, out ce);
                    if (ce == null || ce.Stale || ce.Scale != scale || !ce.Region.Contains(vis)) RequestRender(i, vis);

                    if (!pr.IntersectsWith(e.ClipRectangle)) continue;
                    g.FillRectangle(shadow, pr.X + Ui.S(2), pr.Y + Ui.S(2), pr.Width, pr.Height);
                    g.FillRectangle(Brushes.White, pr);
                    if (ce != null) DrawEntry(g, ce, pr, e.ClipRectangle);
                    g.DrawRectangle(border, pr.X - 1, pr.Y - 1, pr.Width + 1, pr.Height + 1);
                    DrawOverlays(g, i, pr);
                }
            }

            Evict();
            UpdateCurrentPage();
        }

        private void DrawEntry(Graphics g, CacheEntry ce, Rectangle pageRect, Rectangle clip)
        {
            double k = scale / ce.Scale;
            var dest = new RectangleF(
                (float)(pageRect.X + ce.Region.X * k), (float)(pageRect.Y + ce.Region.Y * k),
                (float)(ce.Region.Width * k), (float)(ce.Region.Height * k));
            RectangleF part = RectangleF.Intersect(dest, clip);
            if (part.Width <= 0 || part.Height <= 0) return;

            var src = new RectangleF(
                (float)((part.X - dest.X) / k), (float)((part.Y - dest.Y) / k),
                (float)(part.Width / k), (float)(part.Height / k));
            bool exact = ce.Scale == scale;
            g.InterpolationMode = exact ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(ce.Bitmap, part, src, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }

        private void DrawOverlays(Graphics g, int page, Rectangle pageRect)
        {
            List<PageRect> list;
            if (highlights.TryGetValue(page, out list))
            {
                using (var b = new SolidBrush(Color.FromArgb(90, 255, 220, 0)))
                {
                    foreach (PageRect r in list) g.FillRectangle(b, ToScreen(r, pageRect));
                }
            }
            if (currentMatch != null && currentMatch.PageIndex == page)
            {
                using (var b = new SolidBrush(Color.FromArgb(120, 255, 120, 0)))
                using (var p = new Pen(Color.FromArgb(230, 230, 90, 0)))
                {
                    foreach (PageRect r in currentMatch.Rects)
                    {
                        Rectangle rr = ToScreen(r, pageRect);
                        g.FillRectangle(b, rr);
                        g.DrawRectangle(p, rr);
                    }
                }
            }
            if (selectionRects.TryGetValue(page, out list))
            {
                using (var b = new SolidBrush(Color.FromArgb(80, 0, 110, 255)))
                {
                    foreach (PageRect r in list) g.FillRectangle(b, ToScreen(r, pageRect));
                }
            }
            if (selectedAnnotation != null && selectedAnnotationPage == page)
            {
                Rectangle rr = ToScreen(selectedAnnotation.Bounds, pageRect);
                rr.Inflate(Ui.S(3), Ui.S(3));
                using (var p = new Pen(Color.FromArgb(0x1A, 0x73, 0xE8), Ui.S(2)) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(p, rr);
                }
            }
            if (penPage == page && penPoints.Count > 1)
            {
                var pts = new PointF[penPoints.Count];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = new PointF((float)(pageRect.X + penPoints[i].X * pageRect.Width), (float)(pageRect.Y + penPoints[i].Y * pageRect.Height));
                float width = (float)Math.Max(1, PenWidthPt * scale);
                using (var p = new Pen(PenColor, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLines(p, pts);
                    g.SmoothingMode = SmoothingMode.Default;
                }
            }
        }

        private Rectangle ToPagePixels(int page, PageRect r)
        {
            Size s = sizes[page];
            int x = (int)Math.Floor(r.X * s.Width);
            int y = (int)Math.Floor(r.Y * s.Height);
            int w = (int)Math.Ceiling(r.Width * s.Width) + 1;
            int h = (int)Math.Ceiling(r.Height * s.Height) + 1;
            return new Rectangle(x, y, w, h);
        }

        private static Rectangle ToScreen(PageRect r, Rectangle pageRect)
        {
            int x = pageRect.X + (int)Math.Floor(r.X * pageRect.Width);
            int y = pageRect.Y + (int)Math.Floor(r.Y * pageRect.Height);
            int w = (int)Math.Ceiling(r.Width * pageRect.Width) + 1;
            int h = (int)Math.Ceiling(r.Height * pageRect.Height) + 1;
            return new Rectangle(x, y, w, h);
        }

        private void RequestRender(int page, Rectangle visible)
        {
            int pw = sizes[page].Width;
            int ph = sizes[page].Height;
            Rectangle region;
            if ((long)pw * ph <= MaxFullPagePixels)
            {
                region = new Rectangle(0, 0, pw, ph);
            }
            else
            {
                region = visible;
                region.Inflate(ClientSize.Width / 2, ClientSize.Height / 2);
                region.Intersect(new Rectangle(0, 0, pw, ph));
            }
            if (region.Width <= 0 || region.Height <= 0) return;

            int version;
            pageVersions.TryGetValue(page, out version);
            PendingRequest pr;
            if (pending.TryGetValue(page, out pr) && pr.Version == version && pr.Region.Contains(visible)) return;

            int id = ++requestCounter;
            pending[page] = new PendingRequest { Id = id, Version = version, Region = region };
            latestRequest[page] = id;

            int gen = generation;
            double s = scale;
            PdfDocument d = doc;
            string key = "p" + gen + ":" + page + "v" + version + ":" + region.X + "," + region.Y + "," + region.Width + "," + region.Height;

            worker.Enqueue(key, true,
                delegate
                {
                    int latest;
                    return generation == gen && page >= keepFirst && page <= keepLast &&
                           latestRequest.TryGetValue(page, out latest) && latest == id;
                },
                delegate
                {
                    Bitmap bmp = Ui.RenderPage(d, page, region.Width, region.Height, pw, ph, region.X, region.Y, false);
                    return new CacheEntry { Scale = s, Region = region, Bitmap = bmp };
                },
                delegate(object result) { OnRendered(page, id, gen, (CacheEntry)result); });
        }

        private void OnRendered(int page, int id, int gen, CacheEntry entry)
        {
            PendingRequest pr;
            if (pending.TryGetValue(page, out pr) && pr.Id == id) pending.Remove(page);

            int latest;
            if (gen != generation || doc == null || page >= tops.Length || !latestRequest.TryGetValue(page, out latest) || latest != id)
            {
                entry.Dispose();
                return;
            }

            CacheEntry old;
            cache.TryGetValue(page, out old);
            if (old != null && !old.Stale && old.Scale == scale)
            {
                Rectangle vis = VisibleRegion(page);
                if (!vis.IsEmpty && old.Region.Contains(vis) && !entry.Region.Contains(vis))
                {
                    entry.Dispose();
                    return;
                }
            }

            cache[page] = entry;
            if (old != null) old.Dispose();
            Invalidate();
        }

        private Rectangle VisibleRegion(int page)
        {
            Rectangle pr = ScreenRectOf(page);
            Rectangle vis = Rectangle.Intersect(pr, ClientRectangle);
            if (!vis.IsEmpty) vis.Offset(-pr.X, -pr.Y);
            return vis;
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

        // ---------- 마우스 ----------

        private Cursor DefaultCursorForTool()
        {
            switch (tool)
            {
                case ToolMode.Hand: return Cursors.Hand;
                case ToolMode.Highlight:
                case ToolMode.Underline:
                case ToolMode.StrikeOut: return Cursors.IBeam;
                case ToolMode.Note:
                case ToolMode.Pen: return Cursors.Cross;
                default: return Cursors.Default;
            }
        }

        private void CancelDrag()
        {
            drag = DragKind.None;
            penPage = -1;
            penPoints.Clear();
            downAnnotation = null;
        }

        private void BeginPan(Point location)
        {
            drag = DragKind.Pan;
            dragStart = location;
            dragScrollStart = ScrollPos;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HideNoteTip();
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (doc != null) ZoomStep(e.Delta > 0 ? 1 : -1, e.Location);
                var he = e as HandledMouseEventArgs;
                if (he != null) he.Handled = true;
                return;
            }
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            HideNoteTip();
            if (doc == null) return;

            if (e.Button == MouseButtons.Middle)
            {
                BeginPan(e.Location);
                return;
            }

            int page;
            PagePoint pp;
            bool onPage = HitPage(e.Location, false, out page, out pp);

            if (e.Button == MouseButtons.Right)
            {
                PdfAnnotationInfo a = onPage ? AnnotationAt(page, pp) : null;
                if (a != null) SelectAnnotation(page, a);
                else ClearAnnotationSelection();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            dragStart = e.Location;

            switch (tool)
            {
                case ToolMode.Hand:
                    BeginPan(e.Location);
                    break;

                case ToolMode.Select:
                    if (!onPage)
                    {
                        ClearSelection();
                        ClearAnnotationSelection();
                        BeginPan(e.Location);
                        break;
                    }
                    downAnnotation = AnnotationAt(page, pp);
                    downAnnotationPage = page;
                    if (IsOverText(page, pp))
                    {
                        drag = DragKind.PendingSelect;
                        selAnchor = new Caret { Page = page, Index = CaretAt(page, pp) };
                    }
                    else if (downAnnotation != null)
                    {
                        ClearSelection();
                        SelectAnnotation(page, downAnnotation);
                        downAnnotation = null;
                    }
                    else
                    {
                        ClearSelection();
                        ClearAnnotationSelection();
                        BeginPan(e.Location);
                    }
                    break;

                case ToolMode.Highlight:
                case ToolMode.Underline:
                case ToolMode.StrikeOut:
                    if (!HitPage(e.Location, true, out page, out pp)) break;
                    ClearAnnotationSelection();
                    drag = DragKind.PendingSelect;
                    selAnchor = new Caret { Page = page, Index = CaretAt(page, pp) };
                    break;

                case ToolMode.Note:
                    if (!onPage) break;
                    drag = DragKind.Note;
                    notePage = page;
                    notePoint = pp;
                    break;

                case ToolMode.Pen:
                    if (!onPage) break;
                    drag = DragKind.Pen;
                    penPage = page;
                    penPoints.Clear();
                    penPoints.Add(pp);
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (doc == null) return;

            switch (drag)
            {
                case DragKind.Pan:
                    ScrollPos = new Point(
                        Math.Max(0, dragScrollStart.X - (e.X - dragStart.X)),
                        Math.Max(0, dragScrollStart.Y - (e.Y - dragStart.Y)));
                    Invalidate();
                    return;

                case DragKind.PendingSelect:
                    if (Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width &&
                        Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height) return;
                    drag = DragKind.Select;
                    downAnnotation = null;
                    ClearAnnotationSelection();
                    hasSelection = true;
                    goto case DragKind.Select;

                case DragKind.Select:
                {
                    if (e.Y < 0) ScrollBy(0, e.Y);
                    else if (e.Y > ClientSize.Height) ScrollBy(0, e.Y - ClientSize.Height);
                    int page;
                    PagePoint pp;
                    if (HitPage(e.Location, true, out page, out pp))
                    {
                        selFocus = new Caret { Page = page, Index = CaretAt(page, pp) };
                        UpdateSelectionRects();
                    }
                    return;
                }

                case DragKind.Pen:
                    penPoints.Add(ClampToPage(penPage, e.Location));
                    Invalidate();
                    return;
            }

            UpdateHoverState(e.Location);
        }

        private void UpdateHoverState(Point location)
        {
            int page;
            PagePoint pp;
            bool onPage = HitPage(location, false, out page, out pp);
            Cursor cursor = DefaultCursorForTool();
            PdfAnnotationInfo note = null;
            if (onPage && (tool == ToolMode.Select || tool == ToolMode.Hand))
            {
                PdfAnnotationInfo a = AnnotationAt(page, pp);
                if (a != null && a.Kind == AnnotationKind.Note && a.Contents.Length > 0) note = a;
                if (tool == ToolMode.Select)
                {
                    if (IsOverText(page, pp)) cursor = Cursors.IBeam;
                    else if (a != null) cursor = Cursors.Hand;
                }
            }
            if (Cursor != cursor) Cursor = cursor;

            if (note != hoverNote)
            {
                hoverNote = note;
                if (note == null) noteTip.Hide(this);
                else noteTip.Show(note.Contents, this, location.X + Ui.S(16), location.Y + Ui.S(16));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            HideNoteTip();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (doc == null) return;

            if (e.Button == MouseButtons.Right)
            {
                if (ContextMenuRequested != null) ContextMenuRequested(e.Location);
                return;
            }

            DragKind finished = drag;
            drag = DragKind.None;
            switch (finished)
            {
                case DragKind.Pan:
                    Cursor = DefaultCursorForTool();
                    break;

                case DragKind.PendingSelect:
                    ClearSelection();
                    if (downAnnotation != null) SelectAnnotation(downAnnotationPage, downAnnotation);
                    else ClearAnnotationSelection();
                    break;

                case DragKind.Select:
                    if (tool != ToolMode.Select && HasSelection && MarkupRequested != null) MarkupRequested();
                    break;

                case DragKind.Note:
                    if (Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width &&
                        Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height && NoteRequested != null)
                        NoteRequested(notePage, notePoint);
                    break;

                case DragKind.Pen:
                {
                    int page = penPage;
                    var points = new List<PagePoint>(penPoints);
                    if (points.Count > 1 && InkCreated != null) InkCreated(page, points);
                    penPage = -1;
                    penPoints.Clear();
                    Invalidate();
                    break;
                }
            }
            downAnnotation = null;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (doc == null || e.Button != MouseButtons.Left || tool != ToolMode.Select) return;
            int page;
            PagePoint pp;
            if (!HitPage(e.Location, false, out page, out pp)) return;
            PdfAnnotationInfo a = AnnotationAt(page, pp);
            if (a != null && a.Kind == AnnotationKind.Note && AnnotationActivated != null)
            {
                drag = DragKind.None;
                AnnotationActivated(page, a);
            }
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            HideNoteTip();
            Invalidate();
        }

        // ---------- 키보드 ----------

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (doc == null || e.Control || e.Alt) return;
            int line = Ui.S(60);
            int pageStep = Math.Max(line, ClientSize.Height - Ui.S(40));
            switch (e.KeyCode)
            {
                case Keys.Up: ScrollBy(0, -line); break;
                case Keys.Down: ScrollBy(0, line); break;
                case Keys.Left: ScrollBy(-line, 0); break;
                case Keys.Right: ScrollBy(line, 0); break;
                case Keys.PageUp: ScrollBy(0, -pageStep); break;
                case Keys.PageDown: ScrollBy(0, pageStep); break;
                case Keys.Space: ScrollBy(0, e.Shift ? -pageStep : pageStep); break;
                case Keys.Home: GoToPage(0); break;
                case Keys.End: GoToPage(tops.Length - 1); break;
                case Keys.Escape:
                    CancelDrag();
                    ClearSelection();
                    ClearAnnotationSelection();
                    Invalidate();
                    break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();
                noteTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
