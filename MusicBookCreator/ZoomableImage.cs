using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MusicBookCreator;

/// <summary>
/// Image view with mouse wheel zoom (around the cursor), drag to pan and double click to reset.
/// The zoom is kept when only <see cref="Source"/> changes (e.g. new preview after a settings change)
/// and reset when <see cref="ZoomKey"/> changes (e.g. another file was selected).
/// </summary>
public sealed class ZoomableImage : Border
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(ZoomableImage),
        new PropertyMetadata(null, (d, e) => ((ZoomableImage)d).image.Source = (ImageSource?)e.NewValue));

    public static readonly DependencyProperty ZoomKeyProperty = DependencyProperty.Register(
        nameof(ZoomKey), typeof(object), typeof(ZoomableImage),
        new PropertyMetadata(null, (d, _) => ((ZoomableImage)d).ResetZoom()));

    private const double ZoomStep = 1.25;
    private const double MaxZoom = 30;

    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly MatrixTransform transform = new();
    private Point dragStart;
    private Matrix dragStartMatrix;

    public ZoomableImage()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        // The transform sits on a panel that fills the whole view, so mouse positions relative to
        // this control are directly usable as transform coordinates.
        Grid canvas = new() { Background = Brushes.Transparent, RenderTransform = transform };
        canvas.Children.Add(image);
        Child = canvas;
        ToolTip = "Mausrad: Zoom · Ziehen: verschieben · Doppelklick: zurücksetzen";
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public object? ZoomKey
    {
        get => GetValue(ZoomKeyProperty);
        set => SetValue(ZoomKeyProperty, value);
    }

    public void ResetZoom() => transform.Matrix = Matrix.Identity;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Matrix matrix = transform.Matrix;
        double factor = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
        double newZoom = matrix.M11 * factor;

        if (newZoom <= 1)
        {
            ResetZoom();
        }
        else if (newZoom <= MaxZoom)
        {
            Point position = e.GetPosition(this);
            matrix.ScaleAt(factor, factor, position.X, position.Y);
            transform.Matrix = matrix;
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            ResetZoom();
            return;
        }

        dragStart = e.GetPosition(this);
        dragStartMatrix = transform.Matrix;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured)
        {
            return;
        }

        Vector delta = e.GetPosition(this) - dragStart;
        Matrix matrix = dragStartMatrix;
        matrix.Translate(delta.X, delta.Y);
        transform.Matrix = matrix;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }
}
