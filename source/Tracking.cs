using System;
using System.Collections.Generic;
using System.Drawing;

// Patch tracking runs on fresh screen frames, independently of neural inference.
static class PixelTracker {
    const int Radius = 2;
    static int Variance(byte[] image, int w, int x, int y) {
        int sum = 0, squares = 0;
        for (int py = -Radius; py <= Radius; ++py) for (int px = -Radius; px <= Radius; ++px) {
            int value = image[(y + py) * w + x + px]; sum += value; squares += value * value;
        }
        return squares - sum * sum / 25;
    }
    static int Cost(byte[] oldImage, byte[] image, int w, int x, int y, int dx, int dy, int sourceMean) {
        int sum = 0;
        for (int py = -Radius; py <= Radius; py += 2) for (int px = -Radius; px <= Radius; px += 2) sum += image[(y + dy + py) * w + x + dx + px];
        int mean = sum / 9, error = 0;
        for (int py = -Radius; py <= Radius; py += 2) for (int px = -Radius; px <= Radius; px += 2)
            error += Math.Abs((oldImage[(y + py) * w + x + px] - sourceMean) - (image[(y + dy + py) * w + x + dx + px] - mean));
        return error;
    }
    static int FullCost(byte[] a, byte[] b, int w, int x, int y, int dx, int dy) {
        int ma = 0, mb = 0, error = 0;
        for (int py = -Radius; py <= Radius; ++py) for (int px = -Radius; px <= Radius; ++px) { ma += a[(y + py) * w + x + px]; mb += b[(y + dy + py) * w + x + dx + px]; }
        ma /= 25; mb /= 25;
        for (int py = -Radius; py <= Radius; ++py) for (int px = -Radius; px <= Radius; ++px) error += Math.Abs((a[(y + py) * w + x + px] - ma) - (b[(y + dy + py) * w + x + dx + px] - mb));
        return error;
    }
    static bool Match(byte[] previous, byte[] current, int w, int h, int x, int y, int range, out float dx, out float dy) {
        dx = dy = 0;
        if (x < Radius + 1 || x >= w - Radius - 1 || y < Radius + 1 || y >= h - Radius - 1 || Variance(previous, w, x, y) < 750) return false;
        int sum = 0;
        for (int py = -Radius; py <= Radius; py += 2) for (int px = -Radius; px <= Radius; px += 2) sum += previous[(y + py) * w + x + px];
        int mean = sum / 9, best = Int32.MaxValue, second = Int32.MaxValue, bx = 0, by = 0;
        int minX = Math.Max(-range, Radius + 1 - x), maxX = Math.Min(range, w - Radius - 2 - x);
        int minY = Math.Max(-range, Radius + 1 - y), maxY = Math.Min(range, h - Radius - 2 - y);
        for (int yy = minY; yy <= maxY; ++yy) for (int xx = minX; xx <= maxX; ++xx) {
            int cost = Cost(previous, current, w, x, y, xx, yy, mean);
            if (cost < best) { second = best; best = cost; bx = xx; by = yy; } else if (cost < second) second = cost;
        }
        if (best > 270 || second == 0 || best * 100L > second * 97L || FullCost(previous, current, w, x, y, bx, by) > 625) return false;
        dx = bx; dy = by;
        if (bx > minX && bx < maxX) {
            int a = Cost(previous, current, w, x, y, bx - 1, by, mean), b = Cost(previous, current, w, x, y, bx + 1, by, mean);
            int curve = a - 2 * best + b;
            if (curve > 1) dx += Math.Max(-.5f, Math.Min(.5f, .5f * (a - b) / curve));
        }
        if (by > minY && by < maxY) {
            int a = Cost(previous, current, w, x, y, bx, by - 1, mean), b = Cost(previous, current, w, x, y, bx, by + 1, mean);
            int curve = a - 2 * best + b;
            if (curve > 1) dy += Math.Max(-.5f, Math.Min(.5f, .5f * (a - b) / curve));
        }
        return true;
    }
    public static Detection[] Advance(byte[] previous, byte[] current, int w, int h, Detection[] detections, uint intervalMs) {
        var result = new List<Detection>();
        if (intervalMs > 150) return result.ToArray();
        int range = intervalMs <= 30 ? 12 : 24;
        foreach (var detection in detections) {
            var box = detection.Box;
            var vx = new List<float>(); var vy = new List<float>();
            var points = new HashSet<int>();
            for (int row = 0; row < 3; ++row) for (int column = 0; column < 3; ++column) {
                int cx = (int)((box.X + box.Width * (.3f + column * .2f)) * w);
                int cy = (int)((box.Y + box.Height * (.2f + row * .25f)) * h);
                int bestVariance = 0, px = cx, py = cy;
                for (int oy = -2; oy <= 2; oy += 2) for (int ox = -2; ox <= 2; ox += 2) {
                    int x = cx + ox, y = cy + oy;
                    if (x < 3 || y < 3 || x >= w - 3 || y >= h - 3) continue;
                    int variance = Variance(previous, w, x, y);
                    if (variance > bestVariance) { bestVariance = variance; px = x; py = y; }
                }
                float dx, dy;
                if (!points.Add(py * w + px)) continue;
                if (Match(previous, current, w, h, px, py, range, out dx, out dy)) { vx.Add(dx); vy.Add(dy); }
            }
            if (vx.Count < 3) continue;
            var sortedX = vx.ToArray(); var sortedY = vy.ToArray(); Array.Sort(sortedX); Array.Sort(sortedY);
            float mx = sortedX[sortedX.Length / 2], my = sortedY[sortedY.Length / 2], sumX = 0, sumY = 0; int good = 0;
            for (int i = 0; i < vx.Count; ++i) if (Math.Abs(vx[i] - mx) <= 2.5f && Math.Abs(vy[i] - my) <= 2.5f) { sumX += vx[i]; sumY += vy[i]; good++; }
            if (good < 3) continue;
            box.X += sumX / good / w; box.Y += sumY / good / h;
            if (box.Right <= 0 || box.X >= 1 || box.Bottom <= 0 || box.Y >= 1) continue;
            result.Add(new Detection { Box = box, Score = detection.Score, ConfirmedAt = detection.ConfirmedAt });
        }
        return result.ToArray();
    }
    static float Overlap(RectangleF a, RectangleF b) {
        var r = RectangleF.Intersect(a, b); float v = Math.Max(0, r.Width) * Math.Max(0, r.Height);
        return v / Math.Max(.000001f, a.Width * a.Height + b.Width * b.Height - v);
    }
    public static Detection[] Reconcile(Detection[] tracked, Detection[] recognized, int confirmedAt, int now) {
        var result = new List<Detection>(); var used = new bool[tracked.Length];
        foreach (var fresh in recognized) {
            int match = -1; float best = .2f;
            for (int i = 0; i < tracked.Length; ++i) if (!used[i]) { float score = Overlap(tracked[i].Box, fresh.Box); if (score > best) { best = score; match = i; } }
            RectangleF box = fresh.Box;
            if (match >= 0) {
                used[match] = true; var prior = tracked[match].Box;
                // Screen motion is already current. Only gently correct detector jitter.
                box = new RectangleF(prior.X * .65f + box.X * .35f, prior.Y * .65f + box.Y * .35f, prior.Width * .65f + box.Width * .35f, prior.Height * .65f + box.Height * .35f);
            }
            result.Add(new Detection { Box = box, Score = fresh.Score, ConfirmedAt = confirmedAt });
        }
        for (int i = 0; i < tracked.Length; ++i) if (!used[i] && unchecked((uint)(now - tracked[i].ConfirmedAt)) <= 200) result.Add(tracked[i]);
        result.Sort(delegate(Detection a, Detection b) { return b.Score.CompareTo(a.Score); });
        var bounded = new List<Detection>();
        foreach (var item in result) {
            bool duplicate = false;
            foreach (var prior in bounded) if (Overlap(prior.Box, item.Box) > .6f) { duplicate = true; break; }
            if (!duplicate) bounded.Add(item);
            if (bounded.Count >= 12) break;
        }
        return bounded.ToArray();
    }
    public static Detection[] Expire(Detection[] input, int now) {
        var result = new List<Detection>();
        foreach (var d in input) if (unchecked((uint)(now - d.ConfirmedAt)) <= 200) result.Add(d);
        return result.ToArray();
    }
}
