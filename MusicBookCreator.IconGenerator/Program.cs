// Generates MusicBookCreator\Assets\app.ico. Usage: dotnet run -c Release -- <app.ico> <preview.png>
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the "music PDF" app icon at several sizes and writes a PNG-compressed .ico (+ a preview PNG).
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
List<byte[]> frames = [];
foreach (int size in sizes)
{
    frames.Add(Png(Render(size)));
}

using (FileStream ico = File.Create(args[0]))
using (BinaryWriter w = new(ico))
{
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
        w.Write(frames[i].Length); w.Write(offset);
        offset += frames[i].Length;
    }

    foreach (byte[] frame in frames) w.Write(frame);
}

// Preview: all sizes side by side on light and dark background.
DrawingVisual preview = new();
using (DrawingContext dc = preview.RenderOpen())
{
    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 300));
    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(0, 300, 640, 300));
    double x = 10;
    foreach (int size in sizes)
    {
        BitmapSource bmp = Render(size);
        dc.DrawImage(bmp, new Rect(x, 20, size, size));
        dc.DrawImage(bmp, new Rect(x, 320, size, size));
        x += size + 12;
    }
}
RenderTargetBitmap previewBmp = new(640, 600, 96, 96, PixelFormats.Pbgra32);
previewBmp.Render(preview);
File.WriteAllBytes(args[1], Png(previewBmp));
Console.WriteLine("ok");

static BitmapSource Render(int size)
{
    const double S = 256; // Design grid.
    DrawingVisual visual = new();
    using (DrawingContext dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / S, size / S));

        // Page with folded corner.
        double l = 40, t = 12, r = 216, b = 244, fold = 52;
        StreamGeometry page = new();
        using (StreamGeometryContext g = page.Open())
        {
            g.BeginFigure(new Point(l, t + 10), true, true);
            g.ArcTo(new Point(l + 10, t), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(r - fold, t), true, true);
            g.LineTo(new Point(r, t + fold), true, true);
            g.LineTo(new Point(r, b - 10), true, true);
            g.ArcTo(new Point(r - 10, b), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(l + 10, b), true, true);
            g.ArcTo(new Point(l, b - 10), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, true);
        }
        page.Freeze();
        dc.PushTransform(new TranslateTransform(4, 5));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, page); // Shadow.
        dc.Pop();
        dc.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 160)), size < 32 ? 8 : 5), page);

        StreamGeometry corner = new();
        using (StreamGeometryContext g = corner.Open())
        {
            g.BeginFigure(new Point(r - fold, t), true, true);
            g.LineTo(new Point(r - fold, t + fold - 8), true, true);
            g.ArcTo(new Point(r - fold + 8, t + fold), new Size(8, 8), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(r, t + fold), true, true);
        }
        corner.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(214, 218, 228)), new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 160)), size < 32 ? 8 : 5), corner);

        // Two beamed eighth notes.
        Brush ink = new SolidColorBrush(Color.FromRgb(28, 36, 64));
        void Head(double cx, double cy)
        {
            dc.PushTransform(new RotateTransform(-22, cx, cy));
            dc.DrawEllipse(ink, null, new Point(cx, cy), 22, 15);
            dc.Pop();
        }

        double stem = 11;
        Head(92, 150);
        Head(160, 132);
        dc.DrawRectangle(ink, null, new Rect(92 + 22 - stem, 66, stem, 84));
        dc.DrawRectangle(ink, null, new Rect(160 + 22 - stem, 48, stem, 84));
        StreamGeometry beam = new();
        using (StreamGeometryContext g = beam.Open())
        {
            g.BeginFigure(new Point(92 + 22 - stem, 66), true, true);
            g.LineTo(new Point(182, 48), true, true);
            g.LineTo(new Point(182, 72), true, true);
            g.LineTo(new Point(92 + 22 - stem, 90), true, true);
        }
        beam.Freeze();
        dc.DrawGeometry(ink, null, beam);

        // Red PDF band.
        Rect band = new(22, 176, 212, 56);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(214, 40, 40)), null, band, 8, 8);
        FormattedText text = new("PDF", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal), 50, Brushes.White, 1.0);
        dc.DrawText(text, new Point(band.Left + (band.Width - text.Width) / 2, band.Top + (band.Height - text.Height) / 2));
        dc.Pop();
    }

    RenderTargetBitmap bmp = new(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(visual);
    bmp.Freeze();
    return bmp;
}

static byte[] Png(BitmapSource source)
{
    PngBitmapEncoder encoder = new();
    encoder.Frames.Add(BitmapFrame.Create(source));
    using MemoryStream ms = new();
    encoder.Save(ms);
    return ms.ToArray();
}
