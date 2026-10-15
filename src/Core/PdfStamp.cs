using System;
using System.Collections.Generic;

namespace PdfCore
{
    public enum PathVerb
    {
        MoveTo,
        LineTo,
        BezierTo,
        Close,
    }

    // 글자 윤곽 등 채워서 그릴 도형. 단위는 pt, 원점은 왼쪽 위, y는 아래로 증가한다.
    // 글꼴을 문서에 넣지 않으려고 글자를 도형으로 바꿔 넣을 때 쓴다 (윤곽 추출은 화면 계층 담당).
    public sealed class VectorShape
    {
        public readonly List<PathVerb> Verbs = new List<PathVerb>();
        // MoveTo·LineTo는 2개, BezierTo는 6개 좌표를 차례로 쌓는다.
        public readonly List<double> Coords = new List<double>();
        public double Width;
        public double Height;

        public bool IsEmpty
        {
            get { return Verbs.Count == 0; }
        }
    }

    public enum StampAnchor
    {
        Center,
        TopLeft,
        TopCenter,
        TopRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
    }

    public sealed class StampStyle
    {
        public PdfColor Color = new PdfColor(0, 0, 0);
        public double Opacity = 1.0;
        // 화면 기준 시계 반대 방향 회전 각도.
        public double RotationDeg;
        public StampAnchor Anchor = StampAnchor.Center;
        public double MarginPt = 28;
        public double Scale = 1.0;
        // 0보다 크면 (회전한) 도형이 페이지 너비·높이의 이 비율 안에 들어가도록 크기를 맞춘다. Scale은 무시된다.
        public double FitFraction;
    }

    public static class StampLayout
    {
        // 도형 좌표(x, y)를 화면 기준 페이지 좌표(pt, 왼쪽 위 원점)로 옮기는 변환 [a, b, c, d, e, f].
        // 결과: X = a*x + c*y + e, Y = b*x + d*y + f. 미리보기와 실제 삽입이 같은 계산을 쓴다.
        public static double[] Compute(VectorShape shape, StampStyle style, double pageWidth, double pageHeight)
        {
            double w = Math.Max(shape.Width, 0.001), h = Math.Max(shape.Height, 0.001);
            double rad = style.RotationDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            double s = style.Scale;
            if (style.FitFraction > 0)
            {
                double bw = Math.Abs(w * cos) + Math.Abs(h * sin);
                double bh = Math.Abs(w * sin) + Math.Abs(h * cos);
                s = Math.Min(pageWidth * style.FitFraction / bw, pageHeight * style.FitFraction / bh);
            }

            double sw = w * s, sh = h * s, m = style.MarginPt;
            double cx, cy;
            switch (style.Anchor)
            {
                case StampAnchor.TopLeft: cx = m + sw / 2; cy = m + sh / 2; break;
                case StampAnchor.TopCenter: cx = pageWidth / 2; cy = m + sh / 2; break;
                case StampAnchor.TopRight: cx = pageWidth - m - sw / 2; cy = m + sh / 2; break;
                case StampAnchor.BottomLeft: cx = m + sw / 2; cy = pageHeight - m - sh / 2; break;
                case StampAnchor.BottomCenter: cx = pageWidth / 2; cy = pageHeight - m - sh / 2; break;
                case StampAnchor.BottomRight: cx = pageWidth - m - sw / 2; cy = pageHeight - m - sh / 2; break;
                default: cx = pageWidth / 2; cy = pageHeight / 2; break;
            }

            // 가운데 정렬 → 크기 조정 → 회전(y가 아래로 증가하므로 시계 반대 방향이 되도록 부호를 맞춤) → 위치 이동
            double a = s * cos, b = -s * sin, c = s * sin, d = s * cos;
            double e = cx - (a * w / 2 + c * h / 2);
            double f = cy - (b * w / 2 + d * h / 2);
            return new[] { a, b, c, d, e, f };
        }
    }
}
