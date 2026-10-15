using System;
using System.Collections.Generic;

namespace PdfCore
{
    public struct PdfSize
    {
        public readonly double Width;
        public readonly double Height;

        public PdfSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }

    // 화면에 보이는 페이지 기준 0~1 비율 좌표 (왼쪽 위가 원점). 확대율·DPI와 무관하다.
    public struct PageRect
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        public PageRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    // PageRect와 같은 0~1 비율 좌표의 점.
    public struct PagePoint
    {
        public readonly double X;
        public readonly double Y;

        public PagePoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public struct PdfColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public PdfColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    public enum MarkupKind
    {
        Highlight,
        Underline,
        StrikeOut,
    }

    public enum AnnotationKind
    {
        Note,
        Highlight,
        Underline,
        StrikeOut,
        Ink,
        Other,
    }

    public sealed class PdfAnnotationInfo
    {
        public readonly int Index;
        public readonly AnnotationKind Kind;
        public readonly PageRect Bounds;
        public readonly string Contents;

        public PdfAnnotationInfo(int index, AnnotationKind kind, PageRect bounds, string contents)
        {
            Index = index;
            Kind = kind;
            Bounds = bounds;
            Contents = contents;
        }
    }

    public sealed class PdfSearchMatch
    {
        public readonly int PageIndex;
        public readonly IList<PageRect> Rects;

        public PdfSearchMatch(int pageIndex, IList<PageRect> rects)
        {
            PageIndex = pageIndex;
            Rects = rects;
        }
    }

    public sealed class PdfBookmark
    {
        public readonly string Title;
        public readonly int PageIndex;
        public readonly IList<PdfBookmark> Children;

        public PdfBookmark(string title, int pageIndex, IList<PdfBookmark> children)
        {
            Title = title;
            PageIndex = pageIndex;
            Children = children;
        }
    }

    [Flags]
    public enum PdfPermissions
    {
        None = 0,
        Print = 1,
        Copy = 2,
        Modify = 4,
        Annotate = 8,
        FillForms = 16,
        Assemble = 32,
        All = Print | Copy | Modify | Annotate | FillForms | Assemble,
    }

    // 저장할 때 적용할 보안 설정. 두 암호가 모두 비어 있으면 "보안 없음"(암호 해제)이다.
    public sealed class PdfSecurity
    {
        public string UserPassword;
        public string OwnerPassword;
        public bool AllowPrint = true;
        public bool AllowCopy = true;
        public bool AllowEdit = true;
        public bool AllowAnnotate = true;

        public bool IsNone
        {
            get { return string.IsNullOrEmpty(UserPassword) && string.IsNullOrEmpty(OwnerPassword); }
        }

        public static PdfSecurity NoSecurity()
        {
            return new PdfSecurity();
        }
    }

    public sealed class PdfSaveOptions
    {
        // null이면 원래 암호 상태를 그대로 유지한다.
        public PdfSecurity Security;
        // 안 쓰는 데이터 제거·구조 압축(무손실).
        public bool Optimize;
    }

    public enum PdfOpenError
    {
        Unknown,
        FileNotFound,
        BadFormat,
        PasswordRequired,
        UnsupportedSecurity,
    }

    public class PdfException : Exception
    {
        public readonly PdfOpenError Error;

        public PdfException(PdfOpenError error, string message) : base(message)
        {
            Error = error;
        }
    }
}
