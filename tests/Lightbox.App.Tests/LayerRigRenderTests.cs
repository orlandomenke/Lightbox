using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A layer binding reaching pixels: the same drawing, with no weights on any
/// stroke, moved because the LAYER follows a bone (Q90).
/// </summary>
/// <remarks>
/// <para>
/// The claims are the ones the record layer could not make. A drawing on a
/// rigged layer <b>renders posed</b>; its cache entry is keyed <b>per timeline
/// position</b>, or a walk cycle comes back as whichever pose rendered first;
/// it <b>refuses the tile path</b>, whose store knows frames by id alone; and
/// the record is <b>never touched</b>, so the strokes stay exactly as drawn.
/// </para>
/// <para>
/// And the load-bearing negative: an unrigged document must go through this
/// funnel exactly as it did before any of it existed.
/// </para>
/// </remarks>
public class LayerRigRenderTests
{
    private const int W = 200, H = 200;

    /// <summary>A one-bone rig that swings 60° between frames 0 and 4.</summary>
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

    private static Stroke PlainStroke() => new()
    {
        Color = "#a03020",
        Points = [new StrokePoint(105, 100, 1), new StrokePoint(150, 100, 1), new StrokePoint(155, 105, 1)],
        Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>A document whose one layer follows the bone, with unweighted strokes on it.</summary>
    private static (Doc Doc, Frame Frame, Layer Layer) LayerRigged(string bone = "b")
    {
        var doc = RiggedDoc();
        var frame = new Frame { Strokes = [PlainStroke()] };
        var layer = new Layer { Name = "Lines", BoneId = bone, Cels = [new Cel { Frame = frame }] };
        doc.Scene.Layers.Add(layer);
        return (doc, frame, layer);
    }

    private static FrameBitmapCache CacheFor(Doc doc)
    {
        var cache = new FrameBitmapCache { Rig = RigIndex.For(doc) };
        cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel, cache.Rig);
        return cache;
    }

