using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// Trace to strokes (Q269): which pixels count as ink, where to read it from,
/// and how short a mark may be — with the drawing at the playhead traced in
/// front of the artist as they turn them.
/// </summary>
/// <remarks>
/// The preview is traced off the UI thread and only the latest one is shown, so
/// dragging a slider over a 1080p drawing (about 0.4 s a trace) stays smooth
/// and lands on what the slider says when it stops. Closing returns the options,
/// or null when cancelled.
/// </remarks>
public partial class TraceDialog : Window
{
    private SKBitmap? _sample;
    private int _generation;

    public TraceDialog()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ComboBox>("SourceBox")!.SelectedIndex = 0;
        Describe("this layer", 0, null);
    }

    /// <summary>Name what is being traced, and hand over the drawing to preview it on.</summary>
    public TraceDialog Describe(string layerName, int drawings, SKBitmap? sample)
    {
        _sample = sample;
        var which = drawings == 1 ? "1 drawing" : $"{drawings} drawings";
        this.FindControl<TextBlock>("Explanation")!.Text =
            $"“{layerName}” holds pixels in {which}. Each line becomes a brush stroke down its middle on a new "
            + "layer above, and this layer is hidden rather than deleted. The exact edge, the order the lines "
            + "were drawn in and the pen's pressure are not in the pixels, so they are guessed.";
        Refresh();
        return this;
    }

    /// <summary>What the controls say now.</summary>
    public LineTraceOptions Options => new()
    {
        Threshold = this.FindControl<Slider>("ThresholdSlider")!.Value / 100.0,
        MinimumLength = this.FindControl<Slider>("DetailSlider")!.Value,
        Source = this.FindControl<ComboBox>("SourceBox")!.SelectedIndex switch
        {
            1 => InkSource.Transparency,
            2 => InkSource.Darkness,
            _ => InkSource.Auto,
        },
    };

    private void OnSourceChanged(object? sender, SelectionChangedEventArgs e) => Refresh();

    private void OnSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var note = this.FindControl<TextBlock>("PreviewNote");
        if (note is null) return;   // still loading
        if (_sample is null)
        {
            note.Text = "No drawing at the playhead to preview.";
            return;
        }
        var generation = ++_generation;
        var options = Options;
        var sample = _sample;
        note.Text = "Tracing…";
        _ = Task.Run(() =>
        {
            var strokes = LineTracer.Trace(sample, options);
            var picture = Picture(sample, strokes);
            return (strokes.Count, picture);
        }).ContinueWith(done =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (done.IsFaulted)
                {
                    if (generation == _generation) note.Text = "This picture could not be traced.";
                    return;
                }
                if (generation != _generation)
                {
                    done.Result.picture.Dispose();   // a newer setting is on its way
                    return;
                }
                var (count, picture) = done.Result;
                var image = this.FindControl<Image>("Preview")!;
                (image.Source as IDisposable)?.Dispose();
                image.Source = picture;
                note.Text = count == 1
                    ? "This drawing traces to 1 stroke."
                    : $"This drawing traces to {count} strokes.";
            });
        }, TaskScheduler.Default);
    }

    /// <summary>The traced strokes over a faint copy of the pixels they came from.</summary>
    private static Avalonia.Media.Imaging.Bitmap Picture(SKBitmap sample, List<Lightbox.Core.Documents.Stroke> strokes)
    {
        using var traced = FrameRasterizer.Rasterize(strokes, sample.Width, sample.Height);
        using var both = new SKBitmap(new SKImageInfo(sample.Width, sample.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(both))
        {
            canvas.Clear(SKColors.White);
            using var faint = new SKPaint { Color = SKColors.White.WithAlpha(70) };
            canvas.DrawBitmap(sample, 0, 0, faint);
            canvas.DrawBitmap(traced, 0, 0);
        }
        using var data = both.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }

    private void OnTrace(object? sender, RoutedEventArgs e) => Close(Options);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
