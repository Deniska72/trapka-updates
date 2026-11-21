using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

public class Detection {
    public int ConfirmedAt;
    public RectangleF Box;
    public float Score;
    public float VelocityX, VelocityY;
}

static class BoxMotion {
    public static void Measure(Detection[] current, Detection[] previous, uint intervalMs) {
        if (intervalMs < 12 || intervalMs > 200) return;
        var used = new bool[previous.Length];
        foreach (var item in current) {
            int match = -1; float best = .15f;
            for (int i = 0; i < previous.Length; ++i) {
                if (used[i]) continue;
                var a = item.Box; var b = previous[i].Box; var intersection = RectangleF.Intersect(a, b);
                float area = Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
                float iou = area / Math.Max(.000001f, a.Width * a.Height + b.Width * b.Height - area);
                if (iou > best) { best = iou; match = i; }
            }
            if (match < 0) continue;
            used[match] = true;
            var prior = previous[match].Box;
            float dx = item.Box.X + item.Box.Width / 2 - prior.X - prior.Width / 2;
            float dy = item.Box.Y + item.Box.Height / 2 - prior.Y - prior.Height / 2;
            if (Math.Abs(dx) > .08f || Math.Abs(dy) > .08f) continue;
            item.VelocityX = Math.Max(-1.25f, Math.Min(1.25f, dx * 1000 / intervalMs));
            item.VelocityY = Math.Max(-1.25f, Math.Min(1.25f, dy * 1000 / intervalMs));
        }
    }
    public static Detection[] Predict(Detection[] current, uint ageMs, uint maximumAge) {
        if (ageMs > maximumAge) return new Detection[0];
        float seconds = Math.Min(ageMs, 50) / 1000f;
        var output = new Detection[current.Length];
        for (int i = 0; i < current.Length; ++i) {
            var item = current[i]; var box = item.Box;
            box.X = Math.Max(0, Math.Min(1 - box.Width, box.X + item.VelocityX * seconds));
            box.Y = Math.Max(0, Math.Min(1 - box.Height, box.Y + item.VelocityY * seconds));
            output[i] = new Detection { Box = box, Score = item.Score };
        }
        return output;
    }
}

static class ScaledScreenCapture {
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] static extern bool StretchBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint operation);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
    public static void Capture(Rectangle source, Bitmap destination) {
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try {
            using (var g = Graphics.FromImage(destination)) {
                IntPtr target = g.GetHdc();
                try {
                    SetStretchBltMode(target, 4); SetBrushOrgEx(target, 0, 0, IntPtr.Zero);
                    if (!StretchBlt(target, 0, 0, destination.Width, destination.Height, screen, source.X, source.Y, source.Width, source.Height, 0x00CC0020)) throw new System.ComponentModel.Win32Exception();
                } finally { g.ReleaseHdc(target); }
            }
        } finally { ReleaseDC(IntPtr.Zero, screen); }
    }
}

