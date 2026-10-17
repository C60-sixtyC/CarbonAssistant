using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;

namespace DeskKeeper.App.Services;

/// <summary>
/// 类似「莫奈取色」：读取当前壁纸，抽取主色并压暗/提亮到可读的强调色，作用到全局主题。
/// </summary>
public static class WallpaperAccent
{
    public static WpfColor? Extract() => ExtractPalette().FirstOrDefault();

    public static IReadOnlyList<WpfColor> ExtractPalette()
    {
        try
        {
            var path = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")
                ?.GetValue("WallPaper") as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Array.Empty<WpfColor>();
            }

            using var bitmap = new Bitmap(path);
            return Analyze(bitmap);
        }
        catch
        {
            return Array.Empty<WpfColor>();
        }
    }

    private static IReadOnlyList<WpfColor> Analyze(Bitmap bitmap)
    {
        const int grid = 12;
        var buckets = new Dictionary<int, Bucket>();

        for (var gy = 0; gy < grid; gy++)
        {
            for (var gx = 0; gx < grid; gx++)
            {
                var x = (gx + 1) * bitmap.Width / (grid + 1);
                var y = (gy + 1) * bitmap.Height / (grid + 1);
                var c = bitmap.GetPixel(x, y);
                if (c.A < 250)
                {
                    continue;
                }

                var key = (c.R >> 5) << 6 | (c.G >> 5) << 3 | (c.B >> 5);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket();
                    buckets[key] = bucket;
                }

                bucket.Count++;
                bucket.R += c.R;
                bucket.G += c.G;
                bucket.B += c.B;
            }
        }

        var candidates = new List<(Bucket Bucket, double Lum)>();
        foreach (var bucket in buckets.Values)
        {
            var r = bucket.R / bucket.Count;
            var g = bucket.G / bucket.Count;
            var b = bucket.B / bucket.Count;
            var lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (lum < 0.08 || lum > 0.92 || max - min < 10)
            {
                continue; // 跳过近黑/近白/近灰
            }

            if (lum >= 0.08 && lum <= 0.92 && max - min >= 10)
            {
                candidates.Add((bucket, lum));
            }
        }

        var accents = new List<WpfColor>();
        foreach (var (bucket, lum) in candidates.OrderByDescending(c => c.Bucket.Count))
        {
            var color = ToAccent(bucket, lum);
            if (accents.Any(existing => Distance(existing, color) < 42))
            {
                continue;
            }

            accents.Add(color);
            if (accents.Count >= 5)
            {
                break;
            }
        }

        return accents;
    }

    private static WpfColor ToAccent(Bucket bucket, double lum)
    {
        var r = bucket.R / bucket.Count;
        var g = bucket.G / bucket.Count;
        var b = bucket.B / bucket.Count;

        // 强调色需要与白色前景字有足够对比：过亮则压暗，过暗则适度提亮。
        if (lum > 0.55)
        {
            var factor = 0.42 / lum;
            r *= factor;
            g *= factor;
            b *= factor;
        }
        else if (lum < 0.16)
        {
            var factor = 0.22 / lum;
            r = 255 - (255 - r) * factor;
            g = 255 - (255 - g) * factor;
            b = 255 - (255 - b) * factor;
        }

        return WpfColor.FromRgb(
            (byte)Math.Clamp(r, 0, 255),
            (byte)Math.Clamp(g, 0, 255),
            (byte)Math.Clamp(b, 0, 255));
    }

    private static double Distance(WpfColor a, WpfColor b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private sealed class Bucket
    {
        public int Count;
        public double R;
        public double G;
        public double B;
    }
}

