using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using N = PdfCore.PdfiumNative;

namespace PdfCore
{
    public sealed class PdfDocument : IDisposable
    {
        private const int MaxBookmarks = 10000;
        private const int NormScale = 1000000;
        private const int MaxLoadedPages = 6;
        private const float NoteSizePt = 22f;

        private sealed class LoadedPage
        {
            public int Index;
            public IntPtr Page;
            public IntPtr Text;
        }

        private static bool initialized;
        private static MemoryStream writeSink;
        private static readonly WriteBlockCallback WriteBlockFn = WriteBlock;

        private IntPtr handle;
        private GCHandle pin;
        private PdfSize[] pageSizes;
        private readonly List<LoadedPage> loaded = new List<LoadedPage>();

        public string FilePath { get; private set; }
        public string Password { get; private set; }
        public int PageCount { get { return pageSizes.Length; } }
        public bool IsEncrypted { get; private set; }
        // 이 문서에서 허용된 작업. 권한 암호로 열었거나 암호가 없으면 모두 허용된다.
        public PdfPermissions Permissions { get; private set; }

        private PdfDocument(IntPtr handle, GCHandle pin, string path, string password)
        {
            this.handle = handle;
            this.pin = pin;
            FilePath = path;
            Password = password;
            IsEncrypted = N.FPDF_GetSecurityHandlerRevision(handle) >= 0;
            Permissions = IsEncrypted ? MapPermissions(N.FPDF_GetDocPermissions(handle)) : PdfPermissions.All;
            RefreshPageSizes();
        }

        private static PdfPermissions MapPermissions(uint bits)
        {
            PdfPermissions p = PdfPermissions.None;
            if ((bits & (1u << 2)) != 0) p |= PdfPermissions.Print;
            if ((bits & (1u << 3)) != 0) p |= PdfPermissions.Modify;
            if ((bits & (1u << 4)) != 0) p |= PdfPermissions.Copy;
            if ((bits & (1u << 5)) != 0) p |= PdfPermissions.Annotate;
            if ((bits & (1u << 8)) != 0) p |= PdfPermissions.FillForms;
            if ((bits & (1u << 10)) != 0) p |= PdfPermissions.Assemble;
            return p;
        }

        // 파일을 메모리로 읽어서 연다. 원본 파일을 잠그지 않으므로 같은 경로에 덮어써서 저장할 수 있다.
        public static PdfDocument Open(string path, string password)
        {
            if (!File.Exists(path))
                throw new PdfException(PdfOpenError.FileNotFound, "파일을 찾을 수 없습니다.");
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                throw new PdfException(PdfOpenError.FileNotFound, "파일을 읽을 수 없습니다. " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new PdfException(PdfOpenError.FileNotFound, "파일을 읽을 권한이 없습니다. " + ex.Message);
            }
            return Load(data, path, password);
        }

        public static PdfDocument Load(byte[] data, string path, string password)
        {
            lock (N.Sync)
            {
                if (!initialized)
                {
                    N.FPDF_InitLibrary();
                    initialized = true;
                }

                GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr doc = N.FPDF_LoadMemDocument64(h.AddrOfPinnedObject(), (UIntPtr)data.Length,
                    password == null ? null : ToUtf8Z(password));
                if (doc == IntPtr.Zero)
                {
                    h.Free();
                    uint err = N.FPDF_GetLastError();
                    switch (err)
                    {
                        case N.FPDF_ERR_FILE: throw new PdfException(PdfOpenError.FileNotFound, "파일을 열 수 없습니다.");
                        case N.FPDF_ERR_FORMAT: throw new PdfException(PdfOpenError.BadFormat, "PDF 형식이 올바르지 않거나 손상된 파일입니다.");
                        case N.FPDF_ERR_PASSWORD: throw new PdfException(PdfOpenError.PasswordRequired, "암호가 필요하거나 암호가 틀렸습니다.");
                        case N.FPDF_ERR_SECURITY: throw new PdfException(PdfOpenError.UnsupportedSecurity, "지원하지 않는 보안 방식입니다.");
                        default: throw new PdfException(PdfOpenError.Unknown, "PDF를 열 수 없습니다. (오류 " + err + ")");
                    }
                }
                return new PdfDocument(doc, h, path, password);
            }
        }

