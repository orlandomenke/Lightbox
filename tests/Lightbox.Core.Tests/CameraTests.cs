using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The camera is a document transform: authored, keyframed, saved, exported.
/// Half of what these tests protect is the arithmetic; the other half is its
/// <em>absence</em>, because a document that never asked for a camera must
/// save, load and render exactly as it did before the feature existed.
/// </summary>
public class CameraTests
{
    private const int W = 1000, H = 600;

    private static Camera TwoKeys() => new()
    {
        OutputWidth = 800,
        OutputHeight = 450,
        Keys =
        [
            new CameraKey { Frame = 0, X = 200, Y = 100, Zoom = 1, RotationDeg = 0, Ease = Easing.Linear },
            new CameraKey { Frame = 10, X = 400, Y = 300, Zoom = 4, RotationDeg = 90, Ease = Easing.Linear },
        ],
    };

    [Fact]
    public void NoCamera_FramesTheWholeSceneDeadCentre()
    {
        var f = CameraOps.At(null, 7, W, H);
        Assert.Equal(new CameraFraming(500, 300, 1, 0), f);
    }

    [Fact]
    public void NoKeys_FramesTheWholeSceneDeadCentre()
    {
        var f = CameraOps.At(new Camera(), 7, W, H);
        Assert.Equal(new CameraFraming(500, 300, 1, 0), f);
    }

    [Fact]
    public void OneKey_IsAStaticFraming()
    {
        var camera = new Camera { Keys = [new CameraKey { Frame = 4, X = 120, Y = 80, Zoom = 2 }] };
        foreach (var frame in new[] { 0, 4, 99 })
        {
            var f = CameraOps.At(camera, frame, W, H);
            Assert.Equal(120, f.X);
            Assert.Equal(80, f.Y);
            Assert.Equal(2, f.Zoom);
        }
    }

    [Fact]
    public void OutsideTheAuthoredRange_TheCameraHoldsRatherThanDrifting()
    {
        var camera = TwoKeys();
        var before = CameraOps.At(camera, -20, W, H);
        var after = CameraOps.At(camera, 500, W, H);

        Assert.Equal(200, before.X);
        Assert.Equal(1, before.Zoom);
        Assert.Equal(400, after.X);
        Assert.Equal(4, after.Zoom);
        Assert.Equal(90, after.RotationDeg);
    }

    [Fact]
    public void PanAndRollInterpolateLinearlyBetweenKeys()
    {
        var f = CameraOps.At(TwoKeys(), 5, W, H);
        Assert.Equal(300, f.X, 6);
        Assert.Equal(200, f.Y, 6);
        Assert.Equal(45, f.RotationDeg, 6);
    }

    [Fact]
    public void ZoomInterpolatesGeometrically_SoAPushHoldsAConstantRate()
    {
        // 1x to 4x: the halfway point of a ratio is its geometric mean, 2x.
        // Linear interpolation would put it at 2.5x, three quarters of the
        // magnification done in half the time, which reads as a lurch.
        var f = CameraOps.At(TwoKeys(), 5, W, H);
        Assert.Equal(2.0, f.Zoom, 6);

        // And the rate really is constant: each quarter multiplies by the same
        // factor.
        var q1 = CameraOps.At(TwoKeys(), 2, W, H).Zoom;
        var q2 = CameraOps.At(TwoKeys(), 4, W, H).Zoom;
        var q3 = CameraOps.At(TwoKeys(), 6, W, H).Zoom;
        Assert.Equal(q2 / q1, q3 / q2, 6);
    }

    [Theory]
    [InlineData(Easing.Linear, 300)]
    [InlineData(Easing.EaseIn, 250)]     // t^2 at t=0.5 is 0.25
    [InlineData(Easing.EaseOut, 350)]    // 1-(1-t)^2 at t=0.5 is 0.75
    [InlineData(Easing.EaseInOut, 300)]  // symmetric, so still halfway
    public void EachEasingShapesTheMove(Easing ease, double expectedX)
    {
        var camera = TwoKeys();
        camera.Keys[0].Ease = ease;
        Assert.Equal(expectedX, CameraOps.At(camera, 5, W, H).X, 6);
    }

    [Fact]
    public void AZeroOrNegativeZoom_FallsBackToOneRatherThanDividingTheFramingAway()
    {
        var camera = new Camera { Keys = [new CameraKey { Frame = 0, X = 10, Y = 10, Zoom = 0 }] };
        Assert.Equal(1, CameraOps.At(camera, 0, W, H).Zoom);
    }

