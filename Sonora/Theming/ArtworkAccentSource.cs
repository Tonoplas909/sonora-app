using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sonora.Appearance.Theming;

namespace Sonora.Theming;

/// <summary>Decodes album art: a 32×32 sample for the accent colour and a 600 px image for the artwork background.</summary>
public static class ArtworkAccentSource
{
    static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<(Argb? Accent, ImageSource? Image)> LoadAsync(string path)
    {
        try
        {
            // Spotify covers are https URLs: a BitmapImage built from a remote URI downloads asynchronously and
            // cannot be frozen, so fetch the bytes first and decode them from memory.
            byte[] bytes = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? await Http.GetByteArrayAsync(path)
                : await File.ReadAllBytesAsync(path);
            return await Task.Run(() =>
            {
                var small = Decode(bytes, 32);
                var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                var pixels = new uint[bgra.PixelWidth * bgra.PixelHeight];
                bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
                return (AccentPicker.Pick(pixels), (ImageSource?)Decode(bytes, 600));
            });
        }
        catch
        {
            return (null, null);
        }
    }

    static BitmapImage Decode(byte[] bytes, int width)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.DecodePixelWidth = width;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
