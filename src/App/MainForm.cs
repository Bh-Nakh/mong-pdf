using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PdfCore;

namespace PdfViewer
{
    internal sealed class MainForm : Form
    {
        public const string AppName = "몽PDF";
        public const string ProgId = "NoAdPDF.Document";

        private static readonly Color IconColor = Color.FromArgb(0x33, 0x33, 0x33);
        private static readonly Color[] PaletteColors = Ui.PaletteColors;

        private const int HistorySteps = 30;
        private const long HistoryBytes = 300L * 1024 * 1024;

        private readonly LaunchOptions options;
        private readonly RenderWorker worker;
        private readonly PageView pageView;
        private readonly ThumbnailView thumbs;
        private readonly TreeView outline;
        private readonly TabControl sideTabs;
        private readonly TabPage thumbsTab;
        private readonly TabPage outlineTab;
        private readonly SplitContainer split;
        private readonly ContextMenuStrip pageMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip viewMenu = new ContextMenuStrip();

        private readonly ToolStrip mainTool;
        private readonly ToolStrip editTool;
        private readonly AppSettings settings;
        private readonly MenuStrip menuBar;
        private ToolStripMenuItem miRecent, miSave, miSaveAs, miOptimizeSave, miPrint, miDefaultApp;
        private ToolStripMenuItem miUndo, miRedo, miCopy, miSelectAll, miFind, miFindNext, miFindPrev;
        private ToolStripMenuItem miZoomIn, miZoomOut, miFitPage, miFitWidth, miActualSize, miPrevPage, miNextPage, miGoToPage, miSidebar;
        private ToolStripMenuItem miDocument, miAnnotate;
        private readonly Dictionary<ToolMode, ToolStripMenuItem> toolMenuItems = new Dictionary<ToolMode, ToolStripMenuItem>();
        private ToolStripButton btnPrev, btnNext, btnZoomOut, btnZoomIn, btnFindPrev, btnFindNext, btnTwoPage;
        private ToolStripMenuItem miTwoPage, miClose;
        private ToolStripTextBox txtPage, txtSearch;
        private ToolStripLabel lblPageCount, lblFind;
        private ToolStripComboBox cmbZoom;
        private ToolStripDropDownButton btnColor;
        private readonly Dictionary<ToolMode, ToolStripButton> toolButtons = new Dictionary<ToolMode, ToolStripButton>();
        private readonly Dictionary<ToolMode, Color> toolColors = new Dictionary<ToolMode, Color>();
        private bool updatingUi;

        // 탭 하나가 가진 문서와 그 문서만의 상태 (편집 기록, 저장 여부, 보안, 보던 위치)
        private sealed class DocTab
        {
            public PdfDocument Doc;
            public readonly EditHistory History = new EditHistory(HistorySteps, HistoryBytes);
            public int SavedVersion;
            // 저장할 때 적용할 보안 설정 (null이면 원래 상태 유지). 저장된 상태와 다르면 "수정됨"이다.
            public PdfSecurity TargetSecurity;
            public PdfSecurity SavedSecurity;
            public PageView.ViewState View;
        }

        private readonly List<DocTab> tabs = new List<DocTab>();
        private readonly DocTabStrip tabStrip;
        private readonly EditHistory noHistory = new EditHistory(1, 1);
        private DocTab cur;

        // 아래 값들은 지금 보고 있는 탭의 것이다 (탭이 없으면 비어 있다).
        private PdfDocument doc
        {
            get { return cur == null ? null : cur.Doc; }
            set { if (cur != null) cur.Doc = value; }
        }

        private EditHistory history
        {
            get { return cur == null ? noHistory : cur.History; }
        }

        private int savedVersion
        {
            get { return cur == null ? 0 : cur.SavedVersion; }
            set { if (cur != null) cur.SavedVersion = value; }
        }

        private PdfSecurity targetSecurity
        {
            get { return cur == null ? null : cur.TargetSecurity; }
            set { if (cur != null) cur.TargetSecurity = value; }
        }

        private PdfSecurity savedSecurity
        {
            get { return cur == null ? null : cur.SavedSecurity; }
            set { if (cur != null) cur.SavedSecurity = value; }
        }

        private volatile int searchGeneration;
        private string searchText;
        private bool searching;
        private readonly List<PdfSearchMatch> matches = new List<PdfSearchMatch>();
        private int matchIndex = -1;

        public MainForm(LaunchOptions options)
        {
            this.options = options;
            settings = options.IsTest ? AppSettings.InMemory() : AppSettings.Load();
            Text = AppName;
            Font = Ui.BaseFont();
            AutoScaleMode = AutoScaleMode.None;
            Icon = LoadAppIcon();
            Size = new Size(Ui.S(1150), Ui.S(820));
            MinimumSize = new Size(Ui.S(560), Ui.S(380));
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            toolColors[ToolMode.Highlight] = PaletteColors[0];
            toolColors[ToolMode.Underline] = PaletteColors[6];
            toolColors[ToolMode.StrikeOut] = PaletteColors[5];
            toolColors[ToolMode.Note] = PaletteColors[0];
            toolColors[ToolMode.Pen] = PaletteColors[5];

            worker = new RenderWorker(this);

            pageView = new PageView(worker) { Dock = DockStyle.Fill, AllowDrop = true, PenColor = toolColors[ToolMode.Pen] };
            pageView.CurrentPageChanged += delegate { OnCurrentPageChanged(); };
            pageView.ZoomChanged += delegate { UpdateZoomText(); };
            pageView.SelectionChanged += delegate { UpdateUiState(); };
            pageView.MarkupRequested += delegate { ApplyMarkup(MarkupFor(pageView.Tool)); };
            pageView.NoteRequested += AddNote;
            pageView.InkCreated += AddInk;
            pageView.AnnotationActivated += EditNote;
            pageView.ContextMenuRequested += ShowViewMenu;

            thumbs = new ThumbnailView(worker) { Dock = DockStyle.Fill, AllowDrop = true, ContextMenuStrip = pageMenu };
            thumbs.PageClicked += delegate(int page) { pageView.GoToPage(page); };
            thumbs.MoveRequested += MovePages;
            thumbs.DeleteRequested += DeletePages;
            AddPageMenuItems(pageMenu.Items);

            outline = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                ShowLines = false,
                FullRowSelect = true,
                ItemHeight = Ui.S(24),
            };
            outline.NodeMouseClick += delegate(object s, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Node.Tag is int) pageView.GoToPage((int)e.Node.Tag);
            };

            thumbsTab = new TabPage("썸네일");
            thumbsTab.Controls.Add(thumbs);
            outlineTab = new TabPage("목차");
            outlineTab.Controls.Add(outline);
            sideTabs = new TabControl { Dock = DockStyle.Fill };
            sideTabs.TabPages.Add(thumbsTab);

