using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class DarkCheck : CheckBox {
    public DarkCheck() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        AutoSize = false;
        Height = 27; Width = 210; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 9);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color accent = Color.FromArgb(92, 176, 197);
        using (var b = new SolidBrush(Checked ? accent : Color.FromArgb(26, 32, 42))) g.FillRectangle(b, 0, 6, 14, 14);
        using (var p = new Pen(Checked ? accent : Color.FromArgb(45, 53, 65))) g.DrawRectangle(p, 0, 6, 14, 14);
        if (Checked) using (var p = new Pen(Color.FromArgb(222, 245, 249), 1.5f)) { g.DrawLine(p, 3, 13, 6, 16); g.DrawLine(p, 6, 16, 11, 9); }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(23, 0, Width - 23, Height), Enabled ? Color.FromArgb(218, 223, 233) : Color.FromArgb(91, 101, 118), BackColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(20, 3, Width - 22, Height - 4));
    }
}

static class TrapBrand {
    public static void Draw(Graphics g, RectangleF rect, Color color, float width) {
        var state = g.Save();
        g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 100f, rect.Height / 100f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = new Pen(color, width * 100f / rect.Width)) {
            p.LineJoin = LineJoin.Round;
            g.DrawLines(p, new PointF[] { new PointF(8, 22), new PointF(24, 12), new PointF(50, 33), new PointF(76, 12), new PointF(92, 22), new PointF(81, 61), new PointF(50, 89), new PointF(19, 61), new PointF(8, 22) });
            g.DrawLines(p, new PointF[] { new PointF(19, 35), new PointF(33, 55), new PointF(33, 39), new PointF(50, 61), new PointF(67, 39), new PointF(67, 55), new PointF(81, 35) });
            g.DrawLine(p, 37, 72, 63, 72);
        }
        g.Restore(state);
    }
}

