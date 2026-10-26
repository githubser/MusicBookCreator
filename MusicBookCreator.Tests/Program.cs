using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using MusicBookCreator;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

// Regression run against the reference scans in <repository>\TestData (read only, not part of the
// Git repository, see TestData\README.md); all output goes to %TEMP%.
// Expected test set (TestData\BH2025Test): first and last file are covers, everything in
// between are printed pages (several pre-cropped by the scanner, print up to the image edge).
string testDataFolder = FindTestData();
string testFolder = args.Length > 0 ? args[0] : Path.Combine(testDataFolder, "BH2025Test");
string outputFolder = Path.Combine(Path.GetTempPath(), "MusicBookCreator.Tests");
if (!Directory.Exists(testFolder))
{
    Console.WriteLine($"FEHLER  Testdaten nicht gefunden: {testFolder} (siehe TestData\\README.md)");
    return 1;
}
if (Directory.Exists(outputFolder))
{
    Directory.Delete(outputFolder, recursive: true);
}

Checks checks = new();
IReadOnlyList<string> files = BatchProcessor.GetFiles(testFolder);
checks.That(files.Count >= 3, $"Testsatz gefunden: {files.Count} Dateien in {testFolder}");
if (files.Count < 3)
{
    return checks.Finish();
}

// 1. Batch processing exactly like the app.
ScanSettings settings = new();
List<BatchItemResult> results = [];
await BatchProcessor.RunAsync(files, outputFolder, settings, new SyncProgress(r => { lock (results) { results.Add(r); } }), CancellationToken.None);
Dictionary<string, BatchItemResult> byName = results.ToDictionary(r => r.FileName, StringComparer.OrdinalIgnoreCase);

checks.That(results.All(r => r.Status != BatchItemStatus.Failed), "Keine Fehler bei der Verarbeitung");
string first = Path.GetFileName(files[0]);
string last = Path.GetFileName(files[^1]);
checks.That(byName[first].Status == BatchItemStatus.Copied, $"Vorderes Cover {first} unverändert kopiert");
checks.That(byName[last].Status == BatchItemStatus.Copied, $"Hinteres Cover {last} unverändert kopiert");
foreach (string file in files.Skip(1).Take(files.Count - 2))
{
    BatchItemResult r = byName[Path.GetFileName(file)];
    checks.That(r.Status == BatchItemStatus.Processed, $"{r.FileName} bearbeitet{(r.Rotated ? " (gedreht)" : string.Empty)}");
}

// 2. Nothing cut off: the line just outside the detected content must not carry print.
foreach (string file in files)
{
    LoadedImage image = ImageIo.Load(file);
    Rectangle? content = DetectContent(image, settings);
    if (content is not { } c || byName[Path.GetFileName(file)].Status != BatchItemStatus.Processed)
    {
        continue;
    }

    string? cut = CutOffSide(image, c, settings.InkThreshold);
    checks.That(cut is null, $"{Path.GetFileName(file)}: nichts abgeschnitten{(cut is null ? string.Empty : $" – Druck direkt {cut} des Inhalts")}");
}

// 2b. Nothing painted over: no ink pixel of the original may turn white in the output
//     (the margin around the content is taken from the image, not painted over).
foreach (string file in files)
{
    BatchItemResult item = byName[Path.GetFileName(file)];
    if (item.Status != BatchItemStatus.Processed)
    {
        continue;
    }

    LoadedImage original = ImageIo.Load(file);
    ScanResult processed = ImageIo.Process(original, settings);
    int lost = CountPaintedOverInk(original, processed, settings);
    checks.That(lost == 0, $"{item.FileName}: kein Druck übermalt{(lost == 0 ? string.Empty : $" – {lost} Druckpixel weiß geworden")}");
}

