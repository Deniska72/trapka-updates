using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

static class LauncherProgram {
    public const string OverlayMutex = "Local\\Trapka.Overlay.v1";
    public static string ExecutablePath { get { return Assembly.GetExecutingAssembly().Location; } }
    public static Process StartTarget(string arguments) {
        string path = ExecutablePath;
        return Process.Start(new ProcessStartInfo(path, arguments) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = false });
    }
    public static bool OverlayRunning() {
        try { using (var mutex = Mutex.OpenExisting(OverlayMutex)) return true; }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread]
    public static int Main(string[] args) {
        SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--live-check") {
            using (var context = new OverlayContext()) {
                var timer = new System.Windows.Forms.Timer { Interval = 30000 };
                timer.Tick += delegate { timer.Stop(); File.WriteAllText(args[1], context.VisionDiagnostics); timer.Dispose(); context.ExitThread(); };
                timer.Start(); Application.Run(context);
            }
            return 0;
        }
        if (args.Length == 3 && args[0] == "--vision-test") {
            try {
                using (var detector = new PersonDetector(VisionAssets.Ensure())) using (var bitmap = new Bitmap(args[1])) {
                    var watch = Stopwatch.StartNew(); var detections = detector.Detect(bitmap, .35f);
                    using (var g = Graphics.FromImage(bitmap)) using (var pen = new Pen(Color.FromArgb(83, 237, 202), 3)) {
                        foreach (var d in detections) g.DrawRectangle(pen, d.Box.X * bitmap.Width, d.Box.Y * bitmap.Height, d.Box.Width * bitmap.Width, d.Box.Height * bitmap.Height);
                    }
                    bitmap.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
                    File.WriteAllText(args[2] + ".txt", "Detected: " + detections.Length + "; inference ms: " + watch.ElapsedMilliseconds);
                    return detections.Length > 0 ? 0 : 3;
                }
            } catch (Exception ex) { File.WriteAllText(args[2] + ".txt", ex.ToString()); return 1; }
        }
        if (args.Length == 1 && args[0] == "--run-overlay") {
            bool created;
            using (var mutex = new Mutex(true, OverlayMutex, out created)) {
                if (!created) return 0;
                try { Application.Run(new OverlayContext()); }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
        if (args.Length == 2 && args[0] == "--portable-child-test") {
            try {
                using (var context = new OverlayContext(false)) { context.VerifyControls(); context.ExitThread(); }
                File.WriteAllText(args[1], "PASS: standalone child started, embedded overlay created and controls verified."); return 0;
            } catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); return 1; }
        }
        if (args.Length == 2 && args[0] == "--portable-selftest") {
            using (var child = StartTarget("--portable-child-test \"" + args[1] + "\"")) {
                if (!child.WaitForExit(10000)) return 2;
                return child.ExitCode;
            }
        }
        if (args.Length == 2 && args[0] == "--export-icon") {
            using (var icon = Emblem.MakeIcon()) using (var stream = File.Create(args[1])) icon.Save(stream);
            return 0;
        }
        using (var window = new LauncherWindow()) {
            if (args.Length == 2 && args[0] == "--preview") {
                window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-16000, -16000);
                window.Show(); Application.DoEvents();
                using (var b = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(b, new Rectangle(Point.Empty, window.Size)); b.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                return 0;
            }
            Application.Run(window); return 0;
        }
    }
}

static class Emblem {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    public static void Draw(Graphics g, RectangleF rect, Color color, float width) {
        var state = g.Save(); g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 100f, rect.Height / 100f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = new Pen(color, width * 100f / rect.Width)) {
            p.LineJoin = LineJoin.Round;
            g.DrawLines(p, new PointF[] { new PointF(8, 22), new PointF(24, 12), new PointF(50, 33), new PointF(76, 12), new PointF(92, 22), new PointF(81, 61), new PointF(50, 89), new PointF(19, 61), new PointF(8, 22) });
            g.DrawLines(p, new PointF[] { new PointF(19, 35), new PointF(33, 55), new PointF(33, 39), new PointF(50, 61), new PointF(67, 39), new PointF(67, 55), new PointF(81, 35) });
            g.DrawLine(p, 37, 72, 63, 72);
        }
        g.Restore(state);
    }
    public static Icon MakeIcon() {
        using (var bitmap = new Bitmap(64, 64)) {
            using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.FromArgb(14, 18, 30)); Draw(g, new RectangleF(7, 6, 50, 50), Color.FromArgb(106, 197, 211), 2); }
            IntPtr handle = bitmap.GetHicon();
            try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }
}

