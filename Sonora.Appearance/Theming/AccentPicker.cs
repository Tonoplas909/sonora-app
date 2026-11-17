namespace Sonora.Appearance.Theming;

/// <summary>Derives a readable accent colour from album artwork pixels.</summary>
public static class AccentPicker
{
    const int Bins = 24;

    /// <summary>
    /// Dominant saturated colour of the image, or null when the image is mostly grey, too dark or too bright.
    /// Pixels are 32-bit values laid out as 0xAARRGGBB (BGRA bytes in memory).
    /// </summary>
    public static Argb? Pick(ReadOnlySpan<uint> bgraPixels)
    {
        if (bgraPixels.Length == 0) return null;
        var weight = new double[Bins];
        var sumR = new double[Bins];
        var sumG = new double[Bins];
        var sumB = new double[Bins];
        var count = new int[Bins];
        int kept = 0;

        foreach (var px in bgraPixels)
        {
            byte a = (byte)(px >> 24), r = (byte)(px >> 16), g = (byte)(px >> 8), b = (byte)px;
            if (a < 128) continue;
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double v = max / 255.0;
            double s = max == 0 ? 0 : (max - min) / (double)max;
            if (s < 0.25 || v < 0.2 || v > 0.95) continue;

            var (h, _, _) = new Argb(255, r, g, b).ToHsl();
            int bin = Math.Min(Bins - 1, (int)(h / (360.0 / Bins)));
            weight[bin] += s;
            sumR[bin] += r;
            sumG[bin] += g;
            sumB[bin] += b;
            count[bin]++;
            kept++;
        }

        if (kept < bgraPixels.Length * 0.02 || kept == 0) return null;
        int best = 0;
        for (int i = 1; i < Bins; i++)
            if (weight[i] > weight[best]) best = i;
        int n = count[best];
        return new Argb(255, (byte)Math.Round(sumR[best] / n), (byte)Math.Round(sumG[best] / n), (byte)Math.Round(sumB[best] / n));
    }

    /// <summary>Moves the colour's lightness away from the surface until the contrast ratio is reached (hue and saturation kept).</summary>
    public static Argb EnsureContrast(Argb color, Argb surface, double minRatio = 3.0)
    {
        if (Argb.Contrast(color, surface) >= minRatio) return color;
        // Try both directions: on mid-luminance surfaces only one of them can reach the ratio.
        var lighter = Walk(color, surface, minRatio, +0.02);
        var darker = Walk(color, surface, minRatio, -0.02);
        bool lighterOk = Argb.Contrast(lighter.Color, surface) >= minRatio;
        bool darkerOk = Argb.Contrast(darker.Color, surface) >= minRatio;
        if (lighterOk && darkerOk) return lighter.Steps <= darker.Steps ? lighter.Color : darker.Color;
        if (lighterOk) return lighter.Color;
        if (darkerOk) return darker.Color;
        return Argb.Contrast(lighter.Color, surface) >= Argb.Contrast(darker.Color, surface) ? lighter.Color : darker.Color;
    }

    static (Argb Color, int Steps) Walk(Argb color, Argb surface, double minRatio, double step)
    {
        var (h, s, l) = color.ToHsl();
        var current = color;
        int steps = 0;
        while (steps < 60)
        {
            l = Math.Clamp(l + step, 0, 1);
            current = Argb.FromHsl(h, s, l, color.A);
            steps++;
            if (Argb.Contrast(current, surface) >= minRatio || l is 0 or 1) break;
        }
        return (current, steps);
    }

    /// <summary>Near-black or white, whichever reads better on the accent.</summary>
    public static Argb OnColor(Argb accent)
    {
        var dark = new Argb(255, 0x0B, 0x0B, 0x0B);
        var light = new Argb(255, 255, 255, 255);
        return Argb.Contrast(accent, dark) >= Argb.Contrast(accent, light) ? dark : light;
    }
}
