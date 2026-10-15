using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PdfViewer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            LaunchOptions options = LaunchOptions.Parse(args);

            // 시험 실행(--shot)이 아니면 창을 하나만 둔다: 이미 떠 있으면 파일만 넘기고 끝낸다.
            if (!options.IsTest && !SingleInstance.TryBecomePrimary())
            {
                var full = new List<string>();
                foreach (string f in options.Files)
                {
                    try
                    {
                        full.Add(Path.GetFullPath(f));
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }
                }
                if (SingleInstance.Forward(full)) return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                Ui.Dpi = g.DpiX;
            }
            MongWebCheck.WebCheck.Start("mongpdf", "몽PDF", new int[] { 47121, 47122, 47123 });   // 웹페이지 설치 확인용
            Application.Run(new MainForm(options));
        }
    }
}
