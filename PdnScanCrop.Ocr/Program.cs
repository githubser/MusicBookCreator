using System;
using System.IO;

namespace PdnScanCrop.Ocr;

/// <summary>
/// Reads an 8-bit grayscale page from stdin (int32 width, int32 height, width*height bytes),
/// scores it with <see cref="OcrScorer"/> and writes "uprightScore rotatedScore" to stdout.
/// Writes "-1 -1" if no OCR engine is available.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            using Stream stdin = Console.OpenStandardInput();
            using BinaryReader reader = new(stdin);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            byte[] pixels = ReadExactly(reader, width * height);

            (int Upright, int Rotated)? scores = OcrScorer.Score(pixels, width, height);
            Console.WriteLine(scores is { } s ? $"{s.Upright} {s.Rotated}" : "-1 -1");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = reader.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return buffer;
    }
}
