using System.IO;
using System.Text.Json;
using PdnScanCrop;

namespace MusicBookCreator;

/// <summary>
/// Last used folders and processing settings, stored in %APPDATA%\Music Book Creator\settings.json.
/// </summary>
public sealed record AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SourceFolder { get; init; } = string.Empty;

    public string TargetFolder { get; init; } = string.Empty;

    public bool CreatePdf { get; init; } = true;

    public ScanSettings Scan { get; init; } = new();

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings are not worth bothering the user with.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience only.
        }
    }
}
