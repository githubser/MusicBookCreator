using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicBookCreator;

/// <summary>
/// Decoded page as BGRA32 pixels.
/// </summary>
public sealed record LoadedImage(int Width, int Height, byte[] Pixels, double DpiX, double DpiY)
{
    public int Stride => Width * 4;
}

/// <summary>
/// Loading/converting images via WPF imaging (JPEG, PNG, TIFF, BMP).
/// </summary>
public static class ImageIo
{
    public static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"];

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static LoadedImage Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        BitmapSource frame = decoder.Frames[0];
        BitmapSource bgra = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        byte[] pixels = new byte[width * 4 * height];
        bgra.CopyPixels(pixels, width * 4, 0);

        return new LoadedImage(width, height, pixels, frame.DpiX, frame.DpiY);
    }

    /// <summary>
    /// Runs the scan processing on a loaded page, with in-process Windows OCR for Auto rotation.
    /// </summary>
    public static unsafe ScanResult Process(LoadedImage image, ScanSettings settings)
    {
        fixed (byte* scan0 = image.Pixels)
        {
            BgraSource source = new(scan0, image.Stride, image.Width, image.Height);
            return ScanProcessor.Process(source, settings, TryOcrScore);
        }
    }

    // OCR only helps Auto rotation; a failing OCR must never fail the page.
    private static (int Upright, int Rotated)? TryOcrScore(byte[] gray, int width, int height)
    {
        try
        {
            return OcrScorer.Score(gray, width, height);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SaveJpeg(ScanResult result, string path, LoadedImage original, int quality)
    {
        using FileStream stream = File.Create(path);
        ScanJpegWriter.Save(result, stream, original.DpiX, original.DpiY, quality);
    }

    /// <summary>
    /// Full-resolution, frozen preview of a loaded page (full size so that zooming stays sharp).
    /// </summary>
    public static BitmapSource ToBitmapSource(LoadedImage image)
    {
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource ToBitmapSource(ScanResult result)
    {
        PixelFormat format = result.IsGrayscale ? PixelFormats.Gray8 : PixelFormats.Bgr32;
        int stride = result.Width * (result.IsGrayscale ? 1 : 4);
        BitmapSource bitmap = BitmapSource.Create(result.Width, result.Height, 96, 96, format, null, result.Pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