static class VisionAssets {
    public static string Ensure() {
        string root = Environment.GetEnvironmentVariable("TRAPKA_RUNTIME_DIR");
        if (String.IsNullOrEmpty(root)) root = Path.Combine(AppPaths.DataRoot, "vision-1");
        Directory.CreateDirectory(root);
        foreach (string name in new[] { "onnxruntime.dll", "onnxruntime_providers_shared.dll", "yolox_tiny.onnx", "yolox_nano.onnx", "ORT-LICENSE.txt", "ORT-NOTICES.txt", "YOLOX-LICENSE.txt" }) {
            string path = Path.Combine(root, name);
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trapka." + name)) {
                if (resource == null) throw new FileNotFoundException("Missing embedded component: " + name);
                byte[] expected;
                using (var hash = SHA256.Create()) expected = hash.ComputeHash(resource);
                bool valid = false;
                if (File.Exists(path)) using (var file = File.OpenRead(path)) using (var hash = SHA256.Create()) valid = Convert.ToBase64String(hash.ComputeHash(file)) == Convert.ToBase64String(expected);
                if (!valid) {
                    string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    resource.Position = 0;
                    using (var file = File.Create(temp)) resource.CopyTo(file);
                    try { if (File.Exists(path)) File.Delete(path); File.Move(temp, path); }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
        }
        return root;
    }
}

class PersonDetector : IDisposable {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr ApiBaseFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetApiFn(uint version);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr ErrorFn(IntPtr status);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ReleaseFn(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet=CharSet.Ansi)] delegate IntPtr EnvFn(int level, string id, out IntPtr env);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr OptionsFn(out IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr ThreadsFn(IntPtr options, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet=CharSet.Ansi)] delegate IntPtr ConfigFn(IntPtr options, string key, string value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet=CharSet.Unicode)] delegate IntPtr SessionFn(IntPtr env, string model, IntPtr options, out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr MemoryFn(int allocator, int kind, out IntPtr memory);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr TensorFn(IntPtr memory, IntPtr data, UIntPtr bytes, long[] shape, UIntPtr dimensions, int type, out IntPtr tensor);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr RunFn(IntPtr session, IntPtr options, IntPtr inputNames, IntPtr inputs, UIntPtr inputCount, IntPtr outputNames, UIntPtr outputCount, IntPtr outputs);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr DataFn(IntPtr value, out IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr ShapeFn(IntPtr value, out IntPtr shape);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CountFn(IntPtr shape, out UIntPtr count);
    IntPtr library, api, env, options, session, memory, inputTensor;
    IntPtr inputNames, outputNames, inputs, outputs, inputName, outputName;
    GCHandle pinned;
    readonly float[] input = new float[3 * 416 * 416];
    readonly float[] output = new float[3549 * 85];
    readonly Bitmap resized = new Bitmap(416, 416, PixelFormat.Format24bppRgb);
    readonly byte[] pixels = new byte[416 * 416 * 3];
    ErrorFn errorMessage;
    ReleaseFn releaseStatus, releaseEnv, releaseOptions, releaseSession, releaseMemory, releaseValue, releaseShape;
    RunFn run; DataFn data; ShapeFn shape; CountFn count;
    T Function<T>(int index) where T : class { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(api, index * IntPtr.Size), typeof(T)) as T; }
    void Check(IntPtr status) {
        if (status == IntPtr.Zero) return;
        string message = Marshal.PtrToStringAnsi(errorMessage(status)); releaseStatus(status);
        throw new InvalidOperationException(message);
    }
    public PersonDetector(string root, bool fastMode = true) {
        try {
            library = LoadLibrary(Path.Combine(root, "onnxruntime.dll"));
            if (library == IntPtr.Zero) throw new InvalidOperationException("ONNX Runtime could not load (" + Marshal.GetLastWin32Error() + "). Install Microsoft Visual C++ 2015–2022 x64 Runtime.");
            var getBase = (ApiBaseFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(library, "OrtGetApiBase"), typeof(ApiBaseFn));
            var getApi = (GetApiFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(getBase()), typeof(GetApiFn));
            api = getApi(20);
            if (api == IntPtr.Zero) throw new InvalidOperationException("ORT API 20 unavailable");
            errorMessage = Function<ErrorFn>(2); releaseStatus = Function<ReleaseFn>(93);
            releaseEnv = Function<ReleaseFn>(92); releaseOptions = Function<ReleaseFn>(100); releaseSession = Function<ReleaseFn>(95);
            releaseMemory = Function<ReleaseFn>(94); releaseValue = Function<ReleaseFn>(96); releaseShape = Function<ReleaseFn>(99);
            Check(Function<EnvFn>(3)(3, "Trapka", out env));
            Check(Function<OptionsFn>(10)(out options));
            Check(Function<ThreadsFn>(24)(options, fastMode ? 1 : 2));
            Check(Function<ConfigFn>(130)(options, "session.intra_op.allow_spinning", "0"));
            Check(Function<ConfigFn>(130)(options, "session.inter_op.allow_spinning", "0"));
            Check(Function<SessionFn>(7)(env, Path.Combine(root, fastMode ? "yolox_nano.onnx" : "yolox_tiny.onnx"), options, out session));
            Check(Function<MemoryFn>(69)(1, 0, out memory));
            pinned = GCHandle.Alloc(input, GCHandleType.Pinned);
            Check(Function<TensorFn>(49)(memory, pinned.AddrOfPinnedObject(), new UIntPtr((uint)(input.Length * 4)), new long[] { 1, 3, 416, 416 }, new UIntPtr(4), 1, out inputTensor));
            inputName = Marshal.StringToHGlobalAnsi("images"); outputName = Marshal.StringToHGlobalAnsi("output");
            inputNames = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(inputNames, inputName);
            outputNames = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(outputNames, outputName);
            inputs = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(inputs, inputTensor);
            outputs = Marshal.AllocHGlobal(IntPtr.Size);
            run = Function<RunFn>(9); data = Function<DataFn>(51); shape = Function<ShapeFn>(65); count = Function<CountFn>(64);
        } catch { Dispose(); throw; }
    }
    public Detection[] Detect(Bitmap bitmap, float threshold) {
        float ratio = Math.Min(416f / bitmap.Width, 416f / bitmap.Height);
        int width = Math.Max(1, (int)(bitmap.Width * ratio)), height = Math.Max(1, (int)(bitmap.Height * ratio));
        {
            using (var g = Graphics.FromImage(resized)) {
                g.Clear(Color.FromArgb(114, 114, 114)); g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(bitmap, new Rectangle(0, 0, width, height), new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
            }
            var bits = resized.LockBits(new Rectangle(0, 0, 416, 416), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try {
                Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length);
                const int plane = 416 * 416;
                for (int y = 0; y < 416; ++y) for (int x = 0; x < 416; ++x) {
                    int src = y * bits.Stride + x * 3, dst = y * 416 + x;
                    input[dst] = pixels[src]; input[plane + dst] = pixels[src + 1]; input[plane * 2 + dst] = pixels[src + 2];
                }
            } finally { resized.UnlockBits(bits); }
        }
        Marshal.WriteIntPtr(outputs, IntPtr.Zero);
        IntPtr value = IntPtr.Zero, tensorShape = IntPtr.Zero;
        try {
            Check(run(session, IntPtr.Zero, inputNames, inputs, new UIntPtr(1), outputNames, new UIntPtr(1), outputs));
            value = Marshal.ReadIntPtr(outputs);
            Check(shape(value, out tensorShape)); UIntPtr length; Check(count(tensorShape, out length));
            if (length.ToUInt64() != (ulong)output.Length) throw new InvalidOperationException("Unexpected model output dimensions");
            IntPtr pointer; Check(data(value, out pointer)); Marshal.Copy(pointer, output, 0, output.Length);
        } finally { if (tensorShape != IntPtr.Zero) releaseShape(tensorShape); if (value != IntPtr.Zero) releaseValue(value); }
        var candidates = new List<Detection>(); int row = 0;
        foreach (int stride in new[] { 8, 16, 32 }) {
            int grid = 416 / stride;
            for (int y = 0; y < grid; ++y) for (int x = 0; x < grid; ++x, ++row) {
                int offset = row * 85; float score = output[offset + 4] * output[offset + 5];
                if (score < threshold || Single.IsNaN(score)) continue;
                // Only retain detections whose best class is person (COCO class zero).
                bool person = true; for (int c = 6; c < 85; ++c) if (output[offset + c] > output[offset + 5]) { person = false; break; }
                if (!person) continue;
                float cx = (output[offset] + x) * stride / ratio, cy = (output[offset + 1] + y) * stride / ratio;
                float w = (float)Math.Exp(output[offset + 2]) * stride / ratio, h = (float)Math.Exp(output[offset + 3]) * stride / ratio;
                if (Single.IsInfinity(w) || Single.IsInfinity(h) || Single.IsNaN(w) || Single.IsNaN(h)) continue;
                float left = Math.Max(0, cx - w / 2), top = Math.Max(0, cy - h / 2);
                float right = Math.Min(bitmap.Width, cx + w / 2), bottom = Math.Min(bitmap.Height, cy + h / 2);
                if (right - left < 2 || bottom - top < 4) continue;
                candidates.Add(new Detection { Box = RectangleF.FromLTRB(left / bitmap.Width, top / bitmap.Height, right / bitmap.Width, bottom / bitmap.Height), Score = score });
            }
        }
        candidates.Sort(delegate(Detection a, Detection b) { return b.Score.CompareTo(a.Score); });
        var selected = new List<Detection>();
        foreach (var candidate in candidates) {
            bool overlaps = false;
            foreach (var prior in selected) if (IoU(candidate.Box, prior.Box) > .45f) { overlaps = true; break; }
            if (!overlaps) selected.Add(candidate);
            if (selected.Count >= 32) break;
        }
        return selected.ToArray();
    }
    static float IoU(RectangleF a, RectangleF b) {
        RectangleF intersection = RectangleF.Intersect(a, b);
        float v = Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
        return v / Math.Max(.000001f, a.Width * a.Height + b.Width * b.Height - v);
    }
    public void Dispose() {
        resized.Dispose();
        if (inputTensor != IntPtr.Zero && releaseValue != null) { releaseValue(inputTensor); inputTensor = IntPtr.Zero; }
        if (pinned.IsAllocated) pinned.Free();
        if (session != IntPtr.Zero && releaseSession != null) { releaseSession(session); session = IntPtr.Zero; }
        if (options != IntPtr.Zero && releaseOptions != null) { releaseOptions(options); options = IntPtr.Zero; }
        if (memory != IntPtr.Zero && releaseMemory != null) { releaseMemory(memory); memory = IntPtr.Zero; }
        if (env != IntPtr.Zero && releaseEnv != null) { releaseEnv(env); env = IntPtr.Zero; }
        foreach (IntPtr pointer in new[] { inputNames, outputNames, inputs, outputs, inputName, outputName }) if (pointer != IntPtr.Zero) Marshal.FreeHGlobal(pointer);
        inputNames = outputNames = inputs = outputs = inputName = outputName = IntPtr.Zero;
        if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
    }
}

