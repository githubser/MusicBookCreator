using System.Drawing;
using System.Runtime.InteropServices;

namespace PdnScanCrop;

/// <summary>
/// Settings for <see cref="ScanProcessor"/>.
/// </summary>
public sealed record ScanSettings
{
    /// <summary>Pixels darker than this luminance (0-255) count as content ("ink").</summary>
    public int InkThreshold { get; init; } = 128;

    /// <summary>Outer border (percent of width/height) that is ignored entirely during detection.</summary>
    public double IgnoreBorderPercent { get; init; } = 1.0;

    /// <summary>White margin (pixels) added around the detected content.</summary>
    public int Margin { get; init; } = 40;

    /// <summary>Luminance from which pixels become pure white. 255 disables background cleanup.</summary>
    public int WhitePoint { get; init; } = 200;

    /// <summary>Contrast change in percent (-100..100), applied after background cleanup.</summary>
    public int Contrast { get; init; } = 10;

    /// <summary>Brightness change in percent (-100..100). Pure white stays white.</summary>
    public int Brightness { get; init; } = -5;

    /// <summary>JPEG quality (1-100).</summary>
    public int JpegQuality { get; init; } = 85;

    public bool Grayscale { get; init; } = true;

    public ScanRotation Rotation { get; init; } = ScanRotation.Auto;

    /// <summary>Straighten slightly skewed scans (up to ±5°) before all other steps.</summary>
    public bool Deskew { get; init; }

    /// <summary>Remove small isolated dark specks (dust) from the result.</summary>
    public bool Despeckle { get; init; }
}

public enum ScanRotation
{
    /// <summary>Rotate by 180° if the page is detected as upside down.</summary>
    Auto,
    None,
    Rotate180
}

/// <summary>
/// Read-only view of a 32-bit BGRA pixel buffer (Paint.NET Surface, GDI+ BitmapData, ...).
/// </summary>
public readonly unsafe struct BgraSource(byte* scan0, int stride, int width, int height)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public uint* Row(int y) => (uint*)(scan0 + (long)y * stride);
}

/// <summary>
/// Processed page. <see cref="Pixels"/> is BGRA32, or 8-bit luminance if <see cref="IsGrayscale"/>.
/// <see cref="IsPlausible"/> is false if the page does not look like a printed page on light
/// paper (no content found, content area tiny, or mostly dark such as photo album pages).
/// <see cref="SkewAngle"/> is the corrected skew in degrees (0 if not straightened), <see cref="SpecksRemoved"/>
/// the number of removed specks.
/// </summary>
public sealed record ScanResult(int Width, int Height, bool IsGrayscale, byte[] Pixels, Rectangle? Content, bool Rotated, bool IsPlausible,
    double SkewAngle = 0, int SpecksRemoved = 0);

/// <summary>
/// Runs OCR on an 8-bit grayscale page upright and rotated by 180° and returns the number of
/// letters in plausible words for both, or null if OCR is unavailable.
/// </summary>
public delegate (int Upright, int Rotated)? OcrScoreProvider(byte[] gray, int width, int height);

/// <summary>
/// Detects the content area of a scanned page, crops it and cleans up the background.
/// Has no Paint.NET and no WinRT dependency so it can be shared between the Paint.NET plugin,
/// the standalone app and test tools. OCR for Auto rotation is passed in as <see cref="OcrScoreProvider"/>.
/// </summary>
public static class ScanProcessor
{
    // Minimum OCR score difference (letters in plausible words) to decide the orientation.
    private const int OcrMinScoreDifference = 6;

    // Deskew: search range, smallest angle worth correcting, and how much sharper the line
    // profile must be than at 0° (avoids random corrections on photos or nearly empty pages).
    private const double MaxDeskewAngle = 5;
    private const double MinDeskewAngle = 0.1;
    private const double MinSkewScoreGain = 0.02;

