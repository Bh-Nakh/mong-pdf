using System;
using System.Drawing;
using System.Globalization;

namespace PdfViewer
{
    // 일반 실행: 몽PDF.exe [파일.pdf]
    // 시험 실행: --shot 결과.png [--pos x,y] [--size w,h] [--zoom fitpage|fitwidth|150] [--page 3] [--search 단어] [--dialog 이름]
    //   포커스를 가져가지 않고 창을 띄운 뒤, 렌더링이 끝나면 화면을 캡처하고 스스로 정상 종료한다.
    internal sealed class LaunchOptions
    {
        public string FilePath;
        // 명령줄로 받은 PDF 파일 전부 (여러 개면 탭으로 연다)
        public readonly System.Collections.Generic.List<string> Files = new System.Collections.Generic.List<string>();
        public string ShotPath;
        public Point? Position;
        public Size? WindowSize;
        public string Zoom;
        public int Page = -1;
        public string Search;
        // 시험 실행에서 이 대화상자(watermark, headerfooter, security, about)를 띄워 그 화면을 캡처한다.
        public string Dialog;
        // 시험 실행에서 2쪽 보기를 켠다.
        public bool TwoPage;

        public bool IsTest
        {
            get { return ShotPath != null; }
        }

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a.ToLowerInvariant())
                {
                    case "--shot": o.ShotPath = next; i++; break;
                    case "--pos": o.Position = ParsePair(next, (x, y) => new Point(x, y)); i++; break;
                    case "--size": o.WindowSize = ParsePair(next, (x, y) => new Size(x, y)); i++; break;
                    case "--zoom": o.Zoom = next; i++; break;
                    case "--page": int p; if (int.TryParse(next, out p)) o.Page = p - 1; i++; break;
                    case "--search": o.Search = next; i++; break;
                    case "--dialog": o.Dialog = next; i++; break;
                    case "--twopage": o.TwoPage = true; break;
                    default:
                        if (!a.StartsWith("--"))
                        {
                            if (o.FilePath == null) o.FilePath = a;
                            o.Files.Add(a);
                        }
                        break;
                }
            }
            return o;
        }

        private static T? ParsePair<T>(string s, Func<int, int, T> make) where T : struct
        {
            if (s == null) return null;
            string[] parts = s.Split(',');
            int x, y;
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
                return make(x, y);
            return null;
        }
    }
}