// 2c. Deskew: a page skewed artificially by a known angle must be detected with that angle.
{
    string sample = files[files.Count / 2];
    LoadedImage original = ImageIo.Load(sample);
    double baseAngle = SkewOf(original, settings.InkThreshold);
    LoadedImage skewed = Rotated(original, -1.5); // Rotating by -1.5° skews straight lines by +1.5°.
    double detected = SkewOf(skewed, settings.InkThreshold);
    double expected = baseAngle + 1.5;
    checks.That(Math.Abs(detected - expected) <= 0.1,
        $"Begradigen: {Path.GetFileName(sample)} künstlich um 1,5° verdreht → erkannt {detected:+0.00;-0.00}° (erwartet {expected:+0.00;-0.00}°, Original {baseAngle:+0.00;-0.00}°)");

    ScanResult straightened = ImageIo.Process(skewed, settings with { Deskew = true });
    checks.That(Math.Abs(straightened.SkewAngle - expected) <= 0.1, $"Begradigen: Verarbeitung korrigiert {straightened.SkewAngle:+0.00;-0.00}°");
}

// 2c'. Deskew on a really skewed scan (provided by the user, measured 1.32°): detected angle and
//      remaining skew of the result.
string SkewedScan = Path.Combine(testDataFolder, "Scannen0651.jpg");
if (File.Exists(SkewedScan))
{
    LoadedImage skewedScan = ImageIo.Load(SkewedScan);
    ScanResult straight = ImageIo.Process(skewedScan, settings with { Deskew = true });
    double residual = ResidualSkew(straight, settings.InkThreshold);
    checks.That(Math.Abs(straight.SkewAngle - 1.32) <= 0.1 && Math.Abs(residual) <= 0.1,
        $"Begradigen echter Scan {Path.GetFileName(SkewedScan)}: korrigiert {straight.SkewAngle:+0.00;-0.00}° (erwartet +1,32°), Restschräge {residual:+0.00;-0.00}°");
}
else
{
    Console.WriteLine($"HINWEIS  {SkewedScan} nicht gefunden – Test für echten schrägen Scan übersprungen");
}

// 2d. Despeckle: artificial dust specks must all be removed, real print must stay untouched.
{
    string sample = files[files.Count / 2];
    ScanResult clean = ImageIo.Process(ImageIo.Load(sample), settings);
    byte[] withSpecks = (byte[])clean.Pixels.Clone();
    int added = AddSpecks(withSpecks, clean.Width, clean.Height, 50);

    byte[] cleanCopy = (byte[])clean.Pixels.Clone();
    int baseline = ScanProcessor.RemoveSpecks(cleanCopy, clean.Width, clean.Height, true);
    int removed = ScanProcessor.RemoveSpecks(withSpecks, clean.Width, clean.Height, true);
    bool identical = withSpecks.AsSpan().SequenceEqual(cleanCopy);
    checks.That(added == 50 && removed - baseline == added && identical,
        $"Flecken entfernen: {removed - baseline} von {added} künstlichen Flecken entfernt, Druck unverändert: {identical} (echte Flecken auf der Seite: {baseline})");
}

// 3. PDF: page count, A4, covers edge to edge, inner pages with >= 1 cm white margin.
List<string> pages = files.Select(f => byName[Path.GetFileName(f)].OutputPath).OfType<string>().ToList();
string pdfPath = Path.Combine(outputFolder, new DirectoryInfo(testFolder).Name + ".pdf");
PdfBookWriter.Write(pdfPath, pages);