            split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = Ui.S(4),
            };
            split.Panel1.Controls.Add(sideTabs);
            split.Panel2.Controls.Add(pageView);

            tabStrip = new DocTabStrip { Visible = false };
            tabStrip.TabSelected += delegate(int i) { ActivateTab(tabs[i]); };
            tabStrip.TabCloseRequested += delegate(int i) { CloseTab(tabs[i]); };
            tabStrip.NewTabRequested += delegate { ShowOpenDialog(); };

            mainTool = BuildMainToolStrip();
            editTool = BuildEditToolStrip();

            menuBar = BuildMenuBar();

            Controls.Add(split);
            Controls.Add(tabStrip);
            Controls.Add(editTool);
            Controls.Add(mainTool);
            Controls.Add(menuBar);
            MainMenuStrip = menuBar;

            foreach (Control c in new Control[] { this, pageView, thumbs })
            {
                c.DragEnter += OnDragEnter;
                c.DragDrop += OnDragDrop;
            }

            if (options.IsTest)
            {
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                if (options.Position.HasValue) Location = options.Position.Value;
            }
            if (options.WindowSize.HasValue) Size = options.WindowSize.Value;
            else if (settings.WindowBounds.HasValue && IsOnScreen(settings.WindowBounds.Value))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = settings.WindowBounds.Value;
                if (settings.Maximized) WindowState = FormWindowState.Maximized;
            }
            split.Panel1Collapsed = !settings.SidebarVisible;
            if (options.TwoPage) settings.TwoPage = true;
            pageView.TwoPage = settings.TwoPage;
            UpdateTwoPageUi();

            SetTool(ToolMode.Select);
            UpdateUiState();
        }

        private static Icon LoadAppIcon()
        {
            using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("PdfViewer.app.ico"))
            {
                return s == null ? null : new Icon(s);
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return options != null && options.IsTest; }
        }

        // ---------- 도구 모음 ----------

        private static ToolStrip NewToolStrip()
        {
            int icon = Ui.S(16);
            return new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(icon, icon),
                Padding = new Padding(Ui.S(6), Ui.S(2), Ui.S(6), Ui.S(2)),
                Renderer = new FlatToolStripRenderer(),
                BackColor = Color.FromArgb(0xE6, 0xEE, 0xF8),
                CanOverflow = true,
            };
        }

        private static Bitmap Glyph(char c)
        {
            return Ui.Glyph(c, Ui.S(16), IconColor);
        }

        private ToolStrip BuildMainToolStrip()
        {
            ToolStrip ts = NewToolStrip();
            AddButton(ts, Glyph(''), null, "사이드바 표시/숨기기 (F4)", delegate { ToggleSidebar(); });
            ts.Items.Add(new ToolStripSeparator());

            btnPrev = AddButton(ts, Glyph(''), null, "이전 페이지", delegate { pageView.GoToPage(pageView.CurrentPage - pageView.PageStep); });
            btnNext = AddButton(ts, Glyph(''), null, "다음 페이지", delegate { pageView.GoToPage(pageView.CurrentPage + pageView.PageStep); });
            txtPage = new ToolStripTextBox { AutoSize = false, Width = Ui.S(48), ToolTipText = "페이지 번호 입력 후 Enter (Ctrl+G)" };
            txtPage.TextBox.TextAlign = HorizontalAlignment.Center;
            txtPage.KeyDown += OnPageBoxKeyDown;
            txtPage.Leave += delegate { UpdatePageText(); };
            ts.Items.Add(txtPage);
            lblPageCount = new ToolStripLabel("/ 0");
            ts.Items.Add(lblPageCount);
            ts.Items.Add(new ToolStripSeparator());
            int icon = ts.ImageScalingSize.Width;
            AddToolButton(ts, ToolMode.Select, Ui.IBeamIcon(icon, IconColor), "선택", "글자·주석 선택 (Esc)");
            AddToolButton(ts, ToolMode.Hand, Glyph(''), "손", "끌어서 화면 이동");
            ts.Items.Add(new ToolStripSeparator());

            btnZoomOut = AddButton(ts, Glyph(''), null, "축소 (Ctrl+-)", delegate { pageView.ZoomStep(-1); });
            cmbZoom = new ToolStripComboBox { AutoSize = false, Width = Ui.S(100), DropDownStyle = ComboBoxStyle.DropDown, ToolTipText = "확대/축소" };
            cmbZoom.Items.AddRange(new object[] { "페이지 맞춤", "너비 맞춤", "50%", "75%", "100%", "125%", "150%", "200%", "300%", "400%" });
            cmbZoom.SelectedIndexChanged += delegate { if (!updatingUi) ApplyZoomText(cmbZoom.Text); };
            cmbZoom.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ApplyZoomText(cmbZoom.Text);
                pageView.Focus();
            };
            cmbZoom.Leave += delegate { UpdateZoomText(); };
            ts.Items.Add(cmbZoom);
            btnZoomIn = AddButton(ts, Glyph(''), null, "확대 (Ctrl++)", delegate { pageView.ZoomStep(1); });
            btnTwoPage = AddButton(ts, Ui.TwoPageIcon(icon, IconColor), null, "한 화면에 2장씩 보기 (Ctrl+3)", delegate { ToggleTwoPage(); });
            ts.Items.Add(new ToolStripSeparator());

            txtSearch = new ToolStripTextBox { AutoSize = false, Width = Ui.S(190), ToolTipText = "문서에서 검색 (Ctrl+F)" };
            txtSearch.KeyDown += OnSearchBoxKeyDown;
            ts.Items.Add(txtSearch);
            btnFindPrev = AddButton(ts, Glyph(''), null, "이전 결과 (Shift+F3)", delegate { FindNext(-1); });
            btnFindNext = AddButton(ts, Glyph(''), null, "다음 결과 (F3, Enter)", delegate { FindNext(1); });
            lblFind = new ToolStripLabel("") { ForeColor = Color.FromArgb(0x55, 0x55, 0x55) };
            ts.Items.Add(lblFind);

            return ts;
        }

        private ToolStrip BuildEditToolStrip()
        {
            ToolStrip ts = NewToolStrip();
            int icon = ts.ImageScalingSize.Width;
            AddToolButton(ts, ToolMode.Highlight, Glyph(''), "형광펜", "글자를 끌어서 형광펜 칠하기");
            AddToolButton(ts, ToolMode.Underline, Glyph(''), "밑줄", "글자를 끌어서 밑줄 긋기");
            AddToolButton(ts, ToolMode.StrikeOut, Ui.StrikeIcon(icon, IconColor), "취소선", "글자를 끌어서 취소선 긋기");
            AddToolButton(ts, ToolMode.Note, Glyph(''), "메모", "클릭한 곳에 메모 붙이기");
            AddToolButton(ts, ToolMode.Pen, Glyph(''), "펜", "자유롭게 그리기");

            btnColor = new ToolStripDropDownButton { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "색", AutoToolTip = false };
            for (int i = 0; i < PaletteColors.Length; i++)
            {
                Color c = PaletteColors[i];
                btnColor.DropDownItems.Add(new ToolStripMenuItem(Ui.PaletteNames[i], Ui.Swatch(c, icon), delegate { SetToolColor(c); }));
            }
            ts.Items.Add(btnColor);
            return ts;
        }

        private void AddPageMenuItems(ToolStripItemCollection items)
        {
            items.Add(new ToolStripMenuItem("왼쪽으로 회전", Glyph(''), delegate { RotatePages(-1); }));
            items.Add(new ToolStripMenuItem("오른쪽으로 회전", Glyph(''), delegate { RotatePages(1); }));
            items.Add(new ToolStripMenuItem("페이지 삭제", Glyph(''), delegate { DeletePages(); }) { ShortcutKeyDisplayString = "Del" });
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("페이지 추출...", Glyph(''), delegate { ExtractPages(); }));
            items.Add(new ToolStripMenuItem("PDF 삽입 (선택 페이지 뒤)...", Glyph(''), delegate { InsertPdf(false); }));
            items.Add(new ToolStripMenuItem("PDF 합치기 (맨 뒤에 추가)...", null, delegate { InsertPdf(true); }));
        }

        private ToolStripButton AddButton(ToolStrip ts, Image image, string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton
            {
                Image = image,
                Text = text ?? "",
                DisplayStyle = text == null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = tip,
                AutoToolTip = false,
                Padding = new Padding(Ui.S(3), Ui.S(2), Ui.S(3), Ui.S(2)),
            };
            b.Click += click;
            ts.Items.Add(b);
            return b;
        }

        private void AddToolButton(ToolStrip ts, ToolMode mode, Image image, string text, string tip)
        {
            ToolStripButton b = AddButton(ts, image, text, tip, delegate { OnToolButton(mode); });
            toolButtons[mode] = b;
        }

        // ---------- 메뉴 막대 (Acrobat Reader·Foxit 같은 PDF 뷰어의 표준 구성) ----------

        private static ToolStripMenuItem Item(string text, string shortcut, EventHandler click)
        {
            var m = new ToolStripMenuItem(text, null, click);
            if (shortcut != null) m.ShortcutKeyDisplayString = shortcut;
            return m;
        }

        private MenuStrip BuildMenuBar()
        {
            var bar = new MenuStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new FlatToolStripRenderer(),
                Padding = new Padding(Ui.S(2), Ui.S(1), 0, Ui.S(1)),
            };

            // 파일
            var file = new ToolStripMenuItem("파일(&F)");
            file.DropDownItems.Add(Item("열기(&O)...", "Ctrl+O", delegate { ShowOpenDialog(); }));
            miRecent = new ToolStripMenuItem("최근에 연 파일(&R)");
            miRecent.DropDownItems.Add(new ToolStripMenuItem("(최근 파일 없음)") { Enabled = false });
            miRecent.DropDownOpening += delegate { FillRecentMenu(); };
            file.DropDownItems.Add(miRecent);
            miClose = Item("닫기(&C)", "Ctrl+W", delegate { if (cur != null) CloseTab(cur); });
            file.DropDownItems.Add(miClose);
            file.DropDownItems.Add(new ToolStripSeparator());
            miSave = Item("저장(&S)", "Ctrl+S", delegate { Save(); });
            miSaveAs = Item("다른 이름으로 저장(&A)...", "Ctrl+Shift+S", delegate { SaveAsDialog(); });
            miOptimizeSave = Item("용량 줄여서 저장(&Z)...", null, delegate { SaveOptimizedDialog(); });
            miOptimizeSave.ToolTipText = "안 쓰는 데이터를 지우고 구조를 압축해 저장합니다 (화질 그대로)";
            file.DropDownItems.AddRange(new ToolStripItem[] { miSave, miSaveAs, miOptimizeSave, new ToolStripSeparator() });
            miPrint = Item("인쇄(&P)...", "Ctrl+P", delegate { PrintDocument(); });
            file.DropDownItems.Add(miPrint);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("끝내기(&X)", "Alt+F4", delegate { Close(); }));

            // 편집
            var edit = new ToolStripMenuItem("편집(&E)");
            miUndo = Item("실행 취소(&U)", "Ctrl+Z", delegate { StepHistory(true); });
            miRedo = Item("다시 실행(&R)", "Ctrl+Y", delegate { StepHistory(false); });
            miCopy = Item("복사(&C)", "Ctrl+C", delegate { CopySelection(); });
            miSelectAll = Item("모두 선택(&A)", "Ctrl+A", delegate { pageView.SelectAll(); });
            miFind = Item("찾기(&F)...", "Ctrl+F", delegate { if (doc != null) { txtSearch.Focus(); txtSearch.SelectAll(); } });
            miFindNext = Item("다음 찾기(&N)", "F3", delegate { FindNext(1); });
            miFindPrev = Item("이전 찾기(&P)", "Shift+F3", delegate { FindNext(-1); });
            miDefaultApp = Item("기본 PDF 앱으로 설정(&D)...", null, delegate { SetAsDefaultApp(); });
            edit.DropDownItems.AddRange(new ToolStripItem[]
            {
                miUndo, miRedo, new ToolStripSeparator(),
                miCopy, miSelectAll, new ToolStripSeparator(),
                miFind, miFindNext, miFindPrev, new ToolStripSeparator(),
                miDefaultApp,
            });
            edit.DropDownOpening += delegate
            {
                bool isDefault = IsDefaultPdfApp();
                miDefaultApp.Text = isDefault ? "기본 PDF 앱으로 설정됨(&D)" : "기본 PDF 앱으로 설정(&D)...";
                miDefaultApp.Checked = isDefault;
            };

            // 보기
            var view = new ToolStripMenuItem("보기(&V)");
            miZoomIn = Item("확대(&I)", "Ctrl++", delegate { pageView.ZoomStep(1); });
            miZoomOut = Item("축소(&O)", "Ctrl+-", delegate { pageView.ZoomStep(-1); });
            miFitPage = Item("페이지 맞춤(&P)", "Ctrl+0", delegate { pageView.SetZoom(ZoomMode.FitPage, 1); });
            miFitWidth = Item("너비 맞춤(&W)", "Ctrl+2", delegate { pageView.SetZoom(ZoomMode.FitWidth, 1); });
            miActualSize = Item("실제 크기(&A)", "Ctrl+1", delegate { pageView.SetZoom(ZoomMode.Custom, 1); });
            miPrevPage = Item("이전 페이지(&B)", null, delegate { pageView.GoToPage(pageView.CurrentPage - pageView.PageStep); });
            miNextPage = Item("다음 페이지(&N)", null, delegate { pageView.GoToPage(pageView.CurrentPage + pageView.PageStep); });
            miGoToPage = Item("페이지로 이동(&G)...", "Ctrl+G", delegate { if (doc != null) { txtPage.Focus(); txtPage.SelectAll(); } });
            miSidebar = Item("사이드바(&S)", "F4", delegate { ToggleSidebar(); });
            miTwoPage = Item("2쪽 보기(&T)", "Ctrl+3", delegate { ToggleTwoPage(); });
            view.DropDownItems.AddRange(new ToolStripItem[]
            {
                miZoomIn, miZoomOut, new ToolStripSeparator(),
                miFitPage, miFitWidth, miActualSize, new ToolStripSeparator(),
                miTwoPage, new ToolStripSeparator(),
                miPrevPage, miNextPage, miGoToPage, new ToolStripSeparator(),
                miSidebar,
            });
            view.DropDownOpening += delegate { miSidebar.Checked = !split.Panel1Collapsed; miTwoPage.Checked = pageView.TwoPage; };

            // 문서 (페이지 관리·워터마크·보안)
            miDocument = new ToolStripMenuItem("문서(&D)");
            AddPageMenuItems(miDocument.DropDownItems);
            miDocument.DropDownItems.Add(new ToolStripSeparator());
            miDocument.DropDownItems.Add(Item("워터마크 추가(&W)...", null, delegate { AddWatermark(); }));
            miDocument.DropDownItems.Add(Item("머리글·바닥글 추가(&H)...", null, delegate { AddHeaderFooter(); }));
            miDocument.DropDownItems.Add(new ToolStripSeparator());
            miDocument.DropDownItems.Add(Item("보안 (암호·권한)(&S)...", null, delegate { EditSecurity(); }));

            // 주석
            miAnnotate = new ToolStripMenuItem("주석(&C)");
            AddToolMenuItem(miAnnotate, ToolMode.Select, "선택 도구(&S)");
            AddToolMenuItem(miAnnotate, ToolMode.Hand, "손 도구(&H)");
            miAnnotate.DropDownItems.Add(new ToolStripSeparator());
            AddToolMenuItem(miAnnotate, ToolMode.Highlight, "형광펜(&L)");
            AddToolMenuItem(miAnnotate, ToolMode.Underline, "밑줄(&U)");
            AddToolMenuItem(miAnnotate, ToolMode.StrikeOut, "취소선(&K)");
            AddToolMenuItem(miAnnotate, ToolMode.Note, "메모(&N)");
            AddToolMenuItem(miAnnotate, ToolMode.Pen, "펜(&P)");
            miAnnotate.DropDownOpening += delegate
            {
                foreach (KeyValuePair<ToolMode, ToolStripMenuItem> kv in toolMenuItems)
                    kv.Value.Checked = pageView.Tool == kv.Key;
            };

            // 도움말
            var help = new ToolStripMenuItem("도움말(&H)");
            help.DropDownItems.Add(Item(AppName + " 정보(&A)", null, delegate
            {
                using (var dlg = new AboutDialog(Icon)) dlg.ShowDialog(this);
            }));
            help.DropDownItems.Add(Item("업데이트 확인(&U)", null, delegate
            {
                MongUpdate.UpdateUi.CheckNow(this, "mongpdf", AppName, CurrentVersion, null);
            }));

            bar.Items.AddRange(new ToolStripItem[] { file, edit, view, miDocument, miAnnotate, help });
            return bar;
        }

        private void AddToolMenuItem(ToolStripMenuItem parent, ToolMode mode, string text)
        {
            ToolStripMenuItem m = Item(text, null, delegate { OnToolButton(mode); });
            toolMenuItems[mode] = m;
            parent.DropDownItems.Add(m);
        }

        private void UpdateMenuState(bool has, bool annotate)
        {
            miClose.Enabled = has;
            miTwoPage.Enabled = has;
            miSave.Enabled = has;
            miSaveAs.Enabled = has;
            miOptimizeSave.Enabled = has;
            miPrint.Enabled = CanDo(PdfPermissions.Print);
            miUndo.Enabled = has && history.CanUndo;
            miRedo.Enabled = has && history.CanRedo;
            miCopy.Enabled = has && pageView.HasSelection;
            miSelectAll.Enabled = has;
            miFind.Enabled = has;
            miFindNext.Enabled = has;
            miFindPrev.Enabled = has && matches.Count > 0;
            miZoomIn.Enabled = has;
            miZoomOut.Enabled = has;
            miFitPage.Enabled = has;
            miFitWidth.Enabled = has;
            miActualSize.Enabled = has;
            miPrevPage.Enabled = has && pageView.CurrentPage > 0;
            miNextPage.Enabled = has && pageView.CurrentPage + pageView.PageStep < doc.PageCount;
            miGoToPage.Enabled = has;
            miDocument.Enabled = has;
            foreach (KeyValuePair<ToolMode, ToolStripMenuItem> kv in toolMenuItems)
                kv.Value.Enabled = has && (!UsesColor(kv.Key) || annotate);
        }

        /// <summary>업데이트 비교에 쓰는 앱 버전 (src\App\AssemblyInfo.cs, 예: 0.3.3)</summary>
        private static string CurrentVersion
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            split.Panel1MinSize = Ui.S(120);
            split.SplitterDistance = Ui.S(170);
            Ui.SetCueBanner(txtSearch.TextBox, "문서에서 검색");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 앱을 실행할 때 새 버전이 있는지 확인 (있을 때만 안내, 사용자가 눌러야 받는다)
            MongUpdate.UpdateUi.CheckAtStartup(this, "mongpdf", AppName, CurrentVersion, null);
            OpenFiles(options.Files);
            if (!options.IsTest) SingleInstance.SetHandler(OnFilesFromOtherInstance);
            if (doc != null)
            {
                if (options.Zoom != null) ApplyZoomText(ZoomArgToText(options.Zoom));
                if (options.Page >= 0) pageView.GoToPage(options.Page);
                if (!string.IsNullOrEmpty(options.Search))
                {
                    txtSearch.Text = options.Search;
                    FindNext(1);
                }
            }
            if (options.IsTest) StartTestCapture();
            else pageView.Focus();
        }

        // ---------- 도구 ----------

        private static bool UsesColor(ToolMode mode)
        {
            return mode != ToolMode.Select && mode != ToolMode.Hand;
        }

        private static MarkupKind MarkupFor(ToolMode mode)
        {
            return mode == ToolMode.Underline ? MarkupKind.Underline : mode == ToolMode.StrikeOut ? MarkupKind.StrikeOut : MarkupKind.Highlight;
        }

        private static ToolMode ToolFor(MarkupKind kind)
        {
            return kind == MarkupKind.Underline ? ToolMode.Underline : kind == MarkupKind.StrikeOut ? ToolMode.StrikeOut : ToolMode.Highlight;
        }

        private void OnToolButton(ToolMode mode)
        {
            // 선택 도구로 글자를 먼저 골라 둔 상태에서 형광펜·밑줄·취소선을 누르면 바로 적용한다.
            bool markup = mode == ToolMode.Highlight || mode == ToolMode.Underline || mode == ToolMode.StrikeOut;
            if (markup && pageView.Tool == ToolMode.Select && pageView.HasSelection)
            {
                ApplyMarkup(MarkupFor(mode));
                return;
            }
            SetTool(mode);
        }

        private void SetTool(ToolMode mode)
        {
            pageView.Tool = mode;
            foreach (KeyValuePair<ToolMode, ToolStripButton> kv in toolButtons) kv.Value.Checked = kv.Key == mode;
            UpdateColorButton();
        }

        private void SetToolColor(Color c)
        {
            ToolMode mode = pageView.Tool;
            if (!UsesColor(mode)) return;
            toolColors[mode] = c;
            if (mode == ToolMode.Pen) pageView.PenColor = c;
            UpdateColorButton();
        }

        private void UpdateColorButton()
        {
            ToolMode mode = pageView.Tool;
            Color c = UsesColor(mode) ? toolColors[mode] : Color.FromArgb(0xC0, 0xC0, 0xC0);
            Image old = btnColor.Image;
            btnColor.Image = Ui.Swatch(c, editTool.ImageScalingSize.Width);
            if (old != null) old.Dispose();
            btnColor.Enabled = doc != null && UsesColor(mode);
        }

        // ---------- 문서 열기·저장 ----------

        private void ShowOpenDialog()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF 문서 (*.pdf)|*.pdf|모든 파일 (*.*)|*.*";
                dlg.Title = "PDF 열기";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenFiles(dlg.FileNames);
            }
        }

        private PdfDocument TryOpenDocument(string path)
        {
            string password = null;
            bool retry = false;
            while (true)
            {
                try
                {
                    return PdfDocument.Open(path, password);
                }
                catch (PdfException ex)
                {
                    if (ex.Error != PdfOpenError.PasswordRequired)
                    {
                        ShowError(ex.Message);
                        return null;
                    }
                    using (var dlg = new PasswordDialog(Path.GetFileName(path), retry))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                        password = dlg.Password;
                    }
                    retry = true;
                }
                catch (DllNotFoundException)
                {
                    ShowError("pdfium.dll 파일을 찾을 수 없습니다. 프로그램을 다시 설치해 주세요.");
                    return null;
                }
            }
        }

        // 여러 파일을 각각 탭으로 열고, 처음 연 파일의 탭을 보여 준다.
        private void OpenFiles(IList<string> paths)
        {
            DocTab firstOpened = null;
            foreach (string path in paths)
            {
                DocTab t = OpenFile(path, false);
                if (t != null && firstOpened == null) firstOpened = t;
            }
            if (firstOpened != null) ActivateTab(firstOpened);
        }

        // 다른 몽PDF 실행(탐색기에서 PDF를 연 경우)에서 넘어온 파일을 탭으로 연다.
        private void OnFilesFromOtherInstance(string[] files)
        {
            PostUi(delegate
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                OpenFiles(files);
                Activate();
            });
        }

        private int FindTab(string fullPath)
        {
            for (int i = 0; i < tabs.Count; i++)
                if (tabs[i].Doc != null && string.Equals(Path.GetFullPath(tabs[i].Doc.FilePath), fullPath, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // 파일을 새 탭으로 연다. 이미 열려 있으면 그 탭을 돌려준다. activate가 true면 그 탭으로 이동한다.
        private DocTab OpenFile(string path, bool activate = true)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                ShowError("파일 경로가 올바르지 않습니다.\n" + path);
                return null;
            }
            int existing = FindTab(full);
            if (existing >= 0)
            {
                if (activate) ActivateTab(tabs[existing]);
                return tabs[existing];
            }

            PdfDocument opened = TryOpenDocument(path);
            if (opened == null) return null;

            var tab = new DocTab { Doc = opened };
            tab.SavedVersion = tab.History.CurrentVersion;
            tabs.Add(tab);
            tabStrip.Add(Path.GetFileName(opened.FilePath), opened.FilePath);
            tabStrip.Visible = true;
            settings.AddRecent(path);
            if (activate) ActivateTab(tab);
            return tab;
        }

        // 지금 보는 탭을 바꾼다. 떠나는 탭의 보던 위치는 기억해 두었다가 돌아오면 되돌린다.
        private void ActivateTab(DocTab tab)
        {
            if (tab == cur) return;
            if (cur != null) cur.View = pageView.CaptureView();
            cur = tab;
            ShowCurrentTab();
        }

        private void ShowCurrentTab()
        {
            CancelSearch();
            worker.Clear();
            if (cur == null)
            {
                pageView.SetDocument(null);
                thumbs.SetDocument(null, false);
                outline.Nodes.Clear();
                if (sideTabs.TabPages.Contains(outlineTab)) sideTabs.TabPages.Remove(outlineTab);
            }
            else
            {
                if (!CanDo(PdfPermissions.Annotate)) SetTool(ToolMode.Select);
                pageView.SetDocument(doc, cur.View);
                thumbs.SetDocument(doc, false);
                LoadOutline();
                tabStrip.Select(tabs.IndexOf(cur));
            }
            ResetSearchState();
            UpdateTitle();
            UpdateUiState();
            OnCurrentPageChanged();
        }

        // 탭을 닫는다. 저장하지 않은 변경이 있으면 먼저 묻는다.
        private void CloseTab(DocTab tab)
        {
            if (tab != cur) ActivateTab(tab);
            if (!ConfirmDiscard()) return;

            int index = tabs.IndexOf(tab);
            PdfDocument closing = tab.Doc;
            tabs.RemoveAt(index);
            tabStrip.RemoveAt(index);
            cur = null;
            if (tabs.Count > 0) cur = tabs[Math.Min(index, tabs.Count - 1)];
            else tabStrip.Visible = false;
            ShowCurrentTab();
            if (closing != null) closing.Dispose();
        }

        private void SwitchTab(int direction)
        {
            if (tabs.Count < 2 || cur == null) return;
            ActivateTab(tabs[(tabs.IndexOf(cur) + direction + tabs.Count) % tabs.Count]);
        }

        private void ToggleTwoPage()
        {
            pageView.TwoPage = !pageView.TwoPage;
            settings.TwoPage = pageView.TwoPage;
            settings.Save();
            UpdateTwoPageUi();
            UpdateUiState();
            OnCurrentPageChanged();
        }

        private void UpdateTwoPageUi()
        {
            btnTwoPage.Checked = pageView.TwoPage;
        }

        private bool IsDirty
        {
            get { return cur != null && IsTabDirty(cur); }
        }

        private static bool IsTabDirty(DocTab t)
        {
            return t.Doc != null && (t.History.CurrentVersion != t.SavedVersion || t.TargetSecurity != t.SavedSecurity);
        }

        private string DocName
        {
            get { return doc == null ? "" : Path.GetFileName(doc.FilePath); }
        }

        private void UpdateTitle()
        {
            Text = doc == null ? AppName : DocName + (IsDirty ? " *" : "") + " - " + AppName;
            int tabIndex = tabs.IndexOf(cur);
            if (tabIndex >= 0) tabStrip.SetText(tabIndex, DocName, IsDirty);
        }

        // 저장하지 않은 변경이 있으면 저장할지 묻는다. 계속 진행해도 되면 true.
        private bool ConfirmDiscard()
        {
            if (!IsDirty || options.IsTest) return true;
            DialogResult r = MessageBox.Show(this, "'" + DocName + "'의 변경 내용을 저장할까요?", AppName,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save();
            return true;
        }

        private bool Save()
        {
            return doc != null && SaveTo(doc.FilePath);
        }

        private bool SaveAsDialog()
        {
            if (doc == null) return false;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PDF 문서 (*.pdf)|*.pdf";
                dlg.DefaultExt = "pdf";
                dlg.FileName = DocName;
                dlg.InitialDirectory = Path.GetDirectoryName(doc.FilePath);
                dlg.Title = "다른 이름으로 저장";
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                bool ok = SaveTo(dlg.FileName);
                if (ok) settings.AddRecent(dlg.FileName);
                return ok;
            }
        }

        private void SaveOptimizedDialog()
        {
            if (doc == null) return;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PDF 문서 (*.pdf)|*.pdf";
                dlg.DefaultExt = "pdf";
                dlg.FileName = DocName;
                dlg.InitialDirectory = Path.GetDirectoryName(doc.FilePath);
                dlg.Title = "용량 줄여서 저장";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                long before = File.Exists(doc.FilePath) ? new FileInfo(doc.FilePath).Length : 0;
                if (!SaveTo(dlg.FileName, true)) return;
                settings.AddRecent(dlg.FileName);
                long after = new FileInfo(dlg.FileName).Length;
                string msg = before > 0
                    ? "저장했습니다.\n\n원본 " + FormatSize(before) + " → " + FormatSize(after) +
                      (after < before ? " (" + (100 - after * 100 / before) + "% 줄어듦)" : "")
                    : "저장했습니다. (" + FormatSize(after) + ")";
                MessageBox.Show(this, msg + "\n\n화질은 그대로이며, 사진이 많은 문서는 크게 줄지 않을 수 있습니다.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB";
            return Math.Max(1, bytes / 1024) + "KB";
        }

        private bool SaveTo(string path, bool optimize = false)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                doc.SaveAs(path, new PdfSaveOptions { Security = targetSecurity, Optimize = optimize });
                savedVersion = history.CurrentVersion;
                savedSecurity = targetSecurity;
                UpdateTitle();
                return true;
            }
            catch (DllNotFoundException)
            {
                ShowError("qpdf30.dll 파일을 찾을 수 없습니다. 프로그램을 다시 설치해 주세요.");
                return false;
            }
            catch (Exception ex)
            {
                ShowError("저장하지 못했습니다.\n" + ex.Message);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void LoadOutline()
        {
            outline.BeginUpdate();
            outline.Nodes.Clear();
            IList<PdfBookmark> marks = doc.GetBookmarks();
            AddOutlineNodes(outline.Nodes, marks);
            outline.EndUpdate();

            bool has = marks.Count > 0;
            if (has && !sideTabs.TabPages.Contains(outlineTab)) sideTabs.TabPages.Add(outlineTab);
            if (!has && sideTabs.TabPages.Contains(outlineTab)) sideTabs.TabPages.Remove(outlineTab);
        }

        private static void AddOutlineNodes(TreeNodeCollection nodes, IList<PdfBookmark> marks)
        {
            foreach (PdfBookmark m in marks)
            {
                var node = new TreeNode(m.Title);
                if (m.PageIndex >= 0) node.Tag = m.PageIndex;
                AddOutlineNodes(node.Nodes, m.Children);
                nodes.Add(node);
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = AllPdfs(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            IList<string> paths = AllPdfs(e.Data);
            if (paths.Count == 0) return;
            Activate();
            OpenFiles(paths);
        }

        private static IList<string> AllPdfs(IDataObject data)
        {
            var result = new List<string>();
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return result;
            foreach (string f in files)
                if (string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)) result.Add(f);
            return result;
        }

        private void PrintDocument()
        {
            if (!Require(PdfPermissions.Print, "인쇄")) return;
            try
            {
                PdfPrinter.Print(this, doc, DocName, pageView.CurrentPage);
            }
            catch (Exception ex)
            {
                ShowError("인쇄하지 못했습니다.\n" + ex.Message);
            }
        }

        // ---------- 최근 파일·기본 앱 ----------

        private void FillRecentMenu()
        {
            foreach (ToolStripItem item in new List<ToolStripItem>(Cast(miRecent.DropDownItems))) item.Dispose();
            miRecent.DropDownItems.Clear();
            if (settings.RecentFiles.Count == 0)
            {
                miRecent.DropDownItems.Add(new ToolStripMenuItem("(최근 파일 없음)") { Enabled = false });
                return;
            }
            int n = 1;
            foreach (string file in new List<string>(settings.RecentFiles))
            {
                string f = file;
                var item = new ToolStripMenuItem("&" + (n++ % 10) + "  " + Path.GetFileName(f), null, delegate { OpenRecent(f); })
                {
                    ToolTipText = f,
                };
                miRecent.DropDownItems.Add(item);
            }
            miRecent.DropDownItems.Add(new ToolStripSeparator());
            miRecent.DropDownItems.Add(new ToolStripMenuItem("목록 지우기", null, delegate
            {
                settings.RecentFiles.Clear();
                settings.Save();
            }));
        }

        private void OpenRecent(string path)
        {
            if (!File.Exists(path))
            {
                ShowError("파일을 찾을 수 없어 최근 목록에서 뺍니다.\n" + path);
                settings.RemoveRecent(path);
                return;
            }
            OpenFile(path);
        }

        private static bool IsDefaultPdfApp()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice"))
                {
                    return k != null && string.Equals(k.GetValue("ProgId") as string, ProgId, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        // Windows 10/11은 프로그램이 스스로 기본 앱이 될 수 없어, 설치 때 등록해 둔 항목을 사용자가 설정 화면에서 고르게 한다.
        private void SetAsDefaultApp()
        {
            if (IsDefaultPdfApp())
            {
                MessageBox.Show(this, "이미 " + AppName + "이(가) 기본 PDF 앱입니다.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string scope = null;
            if (IsRegistered(Microsoft.Win32.Registry.CurrentUser)) scope = "registeredAppUser";
            else if (IsRegistered(Microsoft.Win32.Registry.LocalMachine)) scope = "registeredAppMachine";
            if (scope == null)
            {
                ShowError("설치 프로그램으로 설치한 경우에만 기본 PDF 앱으로 설정할 수 있습니다.");
                return;
            }
            MessageBox.Show(this, "Windows 설정이 열리면 '.pdf' 항목에서 " + AppName + "을(를) 선택하세요.", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            try
            {
                System.Diagnostics.Process.Start("ms-settings:defaultapps?" + scope + "=" + Uri.EscapeDataString(AppName));
            }
            catch (Exception)
            {
                System.Diagnostics.Process.Start("ms-settings:defaultapps");
            }
        }

        private static bool IsRegistered(Microsoft.Win32.RegistryKey root)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = root.OpenSubKey(@"Software\RegisteredApplications"))
                {
                    return k != null && k.GetValue(AppName) != null;
                }
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        private static bool IsOnScreen(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle inter = Rectangle.Intersect(s.WorkingArea, r);
                if (inter.Width >= 100 && inter.Height >= 100) return true;
            }
            return false;
        }

        // ---------- 권한 ----------

        private bool CanDo(PdfPermissions p)
        {
            return doc != null && (doc.Permissions & p) == p;
        }

        // 문서 권한에 막힌 작업이면 안내하고 false를 돌려준다.
        private bool Require(PdfPermissions p, string what)
        {
            if (doc == null) return false;
            if (CanDo(p)) return true;
            ShowError("이 문서는 " + what + "이(가) 제한되어 있습니다.\n'문서 → 보안'에서 권한 암호를 입력하면 제한을 풀 수 있습니다.");
            return false;
        }

        // ---------- 워터마크·머리글·보안 ----------

        private void AddWatermark()
        {
            if (!Require(PdfPermissions.Modify, "내용 편집")) return;
            using (var dlg = new WatermarkDialog(doc, pageView.CurrentPage))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                VectorShape shape = dlg.BuildShape();
                StampStyle style = dlg.BuildStyle();
                int[] pages = dlg.Pages;
                ApplyEdit(delegate(PdfDocument d) { foreach (int p in pages) d.StampShape(p, shape, style); }, false, pages);
            }
        }

        private void AddHeaderFooter()
        {
            if (!Require(PdfPermissions.Modify, "내용 편집")) return;
            using (var dlg = new HeaderFooterDialog(doc, pageView.CurrentPage))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int[] pages = dlg.Pages;
                var work = new List<KeyValuePair<int, IList<KeyValuePair<VectorShape, StampStyle>>>>();
                foreach (int p in pages) work.Add(new KeyValuePair<int, IList<KeyValuePair<VectorShape, StampStyle>>>(p, dlg.ItemsFor(p)));
                ApplyEdit(delegate(PdfDocument d)
                {
                    foreach (var w in work)
                        foreach (KeyValuePair<VectorShape, StampStyle> item in w.Value) d.StampShape(w.Key, item.Key, item.Value);
                }, false, pages);
            }
        }

        private void EditSecurity()
        {
            if (doc == null) return;
            using (var dlg = new SecurityDialog(doc, targetSecurity))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.UnlockPassword != null)
                {
                    UnlockWithOwnerPassword(dlg.UnlockPassword);
                    return;
                }
                targetSecurity = dlg.Result;
                UpdateTitle();
                UpdateUiState();
                MessageBox.Show(this, (targetSecurity.IsNone ? "보안 제거는" : "새 보안 설정은") + " 저장할 때 적용됩니다. (Ctrl+S)",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void UnlockWithOwnerPassword(string ownerPassword)
        {
            PdfDocument unlocked;
            try
            {
                unlocked = PdfDocument.Load(doc.SaveToBytes(), doc.FilePath, ownerPassword);
            }
            catch (PdfException)
            {
                ShowError("권한 암호가 올바르지 않습니다.");
                return;
            }
            if (unlocked.Permissions != PdfPermissions.All)
            {
                unlocked.Dispose();
                ShowError("입력한 암호는 열기 암호입니다. 제한을 풀려면 권한 암호를 입력하세요.");
                return;
            }
            ReplaceDocument(unlocked);
            UpdateUiState();
            MessageBox.Show(this, "권한 제한을 풀었습니다. 이제 보안 설정을 바꿀 수 있습니다.", AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 편집 공통 ----------

        // 편집 직전 상태를 기록해 두고 편집을 실행한다. 실패하면 편집 전 상태로 되돌린다.
        private bool ApplyEdit(Action<PdfDocument> edit, bool structural, params int[] refreshPages)
        {
            if (doc == null) return false;
            byte[] before;
            Cursor = Cursors.WaitCursor;
            try
            {
                before = doc.SaveToBytes();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                ShowError("편집을 준비하지 못했습니다.\n" + ex.Message);
                return false;
            }

            try
            {
                edit(doc);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                ShowError("편집하지 못했습니다.\n" + ex.Message);
                ReplaceDocument(PdfDocument.Load(before, doc.FilePath, doc.Password));
                return false;
            }
            Cursor = Cursors.Default;

            history.Record(before);
            if (structural)
            {
                ReplaceDocument(doc);
            }
            else
            {
                foreach (int p in refreshPages)
                {
                    pageView.RefreshPage(p);
                    thumbs.RefreshPage(p);
                }
            }
            UpdateTitle();
            UpdateUiState();
            return true;
        }

        // 문서 내용이 크게 바뀌었을 때(페이지 편집, 실행 취소) 화면을 모두 새로 맞춘다.
        private void ReplaceDocument(PdfDocument newDoc)
        {
            PdfDocument old = doc;
            CancelSearch();
            ResetSearchState();
            worker.Clear();
            doc = newDoc;
            pageView.ReloadDocument(doc);
            thumbs.SetDocument(doc, true);
            LoadOutline();
            if (old != null && old != newDoc) old.Dispose();
            OnCurrentPageChanged();
        }

        private void StepHistory(bool undo)
        {
            if (doc == null || (undo ? !history.CanUndo : !history.CanRedo)) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                byte[] current = doc.SaveToBytes();
                byte[] target = undo ? history.Undo(current) : history.Redo(current);
                ReplaceDocument(PdfDocument.Load(target, doc.FilePath, doc.Password));
            }
            catch (Exception ex)
            {
                ShowError((undo ? "실행 취소" : "다시 실행") + "하지 못했습니다.\n" + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            UpdateTitle();
            UpdateUiState();
        }

        // ---------- 페이지 편집 ----------

        private int[] TargetPages()
        {
            int[] sel = thumbs.SelectedPages;
            if (sel.Length > 0) return sel;
            int c = pageView.CurrentPage;
            return c >= 0 ? new[] { c } : new int[0];
        }

        private static int[] Range(int start, int count)
        {
            var r = new int[count];
            for (int i = 0; i < count; i++) r[i] = start + i;
            return r;
        }

        private void RotatePages(int quarterTurns)
        {
            if (!Require(PdfPermissions.Assemble, "페이지 편집")) return;
            int[] pages = TargetPages();
            if (pages.Length == 0) return;
            if (ApplyEdit(delegate(PdfDocument d) { d.RotatePages(pages, quarterTurns); }, true))
                thumbs.SelectPages(pages);
        }

        private void DeletePages()
        {
            if (!Require(PdfPermissions.Assemble, "페이지 편집")) return;
            int[] pages = TargetPages();
            if (pages.Length == 0) return;
            if (pages.Length >= doc.PageCount)
            {
                ShowError("모든 페이지를 삭제할 수는 없습니다. 최소 한 페이지는 남아 있어야 합니다.");
                return;
            }
            if (!ApplyEdit(delegate(PdfDocument d) { d.DeletePages(pages); }, true)) return;
            int go = Math.Min(pages[0], doc.PageCount - 1);
            pageView.GoToPage(go);
            thumbs.SelectPages(new[] { go });
        }

        private void MovePages(int[] pages, int dest)
        {
            if (!Require(PdfPermissions.Assemble, "페이지 편집")) return;
            if (!ApplyEdit(delegate(PdfDocument d) { d.MovePages(pages, dest); }, true)) return;
            pageView.GoToPage(dest);
            thumbs.SelectPages(Range(dest, pages.Length));
        }

        private void ExtractPages()
        {
            if (!Require(PdfPermissions.Assemble | PdfPermissions.Copy, "페이지 추출")) return;
            int[] pages = TargetPages();
            if (pages.Length == 0) return;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PDF 문서 (*.pdf)|*.pdf";
                dlg.DefaultExt = "pdf";
                dlg.FileName = Path.GetFileNameWithoutExtension(doc.FilePath) + "_추출.pdf";
                dlg.InitialDirectory = Path.GetDirectoryName(doc.FilePath);
                dlg.Title = pages.Length + "개 페이지 추출";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    doc.ExtractPages(pages, dlg.FileName);
                    MessageBox.Show(this, pages.Length + "개 페이지를 '" + Path.GetFileName(dlg.FileName) + "'(으)로 저장했습니다.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    ShowError("페이지를 추출하지 못했습니다.\n" + ex.Message);
                }
            }
        }

        private void InsertPdf(bool atEnd)
        {
            if (!Require(PdfPermissions.Assemble, "페이지 편집")) return;
            string path;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF 문서 (*.pdf)|*.pdf";
                dlg.Title = atEnd ? "맨 뒤에 합칠 PDF" : "삽입할 PDF";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            PdfDocument source = TryOpenDocument(path);
            if (source == null) return;
            try
            {
                int[] pages = TargetPages();
                int index = atEnd || pages.Length == 0 ? doc.PageCount : pages[pages.Length - 1] + 1;
                int count = source.PageCount;
                if (!ApplyEdit(delegate(PdfDocument d) { d.InsertPagesFrom(source, index); }, true)) return;
                pageView.GoToPage(index);
                thumbs.SelectPages(Range(index, count));
            }
            finally
            {
                source.Dispose();
            }
        }

        // ---------- 주석 ----------

        private void ApplyMarkup(MarkupKind kind)
        {
            if (!Require(PdfPermissions.Annotate, "주석 추가")) return;
            IList<TextRange> ranges = pageView.GetSelectionRanges();
            if (ranges.Count == 0) return;
            PdfColor color = Ui.ToPdf(toolColors[ToolFor(kind)]);
            var pages = new List<int>();
            foreach (TextRange r in ranges) pages.Add(r.Page);
            pageView.ClearSelection();
            ApplyEdit(delegate(PdfDocument d)
            {
                foreach (TextRange r in ranges) d.AddTextMarkup(r.Page, r.Start, r.Count, kind, color);
            }, false, pages.ToArray());
        }

        private void AddNote(int page, PagePoint at)
        {
            if (!Require(PdfPermissions.Annotate, "주석 추가")) return;
            string text;
            using (var dlg = new NoteDialog("메모 추가", ""))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                text = dlg.NoteText.Trim();
            }
            if (text.Length == 0) return;
            PdfColor color = Ui.ToPdf(toolColors[ToolMode.Note]);
            ApplyEdit(delegate(PdfDocument d) { d.AddNote(page, at, text, color); }, false, page);
        }

        private void EditNote(int page, PdfAnnotationInfo note)
        {
            if (!Require(PdfPermissions.Annotate, "주석 편집")) return;
            string text;
            using (var dlg = new NoteDialog("메모 편집", note.Contents))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                text = dlg.NoteText.Trim();
            }
            if (text == note.Contents) return;
            ApplyEdit(delegate(PdfDocument d) { d.SetAnnotationContents(page, note.Index, text); }, false, page);
        }

        private void AddInk(int page, IList<PagePoint> stroke)
        {
            if (!Require(PdfPermissions.Annotate, "주석 추가")) return;
            PdfColor color = Ui.ToPdf(toolColors[ToolMode.Pen]);
            float width = pageView.PenWidthPt;
            ApplyEdit(delegate(PdfDocument d) { d.AddInk(page, stroke, color, width); }, false, page);
        }

        private void DeleteSelectedAnnotation()
        {
            PdfAnnotationInfo a = pageView.SelectedAnnotation;
            int page = pageView.SelectedAnnotationPage;
            if (a == null || page < 0) return;
            if (!Require(PdfPermissions.Annotate, "주석 편집")) return;
            ApplyEdit(delegate(PdfDocument d) { d.RemoveAnnotation(page, a.Index); }, false, page);
        }

        private void CopySelection()
        {
            if (!pageView.HasSelection) return;
            if (!Require(PdfPermissions.Copy, "내용 복사")) return;
            string text = pageView.GetSelectedText();
            if (text.Length == 0) return;
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowError("클립보드를 다른 프로그램이 사용 중입니다. 잠시 후 다시 시도해 주세요.");
            }
        }

        private void ShowViewMenu(Point location)
        {
            foreach (ToolStripItem item in new List<ToolStripItem>(Cast(viewMenu.Items))) item.Dispose();
            viewMenu.Items.Clear();
            if (doc == null) return;

            if (pageView.HasSelection)
            {
                viewMenu.Items.Add(new ToolStripMenuItem("복사", Glyph(''), delegate { CopySelection(); }) { ShortcutKeyDisplayString = "Ctrl+C" });
                viewMenu.Items.Add(new ToolStripSeparator());
                viewMenu.Items.Add(new ToolStripMenuItem("형광펜", Glyph(''), delegate { ApplyMarkup(MarkupKind.Highlight); }));
                viewMenu.Items.Add(new ToolStripMenuItem("밑줄", Glyph(''), delegate { ApplyMarkup(MarkupKind.Underline); }));
                viewMenu.Items.Add(new ToolStripMenuItem("취소선", Ui.StrikeIcon(Ui.S(16), IconColor), delegate { ApplyMarkup(MarkupKind.StrikeOut); }));
            }

            PdfAnnotationInfo a = pageView.SelectedAnnotation;
            int page = pageView.SelectedAnnotationPage;
            if (a != null)
            {
                if (viewMenu.Items.Count > 0) viewMenu.Items.Add(new ToolStripSeparator());
                if (a.Kind == AnnotationKind.Note)
                    viewMenu.Items.Add(new ToolStripMenuItem("메모 편집...", Glyph(''), delegate { EditNote(page, a); }));
                viewMenu.Items.Add(new ToolStripMenuItem("주석 삭제", Glyph(''), delegate { DeleteSelectedAnnotation(); }) { ShortcutKeyDisplayString = "Del" });
            }

            if (viewMenu.Items.Count == 0)
                viewMenu.Items.Add(new ToolStripMenuItem("모두 선택", null, delegate { pageView.SelectAll(); }) { ShortcutKeyDisplayString = "Ctrl+A" });
            viewMenu.Show(pageView, location);
        }

        private static IEnumerable<ToolStripItem> Cast(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items) yield return item;
        }

        // ---------- 페이지·확대 표시 ----------

        private void OnCurrentPageChanged()
        {
            UpdatePageText();
            thumbs.CurrentPage = pageView.CurrentPage;
            UpdateUiState();
        }

        private void UpdatePageText()
        {
            int count = doc == null ? 0 : doc.PageCount;
            if (!txtPage.Focused) txtPage.Text = doc == null ? "" : (pageView.CurrentPage + 1).ToString();
            lblPageCount.Text = "/ " + count;
        }

        private void OnPageBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                int n;
                if (doc != null && int.TryParse(txtPage.Text.Trim(), out n))
                    pageView.GoToPage(Math.Max(1, Math.Min(doc.PageCount, n)) - 1);
                pageView.Focus();
                UpdatePageText();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                pageView.Focus();
                UpdatePageText();
            }
        }

        private void UpdateZoomText()
        {
            if (cmbZoom.Focused) return;
            updatingUi = true;
            cmbZoom.Text = doc == null ? "" : Math.Round(pageView.Zoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
            updatingUi = false;
        }

        private void ApplyZoomText(string text)
        {
            if (text == null) return;
            text = text.Trim();
            if (text == "페이지 맞춤") pageView.SetZoom(ZoomMode.FitPage, 1);
            else if (text == "너비 맞춤") pageView.SetZoom(ZoomMode.FitWidth, 1);
            else
            {
                double pct;
                if (double.TryParse(text.TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out pct) && pct > 0)
                    pageView.SetZoom(ZoomMode.Custom, pct / 100.0);
            }
            UpdateZoomText();
        }

        private static string ZoomArgToText(string arg)
        {
            switch (arg.ToLowerInvariant())
            {
                case "fitpage": return "페이지 맞춤";
                case "fitwidth": return "너비 맞춤";
                default: return arg;
            }
        }

        private void ToggleSidebar()
        {
            split.Panel1Collapsed = !split.Panel1Collapsed;
            settings.SidebarVisible = !split.Panel1Collapsed;
            settings.Save();
        }

        private void UpdateUiState()
        {
            bool has = doc != null;
            int page = pageView.CurrentPage;
            btnPrev.Enabled = has && page > 0;
            btnNext.Enabled = has && page + pageView.PageStep < doc.PageCount;
            txtPage.Enabled = has;
            btnZoomOut.Enabled = has;
            btnZoomIn.Enabled = has;
            btnTwoPage.Enabled = has;
            cmbZoom.Enabled = has;
            txtSearch.Enabled = has;
            btnFindPrev.Enabled = has && matches.Count > 0;
            btnFindNext.Enabled = has;
            bool annotate = CanDo(PdfPermissions.Annotate);
            foreach (KeyValuePair<ToolMode, ToolStripButton> kv in toolButtons)
                kv.Value.Enabled = has && (!UsesColor(kv.Key) || annotate);
            btnColor.Enabled = has && annotate && UsesColor(pageView.Tool);
            UpdateMenuState(has, annotate);
        }

        // ---------- 검색 ----------

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                FindNext(e.Shift ? -1 : 1);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CancelSearch();
                ResetSearchState();
                pageView.Focus();
            }
        }

        private void FindNext(int direction)
        {
            if (doc == null) return;
            string text = txtSearch.Text.Trim();
            if (text.Length == 0) return;
            if (text != searchText)
            {
                StartSearch(text);
                return;
            }
            if (matches.Count == 0) return;
            matchIndex = (matchIndex + direction + matches.Count) % matches.Count;
            pageView.ShowMatch(matches[matchIndex]);
            UpdateFindLabel();
        }

        private void StartSearch(string text)
        {
            CancelSearch();
            ResetSearchState();
            searchText = text;
            searching = true;
            UpdateFindLabel();

            int gen = searchGeneration;
            PdfDocument d = doc;
            int start = Math.Max(0, pageView.CurrentPage);
            int count = d.PageCount;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int k = 0; k < count; k++)
                {
                    if (gen != searchGeneration) return;
                    int page = (start + k) % count;
                    IList<PdfSearchMatch> found;
                    try
                    {
                        found = d.SearchPage(page, text, false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (PdfException)
                    {
                        continue;
                    }
                    if (found.Count > 0) PostUi(delegate { OnSearchFound(gen, found); });
                }
                PostUi(delegate { OnSearchDone(gen); });
            });
        }

        private void PostUi(Action action)
        {
            try
            {
                if (!IsDisposed) BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnSearchFound(int gen, IList<PdfSearchMatch> found)
        {
            if (gen != searchGeneration) return;
            matches.AddRange(found);
            foreach (PdfSearchMatch m in found) pageView.AddHighlights(m.PageIndex, m.Rects);
            if (matchIndex < 0)
            {
                matchIndex = 0;
                pageView.ShowMatch(matches[0]);
            }
            UpdateFindLabel();
            UpdateUiState();
        }

        private void OnSearchDone(int gen)
        {
            if (gen != searchGeneration) return;
            searching = false;
            UpdateFindLabel();
        }

        private void CancelSearch()
        {
            searchGeneration++;
            searching = false;
        }

        private void ResetSearchState()
        {
            searchText = null;
            matches.Clear();
            matchIndex = -1;
            pageView.ClearHighlights();
            UpdateFindLabel();
            UpdateUiState();
        }

        private void UpdateFindLabel()
        {
            if (searchText == null) lblFind.Text = "";
            else if (matches.Count == 0) lblFind.Text = searching ? "검색 중…" : "결과 없음";
            else lblFind.Text = (matchIndex + 1) + " / " + matches.Count + (searching ? " …" : "");
        }

        // ---------- 단축키 ----------

        private bool TextInputFocused
        {
            get { return txtSearch.Focused || txtPage.Focused || cmbZoom.Focused; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.O: ShowOpenDialog(); return true;
                case Keys.Control | Keys.S: Save(); return true;
                case Keys.Control | Keys.Shift | Keys.S: SaveAsDialog(); return true;
                case Keys.Control | Keys.P: PrintDocument(); return true;
                case Keys.Control | Keys.W:
                    if (cur != null) CloseTab(cur);
                    return true;
                case Keys.Control | Keys.Tab: SwitchTab(1); return true;
                case Keys.Control | Keys.Shift | Keys.Tab: SwitchTab(-1); return true;
                case Keys.Control | Keys.D3:
                    if (doc != null) ToggleTwoPage();
                    return true;
                case Keys.Control | Keys.F:
                    if (doc != null) { txtSearch.Focus(); txtSearch.SelectAll(); }
                    return true;
                case Keys.Control | Keys.G:
                    if (doc != null) { txtPage.Focus(); txtPage.SelectAll(); }
                    return true;
                case Keys.F3: FindNext(1); return true;
                case Keys.Shift | Keys.F3: FindNext(-1); return true;
                case Keys.F4: ToggleSidebar(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add:
                    if (doc != null) pageView.ZoomStep(1);
                    return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract:
                    if (doc != null) pageView.ZoomStep(-1);
                    return true;
                case Keys.Control | Keys.D0: pageView.SetZoom(ZoomMode.FitPage, 1); return true;
                case Keys.Control | Keys.D1: pageView.SetZoom(ZoomMode.Custom, 1); return true;
                case Keys.Control | Keys.D2: pageView.SetZoom(ZoomMode.FitWidth, 1); return true;

                case Keys.Control | Keys.Z:
                    if (TextInputFocused) break;
                    StepHistory(true);
                    return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z:
                    if (TextInputFocused) break;
                    StepHistory(false);
                    return true;
                case Keys.Control | Keys.C:
                    if (TextInputFocused) break;
                    CopySelection();
                    return true;
                case Keys.Control | Keys.A:
                    if (TextInputFocused || thumbs.Focused) break;
                    pageView.SelectAll();
                    return true;
                case Keys.Delete:
                    if (TextInputFocused || thumbs.Focused || pageView.SelectedAnnotation == null) break;
                    DeleteSelectedAnnotation();
                    return true;
                case Keys.Escape:
                    if (!TextInputFocused && pageView.Tool != ToolMode.Select) SetTool(ToolMode.Select);
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- 시험 실행 (--shot) ----------

        private void StartTestCapture()
        {
            int idleTicks = 0;
            DateTime deadline = DateTime.Now.AddSeconds(20);
            var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += delegate
            {
                idleTicks = worker.IsIdle && !searching ? idleTicks + 1 : 0;
                if (idleTicks < 4 && DateTime.Now < deadline) return;
                timer.Stop();
                timer.Dispose();
                if (options.Dialog != null && doc != null)
                {
                    CaptureTestDialog();
                    return;
                }
                try
                {
                    SaveScreenshot(Bounds, options.ShotPath);
                }
                finally
                {
                    Close();
                }
            };
            timer.Start();
        }

        private static void SaveScreenshot(Rectangle bounds, string path)
        {
            using (var bmp = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private void CaptureTestDialog()
        {
            ToolDialog.NoActivateForTest = true;
            Form dlg;
            switch (options.Dialog)
            {
                case "watermark": dlg = new WatermarkDialog(doc, pageView.CurrentPage); break;
                case "headerfooter": dlg = new HeaderFooterDialog(doc, pageView.CurrentPage); break;
                case "security": dlg = new SecurityDialog(doc, targetSecurity); break;
                default: dlg = new AboutDialog(Icon); break;
            }
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(Left + Ui.S(40), Top + Ui.S(40));
            dlg.TopMost = true;
            dlg.Show(this);
            var wait = new System.Windows.Forms.Timer { Interval = 1200 };
            wait.Tick += delegate
            {
                wait.Stop();
                wait.Dispose();
                try
                {
                    SaveScreenshot(dlg.Bounds, options.ShotPath);
                }
                finally
                {
                    dlg.Close();
                    dlg.Dispose();
                    Close();
                }
            };
            wait.Start();
        }

        // 저장하지 않은 탭이 있으면 하나씩 보여 주며 묻는다. 모두 닫아도 되면 true.
        private bool ConfirmDiscardAll()
        {
            foreach (DocTab t in new List<DocTab>(tabs))
            {
                if (!IsTabDirty(t) || options.IsTest) continue;
                ActivateTab(t);
                if (!ConfirmDiscard()) return false;
            }
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && !ConfirmDiscardAll()) e.Cancel = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            settings.Maximized = WindowState == FormWindowState.Maximized;
            settings.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.Save();
            CancelSearch();
            worker.Dispose();
            foreach (DocTab t in tabs)
                if (t.Doc != null) t.Doc.Dispose();
            base.OnFormClosed(e);
        }
    }
}