        public PdfSize GetPageSize(int pageIndex)
        {
            return pageSizes[pageIndex];
        }

        private void RefreshPageSizes()
        {
            int count = Math.Max(0, N.FPDF_GetPageCount(handle));
            var sizes = new PdfSize[count];
            for (int i = 0; i < count; i++)
            {
                FS_SIZEF s;
                sizes[i] = N.FPDF_GetPageSizeByIndexF(handle, i, out s) != 0 ? new PdfSize(s.Width, s.Height) : new PdfSize(612, 792);
            }
            pageSizes = sizes;
        }

        // ---------- 페이지 핸들 캐시 (잠금 안에서만 호출) ----------

        private LoadedPage GetPage(int index)
        {
            for (int i = loaded.Count - 1; i >= 0; i--)
            {
                LoadedPage lp = loaded[i];
                if (lp.Index != index) continue;
                loaded.RemoveAt(i);
                loaded.Add(lp);
                return lp;
            }

            if (index < 0 || index >= pageSizes.Length) throw new ArgumentOutOfRangeException("index");
            IntPtr page = N.FPDF_LoadPage(handle, index);
            if (page == IntPtr.Zero) throw new PdfException(PdfOpenError.Unknown, "페이지를 불러올 수 없습니다.");
            var created = new LoadedPage { Index = index, Page = page };
            loaded.Add(created);
            if (loaded.Count > MaxLoadedPages)
            {
                ClosePage(loaded[0]);
                loaded.RemoveAt(0);
            }
            return created;
        }

        private IntPtr GetTextPage(int index)
        {
            LoadedPage lp = GetPage(index);
            if (lp.Text == IntPtr.Zero)
            {
                lp.Text = N.FPDFText_LoadPage(lp.Page);
                if (lp.Text == IntPtr.Zero) throw new PdfException(PdfOpenError.Unknown, "페이지의 텍스트를 읽을 수 없습니다.");
            }
            return lp.Text;
        }

        private static void ClosePage(LoadedPage lp)
        {
            if (lp.Text != IntPtr.Zero) N.FPDFText_ClosePage(lp.Text);
            N.FPDF_ClosePage(lp.Page);
        }

        private void CloseLoadedPages()
        {
            foreach (LoadedPage lp in loaded) ClosePage(lp);
            loaded.Clear();
        }

        // ---------- 보기 ----------

