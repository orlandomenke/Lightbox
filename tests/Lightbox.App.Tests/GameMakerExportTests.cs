using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using SkiaSharp;

using Lightbox.Core.Projects;
namespace Lightbox.App.Tests;

/// <summary>
/// Export for GameMaker: strips named so GameMaker slices them, and no project files.
/// </summary>
/// <remarks>
/// <para>
/// The one engine target with nothing to run and nothing to inspect on the far side, so
/// unusually these tests can check almost everything that matters — the layout is
/// verifiable from the PNG itself, and the naming contract is a string. What cannot be
/// checked here is GameMaker's half of the convention, which is documented behaviour
/// rather than something we implement.
/// </para>
/// <para>
/// The layout assertions are the load-bearing ones, and they are pixel assertions rather
/// than option assertions on purpose: "we passed Padding = 0" is not the same claim as
/// "the cells are where division says they are".
/// </para>
/// </remarks>
public class GameMakerExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-gm-" + Guid.NewGuid().ToString("N"));

    public GameMakerExportTests() => Directory.CreateDirectory(_dir);

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
        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel { Frame = new Frame() });

        for (var i = 0; i < frames; i++)
        {
            if (layer.Cels[i].Frame is not Frame p) continue;
            double x = 40 + i * 10;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#20a040",
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

    private static Doc Tagged()
    {
        var doc = Walking(6);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "run cycle", Start = 0, End = 3 },
            new AnimationTag { Name = "idle", Start = 4, End = 5 },
        ];
        return doc;
    }

    private static JsonElement Block(GameMakerExportResult r) =>
        JsonDocument.Parse(File.ReadAllText(r.MetadataPath)).RootElement.GetProperty("gamemaker");

    // ---- the naming contract, end to end -------------------------------------------

    [Fact]
    public void AnUntaggedDocumentIsOneStripNamedWithItsFrameCount()
    {
        var result = GameMakerExporter.Export(Walking(4), At("walk.png"));

        var strip = Assert.Single(result.StripPaths);
        Assert.Equal("walk_strip4.png", Path.GetFileName(strip));
        Assert.True(File.Exists(strip));
    }

    [Fact]
    public void TheNumberInTheNameIsTheNumberOfCellsInTheImage()
    {
        // The contract that matters, checked against the image rather than the options:
        // GameMaker divides the width by this number, so if they disagree every frame is
        // sliced through the middle.
        var result = GameMakerExporter.Export(Walking(5), At("run.png"));
        var strip = Assert.Single(result.StripPaths);

        var (_, claimed) = GameMakerConvert.ReadStripName(strip);
        using var image = SKBitmap.Decode(strip);

        Assert.Equal(5, claimed);
        Assert.Equal(0, image.Width % claimed);
        Assert.Equal(image.Width / claimed, Block(result).GetProperty("frameWidth").GetInt32());
    }

    [Fact]
    public void NoProjectFileIsEverWritten()
    {
        // The decision this target rests on: .yy is version-coupled and carries GUIDs the
        // IDE owns, so it is not written at all.
        GameMakerExporter.Export(Walking(3), At("hero.png"));

        Assert.Empty(Directory.GetFiles(_dir, "*.yy", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_dir, "*.yyp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_dir, "*.yymps", SearchOption.AllDirectories));
    }

    [Fact]
    public void ThereIsNoImporterScriptBecauseGameMakerCannotRunOne()
    {
        // Unlike all three other engine targets. Asserted so nobody adds one on the
        // assumption that the pattern is universal.
        GameMakerExporter.Export(Walking(3), At("hero.png"));

        Assert.Empty(Directory.GetFiles(_dir, "*.py"));
        Assert.Empty(Directory.GetFiles(_dir, "*.gd"));
        Assert.Empty(Directory.GetFiles(_dir, "*.cs"));
    }

    // ---- the layout is forced, and checked in pixels --------------------------------

    [Fact]
    public void TheStripIsOneRow()
    {
        var result = GameMakerExporter.Export(Walking(6), At("row.png"));
        var strip = Assert.Single(result.StripPaths);

        using var image = SKBitmap.Decode(strip);
        var block = Block(result);

        Assert.Equal(block.GetProperty("frameHeight").GetInt32(), image.Height);
        Assert.Equal(block.GetProperty("frameWidth").GetInt32() * 6, image.Width);
    }

    [Fact]
    public void APaddingRequestIsOverriddenRatherThanHonoured()
    {
        // One pixel of gutter would put every frame a fraction of a cell out, because the
        // cell width is derived by division. Checked in pixels: with padding the width
        // would not divide cleanly by the frame count.
        var result = GameMakerExporter.Export(
            Walking(4), At("pad.png"),
            new GameMakerExportOptions { Sheet = new SpriteSheetOptions { Padding = 3 } });

        using var image = SKBitmap.Decode(Assert.Single(result.StripPaths));
        Assert.Equal(0, image.Width % 4);
        Assert.Equal(image.Width / 4, Block(result).GetProperty("frameWidth").GetInt32());
    }

    [Fact]
    public void APackedLayoutIsOverriddenRatherThanHonoured()
    {
        var result = GameMakerExporter.Export(
            Walking(4), At("packed.png"),
            new GameMakerExportOptions { Sheet = new SpriteSheetOptions { Pack = SpritePack.Skyline } });

        using var image = SKBitmap.Decode(Assert.Single(result.StripPaths));
        var block = Block(result);

        Assert.Equal(block.GetProperty("frameHeight").GetInt32(), image.Height);
        Assert.Equal(block.GetProperty("frameWidth").GetInt32() * 4, image.Width);
    }

    [Fact]
    public void APerFrameTrimIsRefusedAndSaidSoRatherThanSilentlyChanged()
    {
        // Cells of differing widths cannot be a strip. The point of the note is that the
        // artist finds out from the export rather than from a wobbling sprite.
        var result = GameMakerExporter.Export(
            Walking(4), At("trim.png"),
            new GameMakerExportOptions { Sheet = new SpriteSheetOptions { Trim = SpriteTrim.PerFrame } });

        Assert.Contains(result.Notes, n => n.Contains("per-frame trim"));

        using var image = SKBitmap.Decode(Assert.Single(result.StripPaths));
        Assert.Equal(0, image.Width % 4);
    }

    [Fact]
    public void ANoneTrimIsStillHonouredBecauseItKeepsCellsUniform()
    {
        // The trim is the one layout control that survives: none and union both give
        // uniform cells, so both are legitimate choices for a strip.
        var result = GameMakerExporter.Export(
            Walking(3), At("notrim.png"),
            new GameMakerExportOptions { Sheet = new SpriteSheetOptions { Trim = SpriteTrim.None } });

        Assert.Empty(result.Notes);
        Assert.Equal(200, Block(result).GetProperty("frameWidth").GetInt32());
    }

    // ---- one strip per animation ---------------------------------------------------

    [Fact]
    public void EachTagBecomesItsOwnStripBecauseAGameMakerSpriteIsOneAnimation()
    {
        var result = GameMakerExporter.Export(Tagged(), At("Knight.png"));

        var names = result.StripPaths.Select(Path.GetFileName).ToList();
        Assert.Equal(["Knight_run_cycle_strip4.png", "Knight_idle_strip2.png"], names);
        Assert.All(result.StripPaths, p => Assert.True(File.Exists(p), $"{p} was not written"));
    }

    [Fact]
    public void EachTagsStripIsExactlyItsOwnFramesWide()
    {
        var result = GameMakerExporter.Export(Tagged(), At("Knight.png"));
        var cell = Block(result).GetProperty("frameWidth").GetInt32();

        using var run = SKBitmap.Decode(result.StripPaths[0]);
        using var idle = SKBitmap.Decode(result.StripPaths[1]);

        Assert.Equal(cell * 4, run.Width);
        Assert.Equal(cell * 2, idle.Width);
    }

    [Fact]
    public void ATagsStripHoldsTheFramesThatTagCoversAndNotTheOnesBeforeIt()
    {
        // The crop has to start at the tag's first frame. Off by one cell and the second
        // animation is the tail of the first — which plays, and looks nearly right.
        var doc = Tagged();
        var result = GameMakerExporter.Export(doc, At("Knight.png"));
        var cell = Block(result).GetProperty("frameWidth").GetInt32();

        using var whole = SKBitmap.Decode(At("Knight_run_cycle_strip4.png"));
        using var idle = SKBitmap.Decode(At("Knight_idle_strip2.png"));

        // Frame 4 of the document is the first cell of "idle" and the fifth cell overall,
        // so it cannot also be inside the four-cell run strip. Compared as pixels: the
        // idle strip's first cell must differ from every cell of the run strip, because
        // the test document moves the drawing 10px per frame.
        var first = ColumnSignature(idle, 0, cell);
        for (var i = 0; i < 4; i++)
        {
            Assert.NotEqual(first, ColumnSignature(whole, i, cell));
        }
    }

    /// <summary>Where the ink is in one cell, as a crude signature.</summary>
    private static int ColumnSignature(SKBitmap image, int cell, int cellWidth)
    {
        var left = cell * cellWidth;
        for (var x = left; x < Math.Min(left + cellWidth, image.Width); x++)
        {
            for (var y = 0; y < image.Height; y++)
            {
                if (image.GetPixel(x, y).Alpha > 8) return x - left;
            }
        }
        return -1;
    }

    [Fact]
    public void TheWholeSheetIsNotLeftBehindWhenItWasCutUp()
    {
        // It is an intermediate, and an extra strip in the folder is one somebody imports
        // by accident.
        var result = GameMakerExporter.Export(Tagged(), At("Knight.png"));

        Assert.False(File.Exists(At("Knight_strip6.png")));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.png").Length);
        Assert.Equal(2, result.StripPaths.Count);
    }

    [Fact]
    public void TwoTagsWithTheSameNameDoNotOverwriteEachOther()
    {
        var doc = Walking(4);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "run", Start = 0, End = 1 },
            new AnimationTag { Name = "run", Start = 2, End = 3 },
        ];

        var result = GameMakerExporter.Export(doc, At("Knight.png"));

        Assert.Equal(2, result.StripPaths.Count);
        Assert.Equal(2, result.StripPaths.Distinct().Count());
        Assert.All(result.StripPaths, p => Assert.True(File.Exists(p)));
    }

    // ---- what GameMaker cannot do is said out loud ----------------------------------

    [Fact]
    public void TheSpeedIsGivenInFramesPerSecondBecauseTheEditorOffersTwoUnits()
    {
        var result = GameMakerExporter.Export(Walking(3), At("fps.png"));

        Assert.Equal(12, Block(result).GetProperty("imageFps").GetInt32());
    }

    [Fact]
    public void AHoldIsAlreadyExpressedAsRepeatedCellsSoUniformSpeedIsEnough()
    {
        // Worth an explicit test because it is the reason a single animation speed is not
        // a limitation here: the sheet writes one cell per timeline frame, so a drawing
        // held on 2s is two identical cells — which is exactly how a strip says "hold".
        var doc = Walking(4);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.Cels[1].Frame = null;   // frame 1 holds frame 0
        layer.Cels[3].Frame = null;   // frame 3 holds frame 2

        var result = GameMakerExporter.Export(doc, At("twos.png"));
        var strip = Assert.Single(result.StripPaths);
        var cell = Block(result).GetProperty("frameWidth").GetInt32();

        Assert.Equal("twos_strip4.png", Path.GetFileName(strip));

        using var image = SKBitmap.Decode(strip);
        // Cell 1 repeats cell 0, and cell 3 repeats cell 2.
        Assert.Equal(ColumnSignature(image, 0, cell), ColumnSignature(image, 1, cell));
        Assert.Equal(ColumnSignature(image, 2, cell), ColumnSignature(image, 3, cell));
        Assert.NotEqual(ColumnSignature(image, 0, cell), ColumnSignature(image, 2, cell));
    }

    [Fact]
    public void APingPongTagIsReportedRatherThanQuietlyFlattened()
    {
        var doc = Walking(4);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "swing", Start = 0, End = 3, Direction = TagDirection.PingPong },
        ];

        var result = GameMakerExporter.Export(doc, At("ping.png"));

        Assert.Contains(result.Notes, n => n.Contains("swing") && n.Contains("image_index"));
    }

    [Fact]
    public void ATagThatDoesNotLoopIsReportedBecauseAGameMakerSpriteAlwaysDoes()
    {
        var doc = Walking(4);
        doc.Scene.Tags = [new AnimationTag { Name = "die", Start = 0, End = 3, Loop = false }];

        var result = GameMakerExporter.Export(doc, At("die.png"));

        Assert.Contains(result.Notes, n => n.Contains("die") && n.Contains("does not loop"));
    }

    [Fact]
    public void AnOrdinaryDocumentHasNothingToReportAndSaysNothing()
    {
        // The other half of the notes being useful: they must be empty when there is
        // nothing wrong, or nobody reads them.
        var result = GameMakerExporter.Export(Walking(4), At("plain.png"));

        Assert.Empty(result.Notes);
        Assert.False(Block(result).TryGetProperty("notes", out _));
    }

    // ---- the sidecar ---------------------------------------------------------------

    [Fact]
    public void TheGenericSidecarKeepsEveryKeyItAlreadyHad()
    {
        var plain = SpriteSheetExporter.Export(Walking(3), At("generic.png"));
        var plainKeys = JsonDocument.Parse(File.ReadAllText(plain.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        var gm = GameMakerExporter.Export(Walking(3), At("gm.png"));
        var keys = JsonDocument.Parse(File.ReadAllText(gm.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.All(plainKeys, k => Assert.Contains(k, keys));
        Assert.Contains("gamemaker", keys);
    }

    [Fact]
    public void TheSidecarNamesEveryStripAndTheSpriteItBecomes()
    {
        var result = GameMakerExporter.Export(Tagged(), At("Knight.png"));

        var strips = Block(result).GetProperty("strips").EnumerateArray().ToList();
        Assert.Equal(2, strips.Count);
        Assert.Equal("Knight_run_cycle_strip4.png", strips[0].GetProperty("file").GetString());
        // The sprite name GameMaker will make, which is the file without the suffix.
        Assert.Equal("Knight_run_cycle", strips[0].GetProperty("sprite").GetString());
        Assert.Equal(4, strips[0].GetProperty("frames").GetInt32());
        Assert.Equal(4, strips[1].GetProperty("from").GetInt32());
    }

    [Fact]
    public void ADocumentWithNoPivotCarriesNoOrigin()
    {
        var result = GameMakerExporter.Export(Walking(2), At("noorigin.png"));

        Assert.False(Block(result).TryGetProperty("origin", out _));
    }

    [Fact]
    public void APivotBecomesAnOriginInsideTheCell()
    {
        var doc = Walking(3);
        doc.Scene.Pivot = new Pivot { X = 55, Y = 90 };

        var result = GameMakerExporter.Export(doc, At("origin.png"));

        var origin = Block(result).GetProperty("origin").EnumerateArray()
            .Select(v => v.GetDouble()).ToList();
        var cell = Block(result).GetProperty("frameWidth").GetInt32();

        Assert.True(origin[0] >= 0 && origin[0] <= cell,
            $"origin x {origin[0]} is outside a {cell}px cell");
        Assert.True(origin[1] >= 0, $"origin y {origin[1]} is above the cell");
    }

    // ---- through the runner --------------------------------------------------------

    [Fact]
    public void AGameMakerPresetWritesEveryStripAndTheSidecar()
    {
        var run = ExportRunner.Run(
            Tagged(), new ExportPreset { Name = "gm", Target = ExportTarget.GameMaker },
            At("run.png"));

        Assert.Equal(3, run.Files.Count);   // two strips and the sidecar
        Assert.Contains("for GameMaker", run.Summary);
        Assert.Contains("strip", run.Summary);
        Assert.All(run.Files, f => Assert.True(File.Exists(f), $"{f} was not written"));
    }

    [Fact]
    public void TheRunnerPutsTheNotesInTheSummaryWhereSomebodyWillSeeThem()
    {
        // "Your ping-pong tag will not ping-pong" is worth reading before the import, not
        // after — so it goes in the status line and not only in the file.
        var doc = Walking(4);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "swing", Start = 0, End = 3, Direction = TagDirection.PingPong },
        ];

        var run = ExportRunner.Run(
            doc, new ExportPreset { Name = "gm", Target = ExportTarget.GameMaker }, At("note.png"));

        Assert.Contains("swing", run.Summary);
    }

    [Fact]
    public void AGameMakerPresetStillReportsWhatItLeftOut()
    {
        var doc = Walking(2);
        doc.Scene.Layers.First(l => !l.IsBackground).OmitFromExport = true;

        var run = ExportRunner.Run(
            doc, new ExportPreset { Name = "gm", Target = ExportTarget.GameMaker }, At("rep.png"));

        Assert.Contains(run.Omitted, o => o.Signal == BackgroundSignal.Pinned);
    }

    [Fact]
    public void ANormalMapIsWrittenForEveryStripRatherThanOnlyTheFirst()
    {
        // The strips are separate images, so one map could only be in register with one
        // of them — and a normal map a pixel out of register rims every silhouette.
        var run = ExportRunner.Run(
            Tagged(),
            new ExportPreset { Name = "gm", Target = ExportTarget.GameMaker, NormalMap = true },
            At("lit.png"));

        Assert.Equal(2, Directory.GetFiles(_dir, "*_normal.png").Length);
        Assert.Equal(5, run.Files.Count);   // two strips, two maps, the sidecar
    }

    [Fact]
    public void TheStripLayoutControlsAreHiddenRatherThanShownAndOverridden()
    {
        var gm = new ExportPreset { Name = "gm", Target = ExportTarget.GameMaker };

        Assert.True(gm.UsesSheetSettings);
        Assert.True(gm.UsesStripLayout);
        Assert.False(new ExportPreset { Name = "s", Target = ExportTarget.SpriteSheet }.UsesStripLayout);
    }
}
