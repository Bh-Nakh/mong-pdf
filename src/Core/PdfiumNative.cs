using System;
using System.Runtime.InteropServices;

namespace PdfCore
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FS_SIZEF
    {
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FS_RECTF
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FS_POINTF
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FS_QUADPOINTSF
    {
        public float X1, Y1, X2, Y2, X3, Y3, X4, Y4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FPDF_FILEWRITE
    {
        public int Version;
        public IntPtr WriteBlock;
    }

    // unsigned long 은 Windows에서 32비트이며, 4GB 미만 크기만 다루므로 uint로 받아도 macOS에서도 하위 32비트가 맞다.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int WriteBlockCallback(IntPtr self, IntPtr data, uint size);

    internal static class PdfiumNative
    {
        private const string Dll = "pdfium.dll";
        private const CallingConvention Cc = CallingConvention.StdCall;

        // PDFium은 스레드 안전하지 않으므로 모든 호출을 이 잠금으로 직렬화한다.
        internal static readonly object Sync = new object();

        internal const int FPDF_ANNOT = 0x01;
        internal const int FPDF_PRINTING = 0x800;
        internal const int FPDFBitmap_BGRA = 4;
        internal const uint FPDF_MATCHCASE = 0x1;

        internal const uint FPDF_NO_INCREMENTAL = 2;
        internal const uint FPDF_REMOVE_SECURITY = 4;
        internal const int FPDF_FILLMODE_WINDING = 2;

        internal const int FPDF_ANNOT_TEXT = 1;
        internal const int FPDF_ANNOT_LINK = 2;
        internal const int FPDF_ANNOT_HIGHLIGHT = 9;
        internal const int FPDF_ANNOT_UNDERLINE = 10;
        internal const int FPDF_ANNOT_SQUIGGLY = 11;
        internal const int FPDF_ANNOT_STRIKEOUT = 12;
        internal const int FPDF_ANNOT_INK = 15;
        internal const int FPDF_ANNOT_POPUP = 16;
        internal const int FPDF_ANNOT_WIDGET = 20;
        internal const int FPDF_ANNOT_FLAG_PRINT = 1 << 2;
        internal const int FPDFANNOT_COLORTYPE_Color = 0;

        internal const uint FPDF_ERR_SUCCESS = 0;
        internal const uint FPDF_ERR_FILE = 2;
        internal const uint FPDF_ERR_FORMAT = 3;
        internal const uint FPDF_ERR_PASSWORD = 4;
        internal const uint FPDF_ERR_SECURITY = 5;

        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDF_InitLibrary();
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDF_DestroyLibrary();
        [DllImport(Dll, CallingConvention = Cc)] internal static extern uint FPDF_GetLastError();

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, byte[] password);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern uint FPDF_GetDocPermissions(IntPtr document);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFPageObj_CreateNewPath(float x, float y);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPath_MoveTo(IntPtr path, float x, float y);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPath_LineTo(IntPtr path, float x, float y);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPath_BezierTo(IntPtr path, float x1, float y1, float x2, float y2, float x3, float y3);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPath_Close(IntPtr path);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPath_SetDrawMode(IntPtr path, int fillMode, int stroke);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPageObj_SetFillColor(IntPtr obj, uint r, uint g, uint b, uint a);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFPage_InsertObject(IntPtr page, IntPtr obj);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPage_GenerateContent(IntPtr page);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDF_CreateNewDocument();
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDF_SaveAsCopy(IntPtr document, IntPtr fileWrite, uint flags);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDF_ImportPagesByIndex(IntPtr dest, IntPtr src, int[] pageIndices, uint length, int index);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDF_MovePages(IntPtr document, int[] pageIndices, uint length, int destPageIndex);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFPage_Delete(IntPtr document, int pageIndex);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPage_GetRotation(IntPtr page);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFPage_SetRotation(IntPtr page, int rotate);
        [DllImport(Dll, CallingConvention = Cc)]
        internal static extern int FPDF_DeviceToPage(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate,
            int deviceX, int deviceY, out double pageX, out double pageY);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_GetLooseCharBox(IntPtr textPage, int index, out FS_RECTF rect);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_GetText(IntPtr textPage, int start, int count, byte[] utf16Result);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFPage_CreateAnnot(IntPtr page, int subtype);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPage_GetAnnotCount(IntPtr page);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFPage_CloseAnnot(IntPtr annot);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFPage_RemoveAnnot(IntPtr page, int index);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_GetSubtype(IntPtr annot);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_SetColor(IntPtr annot, int type, uint r, uint g, uint b, uint a);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_AppendAttachmentPoints(IntPtr annot, ref FS_QUADPOINTSF quad);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_SetRect(IntPtr annot, ref FS_RECTF rect);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_GetRect(IntPtr annot, out FS_RECTF rect);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_SetStringValue(IntPtr annot, byte[] key, byte[] utf16Value);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern uint FPDFAnnot_GetStringValue(IntPtr annot, byte[] key, byte[] buffer, uint buflen);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_AddInkStroke(IntPtr annot, FS_POINTF[] points, UIntPtr count);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_SetBorder(IntPtr annot, float hRadius, float vRadius, float width);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFAnnot_SetFlags(IntPtr annot, int flags);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int pageIndex, out FS_SIZEF size);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern uint FPDF_GetMetaText(IntPtr document, byte[] tag, byte[] buffer, uint buflen);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Dll, CallingConvention = Cc)]
        internal static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate,
            double pageX, double pageY, out int deviceX, out int deviceY);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Dll, CallingConvention = Cc)]
        internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFText_FindStart(IntPtr textPage, byte[] utf16Find, uint flags, int startIndex);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_FindNext(IntPtr handle);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_GetSchResultIndex(IntPtr handle);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_GetSchCount(IntPtr handle);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern void FPDFText_FindClose(IntPtr handle);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFText_CountRects(IntPtr textPage, int startIndex, int count);
        [DllImport(Dll, CallingConvention = Cc)]
        internal static extern int FPDFText_GetRect(IntPtr textPage, int rectIndex, out double left, out double top, out double right, out double bottom);

        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, byte[] buffer, uint buflen);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFBookmark_GetAction(IntPtr bookmark);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern IntPtr FPDFAction_GetDest(IntPtr document, IntPtr action);
        [DllImport(Dll, CallingConvention = Cc)] internal static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);
    }
}
