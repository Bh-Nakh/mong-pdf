using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace PdfViewer
{
    // %AppData%\NoAdPDF\settings.txt 에 최근 파일·창 위치를 저장한다.
    internal sealed class AppSettings
    {
        private const int MaxRecent = 10;

        private readonly string path;
        public readonly List<string> RecentFiles = new List<string>();
        public Rectangle? WindowBounds;
        public bool Maximized;
        public bool SidebarVisible = true;
        public bool TwoPage;

        private AppSettings(string path)
        {
            this.path = path;
        }

        // 시험 실행에서는 사용자 설정을 건드리지 않도록 파일 없이 메모리에서만 쓴다.
        public static AppSettings InMemory()
        {
            return new AppSettings(null);
        }

        public static AppSettings Load()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NoAdPDF");
            var s = new AppSettings(Path.Combine(dir, "settings.txt"));
            try
            {
                if (!File.Exists(s.path)) return s;
                foreach (string line in File.ReadAllLines(s.path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "recent":
                            if (s.RecentFiles.Count < MaxRecent) s.RecentFiles.Add(value);
                            break;
                        case "window":
                            int[] v = ParseInts(value);
                            if (v != null && v.Length == 4 && v[2] > 100 && v[3] > 100) s.WindowBounds = new Rectangle(v[0], v[1], v[2], v[3]);
                            break;
                        case "maximized":
                            s.Maximized = value == "1";
                            break;
                        case "sidebar":
                            s.SidebarVisible = value != "0";
                            break;
                        case "twopage":
                            s.TwoPage = value == "1";
                            break;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return s;
        }

        public void Save()
        {
            if (path == null) return;
            var lines = new List<string>();
            if (WindowBounds.HasValue)
            {
                Rectangle r = WindowBounds.Value;
                lines.Add("window=" + r.X + "," + r.Y + "," + r.Width + "," + r.Height);
            }
            lines.Add("maximized=" + (Maximized ? "1" : "0"));
            lines.Add("sidebar=" + (SidebarVisible ? "1" : "0"));
            lines.Add("twopage=" + (TwoPage ? "1" : "0"));
            foreach (string f in RecentFiles) lines.Add("recent=" + f);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public void AddRecent(string file)
        {
            string full = Path.GetFullPath(file);
            RecentFiles.RemoveAll(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase));
            RecentFiles.Insert(0, full);
            if (RecentFiles.Count > MaxRecent) RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
            Save();
        }

        public void RemoveRecent(string file)
        {
            RecentFiles.RemoveAll(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        private static int[] ParseInts(string s)
        {
            string[] parts = s.Split(',');
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out r[i])) return null;
            return r;
        }
    }
}
