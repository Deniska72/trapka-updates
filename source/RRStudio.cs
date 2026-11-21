using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

[Serializable]
public class Settings {
    public bool FastVision = true;
    public bool VisionEnabled = true;
    public int Confidence = 35;
    public bool Boxes = true, Skeletons = true, Names = true, Radar = true, Animate = true;
    public bool Snaplines = false, HealthBar = true, HeadCircle = true, EyeLine = false;
    public int Distance = 180;
    public void Validate() { Distance = Math.Max(50, Math.Min(300, Distance)); Confidence = Math.Max(15, Math.Min(80, Confidence)); }
}

static class Program {
    public static readonly string ConfigPath = Path.Combine(AppPaths.DataRoot, "settings.xml");
    public static Settings Load(string path) {
        try { using (var s = File.OpenRead(path)) { var c = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(s); c.Validate(); return c; } }
        catch (IOException) { return new Settings(); }
        catch (InvalidOperationException) { return new Settings(); }
        catch (UnauthorizedAccessException) { return new Settings(); }
    }
    public static void Save(Settings c, string path) {
        c.Validate();
        using (var s = File.Create(path)) new XmlSerializer(typeof(Settings)).Serialize(s, c);
    }
    [STAThread]
    static int Main(string[] args) {
        if (args.Length == 2 && args[0] == "--selftest") {
            try {
                var c = new Settings { Boxes = false, Distance = 999 };
                Save(c, args[1]); var restored = Load(args[1]);
                if (restored.Boxes || restored.Distance != 300) throw new Exception("Settings round-trip failed");
                File.WriteAllText(args[1], "invalid XML");
                if (!Load(args[1]).Boxes) throw new Exception("Invalid configuration fallback failed");
                File.Delete(args[1]);
                if (Scene.IsWithinRange(200, 180) || !Scene.IsWithinRange(80, 180)) throw new Exception("Distance filter failed");
                using (var preview = Scene.Render(new Settings(), 980, 660, 1.5)) {
                    if (preview.Width != 980) throw new Exception("Preview failed");
                }
                File.WriteAllText(args[1] + ".result.txt", "PASS: settings round-trip, bounds, malformed XML recovery, distance filtering, scene rendering.");
                return 0;
            } catch (Exception ex) { File.WriteAllText(args[1] + ".result.txt", ex.ToString()); return 1; }
        }
        Native.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--menu-preview") {
            using (var context = new OverlayContext(false)) { context.SaveMenuPreview(args[1]); context.ExitThread(); }
            return 0;
        }
        if (args.Length == 2 && args[0] == "--preview") {
            using (var form = new Studio()) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-16000, -16000); form.Show(); Application.DoEvents();
                using (var bmp = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                    bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Close();
            }
            return 0;
        }
        if (args.Length == 2 && args[0] == "--overlay-selftest") {
            try {
                using (var overlay = new DesktopOverlay(new Settings())) {
                    IntPtr handle = overlay.Handle;
                    int styles = Native.GetWindowLong(handle, -20);
                    if ((styles & 0x80020) != 0x80020 || (styles & 0x08000000) == 0)
                        throw new Exception("Layered, click-through or no-activate style missing");
                    if (!overlay.TopMost || overlay.FormBorderStyle != FormBorderStyle.None || overlay.ShowInTaskbar)
                        throw new Exception("Overlay window properties failed");
                }
                File.WriteAllText(args[1], "PASS: real window handle, layered transparency, click-through, no-activate, topmost, borderless, no taskbar entry.");
                return 0;
            } catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); return 1; }
        }
        if (args.Length == 2 && args[0] == "--overlay-smoketest") {
            using (var context = new OverlayContext(false)) {
                var timer = new Timer { Interval = 100 };
                timer.Tick += delegate {
                    timer.Stop();
                    try { context.VerifyControls(); File.WriteAllText(args[1], "PASS: menu toggles, game-relative layout, following window movement, hiding on focus loss and missing game, layer toggles, demo opt-in/out."); }
                    catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
                    timer.Dispose(); context.ExitThread();
                };
                timer.Start(); Application.Run(context);
            }
            return Environment.ExitCode;
        }
        if (args.Length == 1 && args[0] == "--studio") Application.Run(new Studio());
        else Application.Run(new OverlayContext());
        return 0;
    }
}

