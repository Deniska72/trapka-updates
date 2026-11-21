using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Native {
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    public delegate bool EnumWindowProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static IntPtr FindGame() {
        IntPtr result = IntPtr.Zero;
        uint self = (uint)Process.GetCurrentProcess().Id;
        EnumWindows(delegate(IntPtr hwnd, IntPtr ignored) {
            if (!IsWindowVisible(hwnd)) return true;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid == self) return true;
            var name = new StringBuilder(512); GetWindowText(hwnd, name, name.Capacity);
            string title = name.ToString();
            if (title.Equals("ROB RUN", StringComparison.OrdinalIgnoreCase) || title.StartsWith("ROB RUN ", StringComparison.OrdinalIgnoreCase)) {
                result = hwnd; return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}

class DesktopOverlay : Form {
    public Detection[] LiveDetections = new Detection[0];
    readonly Settings config;
    public bool DemoMarkers;
    public string Connection = "Окно ROB RUN не найдено";
    public Action<int> Hotkey;
    public double Time;
    public DesktopOverlay(Settings settings) {
        config = settings; Text = "Trapka Overlay";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(1, 1, 1); TransparencyKey = BackColor;
        Bounds = Screen.PrimaryScreen.Bounds;
        DoubleBuffered = true;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams {
        get { var p = base.CreateParams; p.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x80; return p; }
    }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x0312 && Hotkey != null) Hotkey(m.WParam.ToInt32());
        base.WndProc(ref m);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(TransparencyKey); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        if (!DemoMarkers) {
            if (!config.VisionEnabled) return;
            foreach (var detection in LiveDetections) {
                RectangleF box = detection.Box;
                box = new RectangleF(box.X * Width, box.Y * Height, box.Width * Width, box.Height * Height);
                if (config.Boxes) using (var pen = new Pen(Color.FromArgb(83, 237, 202), 2)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                if (config.Names) using (var font = new Font("Segoe UI", 9)) {
                    string label = "CHARACTER  " + (int)(detection.Score * 100) + "%";
                    SizeF size = g.MeasureString(label, font);
                    float y = Math.Max(0, box.Y - 24);
                    using (var brush = new SolidBrush(Color.FromArgb(16, 24, 32))) g.FillRectangle(brush, box.X, y, size.Width + 8, 22);
                    g.DrawString(label, font, Brushes.White, box.X + 4, y + 2);
                }
            }
            return;
        }
        for (int i = 0; i < 3; i++) {
            int distance = 48 + 50 * i;
            if (!Scene.IsWithinRange(distance, config.Distance)) continue;
            float x = Width * (.35f + i * .18f) + (float)Math.Sin(Time + i) * 18;
            float y = Height * (.4f + i * .035f), height = Height * (.25f - i * .04f), width = height * .45f;
            using (var pen = new Pen(Color.FromArgb(63, 215, 183), 2)) {
                if (config.Boxes) g.DrawRectangle(pen, x, y, width, height);
                float headX = x + width / 2;
                if (config.Snaplines) g.DrawLine(pen, Width / 2f, Height - 1, headX, y + height);
                if (config.HeadCircle) g.DrawEllipse(pen, headX - 9, y + 5, 18, 18);
                if (config.EyeLine) g.DrawLine(pen, headX, y + 14, headX + 40, y + 8);
                if (config.HealthBar) {
                    using (var back = new Pen(Color.FromArgb(41, 48, 59), 3)) g.DrawLine(back, x - 6, y, x - 6, y + height);
                    using (var health = new Pen(Color.FromArgb(111, 204, 143), 3)) g.DrawLine(health, x - 6, y + height * .2f, x - 6, y + height);
                }
                if (config.Skeletons) {
                    float cx = x + width / 2, neck = y + height * .22f, hip = y + height * .6f;
                    g.DrawLine(pen, cx, y + 23, cx, hip);
                    g.DrawLine(pen, cx, neck, x + 4, y + height * .5f);
                    g.DrawLine(pen, cx, neck, x + width - 4, y + height * .5f);
                    g.DrawLine(pen, cx, hip, x + 5, y + height);
                    g.DrawLine(pen, cx, hip, x + width - 5, y + height);
                }
            }
            if (config.Names) using (var font = new Font("Segoe UI", 9)) {
                string text = "TEST " + (i + 1) + " / " + distance + " м";
                SizeF size = g.MeasureString(text, font);
                using (var b = new SolidBrush(Color.FromArgb(17, 25, 35))) g.FillRectangle(b, x, y - 24, size.Width + 8, 21);
                g.DrawString(text, font, Brushes.White, x + 4, y - 23);
            }
        }
        if (config.Radar) {
            int rx = Width - 190, ry = 30;
            using (var b = new SolidBrush(Color.FromArgb(17, 25, 35))) g.FillRectangle(b, rx, ry, 160, 182);
            using (var p = new Pen(Color.FromArgb(63, 215, 183))) {
                g.DrawEllipse(p, rx + 10, ry + 10, 140, 140);
                g.DrawLine(p, rx + 80, ry + 10, rx + 80, ry + 150);
                g.DrawLine(p, rx + 10, ry + 80, rx + 150, ry + 80);
            }
            g.FillEllipse(Brushes.White, rx + 77, ry + 77, 6, 6);
            for (int i = 0; i < 3; ++i) if (Scene.IsWithinRange(48 + 50 * i, config.Distance)) {
                double angle = Time * .15 + i * 2;
                using (var b = new SolidBrush(Color.FromArgb(63, 215, 183))) g.FillEllipse(b, rx + 77 + (float)Math.Cos(angle) * (22 + i * 16), ry + 77 + (float)Math.Sin(angle) * (22 + i * 16), 6, 6);
            }
            using (var font = new Font("Segoe UI", 9)) g.DrawString("ТЕСТОВЫЙ РАДАР", font, Brushes.White, rx + 13, ry + 157);
        }
    }
}

class OverlayContext : ApplicationContext {
    readonly Settings config = Program.Load(Program.ConfigPath);
    readonly DesktopOverlay overlay;
    readonly OverlayMenu menu;
    readonly Timer timer = new Timer();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly VisionWorker vision;
    bool captureExcluded;
    bool menuRequested = false;
    bool layerEnabled = true;
    int ticks;
    bool exiting;
    public OverlayContext(bool enableVision = true) {
        overlay = new DesktopOverlay(config);
        menu = new OverlayMenu(config);
        menu.DemoChanged = delegate(bool v) { overlay.DemoMarkers = v; overlay.Invalidate(); };
        menu.RequestHide = delegate { menuRequested = false; menu.Hide(); };
        menu.RequestExit = delegate { ExitThread(); };
        menu.Changed = delegate { overlay.Invalidate(); };
        menu.FormClosing += delegate { if (!exiting) ExitThread(); };
        overlay.Hotkey = delegate(int id) { if (id == 1) ToggleMenu(); else if (id == 2) { layerEnabled = !layerEnabled; SyncWindow(); } else if (id == 3) ExitThread(); };
        IntPtr overlayHandle = overlay.Handle;
        captureExcluded = Native.SetWindowDisplayAffinity(overlay.Handle, 0x11) && Native.SetWindowDisplayAffinity(menu.Handle, 0x11);
        vision = enableVision ? new VisionWorker() : null;
        bool insert = Native.RegisterHotKey(overlay.Handle, 1, 0x4000, 0x2D);
        bool f2 = Native.RegisterHotKey(overlay.Handle, 2, 0x4000, 0x71);
        bool end = Native.RegisterHotKey(overlay.Handle, 3, 0x4002, 0x23);
        menu.Status.Text = insert && f2 && end ? "Готово. Клики проходят в игру.\nМеню управляется отдельно." : "Часть горячих клавиш уже занята.\nМеню и выход доступны через значок в трее.";
        var context = new ContextMenuStrip();
        context.Items.Add("Меню", null, delegate { ToggleMenu(); });
        context.Items.Add("Включить / выключить слой", null, delegate { layerEnabled = !layerEnabled; SyncWindow(); });
        context.Items.Add("Выход", null, delegate { ExitThread(); });
        tray.Icon = SystemIcons.Application; tray.Text = "Trapka"; tray.ContextMenuStrip = context; tray.Visible = true;
        tray.DoubleClick += delegate { ToggleMenu(); };
        menu.Show();
        SyncWindow(); timer.Interval = 16;
        timer.Tick += delegate {
            if (config.Animate) overlay.Time += .016;
            if (++ticks % 4 == 0) SyncWindow();
            if (vision != null) { overlay.LiveDetections = vision.Snapshot(); if (menu.Visible && ticks % 12 == 0) menu.Status.Text = captureExcluded ? vision.Status : "Screen capture exclusion unavailable. Recognition paused."; }
            if (overlay.Visible) overlay.Invalidate();
        };
        timer.Start();
    }
    void ToggleMenu() {
        IntPtr game = Native.FindGame();
        IntPtr foreground = Native.GetForegroundWindow();
        if (game == IntPtr.Zero || (foreground != game && foreground != menu.Handle)) return;
        menuRequested = !menuRequested; SyncWindow();
        if (menu.Visible) menu.Activate();
    }
    public void VerifyControls() {
        menuRequested = true;
        if (overlay.DemoMarkers) throw new Exception("Demo should be off");
        var area = new Rectangle(100, 120, 1000, 720);
        ApplyGameState(area, true);
        if (!menu.Visible || !area.Contains(menu.Bounds)) throw new Exception("Menu not inside game");
        menuRequested = false; ApplyGameState(area, true);
        if (menu.Visible) throw new Exception("Menu did not hide");
        menuRequested = true; ApplyGameState(area, true);
        if (!menu.Visible) throw new Exception("Menu did not reopen");
        var moved = new Rectangle(280, 160, 1100, 760);
        Point previous = menu.Location;
        ApplyGameState(moved, true);
        if (!moved.Contains(menu.Bounds) || menu.Left - previous.X != moved.Left - area.Left) throw new Exception("Menu did not follow game");
        ApplyGameState(moved, false);
        if (menu.Visible || overlay.Visible) throw new Exception("Windows visible outside game focus");
        ApplyGameState(null, false);
        if (menu.Visible || overlay.Visible) throw new Exception("Windows visible without game");
        ApplyGameState(area, true);
        layerEnabled = false; ApplyGameState(area, true);
        if (overlay.Visible) throw new Exception("Layer did not hide");
        layerEnabled = true;
        var check = menu.DemoToggle;
        check.Checked = true;
        if (!overlay.DemoMarkers) throw new Exception("Demo opt-in failed");
        check.Checked = false;
        if (overlay.DemoMarkers) throw new Exception("Demo opt-out failed");
    }
    public void SaveMenuPreview(string path) {
        timer.Stop(); overlay.Hide();
        menu.Location = new Point(-16000, -16000); menu.Show();
        Application.DoEvents();
        for (int i = 0; i < 4; ++i) { menu.Invalidate(true); menu.Update(); }
        using (var bitmap = new Bitmap(menu.Width, menu.Height)) {
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    public string VisionDiagnostics {
        get { return "Capture exclusion: " + captureExcluded + "; overlay visible: " + overlay.Visible + "; detections: " + overlay.LiveDetections.Length + "; " + (vision == null ? "No worker" : vision.Diagnostics); }
    }
    void SyncWindow() {
        IntPtr game = Native.FindGame();
        if (game == IntPtr.Zero || Native.IsIconic(game)) { ApplyGameState(null, false); return; }
        Native.Rect rect; var origin = new Native.Point();
        bool valid = Native.GetClientRect(game, out rect) && Native.ClientToScreen(game, ref origin) && rect.Right > 0 && rect.Bottom > 0;
        if (!valid) { ApplyGameState(null, false); return; }
        IntPtr foreground = Native.GetForegroundWindow();
        ApplyGameState(new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom), foreground == game || foreground == menu.Handle);
    }
    void ApplyGameState(Rectangle? area, bool foreground) {
        bool visible = area.HasValue && foreground;
        if (area.HasValue) { overlay.Bounds = area.Value; menu.PlaceInGame(area.Value); }
        bool showMenu = visible && menuRequested && area.Value.Width >= menu.Width && area.Value.Height >= menu.Height;
        if (showMenu != menu.Visible) { if (showMenu) menu.Show(); else menu.Hide(); }
        bool showLayer = visible && layerEnabled;
        if (showLayer != overlay.Visible) { if (showLayer) overlay.Show(); else overlay.Hide(); }
        if (vision != null) vision.SetTarget(area ?? Rectangle.Empty, visible && layerEnabled && !menu.Visible && !overlay.DemoMarkers && config.VisionEnabled && captureExcluded, config.Confidence / 100f, config.FastVision);
    }
    protected override void ExitThreadCore() {
        if (exiting) return; exiting = true;
        timer.Stop(); timer.Dispose();
        if (vision != null) vision.Dispose();
        for (int i = 1; i <= 3; ++i) Native.UnregisterHotKey(overlay.Handle, i);
        tray.Visible = false; tray.Dispose(); overlay.Dispose(); menu.Dispose();
        base.ExitThreadCore();
    }
}
