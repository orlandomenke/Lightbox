using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace Lightbox.App.Rendering;

public sealed partial class CanvasControl
{
    /// <summary>
    /// The ground round the artwork, from the theme (Q203): Dark-lit keeps the
    /// old dark grey, Studio grey a neutral mid-grey for judging values. Read
    /// here, on the UI thread, when the theme changes, and carried to the
    /// render thread in the frame's <see cref="ViewState"/>; never looked up per
    /// frame.
    /// </summary>
    private SKColor _surround = new(0x2b, 0x2b, 0x2b);

    /// <summary>The surround the next frame will clear to. Tests only.</summary>
    internal SKColor SurroundForTests => _surround;

    private void ReadSurround()
    {
        var c = Controls.ThemeColour.Of(this, "CanvasSurroundBrush", Color.FromRgb(0x2b, 0x2b, 0x2b));
        _surround = new SKColor(c.R, c.G, c.B);
    }

    // Here rather than in the constructor: CanvasControl.cs may shrink but
    // not grow (MonolithRatchetTests).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == Avalonia.Controls.ThemeVariantScope.ActualThemeVariantProperty)
        {
            ReadSurround();
            InvalidateVisual();
        }
    }
}
