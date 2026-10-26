using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace MusicBookCreator;

/// <summary>
/// Writes a PDF with one A4 page per image. The first and last page are filled edge to edge
/// (aspect ratio kept, overflow cropped centered); all other pages fit the whole image inside
/// a margin of at least 1 cm (aspect ratio kept, centered). Landscape images get landscape pages.
/// </summary>
/// <remarks>
/// Minimal PDF 1.4 writer: JPEG files are embedded unchanged (DCTDecode), other formats and
/// CMYK JPEGs are converted to JPEG first. Image data is streamed, so large books need little memory.
/// </remarks>
public static class PdfBookWriter
{
    private const double PointsPerCm = 72 / 2.54;
    private const double A4ShortSide = 21.0 * PointsPerCm;
    private const double A4LongSide = 29.7 * PointsPerCm;
    private const double InnerMargin = 1.0 * PointsPerCm;
    private const int ConversionJpegQuality = 92;

    public static void Write(string pdfPath, IReadOnlyList<string> imagePaths, CancellationToken cancellationToken = default)
    {
        if (imagePaths.Count == 0)
        {
            throw new ArgumentException("Keine Bilder für die PDF.", nameof(imagePaths));
        }

        // Object numbers: 1 catalog, 2 page tree, then per page: page, content stream, image.
        List<long> offsets = [];
        using FileStream file = File.Create(pdfPath);

        void BeginObject(int number)
        {
            while (offsets.Count < number)
            {
                offsets.Add(0);
            }

            offsets[number - 1] = file.Position;
            WriteAscii(file, $"{number} 0 obj\n");
        }

        WriteAscii(file, "%PDF-1.4\n");
        file.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']); // Binary marker for transfer tools.

        BeginObject(1);
        WriteAscii(file, "<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        List<int> pageObjects = [];
        for (int i = 0; i < imagePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int pageObject = 3 + i * 3;
            int contentObject = pageObject + 1;
            int imageObject = pageObject + 2;
            pageObjects.Add(pageObject);

            JpegData jpeg = JpegData.Load(imagePaths[i]);
            bool fillPage = i == 0 || i == imagePaths.Count - 1;
            bool landscape = jpeg.Width > jpeg.Height;
            double pageWidth = landscape ? A4LongSide : A4ShortSide;
            double pageHeight = landscape ? A4ShortSide : A4LongSide;
            (double x, double y, double width, double height) = Place(jpeg.Width, jpeg.Height, pageWidth, pageHeight, fillPage);

            BeginObject(pageObject);
            WriteAscii(file,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] " +
                $"/Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>\nendobj\n");

            string content = $"q {F(width)} 0 0 {F(height)} {F(x)} {F(y)} cm /Im0 Do Q\n";
            BeginObject(contentObject);
            WriteAscii(file, $"<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");

            BeginObject(imageObject);
            WriteAscii(file,
                $"<< /Type /XObject /Subtype /Image /Width {jpeg.Width} /Height {jpeg.Height} " +
                $"/ColorSpace {(jpeg.Components == 1 ? "/DeviceGray" : "/DeviceRGB")} /BitsPerComponent 8 " +
                $"/Filter /DCTDecode /Length {jpeg.Bytes.Length} >>\nstream\n");
            file.Write(jpeg.Bytes);
            WriteAscii(file, "\nendstream\nendobj\n");
        }

        BeginObject(2);
        WriteAscii(file, $"<< /Type /Pages /Count {pageObjects.Count} /Kids [{string.Join(" ", pageObjects.Select(n => $"{n} 0 R"))}] >>\nendobj\n");

        long xrefOffset = file.Position;
        StringBuilder xref = new();
        xref.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            xref.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        xref.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        WriteAscii(file, xref.ToString());
    }

    /// <summary>
    /// Position and size of the image on the page (PDF coordinates, origin bottom left).
    /// </summary>
    private static (double X, double Y, double Width, double Height) Place(int imageWidth, int imageHeight, double pageWidth, double pageHeight, bool fillPage)
    {
        double areaWidth = fillPage ? pageWidth : pageWidth - 2 * InnerMargin;
        double areaHeight = fillPage ? pageHeight : pageHeight - 2 * InnerMargin;
        double scaleX = areaWidth / imageWidth;
        double scaleY = areaHeight / imageHeight;

        // Fill: cover the whole page (overflow is cut off by the page). Otherwise: fit completely.
        double scale = fillPage ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        double width = imageWidth * scale;
        double height = imageHeight * scale;
        return ((pageWidth - width) / 2, (pageHeight - height) / 2, width, height);
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// JPEG bytes ready for embedding plus the dimensions from its frame header.
    /// </summary>
    private sealed record JpegData(byte[] Bytes, int Width, int Height, int Components)
    {
        public static JpegData Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (TryReadFrameHeader(bytes, out int width, out int height, out int components) && components is 1 or 3)
            {
                return new JpegData(bytes, width, height, components);
            }

            // PNG/TIFF/BMP or CMYK JPEG: convert to an RGB JPEG.
            using MemoryStream input = new(bytes);
            BitmapSource source = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            FormatConvertedBitmap rgb = new(source, System.Windows.Media.PixelFormats.Bgr24, null, 0);
            JpegBitmapEncoder encoder = new() { QualityLevel = ConversionJpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(rgb));
            using MemoryStream output = new();
            encoder.Save(output);
            return new JpegData(output.ToArray(), rgb.PixelWidth, rgb.PixelHeight, 3);
        }

        /// <summary>
        /// Reads width, height and component count from the first SOF marker; false if not a JPEG.
        /// </summary>
        private static bool TryReadFrameHeader(byte[] data, out int width, out int height, out int components)
        {
            width = height = components = 0;
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            {
                return false;
            }

            int pos = 2;
            while (pos + 4 <= data.Length)
            {
                if (data[pos] != 0xFF)
                {
                    return false;
                }

                byte marker = data[pos + 1];
                if (marker == 0xFF)
                {
                    pos++; // Fill byte.
                    continue;
                }

                int length = (data[pos + 2] << 8) | data[pos + 3];

                // SOF0..SOF15 except DHT (C4), JPG (C8) and DAC (CC).
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    if (pos + 10 > data.Length)
                    {
                        return false;
                    }

                    height = (data[pos + 5] << 8) | data[pos + 6];
                    width = (data[pos + 7] << 8) | data[pos + 8];
                    components = data[pos + 9];
                    return width > 0 && height > 0;
                }

                pos += 2 + length;
            }

            return false;
        }
    }
}
