using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PdfViewer
{
    internal sealed class AboutDialog : ToolDialog
    {
        public AboutDialog(Icon appIcon)
            : base(MainForm.AppName + " 정보", new Size(Ui.S(440), Ui.S(300)), 0)
        {
            if (appIcon != null)
            {
                var pic = new PictureBox
                {
                    Image = new Icon(appIcon, Ui.S(48), Ui.S(48)).ToBitmap(),
                    SizeMode = PictureBoxSizeMode.AutoSize,
                };
                AddRow(null, pic);
            }
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            AddRow(null, new Label
            {
                Text = MainForm.AppName + "  " + v.Major + "." + v.Minor + "." + v.Build,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            });
            AddRow(null, Hint("광고 없는 PDF 뷰어·편집기\n\n사용한 오픈소스 구성 요소\n· PDFium (BSD-3-Clause)\n· qpdf (Apache License 2.0)\n· Microsoft Visual C++ 런타임 (재배포 허용 파일)\n\n각 라이선스 전문은 설치 폴더의 licenses 폴더에 있습니다."));

            var open = new Button { Text = "라이선스 폴더 열기", AutoSize = true, Height = Ui.S(30) };
            open.Click += delegate { OpenLicenses(); };
            Buttons.Controls.Add(open);
            Buttons.Controls[0].Visible = false;
        }

        private static void OpenLicenses()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "licenses");
            if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
        }
    }
}
