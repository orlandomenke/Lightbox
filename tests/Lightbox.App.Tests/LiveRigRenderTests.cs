using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The live rig through the render funnel: a frame with bound strokes renders
/// posed for the timeline position asking for it, keyed per position — and a
/// document without a rig goes through the cache exactly as it always did.
/// Live and bake are one construction, so the cache's posed pixels must equal
/// a baked frame's, byte for byte.
/// </summary>
public class LiveRigRenderTests
{
    private const int W = 200, H = 200;

    private static Doc RiggedDoc()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Armature = new Armature
        {
            Bones = [new Bone { Id = "b", Name = "arm", X = 100, Y = 100, Length = 60 }],
        };
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0 },
                new PoseKey { Frame = 4, Bones = { ["b"] = new BonePose { RotationDeg = 60 } } },
            ],
        };
        return doc;
    }

    private static Frame BoundFrame()
    {
        var stroke = new Stroke
        {
            Color = "#a03020",
            Points = [new StrokePoint(105, 100, 1), new StrokePoint(150, 100, 1), new StrokePoint(155, 105, 1)],
            Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
        Skinning.AssignAll(stroke, "b");
        return new Frame { Strokes = [stroke] };
    }

    private static FrameBitmapCache CacheFor(Doc doc)
    {
        var cache = new FrameBitmapCache { Rig = RigIndex.For(doc) };
        // Index first, resolver second, and the resolver reads the index the
        // cache holds — the pair must agree or a posed frame gets a key that
        // says "any position".
        cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel, cache.Rig);
        return cache;
    }

    private static bool Identical(SKBitmap a, SKBitmap b)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }

    [Fact]
    public void ABoundFrameRendersItsPosePerTimelinePosition()
    {
        var doc = RiggedDoc();
        var frame = BoundFrame();
        using var cache = CacheFor(doc);

        var at0 = cache.Get(frame, W, H, celIndex: 0);
        var at4 = cache.Get(frame, W, H, celIndex: 4);

        // Two positions, two pictures — and two live cache entries, so
        // scrubbing back does not re-render.
        Assert.False(Identical(at0, at4), "the pose at frame 4 rendered as the rest pose");
        Assert.True(cache.Holds(frame, W, H, 1.0, 0));
        Assert.True(cache.Holds(frame, W, H, 1.0, 4));
    }

    [Fact]
    public void TheLiveRenderIsByteForByteTheBakedRender()
    {
        var doc = RiggedDoc();
        var live = BoundFrame();
        using var cache = CacheFor(doc);
        var liveBmp = cache.Get(live, W, H, celIndex: 4);

        // Bake the same frame at the same position and render it through a
        // cache with no resolver at all — the record now carries the pose.
        var baked = BoundFrame();
        Skinning.BakeFrame(baked, doc.Armature!, ArmatureOps.PoseAt(doc.Scene.PoseTrack, 4));
        using var plainCache = new FrameBitmapCache();
        var bakedBmp = plainCache.Get(baked, W, H);

        Assert.True(Identical(liveBmp, bakedBmp), "live pose and bake disagree");
    }

    [Fact]
    public void AnUnriggedDocumentNeverConsultsThePose()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        var frame = BoundFrame(); // bound strokes, but the doc has no armature
        using var cache = CacheFor(doc);

        var a = cache.Get(frame, W, H, celIndex: 0);
        var b = cache.Get(frame, W, H, celIndex: 4);

        // No armature means no posing — both positions are the drawing as
        // recorded, whatever the strokes' weights say.
        Assert.True(Identical(a, b));
    }

    [Fact]
    public void AnUnboundFrameKeysByIdAloneAsItAlwaysDid()
    {
        var doc = RiggedDoc();
        var frame = new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Color = "#204080",
                    Points = [new StrokePoint(20, 20, 1), new StrokePoint(60, 60, 1)],
                    Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
                },
            ],
        };
        using var cache = CacheFor(doc);

        cache.Get(frame, W, H, celIndex: 0);
        // Same entry serves every timeline position: one render, one bitmap.
        Assert.True(cache.Holds(frame, W, H, 1.0, celIndex: 7));
    }

    [Fact]
    public void ABoundFrameRefusesTheTilePath()
    {
        // A frame carrying painted weights answers for itself, so the flag is
        // redundant here — and it must still refuse without it.
        Assert.False(TileFrameCache.CanTileFrame(BoundFrame()));
        Assert.Equal(
            TileFallbackReason.BoundStrokes,
            TileFallback.Reason(
                BoundFrame(), hasCamera: false, haveViewport: true, liveEffectHere: false, posed: false,
                shaped: false));
    }

    [Fact]
    public void AFrameOnARiggedLayerRefusesTheTilePathToo()
    {
        // The layer-level case (Q90): no weights on the frame at all, so
        // nothing about the frame says "posed" — and tiling it would draw the
        // whole cycle at rest, which reads as the rig not working.
        var plain = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(1, 1, 1), new StrokePoint(9, 9, 1)] }] };
        Assert.False(plain.HasBoundStrokes);

        Assert.True(TileFrameCache.CanTileFrame(plain, posed: false));
        Assert.False(TileFrameCache.CanTileFrame(plain, posed: true));
        Assert.Equal(
            TileFallbackReason.BoundStrokes,
            TileFallback.Reason(
                plain, hasCamera: false, haveViewport: true, liveEffectHere: false, posed: true,
                shaped: false));
    }
}
