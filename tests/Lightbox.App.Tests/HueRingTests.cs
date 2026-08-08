using Avalonia;
using Avalonia.Media;
using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The hue ring's geometry: the marker and the pointer are inverses, red is
/// at the top, and the ring only ever writes the H component.
/// </summary>
public class HueRingTests
{
    private static readonly Point Centre = new(100, 100);

    [Theory]
    [InlineData(0, 100, 20)]      // red at the top
    [InlineData(90, 180, 100)]    // 90° at the right
    [InlineData(180, 100, 180)]   // 180° at the bottom
    [InlineData(270, 20, 100)]    // 270° at the left
    public void TheMarkerSitsWhereTheHueLives(double hue, double x, double y)
    {
        var at = HueRing.MarkerAt(hue, Centre, 80);
        Assert.Equal(x, at.X, 3);
        Assert.Equal(y, at.Y, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(137.5)]
    [InlineData(300)]
    public void ThePointerAndTheMarkerAreInverses(double hue)
    {
        var at = HueRing.MarkerAt(hue, Centre, 80);
        Assert.Equal(hue, HueRing.HueFromPoint(at, Centre), 3);
    }

    [Fact]
    public void TheRingWritesOnlyTheHue()
    {
        // The SV square owns saturation and value; a ring that reset either
        // would fight it on every drag.
        var ring = new HueRing
        {
            HsvColor = new HsvColor(0.5, 200, 0.25, 0.75),
        };

        ring.GetType().GetProperty("HsvColor")!.SetValue(
            ring, new HsvColor(0.5, 200, 0.25, 0.75));
        // Drive the private Hue path the way the pointer does: via reflection
        // on the property the pointer handlers write.
        var hueProp = ring.GetType().GetProperty(
            "Hue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        hueProp.SetValue(ring, 30.0);

        Assert.Equal(30, ring.HsvColor.H, 3);
        Assert.Equal(0.25, ring.HsvColor.S, 3);
        Assert.Equal(0.75, ring.HsvColor.V, 3);
        Assert.Equal(0.5, ring.HsvColor.A, 3);
    }
}
