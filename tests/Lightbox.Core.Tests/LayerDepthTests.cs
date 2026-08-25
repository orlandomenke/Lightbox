using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The multiplane arithmetic (stage 1 of docs/DESIGN-3d-space.md), and the
/// record's one absolute rule: a depth nobody authored writes nothing.
/// </summary>
public class LayerDepthTests
{
    [Fact]
    public void TheFactorIsOneOnThePicturePlane()
    {
        Assert.Equal(1, LayerDepth.Factor(null));
        Assert.Equal(1, LayerDepth.Factor(0));
    }

    [Fact]
    public void TheFactorFallsWithDistanceAndRisesInFront()
    {
        // f / (f + d) with f = 1000.
        Assert.Equal(0.5, LayerDepth.Factor(1000), 10);
        Assert.Equal(0.25, LayerDepth.Factor(3000), 10);
        Assert.Equal(2.0, LayerDepth.Factor(-500), 10);
        // Past the clamps: infinitely far still moves a little (a factor of
        // exactly 0 would park a layer for ever), and a layer at or behind
        // the camera — where the division blows up — pins to the near clamp.
        Assert.Equal(0.05, LayerDepth.Factor(1e9), 10);
        Assert.Equal(4.0, LayerDepth.Factor(-800), 10);
        Assert.Equal(0.05, LayerDepth.Factor(-2000), 10);
    }

    [Fact]
    public void AttenuationTakesPanLinearlyZoomGeometricallyAndLeavesRoll()
    {
        var home = new CameraFraming(400, 100, 1, 0);
        var framing = new CameraFraming(500, 140, 4, 30);

        var att = LayerDepth.Attenuate(framing, home, 1000); // factor 0.5

        Assert.Equal(450, att.X, 10);       // half the pan
        Assert.Equal(120, att.Y, 10);
        Assert.Equal(2, att.Zoom, 10);      // 4^0.5 — zoom is a ratio
        Assert.Equal(30, att.RotationDeg);  // roll turns every plane alike
    }

    [Fact]
    public void AttenuationAtThePlaneReturnsTheFramingUntouched()
    {
        var home = new CameraFraming(400, 100, 1, 0);
        var framing = new CameraFraming(500, 140, 4, 30);
        Assert.Equal(framing, LayerDepth.Attenuate(framing, home, null));
        Assert.Equal(framing, LayerDepth.Attenuate(framing, home, 0));
    }

    [Fact]
    public void ADepthLeftAtItsDefaultWritesNoKey()
    {
        // The design's rule 1, testable the cheap way: a document that never
        // uses depth serialises byte-identically to today, so the key must
        // not exist at all — not sit there as zero.
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        Assert.DoesNotContain("\"depth\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void AnAuthoredDepthSurvivesTheRoundTrip()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        doc.Scene.Layers.First(l => !l.IsBackground).Depth = 750;

        var json = DocJson.Serialize(doc);
        Assert.Contains("\"depth\"", json);

        var back = DocJson.Deserialize(json);
        Assert.Equal(750, back.Scene.Layers.First(l => !l.IsBackground).Depth);
    }
}
