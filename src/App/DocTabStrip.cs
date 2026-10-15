using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PdfViewer
{
    // 창 위쪽에 열려 있는 PDF 파일을 탭으로 보여 준다.
    // 탭이 넘치면 양옆에 화살표가 생기고, 화살표는 선택을 바꾸지 않고 탭 목록만 한 칸씩 민다.
    internal sealed class DocTabStrip : Control
    {
        private sealed class Item
        {
            public string Text;
            public string Tip;
            public bool Dirty;
        }

        private static readonly Color Back = Color.FromArgb(0xA9, 0xBC, 0xD6);
        private static readonly Color TabBack = Color.FromArgb(0xCF, 0xDB, 0xEB);
        private static readonly Color TabActive = Color.FromArgb(0x1A, 0x73, 0xE8);
        private static readonly Color TabHover = Color.FromArgb(0xE3, 0xEB, 0xF6);
        private static readonly Color Accent = Color.FromArgb(0x1A, 0x73, 0xE8);
        private static readonly Color TextColor = Color.FromArgb(0x33, 0x33, 0x33);
        private static readonly Color DimColor = Color.FromArgb(0xB0, 0xB0, 0xB0);

        private readonly List<Item> items = new List<Item>();
        private readonly ToolTip tip = new ToolTip();
        private int selected = -1;
        private int first;          // 맨 왼쪽에 보이는 탭 번호
        private int hoverTab = -1;
        private bool hoverClose;
        private int hoverButton;    // 1 = 왼쪽 화살표, 2 = 오른쪽 화살표, 3 = 새 탭(+)
        private string lastTip;

        public event Action<int> TabSelected;
        public event Action<int> TabCloseRequested;
        public event Action NewTabRequested;

        public DocTabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = Ui.S(32);
            BackColor = Back;
            TabStop = false;
        }

        public int Count { get { return items.Count; } }
        public int SelectedIndex { get { return selected; } }

        public void Add(string text, string toolTip)
        {
            items.Add(new Item { Text = text, Tip = toolTip });
            Invalidate();
        }

        public void RemoveAt(int index)
        {
            items.RemoveAt(index);
            if (selected > index) selected--;
            else if (selected == index) selected = Math.Min(selected, items.Count - 1);
            first = Math.Max(0, Math.Min(first, MaxFirst()));
            hoverTab = -1;
            Invalidate();
        }

        public void SetText(int index, string text, bool dirty)
        {
            Item it = items[index];
            if (it.Text == text && it.Dirty == dirty) return;
            it.Text = text;
            it.Dirty = dirty;
            Invalidate();
        }

        public void Select(int index)
        {
            selected = index;
            EnsureVisible(index);
            Invalidate();
        }

        // ---------- 배치 ----------

        private int TabHeight { get { return Height - Ui.S(4); } }
        private int ArrowW { get { return Ui.S(22); } }
        private int PlusW { get { return Ui.S(28); } }

        private int TabWidth(int index)
        {
            int w = TextRenderer.MeasureText(DisplayText(index), Font).Width + Ui.S(16) + Ui.S(24);
            return Math.Max(Ui.S(90), Math.Min(Ui.S(200), w));
        }

        private string DisplayText(int index)
        {
            return items[index].Text + (items[index].Dirty ? " *" : "");
        }

        private int TotalWidth()
        {
            int t = 0;
            for (int i = 0; i < items.Count; i++) t += TabWidth(i);
            return t;
        }

        private bool Overflow
        {
            get { return TotalWidth() + PlusW + Ui.S(8) > ClientSize.Width; }
        }

        // 탭이 놓이는 영역의 왼쪽·오른쪽 끝
        private int AreaLeft { get { return Overflow ? ArrowW + Ui.S(2) : Ui.S(6); } }
        private int AreaRight { get { return ClientSize.Width - PlusW - (Overflow ? ArrowW + Ui.S(2) : Ui.S(4)); } }

        // 이 번호부터 끝까지 영역 안에 다 들어가는 가장 작은 번호 (이보다 더 밀 수 없다)
        private int MaxFirst()
        {
            if (!Overflow) return 0;
            int avail = AreaRight - AreaLeft;
            int used = 0;
            int idx = items.Count;
            while (idx > 0 && used + TabWidth(idx - 1) <= avail)
            {
                used += TabWidth(idx - 1);
                idx--;
            }
            return Math.Min(idx, Math.Max(0, items.Count - 1));
        }

        private Rectangle TabRect(int index)
        {
            int x = AreaLeft;
            for (int i = first; i < index; i++) x += TabWidth(i);
            return new Rectangle(x, Ui.S(4), TabWidth(index), TabHeight);
        }

        private Rectangle CloseRect(Rectangle tab)
        {
            int s = Ui.S(16);
            return new Rectangle(tab.Right - s - Ui.S(6), tab.Top + (tab.Height - s) / 2, s, s);
        }

        private Rectangle LeftArrowRect() { return new Rectangle(Ui.S(2), Ui.S(4), ArrowW, TabHeight); }
        private Rectangle RightArrowRect() { return new Rectangle(ClientSize.Width - PlusW - ArrowW - Ui.S(2), Ui.S(4), ArrowW, TabHeight); }

        private Rectangle PlusRect()
        {
            int x = Overflow ? ClientSize.Width - PlusW : Math.Min(AreaLeft + TotalWidth() + Ui.S(2), ClientSize.Width - PlusW);
            return new Rectangle(x, Ui.S(4), PlusW, TabHeight);
        }

        private void EnsureVisible(int index)
        {
            if (index < 0 || index >= items.Count) return;
            if (!Overflow)
            {
                first = 0;
                return;
            }
            if (index < first) first = index;
            int avail = AreaRight - AreaLeft;
            while (first < index && TabRect(index).Right > AreaLeft + avail) first++;
            first = Math.Max(0, Math.Min(first, MaxFirst()));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            first = Math.Max(0, Math.Min(first, MaxFirst()));
            if (!Overflow) first = 0;
            Invalidate();
        }

        // ---------- 그리기 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Back);
            if (items.Count == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool overflow = Overflow;
            int left = AreaLeft, right = AreaRight;
            for (int i = first; i < items.Count; i++)
            {
                Rectangle r = TabRect(i);
                if (r.Left >= right) break;
                DrawTab(g, i, r);
            }
            // 영역 밖으로 삐져나온 탭 끝은 배경색으로 덮어 가린다.
            using (var back = new SolidBrush(Back))
            {
                g.FillRectangle(back, right, 0, ClientSize.Width - right, Height);
                if (left > 0) g.FillRectangle(back, 0, 0, left, Height);
            }

            if (overflow)
            {
                DrawArrow(g, LeftArrowRect(), true, first > 0, hoverButton == 1);
                DrawArrow(g, RightArrowRect(), false, first < MaxFirst(), hoverButton == 2);
            }
            DrawPlus(g, PlusRect(), hoverButton == 3);
        }

        private void DrawTab(Graphics g, int i, Rectangle r)
        {
            bool sel = i == selected;
            Color fill = sel ? TabActive : (i == hoverTab ? TabHover : TabBack);
            using (var path = RoundTop(r, Ui.S(6)))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }
            Color fore = sel ? Color.White : TextColor;

            Rectangle close = CloseRect(r);
            var textRect = new Rectangle(r.Left + Ui.S(10), r.Top, close.Left - r.Left - Ui.S(12), r.Height);
            TextRenderer.DrawText(g, DisplayText(i), Font, textRect, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            bool hot = i == hoverTab && hoverClose;
            if (hot)
            {
                using (var b = new SolidBrush(sel ? Color.FromArgb(0x4C, 0x92, 0xF0) : Color.FromArgb(0xB4, 0xC4, 0xDA)))
                    g.FillEllipse(b, close);
            }
            if (sel || i == hoverTab)
            {
                int m = Ui.S(5);
                using (var p = new Pen(fore, Math.Max(1f, Ui.S(1))))
                {
                    g.DrawLine(p, close.Left + m, close.Top + m, close.Right - m, close.Bottom - m);
                    g.DrawLine(p, close.Left + m, close.Bottom - m, close.Right - m, close.Top + m);
                }
            }
        }

        private static GraphicsPath RoundTop(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
            path.AddLine(r.Right - 1, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
        }

        private void DrawArrow(Graphics g, Rectangle r, bool left, bool enabled, bool hot)
        {
            if (hot && enabled)
            {
                using (var b = new SolidBrush(TabHover))
                    g.FillRectangle(b, r);
            }
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2, s = Ui.S(5);
            Point[] tri = left
                ? new[] { new Point(cx + s / 2, cy - s), new Point(cx + s / 2, cy + s), new Point(cx - s / 2, cy) }
                : new[] { new Point(cx - s / 2, cy - s), new Point(cx - s / 2, cy + s), new Point(cx + s / 2, cy) };
            using (var b = new SolidBrush(enabled ? TextColor : DimColor))
                g.FillPolygon(b, tri);
        }

        private void DrawPlus(Graphics g, Rectangle r, bool hot)
        {
            int s = Ui.S(22);
            var circle = new Rectangle(r.Left + (r.Width - s) / 2, r.Top + (r.Height - s) / 2, s, s);
            if (hot)
            {
                using (var b = new SolidBrush(Color.FromArgb(0xCB, 0xCE, 0xD3)))
                    g.FillEllipse(b, circle);
            }
            int cx = circle.Left + s / 2, cy = circle.Top + s / 2, m = Ui.S(5);
            using (var p = new Pen(TextColor, Math.Max(1f, Ui.S(1))))
            {
                g.DrawLine(p, cx - m, cy, cx + m, cy);
                g.DrawLine(p, cx, cy - m, cx, cy + m);
            }
        }

        // ---------- 마우스 ----------

        private int TabAt(Point p)
        {
            if (p.X < AreaLeft || p.X >= AreaRight) return -1;
            for (int i = first; i < items.Count; i++)
            {
                Rectangle r = TabRect(i);
                if (r.Left >= AreaRight) break;
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        private int ButtonAt(Point p)
        {
            if (Overflow)
            {
                if (LeftArrowRect().Contains(p)) return 1;
                if (RightArrowRect().Contains(p)) return 2;
            }
            return PlusRect().Contains(p) ? 3 : 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int tab = TabAt(e.Location);
            bool close = tab >= 0 && CloseRect(TabRect(tab)).Contains(e.Location);
            int button = ButtonAt(e.Location);
            if (tab != hoverTab || close != hoverClose || button != hoverButton)
            {
                hoverTab = tab;
                hoverClose = close;
                hoverButton = button;
                Invalidate();
            }
            string text = tab >= 0 && !close ? items[tab].Tip : null;
            if (text != lastTip)
            {
                lastTip = text;
                if (text == null) tip.Hide(this);
                else tip.Show(text, this, e.X + Ui.S(12), Height);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverTab = -1;
            hoverClose = false;
            hoverButton = 0;
            lastTip = null;
            tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int button = ButtonAt(e.Location);
            if (e.Button == MouseButtons.Left && button != 0)
            {
                if (button == 1) Push(-1);
                else if (button == 2) Push(1);
                else if (NewTabRequested != null) NewTabRequested();
                return;
            }
            int tab = TabAt(e.Location);
            if (tab < 0) return;
            if (e.Button == MouseButtons.Middle)
            {
                if (TabCloseRequested != null) TabCloseRequested(tab);
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (CloseRect(TabRect(tab)).Contains(e.Location))
                {
                    if (TabCloseRequested != null) TabCloseRequested(tab);
                }
                else if (tab != selected && TabSelected != null)
                {
                    TabSelected(tab);
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Push(e.Delta > 0 ? -1 : 1);
        }

        // 선택은 그대로 두고 보이는 탭 목록만 한 칸 민다. 끝에 닿으면 더 밀리지 않는다.
        private void Push(int direction)
        {
            int next = Math.Max(0, Math.Min(MaxFirst(), first + direction));
            if (next == first) return;
            first = next;
            hoverTab = -1;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
