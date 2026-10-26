using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdnScanCrop;

/// <summary>
/// Writes a <see cref="ScanResult"/> as JPEG via WPF.
/// Grayscale results are saved as single-channel JPEG (GDI+ can only write colour JPEGs).
/// </summary>
public static class ScanJpegWriter
{
    public static void Save(ScanResult result, Stream output, double dpiX, double dpiY, int quality)
    {
        PixelFormat format = result.IsGrayscale ? PixelFormats.Gray8 : PixelFormats.Bgr32;
        int stride = result.Width * (result.IsGrayscale ? 1 : 4);

        BitmapSource bitmap = BitmapSource.Create(result.Width, result.Height, dpiX, dpiY, format, null, result.Pixels, stride);
        bitmap.Freeze();

        JpegBitmapEncoder encoder = new() { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
    }
}
