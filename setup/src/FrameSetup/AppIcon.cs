using Avalonia.Media.Imaging;
using SkiaSharp;

namespace FrameKit.Setup.App;

internal static class AppIcon
{
    public static Bitmap? Load(byte[]? png)
    {
        if (png is not { Length: > 0 })
        {
            return null;
        }
        try
        {
            using var stream = new MemoryStream(png);
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public static string Describe(byte[]? png)
    {
        if (png is not { Length: > 0 })
        {
            return "none";
        }
        SKBitmap? decoded;
        try
        {
            decoded = SKBitmap.Decode(png);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            decoded = null;
        }
        using var bitmap = decoded ?? throw new InvalidDataException("the app icon (app-icon.png) isn't an image Setup can show");
        return $"{bitmap.Width}x{bitmap.Height}, {png.Length} bytes";
    }
}
