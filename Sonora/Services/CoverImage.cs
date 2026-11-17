using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sonora.Services;

/// <summary>
/// Turns any image the user picks into what Spotify accepts for a playlist cover: a square JPEG, sent
/// base64-encoded, under 256 KB once encoded. Quality is stepped down until it fits rather than failing.
/// </summary>
public static class CoverImage
{
    /// <summary>Spotify's limit is 256 KB of base64, so the JPEG itself must stay under about three quarters of that.</summary>
    const int MaxJpegBytes = 180 * 1024;

    const int Side = 640;

    /// <summary>Reads an image file and returns a square JPEG small enough to upload, or null if it cannot.</summary>
    public static byte[]? ToSquareJpeg(string file)
    {
        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(file);
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            source.EndInit();

            var square = CropToSquare(source);
            foreach (var quality in new[] { 90, 75, 60, 45, 30 })
            {
                var bytes = Encode(square, quality);
                if (bytes.Length <= MaxJpegBytes) return bytes;
            }
            return null; // even at the lowest quality it will not fit: better to say so than to send a reject
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Takes the largest centred square and scales it to the cover size.</summary>
    static BitmapSource CropToSquare(BitmapSource source)
    {
        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        if (side <= 0) return source;
        var crop = new CroppedBitmap(source, new System.Windows.Int32Rect(
            (source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));

        var scale = (double)Side / side;
        var scaled = new TransformedBitmap(crop, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    static byte[] Encode(BitmapSource image, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
