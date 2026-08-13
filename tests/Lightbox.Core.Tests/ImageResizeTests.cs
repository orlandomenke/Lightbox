using System.Reflection;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Tests;

/// <summary>
/// Scaling the artwork rather than the paper.
/// </summary>
/// <remarks>
/// The interesting failure here is not arithmetic, it is omission: a
/// coordinate the visitor forgets stays at its old scale and lands somewhere
/// wrong, and nothing goes red. <see cref="EveryCoordinateOnTheSceneIsAccountedFor"/>
/// is the guard against that — the same job
/// <c>StrokeCloneTests</c> does for copying.
/// </remarks>
public class ImageResizeTests
{
    /// <summary>Records what it was asked to scale; scaling pixels is Skia's job.</summary>
    private sealed class FakeResampler : IPixelResampler
    {
        public List<(string Png, double X, double Y)> Calls { get; } = [];

        public string? Resample(string pngBase64, double scaleX, double scaleY)
        {
            Calls.Add((pngBase64, scaleX, scaleY));
            return pngBase64 + "@scaled";
        }
    }

    private static Doc DocWithAStroke(out Stroke stroke)
    {
        var doc = new Doc();
        stroke = new Stroke
        {
            Points = [new StrokePoint(100, 50, 1), new StrokePoint(200, 150, 0.5)],
            Brush = new BrushSettings { Size = 10, TextureScale = 12 },
        };
        var captured = stroke;
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame { Strokes = [captured] } }],
        });
        return doc;
    }

    [Fact]
    public void DoublingTheImageDoublesTheGeometryAndTheBrush()
    {
        var doc = DocWithAStroke(out var stroke);

        Assert.True(ImageResize.Apply(doc, 1920, 1080, new FakeResampler()));

        Assert.Equal(1920, doc.Scene.Width);
        Assert.Equal(1080, doc.Scene.Height);
        Assert.Equal(200, stroke.Points[0].X);
        Assert.Equal(100, stroke.Points[0].Y);
        Assert.Equal(400, stroke.Points[1].X);
        // The mark has to grow with the drawing, or a doubled document comes
        // back as the same picture drawn with a pen half the width.
        Assert.Equal(20, stroke.Brush.Size);
        Assert.Equal(24, stroke.Brush.TextureScale);
        // Pressure is not a coordinate.
        Assert.Equal(0.5, stroke.Points[1].Pressure);
    }

    [Fact]
    public void AnUnlinkedResizeMovesGeometryExactlyAndTheMarkByTheAverage()
    {
        // A dab has one diameter and no axes, so a 2x1 resize cannot scale the
        // mark faithfully. The geometric mean is the stated compromise; what
        // must not happen is the geometry being averaged too.
        var doc = DocWithAStroke(out var stroke);

        ImageResize.Apply(doc, 1920, 540, new FakeResampler());

        Assert.Equal(200, stroke.Points[0].X);
        Assert.Equal(50, stroke.Points[0].Y);
        Assert.Equal(10 * Math.Sqrt(2), stroke.Brush.Size, 6);
    }

    [Fact]
    public void TheOriginScalesWithTheDrawingItOffsets()
    {
        var doc = new Doc();
        CanvasResize.Apply(doc.Scene, 1200, 540, ResizeAnchor.TopRight);
        Assert.Equal(-240, doc.Scene.Left);

        ImageResize.Apply(doc, 2400, 1080, new FakeResampler());

        // Twice the document, twice the margin that was added to its left.
        Assert.Equal(-480, doc.Scene.Left);
        Assert.Equal(2400, doc.Scene.Width);
    }

    [Fact]
    public void ThePixelPayloadsAreHandedToTheResampler()
    {
        var doc = DocWithAStroke(out var stroke);
        stroke.Baked = new BakedSample { PngBase64 = "baked", X = 30, Y = 40 };
        doc.Scene.Layers[0].Cels[0].Frame!.PngBase64 = "baseline";
        var resampler = new FakeResampler();

        ImageResize.Apply(doc, 1920, 1080, resampler);

        Assert.Equal(2, resampler.Calls.Count);
        Assert.All(resampler.Calls, c => Assert.Equal(2.0, c.X));
        Assert.Equal("baked@scaled", stroke.Baked!.PngBase64);
        Assert.Equal(60, stroke.Baked.X);
        Assert.Equal(80, stroke.Baked.Y);
        Assert.Equal("baseline@scaled", doc.Scene.Layers[0].Cels[0].Frame!.PngBase64);
    }

    [Fact]
    public void APayloadThatWillNotResampleIsDroppedRatherThanLeftAtTheOldSize()
    {
        var doc = DocWithAStroke(out var stroke);
        stroke.Baked = new BakedSample { PngBase64 = "corrupt", X = 1, Y = 1 };

        ImageResize.Apply(doc, 1920, 1080, new NullResampler());

        // Keeping it would render a patch of the old scale into the middle of
        // a rescaled drawing, which nobody would trace back to a resize.
        Assert.Null(stroke.Baked);
    }

    private sealed class NullResampler : IPixelResampler
    {
        public string? Resample(string pngBase64, double scaleX, double scaleY) => null;
    }

    [Fact]
    public void SettingTheResolutionAloneResamplesNothing()
    {
        var doc = DocWithAStroke(out var stroke);
        var resampler = new FakeResampler();

        Assert.True(ImageResize.Apply(doc, 960, 540, resampler, ppi: 300));

        Assert.Equal(300, doc.Scene.Ppi);
        Assert.Equal(960, doc.Scene.Width);
        Assert.Equal(100, stroke.Points[0].X);
        Assert.Empty(resampler.Calls);
    }

    [Fact]
    public void AResizeThatAsksForNothingReportsThat()
    {
        var doc = DocWithAStroke(out _);

        Assert.False(ImageResize.Apply(doc, 960, 540, new FakeResampler()));
        Assert.False(ImageResize.Apply(doc, 960, 540, new FakeResampler(), ppi: 72));
        Assert.False(ImageResize.Apply(doc, 0, 540, new FakeResampler()));
    }

    [Fact]
    public void EverythingHangingOffTheSceneMovesTogether()
    {
        var doc = new Doc();
        doc.Scene.Guides = [new Guide { X = 100, Y = 50, Spacing = 32 }];
        doc.Scene.Camera = new Camera { Keys = [new CameraKey { X = 300, Y = 200, Zoom = 2 }] };
        doc.Scene.Pivot = new Pivot { X = 480, Y = 540 };
        doc.ClipRegions["c"] = new ClipRegion
        {
            Contours = [[new StrokePoint(10, 20, 1)]],
            Feather = 4,
        };
        doc.Armature = new Armature
        {
            Bones = [new Bone { Id = "spine", X = 480, Y = 270, Length = 100, RotationDeg = 30 }],
        };
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["spine"] = new BonePose { X = 12, Y = 6, RotationDeg = 15 } } }],
        };
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame
            {
                Placements = [new SymbolPlacement { X = 10, Y = 20, ScaleX = 1, ScaleY = 1 }],
                Anchors = new Dictionary<string, AnchorPoint> { ["hand"] = new(60, 70) },
                Shapes = new Dictionary<string, ShapeBox> { ["body"] = new(1, 2, 3, 4) },
            } }],
        });

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        Assert.Equal(200, doc.Scene.Guides![0].X);
        Assert.Equal(64, doc.Scene.Guides[0].Spacing);
        Assert.Equal(600, doc.Scene.Camera!.Keys[0].X);
        // Zoom is a ratio against a scene that scaled with it, so the shot
        // frames the same part of the drawing.
        Assert.Equal(2, doc.Scene.Camera.Keys[0].Zoom);
        Assert.Equal(960, doc.Scene.Pivot!.X);
        Assert.Equal(20, doc.ClipRegions["c"].Contours[0][0].X);
        Assert.Equal(8, doc.ClipRegions["c"].Feather);

        var bone = doc.Armature!.Bones[0];
        Assert.Equal(960, bone.X);
        Assert.Equal(540, bone.Y);
        Assert.Equal(200, bone.Length);
        // Rotation is not a length and does not scale.
        Assert.Equal(30, bone.RotationDeg);
        var spinePose = doc.Scene.PoseTrack!.Keys[0].Bones["spine"];
        Assert.Equal(24, spinePose.X);
        Assert.Equal(12, spinePose.Y);
        Assert.Equal(15, spinePose.RotationDeg);

        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal(20, frame.Placements![0].X);
        // The symbol keeps its own space — it is shared, and this document
        // rescaling must not reach into it. The placement carries the change.
        Assert.Equal(2, frame.Placements[0].ScaleX);
        Assert.Equal(120, frame.Anchors!["hand"].X);
        Assert.Equal(new ShapeBox(2, 4, 6, 8), frame.Shapes!["body"]);
    }

    [Fact]
    public void EveryCoordinateOnTheSceneIsAccountedFor()
    {
        // The omission guard. A new coordinate-bearing property on Scene is
        // either scaled by ImageResize or listed here as deliberately not —
        // and adding one without doing either fails this test rather than
        // going quiet. Same rule as Stroke.Clone: the list is exhaustive, not
        // "the ones that matter".
        var handled = new HashSet<string>
        {
            // Scaled by ImageResize.
            nameof(Scene.Width), nameof(Scene.Height),
            nameof(Scene.OriginX), nameof(Scene.OriginY),
            nameof(Scene.Layers), nameof(Scene.Guides), nameof(Scene.Camera),
            nameof(Scene.Pivot), nameof(Scene.References), nameof(Scene.Ppi),
            nameof(Scene.PoseTrack),

            // Carry no document coordinate, on purpose.
            nameof(Scene.Id), nameof(Scene.Name), nameof(Scene.Fps),
            nameof(Scene.FrameCount), nameof(Scene.BackgroundColor),
            nameof(Scene.TransparentBackground), nameof(Scene.Markers),
            nameof(Scene.LayerGroups), nameof(Scene.FrameGroups),
            nameof(Scene.GhostFrames), nameof(Scene.Anchors), nameof(Scene.Tags),
            nameof(Scene.Shapes), nameof(Scene.Audio),
        };

        var actual = typeof(Scene)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)
            .ToList();

        var unaccounted = actual.Where(name => !handled.Contains(name)).ToList();
        Assert.True(
            unaccounted.Count == 0,
            $"Scene grew {string.Join(", ", unaccounted)}. If it holds a document coordinate, "
            + "scale it in ImageResize.Apply; if it does not, say so in this test's list.");
    }
}
