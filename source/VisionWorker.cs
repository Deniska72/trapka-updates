using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

class VisionWorker : IDisposable {
    class Frame : IDisposable {
        public Bitmap Image;
        public byte[] Gray;
        public int References, Tick, Generation;
        public long Sequence;
        public Frame(int width, int height) { Image = new Bitmap(width, height, PixelFormat.Format24bppRgb); Gray = new byte[width * height]; }
        public void Dispose() { Image.Dispose(); }
    }
    class Recognition { public Frame Source; public Detection[] Boxes; }
    readonly object gate = new object();
    readonly Frame[] pool = new Frame[6];
    readonly AutoResetEvent available = new AutoResetEvent(false);
    readonly Thread captureThread, inferenceThread;
    readonly Action<Rectangle, Bitmap> captureOverride;
    readonly Func<Bitmap, float, Detection[]> detectionOverride;
    volatile bool stopped;
    Rectangle area;
    bool enabled, fastMode = true;
    int generation;
    float confidence = .35f;
    Frame latestFrame;
    Recognition pending;
    Detection[] latest = new Detection[0];
    int latestAt, captureCount, detectionCount, maxFound;
    long sequence, totalCaptureMs, totalInferenceMs;
    string status = "Starting frame tracker…";
    public VisionWorker(Action<Rectangle, Bitmap> capture = null, Func<Bitmap, float, Detection[]> detect = null) {
        captureOverride = capture; detectionOverride = detect;
        captureThread = new Thread(Capture) { IsBackground = true, Priority = ThreadPriority.Normal, Name = "Trapka frame tracker" };
        inferenceThread = new Thread(Infer) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Trapka recognition" };
        captureThread.Start(); inferenceThread.Start();
    }
    void Release(Frame frame) { if (frame != null) { if (frame.References <= 0) throw new InvalidOperationException("Frame reference underflow"); frame.References--; } }
    Frame Rent(int width, int height) {
        for (int i = 0; i < pool.Length; ++i) {
            if (pool[i] != null && pool[i].References != 0) continue;
            if (pool[i] != null && (pool[i].Image.Width != width || pool[i].Image.Height != height)) { pool[i].Dispose(); pool[i] = null; }
            if (pool[i] == null) pool[i] = new Frame(width, height);
            pool[i].References = 1; return pool[i];
        }
        return null;
    }
    public void SetTarget(Rectangle target, bool active, float threshold, bool fast = true) {
        lock (gate) {
            if (area != target || enabled != active || fastMode != fast) {
                generation++; latest = new Detection[0]; latestAt = 0;
                Release(latestFrame); latestFrame = null;
                if (pending != null) { Release(pending.Source); pending = null; }
            }
            area = target; enabled = active; confidence = threshold; fastMode = fast;
        }
    }
    public Detection[] Snapshot() {
        lock (gate) return unchecked((uint)(Environment.TickCount - latestAt)) > 90 ? new Detection[0] : latest;
    }
    public string Status { get { lock (gate) return status; } }
    public int CapturedFrames { get { lock (gate) return captureCount; } }
    public int RecognizedFrames { get { lock (gate) return detectionCount; } }
    public string Diagnostics {
        get { lock (gate) return status + "; capture/track frames: " + captureCount + "; mean capture/track ms: " + (captureCount == 0 ? 0 : totalCaptureMs / captureCount) + "; recognition frames: " + detectionCount + "; mean inference ms: " + (detectionCount == 0 ? 0 : totalInferenceMs / detectionCount) + "; max detections: " + maxFound; }
    }
    static void Gray(Frame frame, ref byte[] scratch) {
        var image = frame.Image;
        var bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try {
            int count = bits.Stride * image.Height;
            if (scratch == null || scratch.Length != count) scratch = new byte[count];
            Marshal.Copy(bits.Scan0, scratch, 0, count);
            for (int y = 0; y < image.Height; ++y) for (int x = 0; x < image.Width; ++x) {
                int i = y * bits.Stride + x * 3;
                frame.Gray[y * image.Width + x] = (byte)((scratch[i] * 29 + scratch[i + 1] * 150 + scratch[i + 2] * 77) >> 8);
            }
        } finally { image.UnlockBits(bits); }
    }
    void Capture() {
        Frame previous = null; byte[] scratch = null; int lastGeneration = -1;
        Detection[] tracks = new Detection[0];
        try {
            while (!stopped) {
                Rectangle target; bool active; int frameGeneration;
                lock (gate) {
                    target = area; active = enabled; frameGeneration = generation;
                    if (lastGeneration != frameGeneration) { Release(previous); previous = null; tracks = new Detection[0]; lastGeneration = frameGeneration; }
                }
                if (!active || target.Width < 32 || target.Height < 32) { Thread.Sleep(25); continue; }
                var watch = Stopwatch.StartNew();
                float scale = Math.Min(416f / target.Width, 416f / target.Height);
                int width = Math.Max(1, (int)(target.Width * scale)), height = Math.Max(1, (int)(target.Height * scale));
                Frame frame;
                lock (gate) frame = Rent(width, height);
                if (frame == null) { Thread.Sleep(2); continue; }
                Recognition recognition = null;
                try {
                    frame.Tick = Environment.TickCount; frame.Generation = frameGeneration; frame.Sequence = ++sequence;
                    try { if (captureOverride == null) ScaledScreenCapture.Capture(target, frame.Image); else captureOverride(target, frame.Image); Gray(frame, ref scratch); }
                    catch (System.ComponentModel.Win32Exception) { lock (gate) { latest = new Detection[0]; status = "Capture paused"; } Thread.Sleep(30); continue; }
                    if (previous != null && previous.Generation == frameGeneration && previous.Image.Width == width && previous.Image.Height == height)
                        tracks = PixelTracker.Advance(previous.Gray, frame.Gray, width, height, tracks, unchecked((uint)(frame.Tick - previous.Tick)));
                    else tracks = new Detection[0];
                    lock (gate) { recognition = pending; pending = null; }
                    if (recognition != null && recognition.Source.Generation == frameGeneration && recognition.Source.Image.Width == width && recognition.Source.Image.Height == height && unchecked((uint)(frame.Tick - recognition.Source.Tick)) <= 150) {
                        var projected = PixelTracker.Advance(recognition.Source.Gray, frame.Gray, width, height, recognition.Boxes, unchecked((uint)(frame.Tick - recognition.Source.Tick)));
                        tracks = PixelTracker.Reconcile(tracks, projected, recognition.Source.Tick, frame.Tick);
                    }
                    tracks = PixelTracker.Expire(tracks, frame.Tick);
                    lock (gate) {
                        if (enabled && generation == frameGeneration) {
                            frame.References++; Release(latestFrame); latestFrame = frame;
                            latest = tracks; latestAt = frame.Tick;
                            captureCount++; totalCaptureMs += watch.ElapsedMilliseconds; maxFound = Math.Max(maxFound, tracks.Length);
                            status = "Tracking " + tracks.Length + " / " + watch.ElapsedMilliseconds + " ms; recognition async";
                            available.Set();
                        }
                        Release(previous); previous = frame; frame = null;
                    }
                } finally {
                    lock (gate) { if (recognition != null) Release(recognition.Source); Release(frame); }
                }
                int delay = Math.Max(1, 16 - (int)watch.ElapsedMilliseconds); Thread.Sleep(delay);
            }
        } catch (Exception ex) { lock (gate) { latest = new Detection[0]; status = "Tracking error: " + ex.Message; } stopped = true; available.Set(); }
        finally { lock (gate) Release(previous); }
    }
    void Infer() {
        PersonDetector detector = null; bool detectorFast = true; long detectedSequence = -1;
        try {
            string root = detectionOverride == null ? VisionAssets.Ensure() : null;
            while (!stopped) {
                available.WaitOne(50);
                Frame frame = null; bool fast; float threshold;
                lock (gate) {
                    fast = fastMode; threshold = confidence;
                    if (enabled && latestFrame != null && latestFrame.Sequence != detectedSequence) { frame = latestFrame; frame.References++; }
                }
                if (frame == null) continue;
                var watch = Stopwatch.StartNew();
                try {
                    if (detectionOverride == null && (detector == null || detectorFast != fast)) {
                        if (detector != null) detector.Dispose(); detector = null;
                        detector = new PersonDetector(root, fast); detectorFast = fast;
                    }
                    var result = detectionOverride == null ? detector.Detect(frame.Image, threshold) : detectionOverride(frame.Image, threshold);
                    if (result.Length > 12) { var strongest = new Detection[12]; Array.Copy(result, strongest, 12); result = strongest; }
                    lock (gate) {
                        detectedSequence = frame.Sequence;
                        if (enabled && generation == frame.Generation) {
                            if (pending != null) Release(pending.Source);
                            pending = new Recognition { Source = frame, Boxes = result }; frame = null;
                            detectionCount++; totalInferenceMs += watch.ElapsedMilliseconds;
                        }
                    }
                } finally { lock (gate) Release(frame); }
                int delay = Math.Max(1, (fast ? 80 : 120) - (int)watch.ElapsedMilliseconds); Thread.Sleep(delay);
            }
        } catch (Exception ex) { lock (gate) { latest = new Detection[0]; status = "Recognition error: " + ex.Message; } stopped = true; }
        finally { if (detector != null) detector.Dispose(); }
    }
    public void Dispose() {
        stopped = true; available.Set();
        bool captured = captureThread.Join(1500), inferred = inferenceThread.Join(2000);
        if (captured && inferred) {
            lock (gate) {
                Release(latestFrame); latestFrame = null;
                if (pending != null) { Release(pending.Source); pending = null; }
                foreach (var frame in pool) if (frame != null) frame.Dispose();
            }
            available.Dispose();
        }
    }
}
