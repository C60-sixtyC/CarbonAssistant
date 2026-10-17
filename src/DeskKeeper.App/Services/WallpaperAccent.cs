using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;

namespace DeskKeeper.App.Services;

/// <summary>
/// 类似「莫奈取色」：读取当前壁纸，抽取主色并压暗/提亮到可读的强调色，作用到全局主题。
/// </summary>
public static class WallpaperAccent
{
    public static WpfColor? Extract()
    {
        try
        {
            var path = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")
                ?.GetValue("WallPaper") as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using var bitmap = new Bitmap(path);
            return Analyze(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static WpfColor? Analyze(Bitmap bitmap)
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

        Bucket? best = null;
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

            if (best is null || bucket.Count > best.Count)
            {
                best = bucket;
            }
        }

        if (best is null)
        {
            return null;
        }

        var accentR = best.R / best.Count;
        var accentG = best.G / best.Count;
        var accentB = best.B / best.Count;
        var accentLum = (0.299 * accentR + 0.587 * accentG + 0.114 * accentB) / 255.0;

        // 强调色需要与白色前景字有足够对比：过亮则压暗，过暗则适度提亮。
        if (accentLum > 0.55)
        {
            var factor = 0.42 / accentLum;
            accentR *= factor;
            accentG *= factor;
            accentB *= factor;
        }
        else if (accentLum < 0.16)
        {
            var factor = 0.22 / accentLum;
            accentR = 255 - (255 - accentR) * factor;
            accentG = 255 - (255 - accentG) * factor;
            accentB = 255 - (255 - accentB) * factor;
        }

        return WpfColor.FromRgb(
            (byte)Math.Clamp(accentR, 0, 255),
            (byte)Math.Clamp(accentG, 0, 255),
            (byte)Math.Clamp(accentB, 0, 255));
    }

    private sealed class Bucket
    {
        public int Count;
        public double R;
        public double G;
        public double B;
    }
}

