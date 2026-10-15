using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using PdfCore;

namespace PdfViewer
{
    // 글자를 윤곽 도형으로 바꾼다. 문서에 글꼴을 넣지 않으므로 한글도 파일 크기가 거의 늘지 않는다.
    internal static class VectorText
    {
        // tight: 실제 글자 윤곽에 딱 맞춘 상자(워터마크용). 아니면 줄 높이 기준 상자(머리글·바닥글 기준선 정렬용).
        public static VectorShape Build(string text, FontFamily family, FontStyle style, float sizePt, bool tight)
        {
            var shape = new VectorShape();
            if (string.IsNullOrEmpty(text)) return shape;
            if (!family.IsStyleAvailable(style)) style = FontStyle.Regular;

            using (var gp = new GraphicsPath(FillMode.Winding))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                gp.AddString(text, family, (int)style, sizePt, PointF.Empty, format);
                if (gp.PointCount == 0) return shape;

                RectangleF b = gp.GetBounds();
                float ox = tight ? b.X : 0, oy = tight ? b.Y : 0;
                PointF[] pts = gp.PathPoints;
                byte[] types = gp.PathTypes;
                for (int i = 0; i < pts.Length; i++)
                {
                    int t = types[i] & 0x07;
                    if (t == 0)
                    {
                        shape.Verbs.Add(PathVerb.MoveTo);
                        Add(shape, pts[i], ox, oy);
                    }
                    else if (t == 1)
                    {
                        shape.Verbs.Add(PathVerb.LineTo);
                        Add(shape, pts[i], ox, oy);
                    }
                    else if (t == 3 && i + 2 < pts.Length)
                    {
                        shape.Verbs.Add(PathVerb.BezierTo);
                        Add(shape, pts[i], ox, oy);
                        Add(shape, pts[i + 1], ox, oy);
                        Add(shape, pts[i + 2], ox, oy);
                        i += 2;
                    }
                    if ((types[i] & 0x80) != 0) shape.Verbs.Add(PathVerb.Close);
                }

                if (tight)
                {
                    shape.Width = b.Width;
                    shape.Height = b.Height;
                }
                else
                {
                    shape.Width = Math.Max(b.Right, 0.1f);
                    int lines = text.Split('\n').Length;
                    shape.Height = lines * sizePt * family.GetLineSpacing(style) / family.GetEmHeight(style);
                }
            }
            return shape;
        }

        private static void Add(VectorShape s, PointF p, float ox, float oy)
        {
            s.Coords.Add(p.X - ox);
            s.Coords.Add(p.Y - oy);
        }

        public static GraphicsPath ToPath(VectorShape shape)
        {
            var gp = new GraphicsPath(FillMode.Winding);
            int ci = 0;
            PointF last = PointF.Empty;
            foreach (PathVerb verb in shape.Verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        gp.StartFigure();
                        last = Pt(shape, ref ci);
                        break;
                    case PathVerb.LineTo:
                    {
                        PointF p = Pt(shape, ref ci);
                        gp.AddLine(last, p);
                        last = p;
                        break;
                    }
                    case PathVerb.BezierTo:
                    {
                        PointF c1 = Pt(shape, ref ci), c2 = Pt(shape, ref ci), p = Pt(shape, ref ci);
                        gp.AddBezier(last, c1, c2, p);
                        last = p;
                        break;
                    }
                    case PathVerb.Close:
                        gp.CloseFigure();
                        break;
                }
            }
            return gp;
        }

        private static PointF Pt(VectorShape s, ref int ci)
        {
            var p = new PointF((float)s.Coords[ci], (float)s.Coords[ci + 1]);
            ci += 2;
            return p;
        }
    }
}
