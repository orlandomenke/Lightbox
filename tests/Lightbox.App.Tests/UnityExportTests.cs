using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

using Lightbox.Core.Projects;
namespace Lightbox.App.Tests;

/// <summary>
/// P5g's walking skeleton: atlas, sidecar with a Unity block, and the importer.
/// </summary>
/// <remarks>
/// The Unity-side script references <c>UnityEditor</c>, so it cannot be compiled or
/// run here. That is precisely why every number it needs is computed on this side —
/// these tests are what stands in for running it.
/// </remarks>
public class UnityExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-unity-" + Guid.NewGuid().ToString("N"));

    public UnityExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string At(string name) => Path.Combine(_dir, name);

    private static Doc Walking(int frames = 4)
    {
        var doc = DocumentFactory.CreateDoc(200, 120, 12, null);
        doc.Scene.FrameCount = frames;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel { Frame = new PaintedFrame() });

        for (var i = 0; i < frames; i++)
        {
            if (layer.Cels[i].Frame is not PaintedFrame p) continue;
            double x = 40 + i * 10, top = 30;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#c02040",
                Points =
                [
                    new StrokePoint(x, top, 1), new StrokePoint(x + 30, top, 1),
                    new StrokePoint(x + 30, 90, 1), new StrokePoint(x, 90, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }
        return doc;
    }

    private static JsonElement Unity(UnityExportResult r) =>
        JsonDocument.Parse(File.ReadAllText(r.MetadataPath)).RootElement.GetProperty("unity");

    // ---- the block is additive --------------------------------------------------

    [Fact]
    public void TheGenericSidecarKeepsEveryKeyItAlreadyHad()
    {
        // Godot and Unreal read the same file, so a Unity export must not remove or
        // rename anything the generic exporter wrote.
        var plain = SpriteSheetExporter.Export(Walking(4), At("plain.png"));
        var plainKeys = JsonDocument.Parse(File.ReadAllText(plain.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        var unity = UnityExporter.Export(Walking(4), At("unity.png"));
        var unityKeys = JsonDocument.Parse(File.ReadAllText(unity.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.All(plainKeys, k => Assert.Contains(k, unityKeys));
        Assert.Contains("unity", unityKeys);
    }

    [Fact]
    public void AnOrdinaryExportStillHasNoUnityBlock()
    {
        var plain = SpriteSheetExporter.Export(Walking(3), At("noblock.png"));
        Assert.DoesNotContain("\"unity\"", File.ReadAllText(plain.MetadataPath));
    }

    // ---- what the importer reads -------------------------------------------------

    [Fact]
    public void EverySpriteGetsARectAndTheCountMatchesTheFrames()
    {
        var result = UnityExporter.Export(Walking(6), At("sprites.png"));

        var sprites = Unity(result).GetProperty("sprites").EnumerateArray().ToList();
        Assert.Equal(6, sprites.Count);
        Assert.Equal(6, result.SpriteCount);
        Assert.All(sprites, s => Assert.Equal(4, s.GetProperty("rect").GetArrayLength()));
    }

    [Fact]
    public void TheRectsAreTheOnesTheSheetExporterWrote()
    {
        // Read back rather than recomputed: two computations of the same rect are
        // two chances to disagree, and the disagreement would be invisible until
        // something rendered wrongly.
        var result = UnityExporter.Export(Walking(4), At("agree.png"));
        var root = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)).RootElement;

        var frames = root.GetProperty("frames").EnumerateArray().ToList();
        var sprites = root.GetProperty("unity").GetProperty("sprites").EnumerateArray().ToList();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i].GetProperty("frame");
            var rect = sprites[i].GetProperty("rect");
            Assert.Equal(frame.GetProperty("x").GetInt32(), rect[0].GetInt32());
            Assert.Equal(frame.GetProperty("y").GetInt32(), rect[1].GetInt32());
            Assert.Equal(frame.GetProperty("w").GetInt32(), rect[2].GetInt32());
            Assert.Equal(frame.GetProperty("h").GetInt32(), rect[3].GetInt32());
        }
    }

    [Fact]
    public void AFeetPivotArrivesAsBottomCentreNormalised()
    {
        // The conversion that would bite, end to end rather than in isolation:
        // three differences apply at once and a flipped one looks like an animation
        // bug. Union trim, so the cell is the ink's bounding box across the run.
        var doc = Walking(4);
        // Pivot at the horizontal middle of the ink and on its bottom edge.
        doc.Scene.Pivot = new Pivot { X = 55, Y = 90 };

        var result = UnityExporter.Export(
            doc, At("pivot.png"),
            new UnityExportOptions { Sheet = new SpriteSheetOptions { Trim = SpriteTrim.Union } });

        var root = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)).RootElement;
        var source = root.GetProperty("frames")[0].GetProperty("spriteSourceSize");
        var rect = root.GetProperty("frames")[0].GetProperty("frame");
        var pivot = root.GetProperty("unity").GetProperty("sprites")[0].GetProperty("pivot");

        var cellLeft = source.GetProperty("x").GetInt32();
        var cellTop = source.GetProperty("y").GetInt32();
        var w = rect.GetProperty("w").GetInt32();
        var h = rect.GetProperty("h").GetInt32();

        // Y first, because that is the one a wrong convention breaks: the pivot sits
        // on the ink's bottom edge, so Unity must see 0 and not 1.
        Assert.Equal(1.0 - (90.0 - cellTop) / (double)h, pivot[1].GetDouble(), 6);
        Assert.Equal(0.0, pivot[1].GetDouble(), 6);
        Assert.Equal((55.0 - cellLeft) / w, pivot[0].GetDouble(), 6);
    }

    [Fact]
    public void AnAnchorIsConvertedTheSameWayAsThePivot()
    {
        var doc = Walking(3);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var hand = Anchors.Declare(doc.Scene, "leftHand");
        Anchors.SetAcross(layer, 0, 3, hand.Id, new AnchorPoint(50, 40));

        var result = UnityExporter.Export(doc, At("anchor.png"));

        var anchors = Unity(result).GetProperty("sprites")[0].GetProperty("anchors");
        var point = anchors.GetProperty("leftHand");
        // Inside the cell and the right way up: an anchor above the cell's middle
        // must be above 0.5, not below it.
        Assert.True(point[1].GetDouble() > 0.5, $"anchor y came out {point[1].GetDouble()}");
    }

    [Fact]
    public void PixelsPerUnitFollowsTheWorldSizeAsked()
    {
        // A 120 px canvas meant to be two units tall is 60 ppu.
        var result = UnityExporter.Export(
            Walking(2), At("ppu.png"), new UnityExportOptions(WorldHeightUnits: 2));

        Assert.Equal(60, Unity(result).GetProperty("pixelsPerUnit").GetDouble(), 6);
    }

    [Fact]
    public void SecondsPerFrameIsExactRatherThanRounded()
    {
        var result = UnityExporter.Export(Walking(2), At("spf.png"));
        Assert.Equal(1.0 / 12, Unity(result).GetProperty("secondsPerFrame").GetDouble(), 10);
    }

    // ---- colliders ---------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoShapesHasNoColliderKey()
    {
        var result = UnityExporter.Export(Walking(2), At("nocol.png"));
        Assert.False(Unity(result).GetProperty("sprites")[0].TryGetProperty("colliders", out _));
    }

    [Fact]
    public void AHurtboxBelowTheFeetPivotArrivesWithANegativeYOffset()
    {
        // End to end, and the failure it guards is the memorable one: a sign error
        // here puts every hurtbox above the character's head. Feet pivot at y = 90,
        // body box from y = 35 to y = 85, so the box's centre is *above* the pivot
        // and its offset must be positive; the second box, below the pivot, must be
        // negative. Both in one test so a version that flipped everything fails.
        var doc = Walking(3);
        doc.Scene.Pivot = new Pivot { X = 55, Y = 90 };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var body = CollisionShapes.Declare(doc.Scene, "body");
        var shadow = CollisionShapes.Declare(doc.Scene, "shadow", ShapeRole.Physics);
        CollisionShapes.SetAcross(layer, 0, 3, body.Id, new ShapeBox(45, 35, 20, 50));
        CollisionShapes.SetAcross(layer, 0, 3, shadow.Id, new ShapeBox(45, 95, 20, 10));

        var result = UnityExporter.Export(
            doc, At("collider.png"), new UnityExportOptions(WorldHeightUnits: 1.2));

        var ppu = Unity(result).GetProperty("pixelsPerUnit").GetDouble();
        Assert.Equal(100, ppu, 6);   // 120 px canvas over 1.2 units

        var colliders = Unity(result).GetProperty("sprites")[0]
            .GetProperty("colliders").EnumerateArray().ToList();
        Assert.Equal(2, colliders.Count);

        var bodyBox = colliders[0];
        Assert.Equal("body", bodyBox.GetProperty("name").GetString());
        Assert.Equal("hurtbox", bodyBox.GetProperty("role").GetString());
        // Centre y = 60, pivot y = 90, so 30 px above the pivot: +0.3 units.
        Assert.Equal(0.3, bodyBox.GetProperty("offset")[1].GetDouble(), 6);
        // Centre x = 55, which is the pivot's x: no horizontal offset.
        Assert.Equal(0.0, bodyBox.GetProperty("offset")[0].GetDouble(), 6);
        Assert.Equal(0.2, bodyBox.GetProperty("size")[0].GetDouble(), 6);
        Assert.Equal(0.5, bodyBox.GetProperty("size")[1].GetDouble(), 6);

        var shadowBox = colliders[1];
        Assert.Equal("physics", shadowBox.GetProperty("role").GetString());
        // Centre y = 100, ten below the pivot: -0.1 units.
        Assert.Equal(-0.1, shadowBox.GetProperty("offset")[1].GetDouble(), 6);
    }

    [Fact]
    public void TrimmingCannotMoveACollider()
    {
        // The pivot and the anchors are cell-relative and so have to be *kept* right
        // through a trim change; a collider is pivot-relative and so is right by
        // construction. Asserted anyway, because "by construction" is a claim about
        // the code and this is the thing that would notice if it stopped being true.
        var doc = Walking(4);
        doc.Scene.Pivot = new Pivot { X = 55, Y = 90 };
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var body = CollisionShapes.Declare(doc.Scene, "body");
        CollisionShapes.SetAcross(layer, 0, 4, body.Id, new ShapeBox(45, 35, 20, 50));

        double[] OffsetWith(SpriteTrim trim, SpritePack pack, string name)
        {
            var r = UnityExporter.Export(
                doc, At(name),
                new UnityExportOptions { Sheet = new SpriteSheetOptions { Trim = trim, Pack = pack } });
            var offset = Unity(r).GetProperty("sprites")[0].GetProperty("colliders")[0]
                .GetProperty("offset");
            return [offset[0].GetDouble(), offset[1].GetDouble()];
        }

        var union = OffsetWith(SpriteTrim.Union, SpritePack.Grid, "tu.png");
        var perFrame = OffsetWith(SpriteTrim.PerFrame, SpritePack.Grid, "tp.png");
        var packed = OffsetWith(SpriteTrim.Union, SpritePack.Skyline, "tk.png");
        var none = OffsetWith(SpriteTrim.None, SpritePack.Grid, "tn.png");

        Assert.Equal(union, perFrame);
        Assert.Equal(union, packed);
        Assert.Equal(union, none);
    }

    [Fact]
    public void WithNoPivotTheColliderIsMeasuredFromTheCellCentre()
    {
        // Unity's default sprite origin is the centre of the rect, so a collider
        // offset means nothing unless it is measured from the same place the sprite
        // will actually pivot on. A box centred on the cell has no offset.
        var doc = Walking(2);
        Assert.Null(doc.Scene.Pivot);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var body = CollisionShapes.Declare(doc.Scene, "body");
        // The ink spans x 40..80 and y 30..90 on frame 0; with a per-frame trim that
        // is the cell, so its centre is (60, 60).
        CollisionShapes.SetAcross(layer, 0, 1, body.Id, new ShapeBox(50, 50, 20, 20));

        var result = UnityExporter.Export(
            doc, At("nopivot.png"),
            new UnityExportOptions { Sheet = new SpriteSheetOptions { Trim = SpriteTrim.PerFrame } });

        var root = JsonDocument.Parse(File.ReadAllText(result.MetadataPath)).RootElement;
        var source = root.GetProperty("frames")[0].GetProperty("spriteSourceSize");
        var rect = root.GetProperty("frames")[0].GetProperty("frame");
        var centreX = source.GetProperty("x").GetInt32() + rect.GetProperty("w").GetInt32() / 2.0;
        var centreY = source.GetProperty("y").GetInt32() + rect.GetProperty("h").GetInt32() / 2.0;
        var ppu = Unity(result).GetProperty("pixelsPerUnit").GetDouble();

        var offset = Unity(result).GetProperty("sprites")[0].GetProperty("colliders")[0]
            .GetProperty("offset");

        Assert.Equal((60 - centreX) / ppu, offset[0].GetDouble(), 6);
        Assert.Equal((centreY - 60) / ppu, offset[1].GetDouble(), 6);
    }

    [Fact]
    public void AColliderIsOffsetAndSizeRatherThanARect()
    {
        // The shape Unity's own API takes, so the script assigns two Vector2s and
        // computes nothing. A rect here would move the ppu division and the Y flip
        // into the one file that cannot be tested.
        var doc = Walking(2);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        var body = CollisionShapes.Declare(doc.Scene, "body");
        CollisionShapes.SetAcross(layer, 0, 2, body.Id, new ShapeBox(45, 35, 20, 50));

        var collider = UnityExporter.Export(doc, At("shape.png"))
            .MetadataPath is { } path
            ? JsonDocument.Parse(File.ReadAllText(path)).RootElement
                .GetProperty("unity").GetProperty("sprites")[0].GetProperty("colliders")[0]
            : default;

        Assert.Equal(2, collider.GetProperty("offset").GetArrayLength());
        Assert.Equal(2, collider.GetProperty("size").GetArrayLength());
        Assert.False(collider.TryGetProperty("x", out _));
        Assert.False(collider.TryGetProperty("rect", out _));
    }

    [Fact]
    public void TheImporterExposesCollidersWithoutTryingToApplyThem()
    {
        // A Sprite is an asset and a collider is a component, so there is nothing on
        // the sliced sprite to set — the script must hand the numbers over rather
        // than pretend it can attach them. Checked as text because the script
        // references UnityEditor and cannot be compiled here.
        Assert.Contains("Collider2DSpec[] CollidersOf", UnityExporter.ImporterSource);
        // And it does not reach for Unity's sprite *physics-shape* provider, which is
        // a different thing from the sprite-rect data provider the slicing needs.
        Assert.DoesNotContain("PhysicsOutline", UnityExporter.ImporterSource);
    }

    // ---- the API the importer is allowed to use ----------------------------------

    [Fact]
    public void SlicingGoesThroughTheDataProviderOnAnyModernUnity()
    {
        // Not style. `TextureImporter.spritesheet` stopped working in Unity 2021.2 and
        // was removed in 2022.2: setting it throws nothing and slices nothing, so the
        // import reports success and produces one sprite. The first version of this
        // exporter used only that property, which means it would have silently done
        // nothing on every Unity anyone is running.
        //
        // Checked as text because the script references UnityEditor and cannot be
        // compiled here — which is the whole reason a written-down assertion about the
        // API is worth having.
        var source = UnityExporter.ImporterSource;

        Assert.Contains("#if UNITY_2021_2_OR_NEWER", source);
        Assert.Contains("using UnityEditor.U2D.Sprites;", source);
        Assert.Contains("SpriteDataProviderFactories", source);
        Assert.Contains("InitSpriteEditorDataProvider", source);
        Assert.Contains("SetSpriteRects", source);
        // Apply() before SaveAndReimport, or the rects never reach the importer.
        Assert.Contains("provider.Apply()", source);

        // The legacy property may still appear — for older editors — but only inside
        // the #else arm. Asserted by position: it must come after the modern path.
        var modern = source.IndexOf("SetSpriteRects", StringComparison.Ordinal);
        var legacy = source.IndexOf("importer.spritesheet", StringComparison.Ordinal);
        Assert.True(
            legacy < 0 || legacy > modern,
            $"legacy spritesheet assignment at {legacy} is not behind the modern path at {modern}");
    }

    [Fact]
    public void EverySlicedSpriteGetsItsOwnId()
    {
        // SpriteRect.spriteID has no counterpart in the old SpriteMetaData, so it is
        // exactly the field a mechanical port forgets. Left at its default, every
        // sprite collides on one empty id.
        Assert.Contains("spriteID = GUID.Generate()", UnityExporter.ImporterSource);
    }

    [Fact]
    public void AMissingSpritePackageIsReportedRatherThanCrashing()
    {
        // The data provider needs com.unity.2d.sprite. It is in every 2D template, but
        // "null reference in LightboxSheetImporter" is a bad way to learn that.
        Assert.Contains("com.unity.2d.sprite", UnityExporter.ImporterSource);
        Assert.Contains("if (provider == null)", UnityExporter.ImporterSource);
    }

    // ---- clips and events --------------------------------------------------------

    [Fact]
    public void EachTagBecomesAClip()
    {
        var doc = Walking(8);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "walk", Start = 0, End = 3 },
            new AnimationTag { Name = "run", Start = 4, End = 7, Loop = false },
        ];

        var result = UnityExporter.Export(doc, At("clips.png"));

        var clips = Unity(result).GetProperty("clips").EnumerateArray().ToList();
        Assert.Equal(2, clips.Count);
        Assert.Equal(2, result.ClipCount);
        Assert.Equal("walk", clips[0].GetProperty("name").GetString());
        Assert.True(clips[0].GetProperty("loop").GetBoolean());
        Assert.False(clips[1].GetProperty("loop").GetBoolean());
    }

    [Fact]
    public void AnEventIsTimedFromItsOwnClipRatherThanFromTheSheet()
    {
        // Unity's AnimationEvent.time is seconds from the *clip's* start. Getting
        // this wrong puts every event in a later clip out by that clip's offset,
        // which reads as "events fire early" and is horrible to chase.
        var doc = Walking(8);
        doc.Scene.Tags = [new AnimationTag { Name = "run", Start = 4, End = 7 }];
        doc.Scene.Markers = [new FrameMarker { Frame = 5, Label = "OnFootstep", IsEvent = true }];

        var result = UnityExporter.Export(doc, At("events.png"));

        var events = Unity(result).GetProperty("clips")[0].GetProperty("events").EnumerateArray().ToList();
        var single = Assert.Single(events);
        Assert.Equal("OnFootstep", single.GetProperty("function").GetString());
        // Frame 5 in a clip starting at 4 is one frame in, not five.
        Assert.Equal(1.0 / 12, single.GetProperty("time").GetDouble(), 10);
    }

    [Fact]
    public void AnEventOutsideAClipIsNotAttachedToIt()
    {
        var doc = Walking(8);
        doc.Scene.Tags = [new AnimationTag { Name = "walk", Start = 0, End = 3 }];
        doc.Scene.Markers = [new FrameMarker { Frame = 6, Label = "OnLate", IsEvent = true }];

        var clip = Unity(UnityExporter.Export(doc, At("outside.png"))).GetProperty("clips")[0];

        Assert.False(clip.TryGetProperty("events", out _));
    }

    [Fact]
    public void AMarkerThatIsNotAnEventNeverReachesAClip()
    {
        var doc = Walking(4);
        doc.Scene.Tags = [new AnimationTag { Name = "walk", Start = 0, End = 3 }];
        doc.Scene.Markers = [new FrameMarker { Frame = 1, Label = "check the hand" }];

        var clip = Unity(UnityExporter.Export(doc, At("note.png"))).GetProperty("clips")[0];

        Assert.False(clip.TryGetProperty("events", out _));
    }

    // ---- the importer, and the file Lightbox must never touch ---------------------

    [Fact]
    public void TheImporterIsWrittenBesideTheSheet()
    {
        var result = UnityExporter.Export(Walking(2), At("imp.png"));

        Assert.NotNull(result.ImporterPath);
        Assert.True(File.Exists(result.ImporterPath));
        var source = File.ReadAllText(result.ImporterPath!);
        Assert.Contains("LightboxSheetImporter", source);
        // Guarded, so it cannot break a player build.
        Assert.Contains("#if UNITY_EDITOR", source);
    }

    [Fact]
    public void AnEditedImporterIsNotOverwritten()
    {
        // It is source we ship, and somebody may well have adjusted it to fit their
        // project. Overwriting that on every export would be the worst kind of
        // helpful.
        var first = UnityExporter.Export(Walking(2), At("keep.png"));
        File.WriteAllText(first.ImporterPath!, "// mine now");

        UnityExporter.Export(Walking(2), At("keep.png"));

        Assert.Equal("// mine now", File.ReadAllText(first.ImporterPath!));
    }

    [Fact]
    public void NoMetaFileIsEverWritten()
    {
        // Unity owns .meta files: they carry GUIDs, they are version-specific YAML,
        // and Unity rewrites them. Hand-writing one is how importers corrupt
        // projects, so the rule gets a test rather than a comment.
        UnityExporter.Export(Walking(4), At("nometa.png"));

        Assert.Empty(Directory.GetFiles(_dir, "*.meta", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheImporterCanBeDeclined()
    {
        var result = UnityExporter.Export(
            Walking(2), At("noimp.png"), new UnityExportOptions(WriteImporter: false));

        Assert.Null(result.ImporterPath);
        Assert.Empty(Directory.GetFiles(_dir, "*.cs"));
    }

    [Fact]
    public void ExportingTwiceProducesTheSameSidecar()
    {
        var doc = Walking(6);
        doc.Scene.Tags = [new AnimationTag { Name = "walk", Start = 0, End = 5 }];

        var first = UnityExporter.Export(doc, At("det1.png"));
        var second = UnityExporter.Export(doc, At("det2.png"));

        Assert.Equal(
            File.ReadAllText(first.MetadataPath).Replace("det1", "det2"),
            File.ReadAllText(second.MetadataPath));
    }
}