    // Despeckle: output pixels darker than this are "visible"; groups up to MaxSpeckArea pixels
    // (at 300 dpi, ≈ 0.3 mm across) are specks. Staccato/augmentation dots are 25+ pixels.
    private const int SpeckDarkness = 200;
    private const int MaxSpeckArea = 12;

    // Pixels around the detected content that are never painted white (≈ 1 mm at 300 dpi).
    private const int ContentSafetyBand = 12;

    // Pages with more ink than this fraction are not light paper with print (e.g. photo albums).
    private const double MaxPlausibleInkFraction = 0.35;

    // The detected content must cover at least this fraction of the page width and height.
    private const double MinPlausibleContentFraction = 0.3;
    // A line (row/column) touching the image edge with more ink than this fraction
    // is treated as scanner border or book-fold shadow, not as content.
    private const double EdgeShadowFraction = 0.5;

    // A line needs at least 1/NoiseDivisor of its length as ink to count as content.
    // Filters dust and single specks.
    private const int NoiseDivisor = 500;

    // Within this outer fraction of the page, shadow lines are skipped even when separated
    // from the edge by white lines (thin page edges, book covers). Kept small so the top
    // staff line of a page is never mistaken for a border.
    private const double OuterZoneFraction = 0.04;

    // Width of the left/right strips (fraction of content width) compared for orientation detection.
    private const double OrientationStripFraction = 0.08;

    // Rows with more ink than this fraction of the content width are treated as staff lines
    // and ignored for orientation detection.
    private const double OrientationLineRowFraction = 0.3;

    // Minimum |orientation score| for Auto to rotate. Below that the page is left as is.
    private const double OrientationMinScore = 0.1;

    /// <summary>
    /// Compares the ink in the left and right strips of the content area.
    /// Sheet music has brackets, clefs and key signatures at the start of every system,
    /// left-aligned text has a straight left and a ragged right edge, so upright pages
    /// have more ink on the left. Returns (left - right) / (left + right):
    /// positive = upright, negative = upside down, near 0 = undecided.
    /// </summary>
    public static unsafe double GetOrientationScore(BgraSource src, Rectangle content, int inkThreshold)
    {
        int strip = Math.Max(1, (int)(content.Width * OrientationStripFraction));
        int lineRowLimit = (int)(content.Width * OrientationLineRowFraction);
        long left = 0;
        long right = 0;

        for (int y = content.Top; y < content.Bottom; y++)
        {
            uint* row = src.Row(y);

            // Staff lines run across the full width and add the same ink to both strips,
            // drowning out the clefs and key signatures. Skip such rows.
            int rowInk = 0;
            for (int x = content.Left; x < content.Right; x++)
            {
                if (Luminance(row[x]) < inkThreshold)
                {
                    rowInk++;
                }
            }

            if (rowInk > lineRowLimit)
            {
                continue;
            }

            for (int x = content.Left; x < content.Left + strip; x++)
            {
                if (Luminance(row[x]) < inkThreshold)
                {
                    left++;
                }
            }

            for (int x = content.Right - strip; x < content.Right; x++)
            {
                if (Luminance(row[x]) < inkThreshold)
                {
                    right++;
                }
            }
        }

        return left + right == 0 ? 0 : (double)(left - right) / (left + right);
    }

    /// <summary>
    /// Decides the orientation from OCR scores: true = upside down, false = upright, null = undecided.
    /// </summary>
    public static bool? DecideOrientation(int uprightScore, int rotatedScore)
    {
        int diff = rotatedScore - uprightScore;
        return Math.Abs(diff) < OcrMinScoreDifference ? null : diff > 0;
    }

    /// <summary>
    /// Returns the content bounds, or null if no content was found.
    /// </summary>
    public static Rectangle? DetectContent(BgraSource src, ScanSettings settings)
        => DetectContent(src, settings, out _);

