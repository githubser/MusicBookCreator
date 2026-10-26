using System.IO;
using PaintDotNet;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace PdnScanCrop;

public sealed class ScanCropFileTypeFactory : IFileTypeFactory
{
    public FileType[] GetFileTypeInstances() => [new ScanCropFileType()];
}

/// <summary>
/// Save-only JPEG file type: crops the scanned page to its content, cleans the background
/// and optionally rotates it before writing. The open document itself stays unchanged.
/// </summary>
public sealed class ScanCropFileType : PropertyBasedFileType
{
    private enum PropertyNames
    {
        Rotation,
        Grayscale,
        Deskew,
        Despeckle,
        WhitePoint,
        Contrast,
        Brightness,
        InkThreshold,
        Margin,
        IgnoreBorderPercent,
        Quality
    }

    public ScanCropFileType()
        : base("JPEG – Scan zugeschnitten", new FileTypeOptions
        {
            LoadExtensions = [],
            SaveExtensions = [".jpg", ".jpeg"],
            SupportsLayers = false
        })
    {
    }

    public override PropertyCollection OnCreateSavePropertyCollection()
    {
        ScanSettings defaults = new();
        List<Property> props =
        [
            StaticListChoiceProperty.CreateForEnum(PropertyNames.Rotation, defaults.Rotation, false),
            new BooleanProperty(PropertyNames.Grayscale, defaults.Grayscale),
            new BooleanProperty(PropertyNames.Deskew, defaults.Deskew),
            new BooleanProperty(PropertyNames.Despeckle, defaults.Despeckle),
            new Int32Property(PropertyNames.WhitePoint, defaults.WhitePoint, 1, 255),
            new Int32Property(PropertyNames.Contrast, defaults.Contrast, -100, 100),
            new Int32Property(PropertyNames.Brightness, defaults.Brightness, -100, 100),
            new Int32Property(PropertyNames.InkThreshold, defaults.InkThreshold, 1, 254),
            new Int32Property(PropertyNames.Margin, defaults.Margin, 0, 1000),
            new DoubleProperty(PropertyNames.IgnoreBorderPercent, defaults.IgnoreBorderPercent, 0, 20),
            new Int32Property(PropertyNames.Quality, defaults.JpegQuality, 1, 100)
        ];
        return new PropertyCollection(props);
    }

    public override ControlInfo OnCreateSaveConfigUI(PropertyCollection props)
    {
        ControlInfo configUI = CreateDefaultSaveConfigUI(props);

        configUI.SetPropertyControlValue(PropertyNames.Rotation, ControlInfoPropertyNames.DisplayName, "Drehen");
        PropertyControlInfo rotationControl = configUI.FindControlForPropertyName(PropertyNames.Rotation)!;
        rotationControl.SetValueDisplayName(ScanRotation.Auto, "Auto");
        rotationControl.SetValueDisplayName(ScanRotation.None, "Nicht drehen");
        rotationControl.SetValueDisplayName(ScanRotation.Rotate180, "180° drehen");

        configUI.SetPropertyControlValue(PropertyNames.Grayscale, ControlInfoPropertyNames.DisplayName, string.Empty);
        configUI.SetPropertyControlValue(PropertyNames.Grayscale, ControlInfoPropertyNames.Description, "Graustufen");

        configUI.SetPropertyControlValue(PropertyNames.Deskew, ControlInfoPropertyNames.DisplayName, string.Empty);
        configUI.SetPropertyControlValue(PropertyNames.Deskew, ControlInfoPropertyNames.Description, "Begradigen (bis ±5°)");

        configUI.SetPropertyControlValue(PropertyNames.Despeckle, ControlInfoPropertyNames.DisplayName, string.Empty);
        configUI.SetPropertyControlValue(PropertyNames.Despeckle, ControlInfoPropertyNames.Description, "Flecken entfernen");

        configUI.SetPropertyControlValue(PropertyNames.WhitePoint, ControlInfoPropertyNames.DisplayName, "Hintergrund weiß ab Helligkeit (255 = aus)");
        configUI.SetPropertyControlValue(PropertyNames.Contrast, ControlInfoPropertyNames.DisplayName, "Kontrast (%)");
        configUI.SetPropertyControlValue(PropertyNames.Brightness, ControlInfoPropertyNames.DisplayName, "Helligkeit (%)");
        configUI.SetPropertyControlValue(PropertyNames.InkThreshold, ControlInfoPropertyNames.DisplayName, "Inhalt-Erkennung: dunkler als");
        configUI.SetPropertyControlValue(PropertyNames.Margin, ControlInfoPropertyNames.DisplayName, "Rand um Inhalt (Pixel)");
        configUI.SetPropertyControlValue(PropertyNames.IgnoreBorderPercent, ControlInfoPropertyNames.DisplayName, "Bildrand ignorieren (%)");
        configUI.SetPropertyControlValue(PropertyNames.Quality, ControlInfoPropertyNames.DisplayName, "JPEG-Qualität");

        return configUI;
    }

    protected override Document OnLoad(Stream input)
        => throw new NotSupportedException();

    protected override unsafe void OnSaveT(Document input, Stream output, PropertyBasedSaveConfigToken token, Surface scratchSurface, ProgressEventHandler progressCallback)
    {
        ScanSettings settings = new()
        {
            Rotation = (ScanRotation)token.GetProperty<StaticListChoiceProperty>(PropertyNames.Rotation)!.Value,
            Grayscale = token.GetProperty<BooleanProperty>(PropertyNames.Grayscale)!.Value,
            Deskew = token.GetProperty<BooleanProperty>(PropertyNames.Deskew)!.Value,
            Despeckle = token.GetProperty<BooleanProperty>(PropertyNames.Despeckle)!.Value,
            WhitePoint = token.GetProperty<Int32Property>(PropertyNames.WhitePoint)!.Value,
            Contrast = token.GetProperty<Int32Property>(PropertyNames.Contrast)!.Value,
            Brightness = token.GetProperty<Int32Property>(PropertyNames.Brightness)!.Value,
            InkThreshold = token.GetProperty<Int32Property>(PropertyNames.InkThreshold)!.Value,
            Margin = token.GetProperty<Int32Property>(PropertyNames.Margin)!.Value,
            IgnoreBorderPercent = token.GetProperty<DoubleProperty>(PropertyNames.IgnoreBorderPercent)!.Value,
            JpegQuality = token.GetProperty<Int32Property>(PropertyNames.Quality)!.Value
        };

        input.Flatten(scratchSurface);

        BgraSource source = new((byte*)scratchSurface.Scan0.VoidStar, scratchSurface.Stride, scratchSurface.Width, scratchSurface.Height);
        ScanResult result = ScanProcessor.Process(source, settings, OcrOrientationDetector.GetScores);

        (double dpiX, double dpiY) = GetDpi(input);
        ScanJpegWriter.Save(result, output, dpiX, dpiY, settings.JpegQuality);
    }

    // Keep the scan resolution so the page prints at its original size.
    private static (double X, double Y) GetDpi(Document document)
        => document.DpuUnit switch
        {
            MeasurementUnit.Inch => (document.DpuX, document.DpuY),
            MeasurementUnit.Centimeter => (document.DpuX * 2.54, document.DpuY * 2.54),
            _ => (96.0, 96.0)
        };
}