class Studio : Form {
    readonly Settings config = Program.Load(Program.ConfigPath);
    readonly Scene scene;
    readonly Panel sidebar = new Panel();
    readonly Label status = new Label();
    static readonly Color Muted = Color.FromArgb(139, 151, 171);
    public Studio() {
        Text = "RR Studio | Demo"; Size = new Size(1180, 760); MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(14, 19, 28); ForeColor = Color.FromArgb(230, 237, 246);
        Font = new Font("Segoe UI", 10); KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        sidebar.Dock = DockStyle.Left; sidebar.Width = 290; sidebar.BackColor = Color.FromArgb(20, 27, 39);
        scene = new Scene(config); scene.Dock = DockStyle.Fill;
        Controls.Add(scene); Controls.Add(sidebar);
        LabelAt("RR / STUDIO", 24, 25, 240, 36, 22, Color.White);
        LabelAt("Личный прототип · v0.1", 26, 66, 240, 25, 10, Muted);
        LabelAt("ВИЗУАЛИЗАЦИЯ", 26, 124, 240, 24, 9, Muted);
        Toggle("Рамки персонажей", 164, config.Boxes, delegate(bool x) { config.Boxes = x; });
        Toggle("Скелеты", 205, config.Skeletons, delegate(bool x) { config.Skeletons = x; });
        Toggle("Имена и дистанция", 246, config.Names, delegate(bool x) { config.Names = x; });
        Toggle("Радар", 287, config.Radar, delegate(bool x) { config.Radar = x; });
        Toggle("Анимация сцены", 328, config.Animate, delegate(bool x) { config.Animate = x; });
        Label value = LabelAt("Дистанция: " + config.Distance + " м", 26, 389, 240, 24, 10, Color.White);
        var range = new TrackBar { Left = 20, Top = 422, Width = 246, Minimum = 50, Maximum = 300, TickFrequency = 50, Value = config.Distance, BackColor = sidebar.BackColor };
        range.ValueChanged += delegate { config.Distance = range.Value; value.Text = "Дистанция: " + config.Distance + " м"; scene.Invalidate(); };
        sidebar.Controls.Add(range);
        var save = new Button { Text = "Сохранить настройки", Left = 26, Top = 487, Width = 238, Height = 43, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(63, 215, 183), ForeColor = Color.FromArgb(13, 28, 29), Cursor = Cursors.Hand };
        save.FlatAppearance.BorderSize = 0;
        save.Click += delegate {
            try { Program.Save(config, Program.ConfigPath); status.Text = "Настройки сохранены"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось сохранить", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        sidebar.Controls.Add(save);
        status.SetBounds(26, 541, 240, 28); status.ForeColor = Muted; status.Text = "Insert — скрыть / показать меню"; status.Font = new Font("Segoe UI", 9); sidebar.Controls.Add(status);
        LabelAt("DEMO / ТЕСТОВЫЕ ДАННЫЕ", 26, 597, 240, 23, 9, Color.FromArgb(63, 215, 183));
        LabelAt("Сцена создана внутри программы.\nПодключения к игре нет.", 26, 625, 240, 48, 9, Muted);
        KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Insert) { sidebar.Visible = !sidebar.Visible; e.Handled = true; } };
    }
    Label LabelAt(string text, int x, int y, int w, int h, int size, Color color) {
        var l = new Label { Text = text, Left = x, Top = y, Width = w, Height = h, ForeColor = color, Font = new Font("Segoe UI", size) };
        sidebar.Controls.Add(l); return l;
    }
    void Toggle(string text, int y, bool initial, Action<bool> change) {
        var c = new CheckBox { Text = text, Checked = initial, Left = 26, Top = y, Width = 240, Height = 30, Cursor = Cursors.Hand };
        c.CheckedChanged += delegate { change(c.Checked); scene.Invalidate(); };
        sidebar.Controls.Add(c);
    }
}

