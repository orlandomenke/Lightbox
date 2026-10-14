using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        // Its own copy, never disposed here: a preview trace still running on
        // a worker when the window closes would otherwise read freed pixels
        // once the caller disposes its bitmap. The copy goes with the window.
        _sample = sample?.Copy();
        var which = drawings == 1 ? "1 drawing" : $"{drawings} drawings";
        this.FindControl<TextBlock>("Explanation")!.Text =
            $"“{layerName}” holds pixels in {which}. Each line becomes one brush stroke on a new layer above, "
            + "and this layer is hidden, not deleted. Edges, drawing order and pressure are guessed from the pixels.";
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

    private bool _syncing;

    private CancellationTokenSource? _pending;

    /// <summary>A slider moved: its field follows, and the preview.</summary>
    private void OnSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        var box = ReferenceEquals(sender, this.FindControl<Slider>("ThresholdSlider")) ? "ThresholdBox" : "DetailBox";
        if (this.FindControl<NumericUpDown>(box) is { } field) field.Value = (decimal)Math.Round(e.NewValue);
        _syncing = false;
        Refresh();
    }

    /// <summary>A value typed: its slider follows, and the preview.</summary>
    private void OnBoxChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_syncing || e.NewValue is not { } value) return;
        _syncing = true;
        var slider = ReferenceEquals(sender, this.FindControl<NumericUpDown>("ThresholdBox")) ? "ThresholdSlider" : "DetailSlider";
        if (this.FindControl<Slider>(slider) is { } control) control.Value = (double)value;
        _syncing = false;
        Refresh();
    }

    /// <remarks>
    /// Awaited rather than posted back: the continuation returns to the UI
    /// thread by itself, so no ambient dispatcher call is needed (B93's ratchet).
    /// </remarks>
    private async void Refresh()
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
        // Stop the preview the last setting started — a drag of the slider
        // otherwise queued one full trace per tick, and the one the slider
        // stopped on waited behind them all (the adversary) — and wait a moment
        // before starting this one, in case the slider is still moving.
        _pending?.Cancel();
        var stop = _pending = new CancellationTokenSource();
        note.Text = "Tracing…";
        (string? Why, int Count, Avalonia.Media.Imaging.Bitmap? Picture) done;
        try
        {
            await Task.Delay(120, stop.Token);
            done = await Task.Run(() =>
            {
                // Asked here, off the UI thread: it reads every pixel.
                if (LineTracer.WhyNot(sample, options) is { } why) return (why, 0, null);
                var strokes = LineTracer.Trace(sample, options, stop.Token);
                return ((string?)null, strokes.Count, (Avalonia.Media.Imaging.Bitmap?)Picture(sample, strokes));
            }, stop.Token);
        }
        catch (OperationCanceledException)
        {
            return;   // a newer setting took over
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (generation == _generation) note.Text = "This picture could not be traced.";
            return;
        }
        if (generation != _generation)
        {
            done.Picture?.Dispose();   // a newer setting is on its way
            return;
        }
        if (done.Why is { } reason)
        {
            note.Text = reason;
            return;
        }
        var image = this.FindControl<Image>("Preview")!;
        (image.Source as IDisposable)?.Dispose();
        image.Source = done.Picture;
        note.Text = done.Count == 1
            ? "This drawing traces to 1 stroke."
            : $"This drawing traces to {done.Count} strokes.";
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

    private void OnTrace(object? sender, RoutedEventArgs e)
    {
        _pending?.Cancel();
        Close(Options);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _pending?.Cancel();
        Close(null);
    }
}
