using System.Diagnostics;
using System.IO;

namespace PdnScanCrop;

/// <summary>
/// OCR scores for Auto rotation (see <see cref="OcrScoreProvider"/>) inside Paint.NET.
/// </summary>
/// <remarks>
/// OCR runs in the helper PdnScanCrop.Ocr.exe (.NET Framework 4.8, built-in WinRT interop),
/// because Paint.NET only ships a trimmed WinRT projection without Windows.Media.Ocr.
/// The helper must be located next to this assembly.
/// </remarks>
public static class OcrOrientationDetector
{
    public const string HelperFileName = "PdnScanCrop.Ocr.exe";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Returns the OCR scores, or null if the helper is missing, failed or no OCR engine is available.
    /// </summary>
    /// <param name="gray">8-bit luminance pixels, row-major without padding.</param>
    public static (int Upright, int Rotated)? GetScores(byte[] gray, int width, int height)
        => TryRunHelper(gray, width, height, out int upright, out int rotated) && upright >= 0
            ? (upright, rotated)
            : null;

    private static bool TryRunHelper(byte[] gray, int width, int height, out int uprightScore, out int rotatedScore)
    {
        uprightScore = rotatedScore = -1;

        string? helperPath = FindHelper();
        if (helperPath is null)
        {
            return false;
        }

        ProcessStartInfo startInfo = new(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using Process process = Process.Start(startInfo)!;

            // Read stdout/stderr asynchronously so the helper can never block on a full pipe.
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            // Protocol: int32 width, int32 height, width * height bytes of 8-bit grayscale.
            using (BinaryWriter writer = new(process.StandardInput.BaseStream))
            {
                writer.Write(width);
                writer.Write(height);
                writer.Write(gray, 0, width * height);
            }

            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                return false;
            }

            string[] parts = output.Result.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return process.ExitCode == 0
                && parts.Length == 2
                && int.TryParse(parts[0], out uprightScore)
                && int.TryParse(parts[1], out rotatedScore);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // OCR is only a helper for Auto rotation; saving must never fail because of it.
            return false;
        }
    }

    /// <summary>
    /// Looks next to the plugin DLL first. Assembly.Location is empty if the host loads the
    /// plugin from memory, so Paint.NET's FileTypes folders (classic install and Store version)
    /// are checked as well.
    /// </summary>
    private static string? FindHelper()
    {
        string[] directories =
        [
            Path.GetDirectoryName(typeof(OcrOrientationDetector).Assembly.Location) ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "FileTypes"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "paint.net App Files", "FileTypes")
        ];

        return directories
            .Where(directory => directory.Length > 0)
            .Select(directory => Path.Combine(directory, HelperFileName))
            .FirstOrDefault(File.Exists);
    }
}
