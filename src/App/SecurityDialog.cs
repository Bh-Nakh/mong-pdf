using System.Drawing;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal sealed class SecurityDialog : ToolDialog
    {
        private readonly bool unlockMode;
        private readonly TextBox unlockBox;
        private readonly CheckBox useOpen, usePerm, allowPrint, allowCopy, allowEdit, allowAnnotate;
        private readonly TextBox openPw, openPw2, permPw, permPw2;

        // 권한이 제한된 문서면 권한 암호 입력만 받고, 아니면 새 보안 설정을 받는다.
        public SecurityDialog(PdfDocument doc, PdfSecurity pending)
            : base("문서 보안", new Size(Ui.S(470), Ui.S(400)), 0)
        {
            unlockMode = pending == null && doc.IsEncrypted && doc.Permissions != PdfPermissions.All;
            if (unlockMode)
            {
                ClientSize = new Size(Ui.S(440), Ui.S(200));
                AddRow(null, Hint("이 문서는 인쇄·복사·편집 권한이 제한되어 있습니다. 보안 설정을 바꾸거나 제한을 풀려면 권한 암호를 입력하세요."));
                unlockBox = Password();
                AddRow("권한 암호", unlockBox);
                return;
            }

            string state = pending != null
                ? (pending.IsNone ? "보안 제거 예정 (저장할 때 적용)" : "새 보안 설정 예정 (저장할 때 적용)")
                : doc.IsEncrypted ? "암호로 보호된 문서" : "보안 없음";
            AddRow("현재 상태", new Label { Text = state, AutoSize = true, Anchor = AnchorStyles.Left });

            PdfSecurity s = pending ?? new PdfSecurity();
            useOpen = new CheckBox { Text = "문서를 열 때 암호 요구", AutoSize = true, Checked = !string.IsNullOrEmpty(s.UserPassword) };
            openPw = Password(s.UserPassword);
            openPw2 = Password(s.UserPassword);
            usePerm = new CheckBox { Text = "인쇄·복사·편집 제한 (권한 암호)", AutoSize = true, Checked = !string.IsNullOrEmpty(s.OwnerPassword) };
            permPw = Password(s.OwnerPassword);
            permPw2 = Password(s.OwnerPassword);
            allowPrint = new CheckBox { Text = "인쇄 허용", AutoSize = true, Checked = s.AllowPrint };
            allowCopy = new CheckBox { Text = "내용 복사 허용", AutoSize = true, Checked = s.AllowCopy };
            allowEdit = new CheckBox { Text = "페이지·내용 편집 허용", AutoSize = true, Checked = s.AllowEdit };
            allowAnnotate = new CheckBox { Text = "주석·양식 입력 허용", AutoSize = true, Checked = s.AllowAnnotate };

            AddRow(null, useOpen);
            AddRow("열기 암호", openPw);
            AddRow("암호 확인", openPw2);
            AddRow(null, usePerm);
            AddRow("권한 암호", permPw);
            AddRow("암호 확인", permPw2);
            var perms = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            perms.Controls.AddRange(new Control[] { allowPrint, allowCopy, allowEdit, allowAnnotate });
            AddRow("허용할 작업", perms);
            AddRow(null, Hint("AES-256으로 암호화하며, 설정은 저장할 때 적용됩니다. 암호를 잊으면 문서를 열 수 없으니 따로 기록해 두세요."));

            useOpen.CheckedChanged += delegate { UpdateEnabled(); };
            usePerm.CheckedChanged += delegate { UpdateEnabled(); };
            UpdateEnabled();

            if (doc.IsEncrypted || (pending != null && !pending.IsNone))
            {
                var remove = new Button { Text = "보안 모두 제거", AutoSize = true, Height = Ui.S(30) };
                remove.Click += delegate
                {
                    Result = PdfSecurity.NoSecurity();
                    removeChosen = true;
                    DialogResult = DialogResult.OK;
                };
                Buttons.Controls.Add(remove);
            }
        }

        private bool removeChosen;

        public PdfSecurity Result { get; private set; }
        public string UnlockPassword { get; private set; }

        private static TextBox Password(string value = null)
        {
            return new TextBox { UseSystemPasswordChar = true, Width = Ui.S(200), Text = value ?? "" };
        }

        private void UpdateEnabled()
        {
            openPw.Enabled = openPw2.Enabled = useOpen.Checked;
            permPw.Enabled = permPw2.Enabled = usePerm.Checked;
            allowPrint.Enabled = allowCopy.Enabled = allowEdit.Enabled = allowAnnotate.Enabled = usePerm.Checked;
        }

        protected override bool ValidateInput()
        {
            if (unlockMode)
            {
                if (unlockBox.Text.Length == 0) return Fail("권한 암호를 입력하세요.");
                UnlockPassword = unlockBox.Text;
                return true;
            }
            if (removeChosen) return true;

            var s = new PdfSecurity();
            if (useOpen.Checked)
            {
                if (openPw.Text.Length == 0) return Fail("열기 암호를 입력하세요.");
                if (openPw.Text != openPw2.Text) return Fail("열기 암호와 확인 암호가 다릅니다.");
                s.UserPassword = openPw.Text;
            }
            if (usePerm.Checked)
            {
                if (permPw.Text.Length == 0) return Fail("권한 암호를 입력하세요.");
                if (permPw.Text != permPw2.Text) return Fail("권한 암호와 확인 암호가 다릅니다.");
                if (permPw.Text == openPw.Text && useOpen.Checked)
                    return Fail("권한 암호는 열기 암호와 달라야 합니다. 같으면 열기 암호만 알아도 모든 제한이 풀립니다.");
                s.OwnerPassword = permPw.Text;
                s.AllowPrint = allowPrint.Checked;
                s.AllowCopy = allowCopy.Checked;
                s.AllowEdit = allowEdit.Checked;
                s.AllowAnnotate = allowAnnotate.Checked;
            }
            Result = s;
            return true;
        }
    }
}