class BrandPanel : Panel {
    public bool Emblem;
    public bool Header;
    public BrandPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e) {
        if (Width < 1 || Height < 1) return;
        using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(16, 21, 34), Color.FromArgb(19, 16, 33), 35)) e.Graphics.FillRectangle(b, ClientRectangle);
        using (var p = new Pen(Color.FromArgb(14, 106, 143, 191)))
            for (int x = -Height; x < Width; x += 34) e.Graphics.DrawLine(p, x, Height, x + Height, 0);
        if (Emblem) TrapBrand.Draw(e.Graphics, new RectangleF(Width - 205, Height - 230, 225, 225), Color.FromArgb(24, 119, 123, 218), 2);
        if (Header) TrapBrand.Draw(e.Graphics, new RectangleF(12, 10, 28, 28), Color.FromArgb(106, 197, 211), 1.5f);
        using (var p = new Pen(Color.FromArgb(27, 32, 48))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

class OverlayMenu : Form {
    readonly Settings config;
    readonly Panel content = new Panel();
    readonly Label title = new Label();
    readonly ToolTip tips = new ToolTip();
    readonly Button[] tabs = new Button[3];
    public readonly Label Status = new Label();
    public readonly DarkCheck DemoToggle;
    public Action<bool> DemoChanged;
    public Action RequestHide;
    public Action RequestExit;
    public Action Changed;
    static readonly Color Surface = Color.FromArgb(15, 18, 26);
    static readonly Color Edge = Color.FromArgb(23, 27, 36);
    public OverlayMenu(Settings settings) {
        config = settings; ShowInTaskbar = false; Text = "Trapka"; ClientSize = new Size(640, 470);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 60, Screen.PrimaryScreen.WorkingArea.Top + 100);
        TopMost = true; BackColor = Color.FromArgb(8, 11, 17); ForeColor = Color.FromArgb(218, 223, 233);
        Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.None;
        var nav = new BrandPanel { Bounds = new Rectangle(12, 12, 60, 446), BackColor = Surface }; Controls.Add(nav);
        nav.Paint += delegate(object sender, PaintEventArgs e) { TrapBrand.Draw(e.Graphics, new RectangleF(14, 16, 32, 32), Color.FromArgb(106, 197, 211), 1.5f); };
        string[] names = { "AIM", "ESP", "MISC" };
        for (int i = 0; i < names.Length; ++i) {
            int index = i;
            tabs[i] = FlatButton(names[i], new Rectangle(0, 75 + i * 48, 60, 38));
            tabs[i].Click += delegate { SelectTab(index); }; nav.Controls.Add(tabs[i]);
        }
        var header = new BrandPanel { Bounds = new Rectangle(82, 12, 546, 44), BackColor = Surface, Header = true }; Controls.Add(header);
        title.SetBounds(50, 10, 450, 28); title.Font = new Font("Segoe UI", 13, FontStyle.Bold); title.BackColor = Color.Transparent; header.Controls.Add(title);
        var close = FlatButton("×", new Rectangle(510, 6, 30, 30)); close.Click += delegate { if (RequestHide != null) RequestHide(); }; header.Controls.Add(close);
        Point dragOrigin = Point.Empty; bool dragging = false;
        MouseEventHandler down = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOrigin = Cursor.Position; } };
        MouseEventHandler move = delegate { if (dragging) { Point now = Cursor.Position; Location = ClampLocation(new Point(Left + now.X - dragOrigin.X, Top + now.Y - dragOrigin.Y)); dragOrigin = now; } };
        MouseEventHandler up = delegate { dragging = false; };
        header.MouseDown += down; header.MouseMove += move; header.MouseUp += up;
        title.MouseDown += down; title.MouseMove += move; title.MouseUp += up;
        content.SetBounds(82, 66, 546, 392); Controls.Add(content);

        var visuals = Page();
        var first = Card(visuals, "ESP 1", 0, 0, 267, 392);
        var second = Card(visuals, "ESP 2", 279, 0, 267, 392);
        AddCheck(first, "2D Box", 44, config.Boxes, delegate(bool v) { config.Boxes = v; });
        AddCheck(first, "Screen recognition", 77, config.VisionEnabled, delegate(bool v) { config.VisionEnabled = v; });
        AddCheck(first, "Confidence label", 110, config.Names, delegate(bool v) { config.Names = v; });
        AddCheck(first, "Fast mode", 143, config.FastVision, delegate(bool v) { config.FastVision = v; });
        AddCheck(first, "Skeleton (demo only)", 176, false, delegate(bool v) { }).Enabled = false;
        AddCheck(first, "Head (demo only)", 209, false, delegate(bool v) { }).Enabled = false;
        AddCheck(first, "Eye line (demo only)", 242, false, delegate(bool v) { }).Enabled = false;
        AddCheck(second, "Radar (demo only)", 44, false, delegate(bool v) { }).Enabled = false;
        var team = AddCheck(second, "Team Check", 77, false, delegate { }); team.Enabled = false;
        var visible = AddCheck(second, "Visible on screen only", 110, true, delegate { }); visible.Enabled = false;
        tips.SetToolTip(team, "Нет данных о командах игроков"); tips.SetToolTip(visible, "Нет данных о видимости игроков");
        var distanceLabel = LabelAt(second, "Confidence: " + config.Confidence + "%", 18, 185, 235, 24);
        var range = new TrackBar { Bounds = new Rectangle(12, 217, 239, 45), Minimum = 15, Maximum = 80, TickFrequency = 10, Value = config.Confidence, BackColor = Surface };
        range.ValueChanged += delegate { config.Confidence = range.Value; distanceLabel.Text = "Confidence: " + config.Confidence + "%"; NotifyChanged(); }; second.Controls.Add(range);
        LabelAt(second, "SCREEN RECOGNITION", 18, 340, 230, 24).ForeColor = Color.FromArgb(92, 176, 197);

        var aim = Page(); var aimCard = Card(aim, "AIM", 0, 0, 546, 392);
        LabelAt(aimCard, "Aim assistance is not connected.", 18, 50, 500, 30).ForeColor = Color.FromArgb(124, 134, 151);

        var misc = Page(); var miscCard = Card(misc, "SETTINGS", 0, 0, 546, 392);
        DemoToggle = AddCheck(miscCard, "Demo markers (TEST)", 44, false, delegate(bool v) { if (DemoChanged != null) DemoChanged(v); });
        AddCheck(miscCard, "Animate preview", 83, config.Animate, delegate(bool v) { config.Animate = v; });
        var save = FlatButton("Save settings", new Rectangle(18, 141, 220, 36)); save.BackColor = Color.FromArgb(32, 61, 72);
        save.Click += delegate { try { Program.Save(config, Program.ConfigPath); Status.Text = "Settings saved"; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save settings"); } }; miscCard.Controls.Add(save);
        var exit = FlatButton("Exit application", new Rectangle(252, 141, 220, 36)); exit.Click += delegate { if (RequestExit != null) RequestExit(); }; miscCard.Controls.Add(exit);
        Status.SetBounds(18, 205, 500, 48); Status.ForeColor = Color.FromArgb(124, 134, 151); miscCard.Controls.Add(Status);
        LabelAt(miscCard, "Insert  Menu    /    F2  Overlay    /    Ctrl+End  Exit", 18, 286, 510, 30);
        LabelAt(miscCard, "Screen detection. Includes NPCs / own character.", 18, 328, 510, 28).ForeColor = Color.FromArgb(124, 134, 151);
        visuals.Tag = 1; aim.Tag = 0; misc.Tag = 2;
        SelectTab(1);
    }
    void NotifyChanged() { if (Changed != null) Changed(); }
    Panel Page() { var p = new Panel { Dock = DockStyle.Fill, BackColor = BackColor }; content.Controls.Add(p); return p; }
    Panel Card(Panel parent, string text, int x, int y, int w, int h) {
        var p = new BrandPanel { Bounds = new Rectangle(x, y, w, h), BackColor = Surface, Emblem = true }; parent.Controls.Add(p);
        var l = LabelAt(p, text, 14, 12, w - 28, 23); l.Font = new Font("Segoe UI", 9, FontStyle.Bold); return p;
    }
    static Label LabelAt(Control parent, string text, int x, int y, int w, int h) {
        var l = new Label { Text = text, Bounds = new Rectangle(x, y, w, h), ForeColor = Color.FromArgb(218, 223, 233), BackColor = Color.Transparent }; parent.Controls.Add(l); return l;
    }
    static Button FlatButton(string text, Rectangle bounds) {
        var b = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Color.FromArgb(124, 134, 151), Cursor = Cursors.Hand, TabStop = true };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 38, 50); return b;
    }
    DarkCheck AddCheck(Control parent, string text, int y, bool value, Action<bool> changed) {
        var c = new DarkCheck { Left = 18, Top = y, Checked = value, Text = text, BackColor = Surface };
        c.CheckedChanged += delegate { changed(c.Checked); NotifyChanged(); }; parent.Controls.Add(c); return c;
    }
    void SelectTab(int index) {
        title.Text = index == 1 ? "Trapka / Visuals" : index == 0 ? "Trapka / Aim" : "Trapka / Settings";
        for (int i = 0; i < tabs.Length; ++i) { tabs[i].ForeColor = i == index ? Color.FromArgb(92, 176, 197) : Color.FromArgb(124, 134, 151); tabs[i].BackColor = i == index ? Color.FromArgb(23, 32, 43) : Surface; }
        foreach (Control p in content.Controls) p.Visible = p.Tag != null && (int)p.Tag == index;
    }
    public Rectangle GameArea;
    public void PlaceInGame(Rectangle area) {
        if (GameArea != area) {
            bool first = GameArea.IsEmpty;
            Point relative = first ? new Point((area.Width - Width) / 2, (area.Height - Height) / 2) : new Point(Left - GameArea.Left, Top - GameArea.Top);
            GameArea = area;
            Location = ClampLocation(new Point(area.Left + relative.X, area.Top + relative.Y));
        }
    }
    Point ClampLocation(Point location) {
        if (GameArea.IsEmpty) return location;
        return new Point(Math.Max(GameArea.Left, Math.Min(GameArea.Right - Width, location.X)), Math.Max(GameArea.Top, Math.Min(GameArea.Bottom - Height, location.Y)));
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(Edge)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}
