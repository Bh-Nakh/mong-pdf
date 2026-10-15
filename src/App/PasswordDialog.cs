using System.Drawing;
using System.Windows.Forms;

namespace PdfViewer
{
    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox box;

        public PasswordDialog(string fileName, bool retry)
        {
            Text = "암호 입력";
            Font = Ui.BaseFont();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(360), Ui.S(140));

            var label = new Label
            {
                AutoSize = false,
                Location = new Point(Ui.S(14), Ui.S(14)),
                Size = new Size(Ui.S(332), Ui.S(40)),
                Text = retry
                    ? "암호가 올바르지 않습니다. 다시 입력하세요."
                    : "'" + fileName + "' 문서는 암호로 보호되어 있습니다.\n열기 암호를 입력하세요.",
            };
            box = new TextBox
            {
                Location = new Point(Ui.S(14), Ui.S(60)),
                Width = Ui.S(332),
                UseSystemPasswordChar = true,
            };
            var ok = new Button
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Location = new Point(Ui.S(180), Ui.S(98)),
                Size = new Size(Ui.S(80), Ui.S(28)),
            };
            var cancel = new Button
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Location = new Point(Ui.S(266), Ui.S(98)),
                Size = new Size(Ui.S(80), Ui.S(28)),
            };
            Controls.AddRange(new Control[] { label, box, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string Password
        {
            get { return box.Text; }
        }
    }
}
