using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

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
