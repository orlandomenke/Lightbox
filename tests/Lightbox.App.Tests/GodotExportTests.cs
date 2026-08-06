using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

using Lightbox.Core.Projects;
namespace Lightbox.App.Tests;

/// <summary>
/// Export for Godot: the atlas, an additive block, and a GDScript importer.
/// </summary>
/// <remarks>
/// The GDScript cannot be run here, which is the same position the Unity importer is
/// in — so the assertions are about what Lightbox writes and about the rules the
/// script must obey, checked as text. The difference from Unity is deliberate and
/// worth stating: Lightbox writes <b>no</b> <c>.tres</c>, so there is no serialised
/// resource format for these tests to be wrong about.
/// </remarks>
public class GodotExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-godot-" + Guid.NewGuid().ToString("N"));

    public GodotExportTests() => Directory.CreateDirectory(_dir);

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
            double x = 40 + i * 10;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#c02040",
                Points =
                [
                    new StrokePoint(x, 30, 1), new StrokePoint(x + 30, 30, 1),
                    new StrokePoint(x + 30, 90, 1), new StrokePoint(x, 90, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }
        return doc;
    }

    private static JsonElement Godot(GodotExportResult r) =>
        JsonDocument.Parse(File.ReadAllText(r.MetadataPath)).RootElement.GetProperty("godot");

    // ---- what Lightbox does and does not write ------------------------------------

    [Fact]
    public void NoTresFileIsEverWritten()
    {
        // The load-bearing rule, and a correction to this repo's earlier plan: a format
        // being plain text is not the same as knowing it. The resource is built by
        // Godot's own API in the script, so its serialisation stays Godot's business.
        var result = GodotExporter.Export(Walking(3), At("hero.png"));

        Assert.Empty(Directory.GetFiles(_dir, "*.tres", SearchOption.AllDirectories));
        Assert.DoesNotContain("gd_resource", GodotExporter.ImporterSource[..200]);
        Assert.True(File.Exists(result.ImporterPath!));
    }

    [Fact]
    public void TheGenericSidecarKeepsEveryKeyItAlreadyHad()
    {
        var plain = SpriteSheetExporter.Export(Walking(3), At("plain.png"));
        var plainKeys = JsonDocument.Parse(File.ReadAllText(plain.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        var godot = GodotExporter.Export(Walking(3), At("godot.png"));
        var keys = JsonDocument.Parse(File.ReadAllText(godot.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.All(plainKeys, k => Assert.Contains(k, keys));
        Assert.Contains("godot", keys);
    }

    [Fact]
    public void TheGodotBlockCarriesOnlyWhatTheGenericFileLacks()
    {
        // Unlike Unity's block, which needed normalised pivots and pre-timed clips. The
        // regions, tags and fps are already in the generic file and are not duplicated,
        // because two copies of a rect are two chances to disagree.
        var result = GodotExporter.Export(Walking(3), At("lean.png"));
        var block = Godot(result);

        var keys = block.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("frameDurations", keys);
        Assert.DoesNotContain("frames", keys);
        Assert.DoesNotContain("fps", keys);
        Assert.DoesNotContain("frameTags", keys);
    }

    [Fact]
    public void FrameDurationsArriveAsMultipliersRatherThanMilliseconds()
    {
        // The unit trap. At 12 fps the sidecar says 83 ms and Godot wants 1.0; passing
        // 83 through would run one frame for nearly seven seconds.
        var result = GodotExporter.Export(Walking(4), At("dur.png"));

        var durations = Godot(result).GetProperty("frameDurations")
            .EnumerateArray().Select(d => d.GetDouble()).ToList();

        Assert.Equal(4, durations.Count);
        Assert.All(durations, d => Assert.Equal(1.0, d, 2));
    }

    [Fact]
    public void ADocumentWithNoPivotCarriesNoOffsets()
    {
        // The camera's rule again: absent, not a list of zeroes an importer would apply.
        var result = GodotExporter.Export(Walking(2), At("nopivot.png"));

        Assert.False(Godot(result).TryGetProperty("spriteOffsets", out _));
    }

    [Fact]
    public void AFeetPivotBecomesAnUpwardSpriteOffset()
    {
        var doc = Walking(3);
        doc.Scene.Pivot = new Pivot { X = 55, Y = 90 };

        var result = GodotExporter.Export(
            doc, At("pivot.png"),
            new GodotExportOptions { Sheet = new SpriteSheetOptions { Trim = SpriteTrim.Union } });

        var offsets = Godot(result).GetProperty("spriteOffsets").EnumerateArray().ToList();
        Assert.Equal(3, offsets.Count);

        // The pivot sits on the ink's bottom edge, so the drawing moves up — negative in
        // Godot's Y-down 2D space.
        var y = offsets[0][1].GetDouble();
        Assert.True(y < 0, $"offset y came out {y}, expected negative for a feet pivot");
    }

    [Fact]
    public void AnEditedImporterIsNotOverwritten()
    {
        var first = GodotExporter.Export(Walking(2), At("a.png"));
        File.WriteAllText(first.ImporterPath!, "# mine now");

        GodotExporter.Export(Walking(2), At("b.png"));

        Assert.Equal("# mine now", File.ReadAllText(first.ImporterPath!));
    }

    [Fact]
    public void TheImporterCanBeSuppressed()
    {
        var result = GodotExporter.Export(
            Walking(2), At("noscript.png"), new GodotExportOptions(WriteImporter: false));

        Assert.Null(result.ImporterPath);
        Assert.Empty(Directory.GetFiles(_dir, "*.gd"));
    }

    // ---- the rules the script must obey, checked as text --------------------------

    [Fact]
    public void TheScriptNeverTouchesProjectGodotOrTheCache()
    {
        // Godot owns those the way Unity owns .meta. A rule with a test rather than a
        // comment, because this is the one that corrupts somebody's project.
        var source = GodotExporter.ImporterSource;

        Assert.DoesNotContain("project.godot", source.Replace("# It never touches project.godot", ""));
        Assert.Contains("not name.begins_with(\".\")", source);
    }

    [Fact]
    public void TheScriptBuildsTheResourceThroughGodotsOwnApi()
    {
        // The whole reason it is a script: Lightbox hands over data, Godot builds the
        // asset, and the serialised format stays correct across versions for free.
        var source = GodotExporter.ImporterSource;

        Assert.Contains("AtlasTexture.new()", source);
        Assert.Contains("SpriteFrames.new()", source);
        Assert.Contains("ResourceSaver.save", source);
        Assert.Contains("add_frame(", source);
        Assert.Contains("set_animation_speed(", source);
    }

    [Fact]
    public void TheScriptOnlyClaimsSidecarsThatAreOurs()
    {
        // Scanning all of res:// for *.json and importing whatever parses would be a
        // trap in a project with its own data files.
        var source = GodotExporter.ImporterSource;

        Assert.Contains("data.has(\"godot\")", source);
        Assert.Contains("\"app\", \"\") == \"Lightbox\"", source);
    }

    [Fact]
    public void TheScriptClipsAtlasFilteringSoNeighbouringSpritesCannotBleed()
    {
        // Filtering off the edge of a region is what puts a sliver of the next sprite
        // on this one, and it is the first thing anybody notices on an atlas.
        Assert.Contains("filter_clip = true", GodotExporter.ImporterSource);
    }

    [Fact]
    public void TheScriptRemovesTheDefaultAnimationWhenTagsSupplyTheirOwn()
    {
        // A new SpriteFrames comes with "default" in it, so tagged sheets would arrive
        // with an empty animation nobody made.
        Assert.Contains("remove_animation(\"default\")", GodotExporter.ImporterSource);
    }

    [Fact]
    public void TheScriptSaysItIsGodotFourAndWhy()
    {
        // add_frame took no duration before 4.0, so the timing would be silently dropped
        // on 3.x — the same class of silent failure as the Unity slicing defect, recorded
        // where somebody reading the script will see it.
        Assert.Contains("Godot 4", GodotExporter.ImporterSource);
        Assert.Contains("3.x", GodotExporter.ImporterSource);
    }

    // ---- through the runner --------------------------------------------------------

    [Fact]
    public void AGodotPresetWritesTheSheetTheSidecarAndTheScript()
    {
        var run = ExportRunner.Run(
            Walking(3), new ExportPreset { Name = "g", Target = ExportTarget.Godot },
            At("run.png"));

        Assert.Equal(3, run.Files.Count);
        Assert.Contains("lightbox_import.gd", run.Files[2]);
        Assert.Contains("for Godot", run.Summary);
        Assert.All(run.Files, f => Assert.True(File.Exists(f), $"{f} was not written"));
    }

    [Fact]
    public void AGodotPresetStillReportsWhatItLeftOut()
    {
        var doc = Walking(2);
        doc.Scene.Layers.First(l => !l.IsBackground).OmitFromExport = true;

        var run = ExportRunner.Run(
            doc, new ExportPreset { Name = "g", Target = ExportTarget.Godot }, At("rep.png"));

        Assert.Contains(run.Omitted, o => o.Signal == Lightbox.Core.Export.BackgroundSignal.Pinned);
    }
}
