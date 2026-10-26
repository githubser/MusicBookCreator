using System.IO;

namespace MusicBookCreator;

public enum BatchItemStatus
{
    Processed,
    Copied,
    Failed
}

public sealed record BatchItemResult(string FileName, BatchItemStatus Status, bool Rotated, string? Message, string? OutputPath = null);

/// <summary>
/// Processes all supported images of a folder (no subfolders) into a separate target folder.
/// Originals are never modified. Pages that do not look like printed pages on light paper
/// are copied unchanged.
/// </summary>
public static class BatchProcessor
{
    // Each page needs several full-size buffers (600 dpi A4 ≈ 140 MB as BGRA), so parallelism is limited.
    private static readonly int MaxParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    public static IReadOnlyList<string> GetFiles(string sourceFolder)
        => Directory.EnumerateFiles(sourceFolder)
            .Where(ImageIo.IsSupported)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static async Task RunAsync(
        IReadOnlyList<string> files,
        string targetFolder,
        ScanSettings settings,
        IProgress<BatchItemResult> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetFolder);

        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = MaxParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(files, options, (file, _) =>
        {
            progress.Report(ProcessFile(file, targetFolder, settings));
            return ValueTask.CompletedTask;
        });
    }

    public static BatchItemResult ProcessFile(string file, string targetFolder, ScanSettings settings)
    {
        string fileName = Path.GetFileName(file);
        try
        {
            LoadedImage image = ImageIo.Load(file);
            ScanResult result = ImageIo.Process(image, settings);

            if (!result.IsPlausible)
            {
                string copy = Path.Combine(targetFolder, fileName);
                File.Copy(file, copy, overwrite: true);
                return new BatchItemResult(fileName, BatchItemStatus.Copied, false, "Kein Seiteninhalt erkannt – unverändert kopiert", copy);
            }

            string target = Path.Combine(targetFolder, Path.GetFileNameWithoutExtension(file) + ".jpg");
            ImageIo.SaveJpeg(result, target, image, settings.JpegQuality);
            List<string> details = [];
            if (result.SkewAngle != 0)
            {
                details.Add($"begradigt um {Math.Abs(result.SkewAngle):0.0#}°");
            }

            if (result.SpecksRemoved > 0)
            {
                details.Add($"{result.SpecksRemoved} Flecken entfernt");
            }

            return new BatchItemResult(fileName, BatchItemStatus.Processed, result.Rotated, details.Count > 0 ? string.Join(", ", details) : null, target);
        }
        // FormatException covers WPF's FileFormatException for corrupt images.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException
                                       or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            return new BatchItemResult(fileName, BatchItemStatus.Failed, false, ex.Message);
        }
    }
}
