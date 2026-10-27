using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MusicBookCreator;

public sealed record FileItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed record LogEntry(string FileName, string Status);

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer previewTimer;
    private CancellationTokenSource? batchCancellation;
    private int previewVersion;

    // Decoded page of the selected file, reused while only settings change.
    private (string Path, LoadedImage Image, BitmapSource Original)? previewCache;

    private string sourceFolder;
    private string targetFolder;
    private int rotationIndex;
    private bool grayscale;
    private bool deskew;
    private bool despeckle;
    private int whitePoint;
    private int contrast;
    private int brightness;
    private int inkThreshold;
    private int margin;
    private double ignoreBorderPercent;
    private int jpegQuality;
    private bool createPdf;

    private FileItem? selectedFile;
    private BitmapSource? originalPreview;
    private BitmapSource? resultPreview;
    private string previewStatus = "Select a file from the list";
    private bool isBusy;
    private int progressValue;
    private int progressMaximum = 1;
    private string statusText = "Bereit";

    public MainViewModel()
    {
        // Timer first: applying the settings below already schedules a preview.
        previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        previewTimer.Tick += async (_, _) =>
        {
            previewTimer.Stop();
            await UpdatePreviewAsync();
        };

        AppSettings saved = AppSettings.Load();
        sourceFolder = saved.SourceFolder;
        targetFolder = saved.TargetFolder;
        createPdf = saved.CreatePdf;
        ApplySettings(saved.Scan);

        BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
        BrowseTargetCommand = new RelayCommand(BrowseTarget, () => !IsBusy);
        StartCommand = new RelayCommand(async () => await StartAsync(), () => !IsBusy);
        CancelCommand = new RelayCommand(() => batchCancellation?.Cancel(), () => IsBusy);
        ResetSettingsCommand = new RelayCommand(() => ApplySettings(new ScanSettings()), () => !IsBusy);

        LoadFiles();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FileItem> Files { get; } = [];

    public ObservableCollection<LogEntry> Log { get; } = [];

    public ICommand BrowseSourceCommand { get; }

    public ICommand BrowseTargetCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ResetSettingsCommand { get; }

    public string SourceFolder
    {
        get => sourceFolder;
        set
        {
            if (Set(ref sourceFolder, value))
            {
                LoadFiles();
            }
        }
    }

    public string TargetFolder
    {
        get => targetFolder;
        set => Set(ref targetFolder, value);
    }

    /// <summary>Index into the rotation combo box; same order as <see cref="ScanRotation"/>.</summary>
    public int RotationIndex
    {
        get => rotationIndex;
        set => SetSetting(ref rotationIndex, value);
    }

    public bool Grayscale
    {
        get => grayscale;
        set => SetSetting(ref grayscale, value);
    }

    public bool Deskew
    {
        get => deskew;
        set => SetSetting(ref deskew, value);
    }

    public bool Despeckle
    {
        get => despeckle;
        set => SetSetting(ref despeckle, value);
    }

    public int WhitePoint
    {
        get => whitePoint;
        set => SetSetting(ref whitePoint, value);
    }

    public int Contrast
    {
        get => contrast;
        set => SetSetting(ref contrast, value);
    }

    public int Brightness
    {
        get => brightness;
        set => SetSetting(ref brightness, value);
    }

    public int InkThreshold
    {
        get => inkThreshold;
        set => SetSetting(ref inkThreshold, value);
    }

    public int Margin
    {
        get => margin;
        set => SetSetting(ref margin, value);
    }

    public double IgnoreBorderPercent
    {
        get => ignoreBorderPercent;
        set => SetSetting(ref ignoreBorderPercent, Math.Round(value, 1));
    }

    public int JpegQuality
    {
        get => jpegQuality;
        set => Set(ref jpegQuality, value); // Does not change the preview.
    }

    /// <summary>After processing, combine all pages into "&lt;source folder&gt;.pdf" in the target folder.</summary>
    public bool CreatePdf
    {
        get => createPdf;
        set => Set(ref createPdf, value);
    }

    public FileItem? SelectedFile
    {
        get => selectedFile;
        set
        {
            if (Set(ref selectedFile, value))
            {
                SchedulePreview();
            }
        }
    }

    public BitmapSource? OriginalPreview
    {
        get => originalPreview;
        private set => Set(ref originalPreview, value);
    }

    public BitmapSource? ResultPreview
    {
        get => resultPreview;
        private set => Set(ref resultPreview, value);
    }

    public string PreviewStatus
    {
        get => previewStatus;
        private set => Set(ref previewStatus, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public int ProgressValue
    {
        get => progressValue;
        private set => Set(ref progressValue, value);
    }

    public int ProgressMaximum
    {
        get => progressMaximum;
        private set => Set(ref progressMaximum, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    public ScanSettings CurrentSettings() => new()
    {
        Rotation = (ScanRotation)RotationIndex,
        Grayscale = Grayscale,
        Deskew = Deskew,
        Despeckle = Despeckle,
        WhitePoint = WhitePoint,
        Contrast = Contrast,
        Brightness = Brightness,
        InkThreshold = InkThreshold,
        Margin = Margin,
        IgnoreBorderPercent = IgnoreBorderPercent,
        JpegQuality = JpegQuality
    };

    public void SaveSettings()
        => new AppSettings { SourceFolder = SourceFolder, TargetFolder = TargetFolder, CreatePdf = CreatePdf, Scan = CurrentSettings() }.Save();

    private void ApplySettings(ScanSettings settings)
    {
        RotationIndex = (int)settings.Rotation;
        Grayscale = settings.Grayscale;
        Deskew = settings.Deskew;
        Despeckle = settings.Despeckle;
        WhitePoint = settings.WhitePoint;
        Contrast = settings.Contrast;
        Brightness = settings.Brightness;
        InkThreshold = settings.InkThreshold;
        Margin = settings.Margin;
        IgnoreBorderPercent = settings.IgnoreBorderPercent;
        JpegQuality = settings.JpegQuality;
    }

    private void LoadFiles()
    {
        Files.Clear();
        previewCache = null;
        if (!Directory.Exists(SourceFolder))
        {
            StatusText = "Choose a source folder";
            return;
        }

        foreach (string file in BatchProcessor.GetFiles(SourceFolder))
        {
            Files.Add(new FileItem(file));
        }

        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            TargetFolder = Path.Combine(SourceFolder, "cleaned");
        }

        StatusText = $"{Files.Count} images in the source folder";
        SelectedFile = Files.FirstOrDefault();
    }

    private void BrowseSource()
    {
        if (PickFolder("Choose source folder", SourceFolder) is { } folder)
        {
            SourceFolder = folder;
        }
    }

    private void BrowseTarget()
    {
        if (PickFolder("Choose target folder", TargetFolder) is { } folder)
        {
            TargetFolder = folder;
        }
    }

    private static string? PickFolder(string title, string initial)
    {
        OpenFolderDialog dialog = new() { Title = title };
        if (Directory.Exists(initial))
        {
            dialog.InitialDirectory = initial;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void SchedulePreview()
    {
        previewTimer.Stop();
        previewTimer.Start();
    }

    private async Task UpdatePreviewAsync()
    {
        int version = ++previewVersion;
        if (SelectedFile is not { } file)
        {
            OriginalPreview = ResultPreview = null;
            PreviewStatus = "Select a file from the list";
            return;
        }

        ScanSettings settings = CurrentSettings();
        var cached = previewCache is { } c && c.Path == file.Path ? c : default((string Path, LoadedImage Image, BitmapSource Original)?);
        PreviewStatus = "Calculating preview …";

        try
        {
            (LoadedImage image, BitmapSource original, ScanResult result) = await Task.Run(() =>
            {
                LoadedImage image = cached?.Image ?? ImageIo.Load(file.Path);
                BitmapSource original = cached?.Original ?? ImageIo.ToBitmapSource(image);
                return (image, original, ImageIo.Process(image, settings));
            });

            if (version != previewVersion)
            {
                return; // A newer preview request is already running.
            }

            previewCache = (file.Path, image, original);
            OriginalPreview = original;
            // Implausible pages are copied unchanged, so show exactly that.
            ResultPreview = result.IsPlausible ? ImageIo.ToBitmapSource(result) : original;
            PreviewStatus = result.IsPlausible
                ? $"{result.Width} × {result.Height} px{Describe(result)}"
                : "No page content detected – will be copied unchanged";
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            if (version == previewVersion)
            {
                OriginalPreview = ResultPreview = null;
                PreviewStatus = $"Error: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Changes beyond cropping/cleanup, e.g. ", rotated 180°, deskewed by 1.2°, 5 specks removed".
    /// </summary>
    private static string Describe(ScanResult result)
    {
        List<string> parts = [];
        if (result.Rotated)
        {
            parts.Add("rotated 180°");
        }

        if (result.SkewAngle != 0)
        {
            parts.Add($"deskewed by {Math.Abs(result.SkewAngle):0.0#}°");
        }

        if (result.SpecksRemoved > 0)
        {
            parts.Add($"{result.SpecksRemoved} specks removed");
        }

        return parts.Count == 0 ? string.Empty : ", " + string.Join(", ", parts);
    }

    private async Task StartAsync()
    {
        if (!ValidateFolders(out string source, out string target))
        {
            return;
        }

        IReadOnlyList<string> files = BatchProcessor.GetFiles(source);
        if (files.Count == 0)
        {
            MessageBox.Show("No images were found in the source folder.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
            && MessageBox.Show(
                "The target folder already contains files. Files with the same name will be overwritten.\n\nContinue?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SaveSettings();
        ScanSettings settings = CurrentSettings();
        Log.Clear();
        ProgressValue = 0;
        ProgressMaximum = files.Count;
        IsBusy = true;
        batchCancellation = new CancellationTokenSource();

        int processed = 0, rotated = 0, copied = 0, failed = 0;
        Dictionary<string, string> outputs = new(StringComparer.OrdinalIgnoreCase);
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Created on the UI thread, so reports arrive on the UI thread (in order, before the
        // continuation after RunAsync, so all counters and outputs are complete afterwards).
        Progress<BatchItemResult> progress = new(item =>
        {
            ProgressValue++;
            if (item.OutputPath is { } outputPath)
            {
                outputs[item.FileName] = outputPath;
            }

            switch (item.Status)
            {
                case BatchItemStatus.Processed:
                    processed++;
                    rotated += item.Rotated ? 1 : 0;
                    break;
                case BatchItemStatus.Copied:
                    copied++;
                    break;
                default:
                    failed++;
                    break;
            }

            string status = item.Status switch
            {
                BatchItemStatus.Processed => "✔ processed" + (item.Rotated ? ", rotated" : string.Empty) + (item.Message is { } details ? ", " + details : string.Empty),
                BatchItemStatus.Copied => $"⚠ {item.Message}",
                _ => $"✘ Error: {item.Message}"
            };
            Log.Insert(0, new LogEntry(item.FileName, status));
            StatusText = $"{ProgressValue} / {ProgressMaximum} …";
        });

        string outcome = "Done";
        string pdfInfo = string.Empty;
        try
        {
            await BatchProcessor.RunAsync(files, target, settings, progress, batchCancellation.Token);

            if (CreatePdf)
            {
                // Page order = source file order; failed files have no output and are left out.
                List<string> pages = files
                    .Select(file => outputs.GetValueOrDefault(Path.GetFileName(file)))
                    .OfType<string>()
                    .ToList();
                pdfInfo = await CreatePdfAsync(source, target, pages, batchCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            outcome = "Cancelled";
        }
        finally
        {
            IsBusy = false;
            batchCancellation.Dispose();
            batchCancellation = null;
        }

        StatusText = $"{outcome} after {stopwatch.Elapsed:mm\\:ss}: {processed} processed ({rotated} rotated), "
            + $"{copied} copied unchanged, {failed} errors{pdfInfo}";
    }

    /// <summary>
    /// Creates "&lt;source folder name&gt;.pdf" in the target folder. Returns a summary for the status line.
    /// </summary>
    private async Task<string> CreatePdfAsync(string source, string target, IReadOnlyList<string> pages, CancellationToken cancellationToken)
    {
        string pdfName = PdfFileName(source);
        if (pages.Count == 0)
        {
            Log.Insert(0, new LogEntry(pdfName, "✘ No PDF created: no pages"));
            return ", no PDF";
        }

        string pdfPath = Path.Combine(target, pdfName);
        StatusText = $"Creating PDF ({pages.Count} pages) …";
        try
        {
            await Task.Run(() => PdfBookWriter.Write(pdfPath, pages, cancellationToken), cancellationToken);
            Log.Insert(0, new LogEntry(pdfName, $"📄 PDF created, {pages.Count} pages"));
            return $", PDF: {pdfName}";
        }
        catch (OperationCanceledException)
        {
            TryDelete(pdfPath); // No half-written PDFs.
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException)
        {
            TryDelete(pdfPath);
            Log.Insert(0, new LogEntry(pdfName, $"✘ PDF error: {ex.Message}"));
            return ", PDF failed";
        }
    }

    private static string PdfFileName(string sourceFolder)
    {
        string name = new DirectoryInfo(sourceFolder).Name;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        // A drive root like "D:\" has no usable folder name.
        return (string.IsNullOrWhiteSpace(name) || name.Trim('_').Length == 0 ? "Scans" : name) + ".pdf";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more we can do; the error is already reported.
        }
    }

    private bool ValidateFolders(out string source, out string target)
    {
        source = target = string.Empty;
        if (!Directory.Exists(SourceFolder))
        {
            MessageBox.Show("Please choose an existing source folder.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            MessageBox.Show("Please choose a target folder.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SourceFolder));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(TargetFolder));
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Source and target folder must be different – the originals are never overwritten.",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void SetSetting<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Set(ref field, value, name))
        {
            SchedulePreview();
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}
