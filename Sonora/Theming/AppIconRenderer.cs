using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sonora.Appearance.Theming;

namespace Sonora.Theming;

/// <summary>Draws an <see cref="AppIconSpec"/>: the logo on a tile, at any size, and as the icon of a window.</summary>
public static class AppIconRenderer
{
    /// <summary>Sizes Windows asks for: title bar and taskbar at the usual DPI scalings, Alt+Tab and the larger views.</summary>
    static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>The icon as a frozen picture of <paramref name="px"/> x <paramref name="px"/> pixels.</summary>
    public static BitmapSource Render(AppIconSpec spec, int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double side = px, m = AppIconSpec.Margin * side;
            var tile = new Rect(m, m, side - 2 * m, side - 2 * m);

            var fill = new LinearGradientBrush(ToColor(spec.TopLeft), ToColor(spec.BottomRight), new Point(0, 0), new Point(1, 1));
            fill.Freeze();
            dc.DrawGeometry(fill, null, Tile(tile, spec.RadiusTopLeft * side, spec.RadiusTopRight * side, spec.RadiusBottomRight * side, spec.RadiusBottomLeft * side));

            var bars = new SolidColorBrush(ToColor(spec.Bars));
            bars.Freeze();
            foreach (var b in AppIconSpec.LogoBars)
            {
                var r = new Rect(b.X * side, b.Y * side, b.Width * side, b.Height * side);
                dc.DrawRoundedRectangle(bars, null, r, r.Width / 2, r.Width / 2);
            }
        }
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>A rectangle whose four corners each have their own radius (clockwise from the top left).</summary>
    static Geometry Tile(Rect r, double tl, double tr, double br, double bl)
    {
        // A radius cannot exceed half a side, or neighbouring corners would overlap.
        double max = Math.Min(r.Width, r.Height) / 2;
        tl = Math.Min(tl, max); tr = Math.Min(tr, max); br = Math.Min(br, max); bl = Math.Min(bl, max);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + tl, r.Top), isFilled: true, isClosed: true);
            c.LineTo(new Point(r.Right - tr, r.Top), false, false);
            if (tr > 0) c.ArcTo(new Point(r.Right, r.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Right, r.Bottom - br), false, false);
            if (br > 0) c.ArcTo(new Point(r.Right - br, r.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Left + bl, r.Bottom), false, false);
            if (bl > 0) c.ArcTo(new Point(r.Left, r.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Left, r.Top + tl), false, false);
            if (tl > 0) c.ArcTo(new Point(r.Left + tl, r.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>
    /// The icon in every size Windows may want, as one .ico held in memory: a window picks the size that suits the
    /// title bar, the taskbar button or Alt+Tab, instead of scaling one picture.
    /// </summary>
    public static BitmapFrame WindowIcon(AppIconSpec spec)
    {
        var images = new List<byte[]>();
        foreach (var size in IconSizes)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(spec, size)));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            images.Add(ms.ToArray());
        }

        using var ico = new MemoryStream();
        using (var w = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count); // reserved, type = icon, count
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = IconSizes[i];
                w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
                w.Write((byte)0); w.Write((byte)0);                                               // palette, reserved
                w.Write((ushort)1); w.Write((ushort)32);                                          // planes, bits per pixel
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var data in images) w.Write(data);
        }
        ico.Position = 0;
        var frame = BitmapFrame.Create(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}