using (FileStream pdfStream = File.OpenRead(pdfPath))
{
    PdfDocument pdf = await PdfDocument.LoadFromStreamAsync(pdfStream.AsRandomAccessStream());
    checks.That(pdf.PageCount == files.Count, $"PDF hat {pdf.PageCount} Seiten (erwartet {files.Count})");

    for (uint i = 0; i < pdf.PageCount; i++)
    {
        using PdfPage page = pdf.GetPage(i);
        bool landscape = page.Size.Width > page.Size.Height;
        double shortSide = Math.Min(page.Size.Width, page.Size.Height);
        double longSide = Math.Max(page.Size.Width, page.Size.Height);
        bool isA4 = Math.Abs(shortSide / 96 * 2.54 - 21.0) < 0.1 && Math.Abs(longSide / 96 * 2.54 - 29.7) < 0.1;

        // Render at 96 dpi: 1 cm ≈ 37.8 px.
        byte[] gray = await RenderGray(page, (uint)Math.Round(page.Size.Width), (uint)Math.Round(page.Size.Height));
        int width = (int)Math.Round(page.Size.Width);
        int height = (int)Math.Round(page.Size.Height);
        bool edgePage = i == 0 || i == pdf.PageCount - 1;
        string label = $"PDF-Seite {i + 1}{(landscape ? " (quer)" : string.Empty)}";

        if (edgePage)
        {
            bool cornersFilled = new[] { (2, 2), (width - 3, 2), (2, height - 3), (width - 3, height - 3) }
                .All(p => gray[p.Item2 * width + p.Item1] < 250);
            checks.That(isA4 && cornersFilled, $"{label}: A4, randlos gefüllt");
        }
        else
        {
            int margin = 36; // Slightly below 1 cm to tolerate rounding of the renderer.
            bool marginWhite = IsBandWhite(gray, width, height, margin);
            checks.That(isA4 && marginWhite, $"{label}: A4, mindestens 1 cm weißer Rand");
        }
    }
}

Console.WriteLine($"\nAusgaben: {outputFolder}");
return checks.Finish();

static unsafe Rectangle? DetectContent(LoadedImage image, ScanSettings settings)
{
    fixed (byte* scan0 = image.Pixels)
    {
        return ScanProcessor.DetectContent(new BgraSource(scan0, image.Stride, image.Width, image.Height), settings);
    }
}

// Returns the side ("oberhalb", ...) where the line directly next to the content still carries
// print (more than noise, less than a full shadow line), or null if nothing was cut off.
static string? CutOffSide(LoadedImage image, Rectangle content, int inkThreshold)
{
    bool Ink(int x, int y)
    {
        int i = (y * image.Width + x) * 4;
        int l = (image.Pixels[i + 2] * 77 + image.Pixels[i + 1] * 150 + image.Pixels[i] * 29) >> 8;
        return l < inkThreshold;
    }

    bool CarriesPrint(int count, int length) => count >= Math.Max(2, length / 500) && count <= length / 2;

    int RowInk(int y) => Enumerable.Range(content.Left, content.Width).Count(x => Ink(x, y));
    int ColumnInk(int x) => Enumerable.Range(content.Top, content.Height).Count(y => Ink(x, y));

    if (content.Top > 0 && CarriesPrint(RowInk(content.Top - 1), content.Width))
    {
        return "oberhalb";
    }

    if (content.Bottom < image.Height && CarriesPrint(RowInk(content.Bottom), content.Width))
    {
        return "unterhalb";
    }

    if (content.Left > 0 && CarriesPrint(ColumnInk(content.Left - 1), content.Height))
    {
        return "links";
    }

    if (content.Right < image.Width && CarriesPrint(ColumnInk(content.Right), content.Height))
    {
        return "rechts";
    }

    return null;
}

static unsafe double SkewOf(LoadedImage image, int inkThreshold)
{
    fixed (byte* scan0 = image.Pixels)
    {
        return ScanProcessor.DetectSkewAngle(new BgraSource(scan0, image.Stride, image.Width, image.Height), inkThreshold);
    }
}

// The TestData folder of the repository: searched upwards from the program location
// (bin\Release\...\ is several levels below the repository root).
static string FindTestData()
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "TestData");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }

    return Path.Combine(AppContext.BaseDirectory, "TestData");
}

// Skew still present in a processed (grayscale) result.
static unsafe double ResidualSkew(ScanResult result, int inkThreshold)
{
    uint[] bgra = new uint[result.Width * result.Height];
    for (int i = 0; i < bgra.Length; i++)
    {
        uint g = result.Pixels[i];
        bgra[i] = 0xFF000000 | (g << 16) | (g << 8) | g;
    }

    fixed (uint* scan0 = bgra)
    {
        return ScanProcessor.DetectSkewAngle(new BgraSource((byte*)scan0, result.Width * 4, result.Width, result.Height), inkThreshold);
    }
}