    private static bool Identical(SKBitmap a, SKBitmap b)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y))
                return false;
        return true;
    }

    private static bool AnyInk(SKBitmap b)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (b.GetPixel(x, y).Alpha > 0)
                return true;
        return false;
    }

    // ---- the index -----------------------------------------------------------------

    [Fact]
    public void AnUnriggedDocumentGetsTheSharedEmptyIndex()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = new Frame() }] });

        // Identity, not equality: an unrigged document must not allocate a map
        // per render, and every gate must fall straight back to asking the
        // frame — which is exactly how the funnel behaved before Q90.
        Assert.Same(RigIndex.Empty, RigIndex.For(doc));
        Assert.True(RigIndex.Empty.IsEmpty);
    }

    [Fact]
    public void TheIndexFindsTheLayerThatMovesADrawing()
    {
        var (doc, frame, layer) = LayerRigged();
        var index = RigIndex.For(doc);

        Assert.False(index.IsEmpty);
        Assert.True(index.IsPosed(frame));
        Assert.Equal(layer.Name, index.LayerOf(frame)!.Name);

        // A drawing on an unrigged layer is not in it, even in the same doc.
        var loose = new Frame { Strokes = [PlainStroke()] };
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = loose }] });
        var again = RigIndex.For(doc);
        Assert.False(again.IsPosed(loose));
        Assert.Null(again.LayerOf(loose));
    }

    [Fact]
    public void TheIndexFollowsTheLinkRatherThanOnlyTheOwnBinding()
    {
        var doc = RiggedDoc();
        var link = new LayerLink { Bones = true };
        doc.Scene.LayerLinks = [link];
        var colourFrame = new Frame { Strokes = [PlainStroke()] };
        var lineFrame = new Frame { Strokes = [PlainStroke()] };
        doc.Scene.Layers.Add(new Layer { Name = "Colour", LinkId = link.Id, BoneId = "b", Cels = [new Cel { Frame = colourFrame }] });
        doc.Scene.Layers.Add(new Layer { Name = "Lines", LinkId = link.Id, Cels = [new Cel { Frame = lineFrame }] });

        var index = RigIndex.For(doc);

        // The lines layer names no bone of its own — the link is what puts it
        // in play, and that is the whole point of linking a character.
        Assert.True(index.IsPosed(lineFrame));
        Assert.Equal("Lines", index.LayerOf(lineFrame)!.Name);
    }

    // ---- reaching pixels -----------------------------------------------------------

    [Fact]
    public void ADrawingOnARiggedLayerRendersPosedWithNoWeightsOnIt()
    {
        var (doc, frame, _) = LayerRigged();
        Assert.False(frame.HasBoundStrokes);        // nothing on the frame says "rigged"
        var cache = CacheFor(doc);

        var atRest = cache.Get(frame, W, H, celIndex: 0).Copy();
        var swung = cache.Get(frame, W, H, celIndex: 4);

        Assert.True(AnyInk(atRest), "the rest pose drew nothing at all");
        Assert.False(Identical(atRest, swung), "the layer binding never reached the pixels");
    }

    [Fact]
    public void TheRecordIsNotTouchedByRendering()
    {
        var (doc, frame, _) = LayerRigged();
        var before = frame.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList();

        var cache = CacheFor(doc);
        cache.Get(frame, W, H, celIndex: 4);

        // Invariant 1: posing decides where marks are STAMPED. The stroke is
        // still exactly where the artist drew it, and still carries no
        // weights — a layer binding writes nothing per stroke, which is what
        // keeps it free on two hundred frames.
        Assert.Equal(before, frame.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
        Assert.Null(frame.Strokes[0].Weights);
        Assert.Null(frame.Strokes[0].RestPoints);
    }

    [Fact]
    public void TheCacheKeyCarriesTheTimelinePosition()
    {
        var (doc, frame, _) = LayerRigged();
        var cache = CacheFor(doc);

        cache.Get(frame, W, H, celIndex: 0);
        var missesAfterFirst = cache.Misses;
        cache.Get(frame, W, H, celIndex: 4);

        // A second position is a second entry, or the whole cycle comes back
        // as whichever pose rendered first.
        Assert.True(cache.Misses > missesAfterFirst, "the second position hit the first position's bitmap");
        Assert.True(cache.Holds(frame, W, H, 1.0, celIndex: 0));
        Assert.True(cache.Holds(frame, W, H, 1.0, celIndex: 4));
    }

    [Fact]
    public void AnUnriggedDocumentKeysByIdAloneJustAsItAlwaysDid()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        var frame = new Frame { Strokes = [PlainStroke()] };
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = frame }] });
        var cache = CacheFor(doc);

        cache.Get(frame, W, H, celIndex: 0);
        var misses = cache.Misses;
        cache.Get(frame, W, H, celIndex: 9);

        // A held drawing must not get one cached bitmap per exposure — that is
        // the cache doing the opposite of its job, and it is what would happen
        // if the index answered yes for everything.
        Assert.Equal(misses, cache.Misses);
    }

    [Fact]
    public void TheLiveRenderIsByteForByteTheBakedRender()
    {
        var (liveDoc, liveFrame, _) = LayerRigged();
        var live = CacheFor(liveDoc).Get(liveFrame, W, H, celIndex: 4).Copy();

        var (bakedDoc, bakedFrame, bakedLayer) = LayerRigged();
        var baked = Skinning.BakeFrame(
            bakedFrame,
            bakedDoc.Armature!,
            ArmatureOps.PoseAt(bakedDoc.Scene.PoseTrack, 4),
            bakedDoc.Scene.RiggedBoneOf(bakedLayer));
        Assert.Equal(1, baked);

        // Bake and live share one construction, so what an artist saw is what
        // they froze — the claim that made live posing safe to ship, now for
        // the layer-level binding too.
        var afterBake = CacheFor(bakedDoc).Get(bakedFrame, W, H, celIndex: 4);
        Assert.True(Identical(live, afterBake), "the baked drawing does not match what was on screen");
    }

    [Fact]
    public void BakingLeavesTheLayerRiggedForTheDrawingsThatComeAfter()
    {
        var (doc, frame, layer) = LayerRigged();

        Skinning.BakeFrame(frame, doc.Armature!, ArmatureOps.PoseAt(doc.Scene.PoseTrack, 4), layer.BoneId);

        // The strokes are ordinary now — a bake is a freeze.
        Assert.Null(frame.Strokes[0].Weights);
        // The layer is not: baking one drawing must not quietly unrig the
        // layer and leave every drawing made after it sitting still.
        Assert.Equal("b", doc.Scene.RiggedBoneOf(layer));
    }

    [Fact]
    public void ABakedDrawingIsNotSwungAgainByTheLayerItSitsOn()
    {
        var (doc, frame, layer) = LayerRigged();
        var cache = CacheFor(doc);
        var live = cache.Get(frame, W, H, celIndex: 4).Copy();

        Skinning.BakeFrame(frame, doc.Armature!, ArmatureOps.PoseAt(doc.Scene.PoseTrack, 4), layer.BoneId);
        cache.Invalidate(frame.Id);

        // The trap this exists for: a baked stroke has no weights either, so
        // on a rigged LAYER it looks exactly like an unbaked one and would be
        // swung a second time — the drawing walking further from the rig every
        // time somebody froze it. A posed stroke carries a rest path and an
        // artist's stroke does not, which is what tells them apart.
        var afterBake = cache.Get(frame, W, H, celIndex: 4);
        Assert.True(Identical(live, afterBake), "the baked drawing was posed a second time");

        // And baking it again is a no-op rather than a second swing.
        Assert.Equal(0, Skinning.BakeFrame(
            frame, doc.Armature!, ArmatureOps.PoseAt(doc.Scene.PoseTrack, 4), layer.BoneId));
    }

    [Fact]
    public void FollowingTheWholeSkeletonBindsByDistanceWithoutWritingWeights()
    {
        var (doc, frame, _) = LayerRigged(bone: "");
        doc.Armature!.Bones.Add(new Bone { Id = "far", Name = "far", X = 20, Y = 190, Length = 10 });
        var cache = CacheFor(doc);

        var atRest = cache.Get(frame, W, H, celIndex: 0).Copy();
        var swung = cache.Get(frame, W, H, celIndex: 4);

        Assert.False(Identical(atRest, swung), "the whole-skeleton binding never reached the pixels");
        // Auto-bound on a copy: the record still carries nothing.
        Assert.Null(frame.Strokes[0].Weights);
    }

    [Fact]
    public void APaintedWeightOnOneStrokeStillWinsOverTheLayers()
    {
        var (doc, frame, _) = LayerRigged();
        doc.Armature!.Bones.Add(new Bone { Id = "still", Name = "still", X = 10, Y = 10, Length = 5 });
        // This line is pinned to a bone that never moves, on a layer that
        // follows one that does. The stroke's own answer has to win, or the
        // weight brush would stop being an override.
        Skinning.AssignAll(frame.Strokes[0], "still");
        var cache = CacheFor(doc);

        var atRest = cache.Get(frame, W, H, celIndex: 0).Copy();
        var swung = cache.Get(frame, W, H, celIndex: 4);

        Assert.True(Identical(atRest, swung), "the layer's binding overrode a painted weight");
    }
}