class Scene : Control {
    readonly Settings config;
    readonly Timer timer = new Timer();
    double time;
    static readonly Color Accent = Color.FromArgb(63, 215, 183);
    static readonly Color Muted = Color.FromArgb(139, 151, 171);
    public Scene(Settings c) {
        config = c; DoubleBuffered = true; ResizeRedraw = true;
        timer.Interval = 33;
        timer.Tick += delegate { if (config.Animate) { time += .033; Invalidate(); } };
        timer.Start();
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    public static bool IsWithinRange(int distance, int maximum) { return distance <= maximum; }
    public static Bitmap Render(Settings c, int width, int height, double t) {
        var b = new Bitmap(width, height);
        using (var g = Graphics.FromImage(b)) PaintScene(g, width, height, c, t);
        return b;
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); PaintScene(e.Graphics, Width, Height, config, time); }
    static void DrawLabel(Graphics g, string s, float x, float y, int size, Color color) {
        using (var font = new Font("Segoe UI", size)) using (var b = new SolidBrush(color)) g.DrawString(s, font, b, x, y);
    }
    static void Line(Graphics g, Color color, float width, float x1, float y1, float x2, float y2) {
        using (var p = new Pen(color, width)) g.DrawLine(p, x1, y1, x2, y2);
    }
    static void PaintScene(Graphics g, int w, int h, Settings c, double t) {
        if (w < 1 || h < 1) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(14, 19, 28));
        DrawLabel(g, "Демонстрационная сцена", 30, 24, 20, Color.White);
        DrawLabel(g, "ROB RUN / концепт интерфейса", 32, 62, 10, Muted);
        using (var brush = new SolidBrush(Color.FromArgb(23, 48, 46))) g.FillRectangle(brush, w - 145, 30, 110, 30);
        DrawLabel(g, "DEMO DATA", w - 135, 36, 9, Accent);
        float horizon = h * .48f;
        using (var sky = new LinearGradientBrush(new Rectangle(0, 108, w, Math.Max(1, h - 108)), Color.FromArgb(22, 34, 49), Color.FromArgb(13, 21, 30), 90)) g.FillRectangle(sky, 0, 108, w, h - 108);
        using (var city = new SolidBrush(Color.FromArgb(29, 41, 55))) {
            g.FillRectangle(city, 30, horizon - 130, w * .13f, 130);
            g.FillRectangle(city, w * .23f, horizon - 82, w * .18f, 82);
            g.FillRectangle(city, w * .63f, horizon - 150, w * .14f, 150);
            g.FillRectangle(city, w * .82f, horizon - 110, w * .15f, 110);
        }
        Color grid = Color.FromArgb(37, 53, 66);
        for (int i = -9; i <= 9; ++i) Line(g, grid, 1, w / 2f + i * 24, horizon, w / 2f + i * 135, h - 65);
        for (int i = 0; i < 9; ++i) { float y = horizon + (float)Math.Pow(i / 8.0, 2) * (h - 65 - horizon); Line(g, grid, 1, 0, y, w, y); }
        int[] distances = { 48, 96, 142, 225 };
        int count = 0;
        for (int i = 0; i < 4; i++) {
            if (!IsWithinRange(distances[i], c.Distance)) continue;
            ++count;
            float x = w * (.19f + i * .19f) + (float)Math.Sin(t * .55 + i * 1.7) * w * .035f;
            float floor = h * (.73f - i * .047f);
            float size = h * (.24f - i * .032f);
            Person(g, c, x, floor, size, i, distances[i], t);
        }
        float cx = w / 2f, cy = h * .52f;
        Line(g, Muted, 1, cx - 6, cy, cx + 6, cy); Line(g, Muted, 1, cx, cy - 6, cx, cy + 6);
        if (c.Radar) {
            float rx = w - 122, ry = h - 181, radius = 80;
            using (var brush = new SolidBrush(Color.FromArgb(225, 18, 29, 39))) g.FillEllipse(brush, rx - radius, ry - radius, radius * 2, radius * 2);
            using (var pen = new Pen(Color.FromArgb(57, 80, 92))) { g.DrawEllipse(pen, rx - radius, ry - radius, radius * 2, radius * 2); g.DrawEllipse(pen, rx - 40, ry - 40, 80, 80); }
            Line(g, grid, 1, rx - radius, ry, rx + radius, ry); Line(g, grid, 1, rx, ry - radius, rx, ry + radius);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, rx - 3, ry - 3, 6, 6);
            for (int i = 0; i < 4; ++i) if (IsWithinRange(distances[i], c.Distance)) {
                double angle = t * .12 + i * 1.8; float r = 15 + distances[i] * .25f;
                using (var b = new SolidBrush(Accent)) g.FillEllipse(b, rx + (float)Math.Cos(angle) * r - 3, ry + (float)Math.Sin(angle) * r - 3, 6, 6);
            }
            DrawLabel(g, "РАДАР · 300 м", rx - 48, ry + 89, 9, Muted);
        }
        using (var b = new SolidBrush(Color.FromArgb(18, 25, 36))) g.FillRectangle(b, 0, h - 62, w, 62);
        DrawLabel(g, "ОБЪЕКТЫ  " + count + " / 4", 30, h - 43, 10, Accent);
        DrawLabel(g, c.Animate ? "Сцена: анимация" : "Сцена: пауза", 220, h - 43, 10, Muted);
        DrawLabel(g, "Insert  Меню", w - 118, h - 43, 10, Muted);
    }
    static void Person(Graphics g, Settings c, float x, float floor, float s, int id, int distance, double t) {
        float y = floor - s, spread = s * .19f;
        float gait = c.Animate ? (float)Math.Sin(t * 2 + id) * s * .045f : 0;
        using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0))) g.FillEllipse(shadow, x - spread, floor - 5, spread * 2, 10);
        using (var body = new Pen(Color.FromArgb(72, 93, 111), s * .11f)) {
            body.StartCap = LineCap.Round; body.EndCap = LineCap.Round;
            g.DrawLine(body, x, y + s * .22f, x, y + s * .58f);
            g.DrawLine(body, x, y + s * .58f, x - spread * .6f, floor + gait);
            g.DrawLine(body, x, y + s * .58f, x + spread * .6f, floor - gait);
            g.DrawLine(body, x, y + s * .3f, x - spread, y + s * .55f);
            g.DrawLine(body, x, y + s * .3f, x + spread, y + s * .55f);
        }
        using (var b = new SolidBrush(Color.FromArgb(93, 114, 128))) g.FillEllipse(b, x - s * .065f, y + s * .03f, s * .13f, s * .15f);
        if (c.Skeletons) {
            Line(g, Accent, 1.3f, x, y + s * .16f, x, y + s * .58f);
            Line(g, Accent, 1.3f, x - spread, y + s * .55f, x, y + s * .3f);
            Line(g, Accent, 1.3f, x + spread, y + s * .55f, x, y + s * .3f);
            Line(g, Accent, 1.3f, x, y + s * .58f, x - spread * .6f, floor + gait);
            Line(g, Accent, 1.3f, x, y + s * .58f, x + spread * .6f, floor - gait);
        }
        if (c.Boxes) using (var p = new Pen(Accent, 1.2f)) g.DrawRectangle(p, x - s * .28f, y - 7, s * .56f, s + 15);
        if (c.Names) { DrawLabel(g, "DEMO " + (id + 1).ToString("00"), x - 34, y - 31, 9, Color.White); DrawLabel(g, distance + " м", x - 17, floor + 14, 9, Muted); }
    }
}