class LaunchButton : Button {
    bool hover;
    public LaunchButton() {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 12, FontStyle.Bold); Text = "ЗАПУСТИТЬ";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) {
        using (var b = new LinearGradientBrush(ClientRectangle, Enabled ? Color.FromArgb(hover ? 139 : 106, 205, 214) : Color.FromArgb(45, 65, 78), Enabled ? Color.FromArgb(125, 148, 224) : Color.FromArgb(43, 46, 66), 0f)) e.Graphics.FillRectangle(b, ClientRectangle);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? Color.FromArgb(12, 22, 32) : Color.FromArgb(166, 184, 199), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10));
    }
}

class LauncherWindow : Form {
    readonly LaunchButton launch = new LaunchButton();
    readonly Label status = new Label();
    readonly Label updateStatus = new Label();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Icon brandIcon;
    Process started;
    bool running;
    bool error;
    bool checking;
    DateTime nextCheck = DateTime.MinValue;
    PreparedUpdate prepared;
    public LauncherWindow() {
        Text = "Trapka — запуск"; ClientSize = new Size(480, 560); FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(14, 18, 30);
        Font = new Font("Segoe UI", 10); ForeColor = Color.White; DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
        brandIcon = Emblem.MakeIcon(); Icon = brandIcon;
        launch.SetBounds(60, 370, 360, 54); launch.Click += delegate { Launch(); }; Controls.Add(launch); AcceptButton = launch;
        status.SetBounds(48, 443, 384, 44); status.TextAlign = ContentAlignment.TopCenter; status.ForeColor = Color.FromArgb(143, 158, 182); status.BackColor = Color.Transparent;
        status.Font = new Font("Segoe UI", 10); Controls.Add(status);
        updateStatus.SetBounds(30, 491, 420, 23); updateStatus.TextAlign = ContentAlignment.MiddleCenter; updateStatus.Font = new Font("Segoe UI", 8); updateStatus.BackColor = Color.Transparent; updateStatus.ForeColor = Color.FromArgb(106, 197, 211); Controls.Add(updateStatus);
        var updateSettings = new Button { Text = "Настройки обновлений", Left = 140, Top = 520, Width = 200, Height = 25, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(143, 158, 182), BackColor = Color.FromArgb(20, 25, 39), Cursor = Cursors.Hand };
        updateSettings.FlatAppearance.BorderSize = 0; updateSettings.Click += delegate { ConfigureUpdates(); }; Controls.Add(updateSettings);
        RefreshState(); timer.Interval = 1000; timer.Tick += delegate { RefreshState(); if (DateTime.UtcNow >= nextCheck) CheckUpdates(); }; timer.Start();
        Shown += delegate { CheckUpdates(); };
    }
    bool IsRunning() {
        if (started != null) { try { if (!started.HasExited) return true; } catch (InvalidOperationException) { } started.Dispose(); started = null; }
        return LauncherProgram.OverlayRunning();
    }
    void RefreshState() {
        bool previous = running; running = IsRunning();
        launch.Enabled = !running;
        launch.Text = running ? "ПРИЛОЖЕНИЕ ЗАПУЩЕНО" : "ЗАПУСТИТЬ";
        if (running) { status.Text = "Trapka запущена.\nОткрой ROB RUN. Меню — Insert."; error = false; }
        else if (previous || !error) status.Text = "Всё готово к запуску";
        if (!running && prepared != null) ApplyPrepared();
    }
    void ConfigureUpdates() {
        using (var dialog = new Form { Text = "Trapka — обновления", ClientSize = new Size(500, 210), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = BackColor, ForeColor = Color.White, Font = Font }) {
            var label = new Label { Text = "HTTPS-ссылка на подписанный манифест обновлений:\nПока сервер не опубликован, оставь поле пустым.", Left = 20, Top = 20, Width = 460, Height = 45 };
            var input = new TextBox { Left = 20, Top = 77, Width = 460, Text = Updates.Source };
            var save = new Button { Text = "Сохранить и проверить", Left = 20, Top = 134, Width = 235, Height = 36 };
            var cancel = new Button { Text = "Отмена", Left = 275, Top = 134, Width = 205, Height = 36, DialogResult = DialogResult.Cancel };
            save.Click += delegate {
                try { Updates.Source = input.Text; prepared = null; nextCheck = DateTime.MinValue; dialog.DialogResult = DialogResult.OK; dialog.Close(); }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Обновления"); }
            };
            dialog.Controls.AddRange(new Control[] { label, input, save, cancel }); dialog.AcceptButton = save; dialog.CancelButton = cancel;
            if (dialog.ShowDialog(this) == DialogResult.OK) CheckUpdates();
        }
    }
    void CheckUpdates() {
        if (checking) return;
        nextCheck = DateTime.UtcNow.AddSeconds(60);
        string source;
        try { source = Updates.Source; }
        catch (Exception) { updateStatus.Text = "Не удалось прочитать настройки обновлений"; return; }
        if (String.IsNullOrEmpty(source)) { updateStatus.Text = "Автообновление: сервер ещё не настроен"; return; }
        checking = true; updateStatus.Text = "Проверка обновлений…";
        ThreadPool.QueueUserWorkItem(delegate {
            PreparedUpdate update = null; string failure = null;
            try { update = Updates.Check(source); }
            catch (Exception) { failure = "Обновление недоступно. Текущая версия сохранена."; }
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate {
                checking = false;
                if (IsDisposed) return;
                try { if (Updates.Source != source) return; }
                catch (IOException) { updateStatus.Text = "Не удалось прочитать настройки обновлений"; return; }
                if (failure != null) { updateStatus.Text = failure; return; }
                prepared = update;
                if (prepared == null) { updateStatus.Text = "Установлена актуальная версия " + Updates.CurrentVersion; return; }
                updateStatus.Text = "Версия " + prepared.Version + " готова. Применится после выхода из Trapka.";
                if (!IsRunning()) ApplyPrepared();
            }); } catch (InvalidOperationException) { }
        });
    }
    void ApplyPrepared() {
        var update = prepared; if (update == null || IsRunning()) return;
        prepared = null;
        try { Updates.Restart(update); Close(); }
        catch (Exception) { updateStatus.Text = "Не удалось открыть новую версию. Перезапусти лаунчер."; }
    }
    void Launch() {
        RefreshState(); if (running || !launch.Enabled) return;
        try { started = LauncherProgram.StartTarget("--run-overlay"); error = false; RefreshState(); }
        catch (Exception ex) { error = true; status.Text = "Не удалось запустить приложение."; MessageBox.Show(this, ex.Message, "Trapka", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 23, 35), Color.FromArgb(25, 16, 38), 45)) e.Graphics.FillRectangle(b, ClientRectangle);
        using (var p = new Pen(Color.FromArgb(17, 111, 151, 198))) for (int x = -Height; x < Width; x += 38) e.Graphics.DrawLine(p, x, Height, x + Height, 0);
        Emblem.Draw(e.Graphics, new RectangleF(250, 285, 295, 295), Color.FromArgb(17, 142, 129, 220), 2);
    }
    void CenterText(Graphics g, string text, float y, int size, FontStyle style, Color color) {
        using (var font = new Font("Segoe UI", size, style)) using (var brush = new SolidBrush(color)) using (var format = new StringFormat { Alignment = StringAlignment.Center }) g.DrawString(text, font, brush, new RectangleF(0, y, Width, 60), format);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        CenterText(g, "TRAPKA  /  v" + Updates.CurrentVersion, 27, 9, FontStyle.Regular, Color.FromArgb(137, 156, 181));
        using (var p = new Pen(Color.FromArgb(32, 91, 126, 157))) g.DrawEllipse(p, 152, 95, 176, 176);
        Emblem.Draw(g, new RectangleF(182, 122, 116, 116), Color.FromArgb(106, 197, 211), 2.5f);
        CenterText(g, "TRAPKA", 277, 30, FontStyle.Bold, Color.FromArgb(236, 243, 251));
        CenterText(g, "ПАНЕЛЬ ЗАПУСКА", 332, 9, FontStyle.Regular, Color.FromArgb(137, 156, 181));
    }
    protected override void Dispose(bool disposing) {
        if (disposing) { timer.Stop(); timer.Dispose(); if (started != null) started.Dispose(); brandIcon.Dispose(); }
        base.Dispose(disposing);
    }
}