    /// <summary>
    /// Returns the content bounds, or null if no content was found.
    /// <paramref name="inkFraction"/> is the fraction of ink pixels on the whole page.
    /// </summary>
    public static unsafe Rectangle? DetectContent(BgraSource src, ScanSettings settings, out double inkFraction)
    {
        inkFraction = 0;
        int width = src.Width;
        int height = src.Height;
        int ignoreX = (int)(width * settings.IgnoreBorderPercent / 100.0);
        int ignoreY = (int)(height * settings.IgnoreBorderPercent / 100.0);

        int left = ignoreX;
        int right = width - 1 - ignoreX;
        int top = ignoreY;
        int bottom = height - 1 - ignoreY;
        if (left >= right || top >= bottom)
        {
            return null;
        }

        // Precompute ink mask once (1 byte per pixel) so the alternating passes are cheap.
        byte[] ink = new byte[width * height];
        int threshold = settings.InkThreshold;
        long inkCount = 0;
        for (int y = 0; y < height; y++)
        {
            uint* row = src.Row(y);
            int offset = y * width;
            for (int x = 0; x < width; x++)
            {
                byte isInk = Luminance(row[x]) < threshold ? (byte)1 : (byte)0;
                ink[offset + x] = isInk;
                inkCount += isInk;
            }
        }

        inkFraction = (double)inkCount / ((long)width * height);

        // Shadows along one edge put ink into every line of the other direction, so the
        // bounds are refined alternately: columns within current rows, rows within current columns.
        for (int pass = 0; pass < 2; pass++)
        {
            int[] colInk = new int[width];
            for (int y = top; y <= bottom; y++)
            {
                int offset = y * width;
                for (int x = left; x <= right; x++)
                {
                    colInk[x] += ink[offset + x];
                }
            }

            if (!FindBounds(colInk, left, right, bottom - top + 1, (int)(width * OuterZoneFraction), out int newLeft, out int newRight))
            {
                return null;
            }

            left = newLeft;
            right = newRight;

            int[] rowInk = new int[height];
            for (int y = top; y <= bottom; y++)
            {
                int offset = y * width;
                int count = 0;
                for (int x = left; x <= right; x++)
                {
                    count += ink[offset + x];
                }

                rowInk[y] = count;
            }

            if (!FindBounds(rowInk, top, bottom, right - left + 1, (int)(height * OuterZoneFraction), out int newTop, out int newBottom))
            {
                return null;
            }

            top = newTop;
            bottom = newBottom;
        }

        // Pages already cropped by the scanner have print right up to the image edge, inside the
        // ignored border. Grow the bounds into it line by line as long as the next line still
        // carries content (more than noise, less than a shadow), so that print is not cut off.
        bool grew;
        do
        {
            grew = false;
            if (top > 0 && IsContentLine(RowInk(top - 1), right - left + 1))
            {
                top--;
                grew = true;
            }

            if (bottom < height - 1 && IsContentLine(RowInk(bottom + 1), right - left + 1))
            {
                bottom++;
                grew = true;
            }

            if (left > 0 && IsContentLine(ColumnInk(left - 1), bottom - top + 1))
            {
                left--;
                grew = true;
            }

            if (right < width - 1 && IsContentLine(ColumnInk(right + 1), bottom - top + 1))
            {
                right++;
                grew = true;
            }
        }
        while (grew);

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);

        int RowInk(int y)
        {
            int count = 0;
            int offset = y * width;
            for (int x = left; x <= right; x++)
            {
                count += ink[offset + x];
            }

            return count;
        }

