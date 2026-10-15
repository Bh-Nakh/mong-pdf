using System.Drawing;
using System.Windows.Forms;

namespace PdfViewer
{
    internal sealed class NoteDialog : Form
    {
        private readonly TextBox box;

        public NoteDialog(string title, string text)
        {
            Text = title;
            Font = Ui.BaseFont();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(400), Ui.S(230));

            box = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(Ui.S(12), Ui.S(12)),
                Size = new Size(Ui.S(376), Ui.S(164)),
                Text = text ?? "",
            };
            var hint = new Label
            {
                AutoSize = false,
                Location = new Point(Ui.S(12), Ui.S(192)),
                Size = new Size(Ui.S(200), Ui.S(24)),
                ForeColor = Color.FromArgb(0x70, 0x70, 0x70),
                Text = "Ctrl+Enter: 확인",
            };
            var ok = new Button
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Location = new Point(Ui.S(222), Ui.S(188)),
                Size = new Size(Ui.S(80), Ui.S(28)),
            };
            var cancel = new Button
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Location = new Point(Ui.S(308), Ui.S(188)),
                Size = new Size(Ui.S(80), Ui.S(28)),
            };
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    e.SuppressKeyPress = true;
                    DialogResult = DialogResult.OK;
                }
            };
            Controls.AddRange(new Control[] { box, hint, ok, cancel });
            CancelButton = cancel;
        }

        public string NoteText
        {
            get { return box.Text; }
        }

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);
            box.Focus();
            box.SelectionStart = box.Text.Length;
        }
    }
}
