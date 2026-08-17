using Avalonia.Input;
using Lightbox.App.Rendering;

namespace Lightbox.App.Tests;

/// <summary>
/// B250: the weight brush follows the paint brush's pressure rule — a pen's
/// axis when it reports one, full strength for everything else. A mouse
/// reports ~0.5, and passing that through halved every dab.
/// </summary>
public class WeightPressureTests
{
    [Fact]
    public void AMouseDabsAtFullStrength()
    {
        Assert.Equal(1.0, CanvasControl.WeightPressure(PointerType.Mouse, 0.5f));
        Assert.Equal(1.0, CanvasControl.WeightPressure(PointerType.Touch, 0.5f));
    }

    [Fact]
    public void APenKeepsItsPressureAxis()
    {
        Assert.Equal(0.6, CanvasControl.WeightPressure(PointerType.Pen, 0.6f), 6);
        // A pen that reports nothing is a pen not yet pressing through a
        // driver quirk — full strength, the paint brush's answer.
        Assert.Equal(1.0, CanvasControl.WeightPressure(PointerType.Pen, 0f));
    }
}