        public string GetTitle()
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                byte[] tag = Encoding.ASCII.GetBytes("Title\0");
                uint len = N.FPDF_GetMetaText(handle, tag, null, 0);
                if (len <= 2) return null;
                byte[] buf = new byte[len];
                N.FPDF_GetMetaText(handle, tag, buf, len);
                string s = Encoding.Unicode.GetString(buf, 0, (int)len - 2).Trim();
                return s.Length == 0 ? null : s;
            }
        }

        // 페이지 전체를 pageWidthPx×pageHeightPx 크기로 그렸을 때, (offsetX, offsetY)부터 bufferWidth×bufferHeight 영역만 BGRA 버퍼에 그린다.
        public void RenderPage(int pageIndex, IntPtr buffer, int stride, int bufferWidth, int bufferHeight,
            int pageWidthPx, int pageHeightPx, int offsetX, int offsetY, bool forPrinting)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                IntPtr bmp = N.FPDFBitmap_CreateEx(bufferWidth, bufferHeight, N.FPDFBitmap_BGRA, buffer, stride);
                if (bmp == IntPtr.Zero) throw new OutOfMemoryException("렌더링 버퍼를 만들 수 없습니다.");
                try
                {
                    N.FPDFBitmap_FillRect(bmp, 0, 0, bufferWidth, bufferHeight, 0xFFFFFFFF);
                    int flags = N.FPDF_ANNOT | (forPrinting ? N.FPDF_PRINTING : 0);
                    N.FPDF_RenderPageBitmap(bmp, page, -offsetX, -offsetY, pageWidthPx, pageHeightPx, 0, flags);
                }
                finally
                {
                    N.FPDFBitmap_Destroy(bmp);
                }
            }
        }

        public IList<PdfSearchMatch> SearchPage(int pageIndex, string text, bool matchCase)
        {
            var result = new List<PdfSearchMatch>();
            if (string.IsNullOrEmpty(text)) return result;

            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                IntPtr textPage = GetTextPage(pageIndex);
                IntPtr find = N.FPDFText_FindStart(textPage, Encoding.Unicode.GetBytes(text + "\0"), matchCase ? N.FPDF_MATCHCASE : 0, 0);
                if (find == IntPtr.Zero) return result;
                try
                {
                    while (N.FPDFText_FindNext(find) != 0)
                    {
                        int start = N.FPDFText_GetSchResultIndex(find);
                        int count = N.FPDFText_GetSchCount(find);
                        IList<PageRect> rects = TextRects(page, textPage, start, count);
                        if (rects.Count > 0) result.Add(new PdfSearchMatch(pageIndex, rects));
                    }
                }
                finally
                {
                    N.FPDFText_FindClose(find);
                }
            }
            return result;
        }

        public IList<PdfBookmark> GetBookmarks()
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                return ReadBookmarks(IntPtr.Zero, new HashSet<IntPtr>());
            }
        }

        private IList<PdfBookmark> ReadBookmarks(IntPtr parent, HashSet<IntPtr> visited)
        {
            var list = new List<PdfBookmark>();
            IntPtr bm = N.FPDFBookmark_GetFirstChild(handle, parent);
            // 손상된 PDF의 순환 목차에서 무한 반복하지 않도록 방문 기록과 개수 제한을 둔다.
            while (bm != IntPtr.Zero && visited.Count < MaxBookmarks && visited.Add(bm))
            {
                list.Add(new PdfBookmark(ReadBookmarkTitle(bm), ReadBookmarkPage(bm), ReadBookmarks(bm, visited)));
                bm = N.FPDFBookmark_GetNextSibling(handle, bm);
            }
            return list;
        }

        private static string ReadBookmarkTitle(IntPtr bm)
        {
            uint len = N.FPDFBookmark_GetTitle(bm, null, 0);
            if (len <= 2) return "";
            byte[] buf = new byte[len];
            N.FPDFBookmark_GetTitle(bm, buf, len);
            return Encoding.Unicode.GetString(buf, 0, (int)len - 2);
        }

        private int ReadBookmarkPage(IntPtr bm)
        {
            IntPtr dest = N.FPDFBookmark_GetDest(handle, bm);
            if (dest == IntPtr.Zero)
            {
                IntPtr action = N.FPDFBookmark_GetAction(bm);
                if (action != IntPtr.Zero) dest = N.FPDFAction_GetDest(handle, action);
            }
            if (dest == IntPtr.Zero) return -1;
            int index = N.FPDFDest_GetDestPageIndex(handle, dest);
            return index >= 0 && index < pageSizes.Length ? index : -1;
        }

        // ---------- 텍스트 ----------

        // 글자마다 화면 기준 비율 좌표 상자를 돌려준다. 줄바꿈처럼 위치가 없는 글자는 크기 0인 상자다.
        public PageRect[] GetCharBoxes(int pageIndex)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                IntPtr textPage = GetTextPage(pageIndex);
                int count = Math.Max(0, N.FPDFText_CountChars(textPage));
                var boxes = new PageRect[count];
                for (int i = 0; i < count; i++)
                {
                    FS_RECTF r;
                    if (N.FPDFText_GetLooseCharBox(textPage, i, out r) != 0 && r.Right > r.Left && r.Top > r.Bottom)
                        boxes[i] = ToNormalized(page, r.Left, r.Top, r.Right, r.Bottom);
                }
                return boxes;
            }
        }

        public string GetText(int pageIndex, int start, int count)
        {
            if (count <= 0) return "";
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr textPage = GetTextPage(pageIndex);
                var buf = new byte[(count + 1) * 2];
                int written = N.FPDFText_GetText(textPage, start, count, buf);
                if (written <= 1) return "";
                return Encoding.Unicode.GetString(buf, 0, (written - 1) * 2);
            }
        }

        private static IList<PageRect> TextRects(IntPtr page, IntPtr textPage, int start, int count)
        {
            var rects = new List<PageRect>();
            int n = N.FPDFText_CountRects(textPage, start, count);
            for (int r = 0; r < n; r++)
            {
                double l, t, rr, b;
                if (N.FPDFText_GetRect(textPage, r, out l, out t, out rr, out b) != 0)
                    rects.Add(ToNormalized(page, l, t, rr, b));
            }
            return rects;
        }

        // ---------- 주석 ----------

        public IList<PdfAnnotationInfo> GetAnnotations(int pageIndex)
        {
            var list = new List<PdfAnnotationInfo>();
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                int count = N.FPDFPage_GetAnnotCount(page);
                for (int i = 0; i < count; i++)
                {
                    IntPtr annot = N.FPDFPage_GetAnnot(page, i);
                    if (annot == IntPtr.Zero) continue;
                    try
                    {
                        int subtype = N.FPDFAnnot_GetSubtype(annot);
                        if (subtype == N.FPDF_ANNOT_LINK || subtype == N.FPDF_ANNOT_WIDGET || subtype == N.FPDF_ANNOT_POPUP) continue;
                        FS_RECTF r;
                        if (N.FPDFAnnot_GetRect(annot, out r) == 0) continue;
                        list.Add(new PdfAnnotationInfo(i, KindOf(subtype), ToNormalized(page, r.Left, r.Top, r.Right, r.Bottom),
                            GetAnnotString(annot, "Contents")));
                    }
                    finally
                    {
                        N.FPDFPage_CloseAnnot(annot);
                    }
                }
            }
            return list;
        }

        private static AnnotationKind KindOf(int subtype)
        {
            switch (subtype)
            {
                case N.FPDF_ANNOT_TEXT: return AnnotationKind.Note;
                case N.FPDF_ANNOT_HIGHLIGHT: return AnnotationKind.Highlight;
                case N.FPDF_ANNOT_UNDERLINE:
                case N.FPDF_ANNOT_SQUIGGLY: return AnnotationKind.Underline;
                case N.FPDF_ANNOT_STRIKEOUT: return AnnotationKind.StrikeOut;
                case N.FPDF_ANNOT_INK: return AnnotationKind.Ink;
                default: return AnnotationKind.Other;
            }
        }

        public void AddTextMarkup(int pageIndex, int start, int count, MarkupKind kind, PdfColor color)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                IntPtr textPage = GetTextPage(pageIndex);
                int n = N.FPDFText_CountRects(textPage, start, count);
                if (n <= 0) return;

                int subtype = kind == MarkupKind.Highlight ? N.FPDF_ANNOT_HIGHLIGHT
                    : kind == MarkupKind.Underline ? N.FPDF_ANNOT_UNDERLINE : N.FPDF_ANNOT_STRIKEOUT;
                IntPtr annot = CreateAnnot(page, subtype, color);
                try
                {
                    var bounds = new FS_RECTF { Left = float.MaxValue, Bottom = float.MaxValue, Right = float.MinValue, Top = float.MinValue };
                    for (int r = 0; r < n; r++)
                    {
                        double l, t, rr, b;
                        if (N.FPDFText_GetRect(textPage, r, out l, out t, out rr, out b) == 0) continue;
                        // 사각형 네 점 순서는 Acrobat 관례(왼쪽 위, 오른쪽 위, 왼쪽 아래, 오른쪽 아래)를 따른다.
                        var q = new FS_QUADPOINTSF
                        {
                            X1 = (float)l, Y1 = (float)t, X2 = (float)rr, Y2 = (float)t,
                            X3 = (float)l, Y3 = (float)b, X4 = (float)rr, Y4 = (float)b,
                        };
                        N.FPDFAnnot_AppendAttachmentPoints(annot, ref q);
                        bounds.Left = Math.Min(bounds.Left, (float)l);
                        bounds.Right = Math.Max(bounds.Right, (float)rr);
                        bounds.Bottom = Math.Min(bounds.Bottom, (float)b);
                        bounds.Top = Math.Max(bounds.Top, (float)t);
                    }
                    N.FPDFAnnot_SetRect(annot, ref bounds);
                }
                finally
                {
                    N.FPDFPage_CloseAnnot(annot);
                }
            }
        }

        public void AddNote(int pageIndex, PagePoint at, string text, PdfColor color)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                double x, y;
                ToPageCoords(page, at, out x, out y);
                IntPtr annot = CreateAnnot(page, N.FPDF_ANNOT_TEXT, color);
                try
                {
                    var rect = new FS_RECTF { Left = (float)x, Top = (float)y, Right = (float)x + NoteSizePt, Bottom = (float)y - NoteSizePt };
                    N.FPDFAnnot_SetRect(annot, ref rect);
                    SetAnnotString(annot, "Contents", text);
                }
                finally
                {
                    N.FPDFPage_CloseAnnot(annot);
                }
            }
        }

        public void AddInk(int pageIndex, IList<PagePoint> stroke, PdfColor color, float widthPt)
        {
            if (stroke.Count < 2) return;
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                var points = new FS_POINTF[stroke.Count];
                var bounds = new FS_RECTF { Left = float.MaxValue, Bottom = float.MaxValue, Right = float.MinValue, Top = float.MinValue };
                for (int i = 0; i < stroke.Count; i++)
                {
                    double x, y;
                    ToPageCoords(page, stroke[i], out x, out y);
                    points[i] = new FS_POINTF { X = (float)x, Y = (float)y };
                    bounds.Left = Math.Min(bounds.Left, (float)x);
                    bounds.Right = Math.Max(bounds.Right, (float)x);
                    bounds.Bottom = Math.Min(bounds.Bottom, (float)y);
                    bounds.Top = Math.Max(bounds.Top, (float)y);
                }
                bounds.Left -= widthPt;
                bounds.Bottom -= widthPt;
                bounds.Right += widthPt;
                bounds.Top += widthPt;

                IntPtr annot = CreateAnnot(page, N.FPDF_ANNOT_INK, color);
                try
                {
                    N.FPDFAnnot_SetBorder(annot, 0, 0, widthPt);
                    N.FPDFAnnot_SetRect(annot, ref bounds);
                    if (N.FPDFAnnot_AddInkStroke(annot, points, (UIntPtr)points.Length) < 0)
                        throw new PdfException(PdfOpenError.Unknown, "펜 주석을 만들 수 없습니다.");
                }
                finally
                {
                    N.FPDFPage_CloseAnnot(annot);
                }
            }
        }

        public void SetAnnotationContents(int pageIndex, int annotIndex, string text)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr annot = N.FPDFPage_GetAnnot(GetPage(pageIndex).Page, annotIndex);
                if (annot == IntPtr.Zero) return;
                try
                {
                    SetAnnotString(annot, "Contents", text);
                }
                finally
                {
                    N.FPDFPage_CloseAnnot(annot);
                }
            }
        }

        public void RemoveAnnotation(int pageIndex, int annotIndex)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                N.FPDFPage_RemoveAnnot(GetPage(pageIndex).Page, annotIndex);
            }
        }

        private static IntPtr CreateAnnot(IntPtr page, int subtype, PdfColor color)
        {
            IntPtr annot = N.FPDFPage_CreateAnnot(page, subtype);
            if (annot == IntPtr.Zero) throw new PdfException(PdfOpenError.Unknown, "주석을 만들 수 없습니다.");
            N.FPDFAnnot_SetColor(annot, N.FPDFANNOT_COLORTYPE_Color, color.R, color.G, color.B, 255);
            N.FPDFAnnot_SetFlags(annot, N.FPDF_ANNOT_FLAG_PRINT);
            return annot;
        }

        private static void SetAnnotString(IntPtr annot, string key, string value)
        {
            N.FPDFAnnot_SetStringValue(annot, Encoding.ASCII.GetBytes(key + "\0"), Encoding.Unicode.GetBytes((value ?? "") + "\0"));
        }

        private static string GetAnnotString(IntPtr annot, string key)
        {
            byte[] k = Encoding.ASCII.GetBytes(key + "\0");
            uint len = N.FPDFAnnot_GetStringValue(annot, k, null, 0);
            if (len <= 2) return "";
            var buf = new byte[len];
            N.FPDFAnnot_GetStringValue(annot, k, buf, len);
            return Encoding.Unicode.GetString(buf, 0, (int)len - 2);
        }

        // ---------- 워터마크·머리글 (도형) ----------

        // 도형을 페이지 내용으로 넣는다. 위치·크기·회전은 StampLayout으로 화면 기준 페이지에서 계산한다.
        public void StampShape(int pageIndex, VectorShape shape, StampStyle style)
        {
            if (shape.IsEmpty) return;
            lock (N.Sync)
            {
                ThrowIfDisposed();
                IntPtr page = GetPage(pageIndex).Page;
                PdfSize size = pageSizes[pageIndex];
                double[] m = StampLayout.Compute(shape, style, size.Width, size.Height);

                // 화면 기준 좌표(pt, 왼쪽 위 원점) → PDF 좌표: 페이지 회전·상자 위치를 PDFium 변환으로 구한 3점에서 얻는다.
                const int K = 100;
                int dw = Math.Max(1, (int)Math.Round(size.Width * K));
                int dh = Math.Max(1, (int)Math.Round(size.Height * K));
                double ox, oy, xx, xy, yx, yy;
                N.FPDF_DeviceToPage(page, 0, 0, dw, dh, 0, 0, 0, out ox, out oy);
                N.FPDF_DeviceToPage(page, 0, 0, dw, dh, 0, dw, 0, out xx, out xy);
                N.FPDF_DeviceToPage(page, 0, 0, dw, dh, 0, 0, dh, out yx, out yy);
                double ux = (xx - ox) / size.Width, uy = (xy - oy) / size.Width;
                double vx = (yx - ox) / size.Height, vy = (yy - oy) / size.Height;

                IntPtr path = IntPtr.Zero;
                int ci = 0;
                var pts = new float[6];
                foreach (PathVerb verb in shape.Verbs)
                {
                    int n = verb == PathVerb.BezierTo ? 3 : verb == PathVerb.Close ? 0 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        double lx = shape.Coords[ci++], ly = shape.Coords[ci++];
                        double dx = m[0] * lx + m[2] * ly + m[4];
                        double dy = m[1] * lx + m[3] * ly + m[5];
                        pts[2 * k] = (float)(ox + dx * ux + dy * vx);
                        pts[2 * k + 1] = (float)(oy + dx * uy + dy * vy);
                    }
                    switch (verb)
                    {
                        case PathVerb.MoveTo:
                            if (path == IntPtr.Zero) path = N.FPDFPageObj_CreateNewPath(pts[0], pts[1]);
                            else N.FPDFPath_MoveTo(path, pts[0], pts[1]);
                            break;
                        case PathVerb.LineTo:
                            N.FPDFPath_LineTo(path, pts[0], pts[1]);
                            break;
                        case PathVerb.BezierTo:
                            N.FPDFPath_BezierTo(path, pts[0], pts[1], pts[2], pts[3], pts[4], pts[5]);
                            break;
                        case PathVerb.Close:
                            N.FPDFPath_Close(path);
                            break;
                    }
                }
                if (path == IntPtr.Zero) return;

                N.FPDFPath_SetDrawMode(path, N.FPDF_FILLMODE_WINDING, 0);
                uint alpha = (uint)Math.Max(0, Math.Min(255, Math.Round(style.Opacity * 255)));
                N.FPDFPageObj_SetFillColor(path, style.Color.R, style.Color.G, style.Color.B, alpha);
                N.FPDFPage_InsertObject(page, path);
                if (N.FPDFPage_GenerateContent(page) == 0)
                    throw new PdfException(PdfOpenError.Unknown, "페이지 내용을 갱신하지 못했습니다.");
            }
        }

        // ---------- 페이지 관리 ----------

        public void RotatePages(IList<int> pages, int quarterTurns)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                foreach (int p in pages)
                {
                    IntPtr page = GetPage(p).Page;
                    int rotation = ((N.FPDFPage_GetRotation(page) + quarterTurns) % 4 + 4) % 4;
                    N.FPDFPage_SetRotation(page, rotation);
                }
                CloseLoadedPages();
                RefreshPageSizes();
            }
        }

        public void DeletePages(IList<int> pages)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                CloseLoadedPages();
                var sorted = new List<int>(pages);
                sorted.Sort();
                for (int i = sorted.Count - 1; i >= 0; i--) N.FPDFPage_Delete(handle, sorted[i]);
                RefreshPageSizes();
            }
        }

        // pages를 옮긴 뒤 그 첫 페이지가 destIndex 위치에 오도록 이동한다 (순서 유지).
        public void MovePages(IList<int> pages, int destIndex)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                CloseLoadedPages();
                var sorted = new List<int>(pages);
                sorted.Sort();
                if (N.FPDF_MovePages(handle, sorted.ToArray(), (uint)sorted.Count, destIndex) == 0)
                    throw new PdfException(PdfOpenError.Unknown, "페이지를 옮길 수 없습니다.");
                RefreshPageSizes();
            }
        }

        public void InsertPagesFrom(PdfDocument source, int insertIndex)
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                source.ThrowIfDisposed();
                CloseLoadedPages();
                if (N.FPDF_ImportPagesByIndex(handle, source.handle, null, 0, insertIndex) == 0)
                    throw new PdfException(PdfOpenError.Unknown, "페이지를 삽입할 수 없습니다.");
                RefreshPageSizes();
            }
        }

        public void ExtractPages(IList<int> pages, string path)
        {
            byte[] data;
            lock (N.Sync)
            {
                ThrowIfDisposed();
                var sorted = new List<int>(pages);
                sorted.Sort();
                IntPtr target = N.FPDF_CreateNewDocument();
                if (target == IntPtr.Zero) throw new PdfException(PdfOpenError.Unknown, "새 문서를 만들 수 없습니다.");
                try
                {
                    if (N.FPDF_ImportPagesByIndex(target, handle, sorted.ToArray(), (uint)sorted.Count, 0) == 0)
                        throw new PdfException(PdfOpenError.Unknown, "페이지를 추출할 수 없습니다.");
                    data = SaveHandle(target, N.FPDF_NO_INCREMENTAL);
                }
                finally
                {
                    N.FPDF_CloseDocument(target);
                }
            }
            WriteFileSafely(path, data);
        }

        // ---------- 저장 ----------

        // 원래 암호 상태 그대로의 문서 내용 (실행 취소 스냅숏용).
        public byte[] SaveToBytes()
        {
            lock (N.Sync)
            {
                ThrowIfDisposed();
                return SaveHandle(handle, N.FPDF_NO_INCREMENTAL);
            }
        }

        public byte[] SaveToBytes(PdfSaveOptions options)
        {
            if (options == null || (options.Security == null && !options.Optimize)) return SaveToBytes();
            byte[] data;
            lock (N.Sync)
            {
                ThrowIfDisposed();
                uint flags = N.FPDF_NO_INCREMENTAL | (options.Security != null ? N.FPDF_REMOVE_SECURITY : 0);
                data = SaveHandle(handle, flags);
            }
            // 보안을 바꿀 때는 PDFium이 암호를 푼 상태로 저장하므로 qpdf에 원래 암호를 줄 필요가 없다.
            return Qpdf.Rewrite(data, options.Security == null ? Password : null, options.Security, options.Optimize);
        }

        public void SaveAs(string path, PdfSaveOptions options)
        {
            WriteFileSafely(path, SaveToBytes(options));
            FilePath = path;
        }

        private static byte[] SaveHandle(IntPtr doc, uint flags)
        {
            var fw = new FPDF_FILEWRITE { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(WriteBlockFn) };
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FPDF_FILEWRITE)));
            writeSink = new MemoryStream();
            try
            {
                Marshal.StructureToPtr(fw, p, false);
                if (N.FPDF_SaveAsCopy(doc, p, flags) == 0)
                    throw new PdfException(PdfOpenError.Unknown, "문서를 저장할 수 없습니다.");
                return writeSink.ToArray();
            }
            finally
            {
                writeSink = null;
                Marshal.FreeHGlobal(p);
            }
        }

        private static int WriteBlock(IntPtr self, IntPtr data, uint size)
        {
            var buf = new byte[size];
            Marshal.Copy(data, buf, 0, (int)size);
            writeSink.Write(buf, 0, buf.Length);
            return 1;
        }

        // 임시 파일에 다 쓴 뒤 바꿔치기해서, 저장 도중 실패해도 원본이 깨지지 않게 한다.
        private static void WriteFileSafely(string path, byte[] data)
        {
            string full = Path.GetFullPath(path);
            string tmp = full + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, data);
                if (File.Exists(full)) File.Replace(tmp, full, null);
                else File.Move(tmp, full);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        // ---------- 좌표 변환 ----------

        private static PageRect ToNormalized(IntPtr page, double left, double top, double right, double bottom)
        {
            int x1, y1, x2, y2;
            N.FPDF_PageToDevice(page, 0, 0, NormScale, NormScale, 0, left, top, out x1, out y1);
            N.FPDF_PageToDevice(page, 0, 0, NormScale, NormScale, 0, right, bottom, out x2, out y2);
            double nx = Math.Min(x1, x2) / (double)NormScale;
            double ny = Math.Min(y1, y2) / (double)NormScale;
            double nw = Math.Abs(x2 - x1) / (double)NormScale;
            double nh = Math.Abs(y2 - y1) / (double)NormScale;
            return new PageRect(nx, ny, nw, nh);
        }

        private static void ToPageCoords(IntPtr page, PagePoint p, out double x, out double y)
        {
            int dx = (int)Math.Round(p.X * NormScale);
            int dy = (int)Math.Round(p.Y * NormScale);
            N.FPDF_DeviceToPage(page, 0, 0, NormScale, NormScale, 0, dx, dy, out x, out y);
        }

        private static byte[] ToUtf8Z(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }

        private void ThrowIfDisposed()
        {
            if (handle == IntPtr.Zero) throw new ObjectDisposedException("PdfDocument");
        }

        public void Dispose()
        {
            lock (N.Sync)
            {
                if (handle == IntPtr.Zero) return;
                CloseLoadedPages();
                N.FPDF_CloseDocument(handle);
                handle = IntPtr.Zero;
                if (pin.IsAllocated) pin.Free();
            }
        }
    }
}