        int ColumnInk(int x)
        {
            int count = 0;
            for (int y = top; y <= bottom; y++)
            {
                count += ink[y * width + x];
            }

            return count;
        }
    }

    private static bool IsContentLine(int lineInk, int lineLength)
        => lineInk >= Math.Max(2, lineLength / NoiseDivisor) && lineInk <= lineLength * EdgeShadowFraction;

    /// <summary>
    /// Creates the output page: content plus margin, background cleaned, optionally rotated.
    /// Pixels outside the content bounds are pure white (removes shadows inside the margin),
    /// except on sides where the image is too small for the full margin: there the image border is kept.
    /// If no content is found, the whole page is kept.
    /// </summary>
    /// <param name="ocr">OCR for Auto rotation; without it only the left/right ink comparison is used.</param>
    public static unsafe ScanResult Process(BgraSource src, ScanSettings settings, OcrScoreProvider? ocr = null)
    {
        if (settings.Deskew && DetectSkewAngle(src, settings.InkThreshold) is var angle && Math.Abs(angle) >= MinDeskewAngle)
        {
            uint[] straightened = Rotate(src, angle);
            fixed (uint* scan0 = straightened)
            {
                BgraSource straightSource = new((byte*)scan0, src.Width * 4, src.Width, src.Height);
                return ProcessCore(straightSource, settings, ocr) with { SkewAngle = angle };
            }
        }

        return ProcessCore(src, settings, ocr);
    }

    private static unsafe ScanResult ProcessCore(BgraSource src, ScanSettings settings, OcrScoreProvider? ocr)
    {
        Rectangle? detected = DetectContent(src, settings, out double inkFraction);
        Rectangle content = detected ?? new Rectangle(0, 0, src.Width, src.Height);
        bool plausible = detected is { } d
            && inkFraction <= MaxPlausibleInkFraction
            && d.Width >= src.Width * MinPlausibleContentFraction
            && d.Height >= src.Height * MinPlausibleContentFraction;

        Rectangle output = Rectangle.Intersect(
            Rectangle.Inflate(content, settings.Margin, settings.Margin),
            new Rectangle(0, 0, src.Width, src.Height));

        // The margin is painted white only on sides where the image has room for the full margin.
        // Where the content is closer to the image edge (e.g. pages pre-cropped by the scanner),
        // the existing image border is kept so that print in it is never painted over.
        // A narrow band right around the content is always kept: faint glyph edges there are
        // below the detection limit but still part of the print.
        int safety = Math.Min(settings.Margin, ContentSafetyBand);
        Rectangle kept = Rectangle.FromLTRB(
            content.Left < settings.Margin ? 0 : Math.Max(0, content.Left - safety),
            content.Top < settings.Margin ? 0 : Math.Max(0, content.Top - safety),
            src.Width - content.Right < settings.Margin ? src.Width : Math.Min(src.Width, content.Right + safety),
            src.Height - content.Bottom < settings.Margin ? src.Height : Math.Min(src.Height, content.Bottom + safety));

        int w = output.Width;
        int h = output.Height;
        int bytesPerPixel = settings.Grayscale ? 1 : 4;
        byte[] pixels = new byte[w * h * bytesPerPixel];
        byte[] tone = CreateToneCurve(settings.WhitePoint, settings.Contrast, settings.Brightness);

        for (int y = 0; y < h; y++)
        {
            int srcY = output.Top + y;
            uint* srcRow = src.Row(srcY);
            bool rowInContent = srcY >= kept.Top && srcY < kept.Bottom;

            for (int x = 0; x < w; x++)
            {
                int srcX = output.Left + x;
                int dst = (y * w + x) * bytesPerPixel;
                bool inContent = rowInContent && srcX >= kept.Left && srcX < kept.Right;
                uint c = srcRow[srcX];

                if (settings.Grayscale)
                {
                    pixels[dst] = inContent ? tone[Luminance(c)] : (byte)255;
                }
                else if (inContent)
                {
                    pixels[dst] = tone[c & 0xFF];
                    pixels[dst + 1] = tone[(c >> 8) & 0xFF];
                    pixels[dst + 2] = tone[(c >> 16) & 0xFF];
                    pixels[dst + 3] = 255;
                }
                else
                {
                    pixels[dst] = pixels[dst + 1] = pixels[dst + 2] = pixels[dst + 3] = 255;
                }
            }
        }

        int specks = settings.Despeckle ? RemoveSpecks(pixels, w, h, settings.Grayscale) : 0;

        bool rotate = settings.Rotation switch
        {
            ScanRotation.Rotate180 => true,
            ScanRotation.Auto => detected is { } c && IsUpsideDown(src, c, settings, ocr, pixels, w, h),
            _ => false
        };

        if (rotate)
        {
            // 180° rotation of a tightly packed buffer = reversed pixel order.
            if (settings.Grayscale)
            {
                Array.Reverse(pixels);
            }
            else
            {
                MemoryMarshal.Cast<byte, uint>(pixels).Reverse();
            }
        }

        return new ScanResult(w, h, settings.Grayscale, pixels, detected, rotate, plausible, SpecksRemoved: specks);
    }

    /// <summary>
    /// Estimates the skew of the page in degrees (positive = lines descend to the right) by
    /// projecting ink pixels onto the vertical axis for candidate angles: staff lines and text
    /// lines give the sharpest profile (largest sum of squared row counts) when aligned.
    /// Returns 0 if no angle is clearly better than 0° (e.g. photos, nearly empty pages).
    /// </summary>
    public static unsafe double DetectSkewAngle(BgraSource src, int inkThreshold)
    {
        // Sample ink pixels on a coarse grid; enough for a precise angle and fast. Coordinates are
        // stored in grid units: in pixel units only every step-th bin would be hit at exactly 0°,
        // which inflates the score there and hides the real skew.
        int step = Math.Max(1, Math.Max(src.Width, src.Height) / 1200);
        List<(int X, int Y)> points = [];
        for (int y = 0; y < src.Height; y += step)
        {
            uint* row = src.Row(y);
            for (int x = 0; x < src.Width; x += step)
            {
                if (Luminance(row[x]) < inkThreshold)
                {
                    points.Add((x / step, y / step));
                }
            }
        }

        if (points.Count < 500)
        {
            return 0;
        }

        int gridWidth = src.Width / step + 1;
        int gridHeight = src.Height / step + 1;
        int binOffset = gridWidth;
        int[] bins = new int[gridHeight + 2 * gridWidth + 2];

        double Score(double degrees)
        {
            Array.Clear(bins);
            double radians = degrees * Math.PI / 180;
            double sin = Math.Sin(radians);
            double cos = Math.Cos(radians);
            foreach ((int x, int y) in points)
            {
                bins[(int)(y * cos - x * sin) + binOffset]++;
            }

            double sum = 0;
            foreach (int count in bins)
            {
                sum += (double)count * count;
            }

            return sum;
        }

        double best = 0;
        double bestScore = Score(0);
        double zeroScore = bestScore;
        for (double a = -MaxDeskewAngle; a <= MaxDeskewAngle; a += 0.25)
        {
            double s = Score(a);
            if (s > bestScore)
            {
                (best, bestScore) = (a, s);
            }
        }

        double coarse = best;
        for (double a = coarse - 0.25; a <= coarse + 0.25; a += 0.02)
        {
            double s = Score(a);
            if (s > bestScore)
            {
                (best, bestScore) = (a, s);
            }
        }

        return bestScore >= zeroScore * (1 + MinSkewScoreGain) ? Math.Round(best, 2) : 0;
    }

    /// <summary>
    /// Rotates the page around its center so that lines with the given skew become horizontal
    /// (bilinear interpolation, uncovered corners are white). Same size as the source.
    /// </summary>
    public static unsafe uint[] Rotate(BgraSource src, double degrees)
    {
        int width = src.Width;
        int height = src.Height;
        uint[] result = new uint[width * height];
        double radians = degrees * Math.PI / 180;
        double sin = Math.Sin(radians);
        double cos = Math.Cos(radians);
        double cx = (width - 1) / 2.0;
        double cy = (height - 1) / 2.0;

        for (int v = 0; v < height; v++)
        {
            double dy = v - cy;
            for (int u = 0; u < width; u++)
            {
                double dx = u - cx;

                // Source position: moving along an output row follows the skewed line in the source.
                double x = cx + dx * cos - dy * sin;
                double y = cy + dx * sin + dy * cos;
                int x0 = (int)Math.Floor(x);
                int y0 = (int)Math.Floor(y);
                if (x0 < 0 || y0 < 0 || x0 >= width - 1 || y0 >= height - 1)
                {
                    result[v * width + u] = 0xFFFFFFFF;
                    continue;
                }

                double fx = x - x0;
                double fy = y - y0;
                uint* r0 = src.Row(y0) + x0;
                uint* r1 = src.Row(y0 + 1) + x0;
                result[v * width + u] = Bilinear(r0[0], r0[1], r1[0], r1[1], fx, fy);
            }
        }

        return result;
    }

    private static uint Bilinear(uint c00, uint c10, uint c01, uint c11, double fx, double fy)
    {
        uint value = 0xFF000000;
        for (int shift = 0; shift < 24; shift += 8)
        {
            double top = ((c00 >> shift) & 0xFF) * (1 - fx) + ((c10 >> shift) & 0xFF) * fx;
            double bottom = ((c01 >> shift) & 0xFF) * (1 - fx) + ((c11 >> shift) & 0xFF) * fx;
            value |= (uint)Math.Clamp((int)Math.Round(top * (1 - fy) + bottom * fy), 0, 255) << shift;
        }

        return value;
    }

    /// <summary>
    /// Paints small isolated dark spots white: connected groups (8-neighbourhood) of visibly dark
    /// pixels whose size is below the speck limit. Notation dots (staccato, augmentation dots)
    /// and punctuation are several times larger and stay. Returns the number of removed specks.
    /// </summary>
    /// <param name="pixels">Output pixels (8-bit gray or BGRA32), modified in place.</param>
    public static int RemoveSpecks(byte[] pixels, int width, int height, bool grayscale)
    {
        int count = width * height;
        bool[] dark = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int l = grayscale
                ? pixels[i]
                : (pixels[i * 4 + 2] * 77 + pixels[i * 4 + 1] * 150 + pixels[i * 4] * 29) >> 8;
            dark[i] = l < SpeckDarkness;
        }

        // Speck limit scales with the resolution (reference: A4 at 300 dpi ≈ 3500 px long side).
        double scale = Math.Max(width, height) / 3500.0;
        int maxArea = Math.Max(2, (int)Math.Round(MaxSpeckArea * scale * scale));

        bool[] visited = new bool[count];
        int[] stack = new int[count];
        List<int> component = [];
        int removed = 0;

        for (int start = 0; start < count; start++)
        {
            if (!dark[start] || visited[start])
            {
                continue;
            }

            // Flood fill the whole group (needed to mark large groups as visited too); pixel
            // positions are only remembered while the group could still be a speck.
            component.Clear();
            int size = 0;
            int top = 0;
            stack[top++] = start;
            visited[start] = true;
            while (top > 0)
            {
                int p = stack[--top];
                if (++size <= maxArea)
                {
                    component.Add(p);
                }

                int px = p % width;
                int py = p / width;
                for (int ny = Math.Max(0, py - 1); ny <= Math.Min(height - 1, py + 1); ny++)
                {
                    for (int nx = Math.Max(0, px - 1); nx <= Math.Min(width - 1, px + 1); nx++)
                    {
                        int n = ny * width + nx;
                        if (dark[n] && !visited[n])
                        {
                            visited[n] = true;
                            stack[top++] = n;
                        }
                    }
                }
            }

            if (size > maxArea)
            {
                continue;
            }

            foreach (int p in component)
            {
                if (grayscale)
                {
                    pixels[p] = 255;
                }
                else
                {
                    pixels[p * 4] = pixels[p * 4 + 1] = pixels[p * 4 + 2] = 255;
                }
            }

            removed++;
        }

        return removed;
    }

    /// <summary>
    /// Auto orientation: OCR decides first (reliable whenever the page has some printed text);
    /// if OCR is undecided or unavailable, the left/right ink comparison is used. If both are
    /// undecided the page is not rotated.
    /// </summary>
    private static bool IsUpsideDown(BgraSource src, Rectangle content, ScanSettings settings, OcrScoreProvider? ocr, byte[] pixels, int width, int height)
    {
        if (ocr is not null)
        {
            byte[] gray = settings.Grayscale ? pixels : ToGray(pixels);
            if (ocr(gray, width, height) is { } scores && DecideOrientation(scores.Upright, scores.Rotated) is { } decided)
            {
                return decided;
            }
        }

        return GetOrientationScore(src, content, settings.InkThreshold) < -OrientationMinScore;
    }

    private static byte[] ToGray(byte[] bgra)
    {
        byte[] gray = new byte[bgra.Length / 4];
        ReadOnlySpan<uint> colors = MemoryMarshal.Cast<byte, uint>(bgra);
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)Luminance(colors[i]);
        }

        return gray;
    }

    /// <summary>
    /// Lookup table combining all tone adjustments:
    /// 1. Levels: whitePoint and above maps to 255, below is stretched linearly
    ///    (removes grey paper tone and show-through from the back page).
    /// 2. Contrast: spreads values around the midpoint 128 by (100 + contrast) percent.
    /// 3. Brightness: scales the distance from white by (100 - brightness) percent, so pure
    ///    white stays white and only the content gets darker (negative) or lighter (positive).
    /// </summary>
    private static byte[] CreateToneCurve(int whitePoint, int contrast, int brightness)
    {
        whitePoint = Math.Clamp(whitePoint, 1, 255);
        double contrastFactor = (100 + Math.Clamp(contrast, -100, 100)) / 100.0;
        double brightnessFactor = (100 - Math.Clamp(brightness, -100, 100)) / 100.0;

        byte[] lut = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            double leveled = v >= whitePoint ? 255 : v * 255.0 / whitePoint;
            double contrasted = Math.Clamp((leveled - 128) * contrastFactor + 128, 0, 255);
            double brightened = 255 - (255 - contrasted) * brightnessFactor;
            lut[v] = (byte)Math.Clamp((int)Math.Round(brightened), 0, 255);
        }

        return lut;
    }

    // BGRA32: B = bits 0-7, G = 8-15, R = 16-23.
    private static int Luminance(uint c)
        => ((int)((c >> 16) & 0xFF) * 77 + (int)((c >> 8) & 0xFF) * 150 + (int)(c & 0xFF) * 29) >> 8;

    /// <summary>
    /// Finds the first and last content line within [start, end].
    /// Lines touching the edge that are mostly ink (shadows/borders) are skipped first.
    /// After that, empty lines are skipped, and within the outer zone (measured from the
    /// image edge) shadow lines are skipped as well.
    /// </summary>
    private static bool FindBounds(int[] lineInk, int start, int end, int lineLength, int outerZone, out int first, out int last)
    {
        int shadowLimit = (int)(lineLength * EdgeShadowFraction);
        int noiseLimit = Math.Max(2, lineLength / NoiseDivisor);
        int lastIndex = lineInk.Length - 1;

        first = start;
        while (first <= end && lineInk[first] > shadowLimit)
        {
            first++;
        }

        while (first <= end && IsBorderLine(lineInk[first], first < outerZone))
        {
            first++;
        }

        last = end;
        while (last >= first && lineInk[last] > shadowLimit)
        {
            last--;
        }

        while (last >= first && IsBorderLine(lineInk[last], last > lastIndex - outerZone))
        {
            last--;
        }

        return first < last;

        bool IsBorderLine(int ink, bool inOuterZone)
            => ink < noiseLimit || (inOuterZone && ink > shadowLimit);
    }
}
