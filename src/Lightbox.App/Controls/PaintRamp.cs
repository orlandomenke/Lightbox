using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// The picture an effect slider's track draws (Q275): the paint in hand
/// fading in from nothing, left to right. Over the checker that reads as
/// opacity; over the ground, as flow.
/// </summary>
/// <remarks>
/// The colour is the artist's, not the theme's, so the track previews the
/// mark the next stroke makes — which is the whole reason the slider is drawn
/// this way rather than as a grey line. A brush is built per colour change,
/// not per frame.
/// </remarks>
public static class PaintRamp
{
    /// <summary>A <c>#rrggbb</c> paint colour to its ramp; anything unreadable to a neutral one.</summary>
    public static readonly IValueConverter FromHex = new FuncValueConverter<string?, IBrush>(hex =>
        Ramp(hex is not null && Color.TryParse(hex, out var paint) ? paint : Colors.Gray));

    public static IBrush Ramp(Color paint) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, paint.R, paint.G, paint.B), 0),
            new GradientStop(Color.FromArgb(255, paint.R, paint.G, paint.B), 1),
        },
    };
}