static unsafe LoadedImage Rotated(LoadedImage image, double degrees)
{
    fixed (byte* scan0 = image.Pixels)
    {
        uint[] rotated = ScanProcessor.Rotate(new BgraSource(scan0, image.Stride, image.Width, image.Height), degrees);
        byte[] bytes = new byte[rotated.Length * 4];
        System.Buffer.BlockCopy(rotated, 0, bytes, 0, bytes.Length);
        return image with { Pixels = bytes };
    }
}

// Puts 2×2 black dots at fixed pseudo-random positions where a 9×9 area is completely white.
static int AddSpecks(byte[] gray, int width, int height, int wanted)
{
    Random random = new(42);
    int added = 0;
    for (int attempt = 0; attempt < 100_000 && added < wanted; attempt++)
    {
        int x = random.Next(5, width - 6);
        int y = random.Next(5, height - 6);
        bool white = true;
        for (int dy = -4; dy <= 4 && white; dy++)
        {
            for (int dx = -4; dx <= 4 && white; dx++)
            {
                white = gray[(y + dy) * width + x + dx] == 255;
            }
        }

        if (white)
        {
            gray[y * width + x] = gray[y * width + x + 1] = gray[(y + 1) * width + x] = gray[(y + 1) * width + x + 1] = 0;
            added++;
        }
    }

    return added;
}

// Maps every output pixel back to the original (crop offset, optional 180° rotation) and counts
// original ink pixels that ended up (almost) white.
static int CountPaintedOverInk(LoadedImage original, ScanResult result, ScanSettings settings)
{
    if (!result.IsGrayscale || result.Content is not { } content)
    {
        return 0;
    }

    int offsetX = Math.Max(0, content.Left - settings.Margin);
    int offsetY = Math.Max(0, content.Top - settings.Margin);
    int lost = 0;
    for (int y = 0; y < result.Height; y++)
    {
        for (int x = 0; x < result.Width; x++)
        {
            int i = ((offsetY + y) * original.Width + offsetX + x) * 4;
            int l = (original.Pixels[i + 2] * 77 + original.Pixels[i + 1] * 150 + original.Pixels[i] * 29) >> 8;
            int ox = result.Rotated ? result.Width - 1 - x : x;
            int oy = result.Rotated ? result.Height - 1 - y : y;
            if (l < settings.InkThreshold && result.Pixels[oy * result.Width + ox] >= 250)
            {
                lost++;
            }
        }
    }

    return lost;
}

static bool IsBandWhite(byte[] gray, int width, int height, int band)
{
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            bool inBand = x < band || x >= width - band || y < band || y >= height - band;
            if (inBand && gray[y * width + x] < 250)
            {
                return false;
            }
        }
    }

    return true;
}

static async Task<byte[]> RenderGray(PdfPage page, uint width, uint height)
{
    using InMemoryRandomAccessStream stream = new();
    await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height });
    BitmapDecoder decoder = BitmapDecoder.Create(stream.AsStreamForRead(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    FormatConvertedBitmap converted = new(decoder.Frames[0], System.Windows.Media.PixelFormats.Gray8, null, 0);
    byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight];
    converted.CopyPixels(pixels, converted.PixelWidth, 0);
    return pixels;
}

sealed class SyncProgress(Action<BatchItemResult> report) : IProgress<BatchItemResult>
{
    public void Report(BatchItemResult value) => report(value);
}

sealed class Checks
{
    private int failed;
    private int passed;

    public void That(bool condition, string description)
    {
        Console.WriteLine($"{(condition ? "OK    " : "FEHLER")}  {description}");
        if (condition)
        {
            passed++;
        }
        else
        {
            failed++;
        }
    }

    public int Finish()
    {
        Console.WriteLine($"\n{passed} OK, {failed} Fehler");
        return failed == 0 ? 0 : 1;
    }
}
