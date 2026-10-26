using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MusicBookCreator;

/// <summary>
/// Scores a page with the built-in Windows OCR upright and rotated by 180°: the score is the
/// number of letters in plausible words. Upside-down text produces mostly fragments and symbol
/// garbage, so the higher score marks the correct orientation.
/// </summary>
public static class OcrScorer
{
    // Words of at least 3 letters (optionally followed by punctuation), or numbers with 1-4 digits.
    private static readonly Regex PlausibleWord = new(@"^(\p{L}{3,}[.,:;!?]?|\d{1,4}[.,]?)$", RegexOptions.Compiled);

    /// <summary>
    /// Returns the scores, or null if no OCR engine is available.
    /// </summary>
    /// <param name="gray">8-bit luminance pixels, row-major without padding. Not modified.</param>
    public static (int Upright, int Rotated)? Score(byte[] gray, int width, int height)
    {
        OcrEngine? engine = CreateEngine();
        if (engine is null)
        {
            return null;
        }

        // OCR has a size limit; scans at 600 dpi can exceed it.
        int scale = 1;
        while (Math.Max(width, height) / scale > OcrEngine.MaxImageDimension)
        {
            scale++;
        }

        byte[] pixels = scale > 1 ? Downscale(gray, width, height, scale, out width, out height) : (byte[])gray.Clone();

        int upright = Score(engine, pixels, width, height);

        // A 180° rotation of a tightly packed buffer is simply the reversed byte order.
        Array.Reverse(pixels);
        int rotated = Score(engine, pixels, width, height);

        return (upright, rotated);
    }

    private static OcrEngine? CreateEngine()
    {
        // Prefer the user's languages (German on a German Windows), fall back to English.
        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null && OcrEngine.IsLanguageSupported(new Language("en-US")))
        {
            engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        }

        return engine;
    }

    private static int Score(OcrEngine engine, byte[] pixels, int width, int height)
    {
        using SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Gray8, width, height);

        // Callers run on worker threads without a synchronization context, so blocking is safe.
        OcrResult result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();

        int score = 0;
        foreach (OcrLine line in result.Lines)
        {
            foreach (OcrWord word in line.Words)
            {
                if (PlausibleWord.IsMatch(word.Text))
                {
                    score += word.Text.Length;
                }
            }
        }

        return score;
    }

    private static byte[] Downscale(byte[] gray, int width, int height, int scale, out int newWidth, out int newHeight)
    {
        newWidth = width / scale;
        newHeight = height / scale;
        byte[] result = new byte[newWidth * newHeight];
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                result[y * newWidth + x] = gray[y * scale * width + x * scale];
            }
        }

        return result;
    }
}