    [Fact]
    public void KeysOutOfOrderStillInterpolateInTimelineOrder()
    {
        var camera = new Camera
        {
            Keys =
            [
                new CameraKey { Frame = 10, X = 400, Ease = Easing.Linear },
                new CameraKey { Frame = 0, X = 200, Ease = Easing.Linear },
            ],
        };
        Assert.Equal(300, CameraOps.At(camera, 5, W, H).X, 6);
    }

    [Fact]
    public void SetKeyReplacesTheKeyAlreadyOnThatFrame()
    {
        var camera = new Camera();
        CameraOps.SetKey(camera, 3, new CameraFraming(10, 20, 1, 0));
        CameraOps.SetKey(camera, 3, new CameraFraming(50, 60, 2, 15));

        Assert.Single(camera.Keys);
        var key = Assert.IsType<CameraKey>(CameraOps.KeyAt(camera, 3));
        Assert.Equal(50, key.X);
        Assert.Equal(2, key.Zoom);
        Assert.Equal(15, key.RotationDeg);
    }

    [Fact]
    public void ClearKeyRemovesOnlyThatFrame()
    {
        var camera = TwoKeys();
        Assert.True(CameraOps.ClearKey(camera, 10));
        Assert.False(CameraOps.ClearKey(camera, 10));
        Assert.Single(camera.Keys);
        Assert.Equal(0, camera.Keys[0].Frame);
    }

    // ---- optionality: absent, not disabled -------------------------------------

    [Fact]
    public void ANewSceneHasNoCamera()
    {
        Assert.Null(new Scene().Camera);
        Assert.Null(DocumentFactory.CreateDoc(100, 100, 12).Scene.Camera);
    }

    [Fact]
    public void ADocumentWithoutACamera_SerializesWithNoCameraKeyAtAll()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        var json = DocJson.Serialize(doc);

        // Not "camera": null, and not a disabled camera object — absent. A
        // sprite-sheet document must not start carrying shot machinery in
        // every diff because the feature exists.
        Assert.DoesNotContain("camera", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddingThenRemovingACamera_ReturnsTheDocumentToItsOriginalBytes()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        var before = DocJson.Serialize(doc);

        doc.Scene.Camera = new Camera { OutputWidth = 640, OutputHeight = 360 };
        CameraOps.SetKey(doc.Scene.Camera!, 0, new CameraFraming(50, 50, 1, 0));
        Assert.NotEqual(before, DocJson.Serialize(doc));

        doc.Scene.Camera = null;
        Assert.Equal(before, DocJson.Serialize(doc));
    }

    // ---- the pivot is optional the same way ------------------------------------

    [Fact]
    public void ANewSceneHasNoPivot_AndTheFileHasNoPivotKey()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        Assert.Null(doc.Scene.Pivot);
        Assert.DoesNotContain("pivot", DocJson.Serialize(doc), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APivotRoundTrips_AndDefaultsToFeetCentre()
    {
        var doc = DocumentFactory.CreateDoc(200, 120, 12);
        // Where a standing character is anchored: the bottom of the canvas,
        // horizontally centred.
        doc.Scene.Pivot = Pivot.BottomCentre(200, 120);

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var pivot = Assert.IsType<Pivot>(reloaded.Scene.Pivot);
        Assert.Equal(100, pivot.X);
        Assert.Equal(120, pivot.Y);
    }

    [Fact]
    public void AShotDocumentCarriesNoPivotAndASpriteDocumentNoCamera()
    {
        // The two output targets, and neither pays for the other.
        var shot = DocumentFactory.CreateDoc(1000, 600, 24);
        shot.Scene.Camera = TwoKeys();
        Assert.DoesNotContain("pivot", DocJson.Serialize(shot), StringComparison.OrdinalIgnoreCase);

        var sprite = DocumentFactory.CreateDoc(64, 64, 12);
        sprite.Scene.Pivot = Pivot.BottomCentre(64, 64);
        Assert.DoesNotContain("camera", DocJson.Serialize(sprite), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACameraRoundTripsThroughSaveAndLoad()
    {
        var doc = DocumentFactory.CreateDoc(1000, 600, 24);
        doc.Scene.Camera = TwoKeys();

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var camera = Assert.IsType<Camera>(reloaded.Scene.Camera);

        Assert.Equal(800, camera.OutputWidth);
        Assert.Equal(450, camera.OutputHeight);
        Assert.Equal(2, camera.Keys.Count);
        Assert.Equal(90, camera.Keys[1].RotationDeg);
        Assert.Equal(Easing.Linear, camera.Keys[1].Ease);
        // And it still interpolates the same after a round trip.
        Assert.Equal(2.0, CameraOps.At(camera, 5, 1000, 600).Zoom, 6);
    }
}
