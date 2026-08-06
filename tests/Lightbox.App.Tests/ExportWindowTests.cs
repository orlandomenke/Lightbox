using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

using Lightbox.Core.Projects;
namespace Lightbox.App.Tests;

/// <summary>
/// P5f: the export window, the preset store, and the runner that turns one into files.
/// </summary>
/// <remarks>
/// The exporters themselves are tested elsewhere; these guard the last mile — that a
/// preset survives a round trip, that a target maps to the right files, that the
/// report reaches the artist, and that a control which does not apply is hidden rather
/// than lying.
/// </remarks>
[Collection("ExportPresetFile")]
public class ExportWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-export-" + Guid.NewGuid().ToString("N"));

    public ExportWindowTests()
    {
        Directory.CreateDirectory(_dir);
        ExportPresetStore.PathOverride = Path.Combine(_dir, "export.json");
    }

    public void Dispose()
    {
        ExportPresetStore.PathOverride = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Doc Walking(int frames = 3)
    {
        var doc = DocumentFactory.CreateDoc(120, 90, 12, "#ffffff");
        doc.Scene.FrameCount = frames;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel { Frame = new PaintedFrame() });

        for (var i = 0; i < frames; i++)
        {
            if (layer.Cels[i].Frame is not PaintedFrame p) continue;
            double x = 30 + i * 8;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#c02040",
                Points =
                [
                    new StrokePoint(x, 20, 1), new StrokePoint(x + 24, 20, 1),
                    new StrokePoint(x + 24, 70, 1), new StrokePoint(x, 70, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }
        return doc;
    }

    // ---- the preset store ---------------------------------------------------------

    [Fact]
    public void APresetRoundTripsThroughTheFile()
    {
        var preset = new ExportPreset
        {
            Name = "Knight atlas",
            Target = ExportTarget.Unity,
            Trim = SpriteTrim.PerFrame,
            Pack = SpritePack.Skyline,
            Padding = 2,
            Columns = 5,
            Background = BackgroundHandling.Detected,
            WorldHeightUnits = 1.8,
            WriteImporter = false,
        };

        ExportPresetStore.Save([preset], lastUsed: preset.Name);
        var back = Assert.Single(ExportPresetStore.Load());

        Assert.Equal(preset, back);
        Assert.Equal("Knight atlas", ExportPresetStore.LoadLastUsed());
    }

    [Fact]
    public void TheBuiltInsAreNeverWrittenToTheFile()
    {
        // Writing them out would freeze them into every artist's settings the first
        // time they opened the app, so a later correction to "Character sprites" would
        // reach nobody.
        ExportPresetStore.Save([]);
        Assert.Empty(ExportPresetStore.Load());
        Assert.Equal(3, ExportPreset.BuiltIns.Count);

        var json = File.ReadAllText(ExportPresetStore.Path);
        Assert.DoesNotContain("Character sprites", json);
    }

    [Fact]
    public void ACorruptFileLeavesAWorkingAppRatherThanADialog()
    {
        File.WriteAllText(ExportPresetStore.Path, "{ this is not json");

        Assert.Empty(ExportPresetStore.Load());
        Assert.Null(ExportPresetStore.LoadLastUsed());
    }

    [Fact]
    public void ANamelessPresetIsDroppedRatherThanShownAsABlankRow()
    {
        ExportPresetStore.Save([new ExportPreset { Name = "  " }, new ExportPreset { Name = "Real" }]);
        Assert.Equal("Real", Assert.Single(ExportPresetStore.Load()).Name);
    }

    [Fact]
    public void TheBuiltInsTakeThePositionsThisPillarAlreadyArguedFor()
    {
        // Named for the job, and each one is a settled argument rather than a taste:
        // union trim stops the character jittering, the grid is what every importer
        // reads, and packing only wins on ragged per-frame cells.
        var character = ExportPreset.BuiltIns.Single(p => p.Name == "Character sprites");
        Assert.Equal(SpriteTrim.Union, character.Trim);
        Assert.Equal(SpritePack.Grid, character.Pack);
        Assert.Equal(BackgroundHandling.Detected, character.Background);

        var atlas = ExportPreset.BuiltIns.Single(p => p.Name == "Packed atlas");
        Assert.Equal(SpriteTrim.PerFrame, atlas.Trim);
        Assert.Equal(SpritePack.Skyline, atlas.Pack);

        var backdrop = ExportPreset.BuiltIns.Single(p => p.Name == "Backdrop");
        Assert.Equal(BackgroundHandling.Everything, backdrop.Background);
        Assert.Equal(SpriteTrim.None, backdrop.Trim);
    }

    // ---- the runner ---------------------------------------------------------------

    [Fact]
    public void ASheetPresetWritesTheImageAndTheSidecar()
    {
        var run = ExportRunner.Run(
            Walking(), new ExportPreset { Name = "s" }, Path.Combine(_dir, "sheet.png"));

        Assert.Equal(2, run.Files.Count);
        Assert.All(run.Files, f => Assert.True(File.Exists(f), $"{f} was not written"));
        Assert.Contains(".json", run.Files[1]);
        // The summary carries numbers, because "Export complete" tells an artist
        // nothing they could act on.
        Assert.Contains("3 frame(s)", run.Summary);
    }

    [Fact]
    public void AUnityPresetAlsoWritesTheImporterAndKeepsItsBlock()
    {
        // The bug this guards: getting the omitted-layer report by exporting a second
        // time would rewrite the sidecar and strip the unity block back off it.
        var run = ExportRunner.Run(
            Walking(), new ExportPreset { Name = "u", Target = ExportTarget.Unity },
            Path.Combine(_dir, "u.png"));

        Assert.Equal(3, run.Files.Count);
        Assert.Contains("LightboxSheetImporter.cs", run.Files[2]);

        var json = File.ReadAllText(run.Files[1]);
        Assert.Contains("\"unity\"", json);
        Assert.Contains("pixelsPerUnit", json);
    }

    [Fact]
    public void AUnityPresetStillReportsWhatItLeftOut()
    {
        var doc = Walking(2);
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        layer.OmitFromExport = true;

        var run = ExportRunner.Run(
            doc, new ExportPreset { Name = "u", Target = ExportTarget.Unity },
            Path.Combine(_dir, "ur.png"));

        Assert.Contains(run.Omitted, o => o.Signal == BackgroundSignal.Pinned);
    }

    [Fact]
    public void APngSequencePresetWritesFramesAndReportsNoOmissions()
    {
        var folder = Path.Combine(_dir, "seq");
        Directory.CreateDirectory(folder);

        var run = ExportRunner.Run(
            Walking(4), new ExportPreset { Name = "p", Target = ExportTarget.PngSequence }, folder);

        Assert.Equal(4, run.Files.Count);
        // A sequence composes the whole scene, paper included, and always has. Claiming
        // background handling applied would be the dishonest answer.
        Assert.Empty(run.Omitted);
        Assert.Contains("PNG frame(s)", run.Summary);
    }

    // ---- the report reaches the artist --------------------------------------------

    [AvaloniaFact]
    public void TheStatusLineNamesTheLayersItLeftOut()
    {
        // Names, not a count: "2 layers left out" is exactly as unhelpful as silence
        // when the question is *which*.
        var vm = new MainViewModel(artist: null);
        var run = new ExportRun(
            ["sheet.png"],
            "3 frame(s)",
            [
                new OmittedLayer("l1", "Grey", BackgroundSignal.FullCanvasFill),
                new OmittedLayer("l2", "Ref photo", BackgroundSignal.Pinned),
            ],
            [new SuspectedBackground("l3", "Sky", "reads like a background")]);

        var line = vm.DescribeExport(run);

        Assert.Contains("Grey (fills the canvas)", line);
        Assert.Contains("Ref photo (never export)", line);
        Assert.Contains("Sky", line);
        Assert.Contains("3 frame(s)", line);
    }

    [AvaloniaFact]
    public void AnExportWithNothingLeftOutSaysOnlyWhatItMade()
    {
        var vm = new MainViewModel(artist: null);
        var line = vm.DescribeExport(new ExportRun(["a.png"], "4 frame(s), 200x100 px", [], []));

        Assert.Equal("4 frame(s), 200x100 px", line);
    }

    // ---- the window ---------------------------------------------------------------

    [AvaloniaFact]
    public void TheControlsRoundTripAPreset()
    {
        var window = new ExportWindow();
        var preset = new ExportPreset
        {
            Name = "rt",
            Target = ExportTarget.Unity,
            Trim = SpriteTrim.PerFrame,
            Pack = SpritePack.Skyline,
            Padding = 3,
            Background = BackgroundHandling.Everything,
            WorldHeightUnits = 2.5,
            WriteImporter = false,
        };

        window.ShowForTests(preset);
        var back = window.GatherForTests("rt");

        // Columns is not on the window, so compare the rest field by field rather than
        // asserting record equality and having it fail for a reason that is by design.
        Assert.Equal(preset.Target, back.Target);
        Assert.Equal(preset.Trim, back.Trim);
        Assert.Equal(preset.Pack, back.Pack);
        Assert.Equal(preset.Padding, back.Padding);
        Assert.Equal(preset.Background, back.Background);
        Assert.Equal(preset.WorldHeightUnits, back.WorldHeightUnits, 6);
        Assert.Equal(preset.WriteImporter, back.WriteImporter);
    }

    [AvaloniaFact]
    public void WhatDoesNotApplyIsHiddenRatherThanDisabled()
    {
        // A PNG sequence has no cells, no atlas and no sidecar. Offering it a layout
        // picker teaches an artist that the controls lie — the camera's rule, applied
        // to a dialog.
        var window = new ExportWindow();

        window.ShowForTests(new ExportPreset { Name = "seq", Target = ExportTarget.PngSequence });
        Assert.False(window.SheetRowsVisibleForTests);
        Assert.False(window.EngineRowsVisibleForTests);

        window.ShowForTests(new ExportPreset { Name = "sheet", Target = ExportTarget.SpriteSheet });
        Assert.True(window.SheetRowsVisibleForTests);
        Assert.False(window.EngineRowsVisibleForTests);

        window.ShowForTests(new ExportPreset { Name = "unity", Target = ExportTarget.Unity });
        Assert.True(window.SheetRowsVisibleForTests);
        Assert.True(window.EngineRowsVisibleForTests);
    }

    [AvaloniaFact]
    public void SavingAPresetKeepsItAndSelectsIt()
    {
        var window = new ExportWindow();
        window.ShowForTests(new ExportPreset { Name = "x", Trim = SpriteTrim.None });

        window.SavePresetForTests("Mine");

        var saved = Assert.Single(ExportPresetStore.Load());
        Assert.Equal("Mine", saved.Name);
        Assert.Equal(SpriteTrim.None, saved.Trim);
    }

    [AvaloniaFact]
    public void SavingOverAnExistingNameReplacesItRatherThanAddingASecondRow()
    {
        var window = new ExportWindow();
        window.ShowForTests(new ExportPreset { Name = "x", Trim = SpriteTrim.None });
        window.SavePresetForTests("Mine");

        window.ShowForTests(new ExportPreset { Name = "x", Trim = SpriteTrim.PerFrame });
        window.SavePresetForTests("mine");   // same name, different case

        var saved = Assert.Single(ExportPresetStore.Load());
        Assert.Equal(SpriteTrim.PerFrame, saved.Trim);
    }

    [AvaloniaFact]
    public void ABlankNameSavesNothing()
    {
        var window = new ExportWindow();
        window.SavePresetForTests("   ");
        Assert.Empty(ExportPresetStore.Load());
    }

    [AvaloniaFact]
    public void ABuiltInCannotBeDeleted()
    {
        // The three built-ins are the floor. Deleting one would leave an artist with no
        // working starting point and nothing to restore it from.
        var window = new ExportWindow();
        window.SelectPresetForTests(0);

        window.DeletePresetForTests();

        Assert.Equal(3, ExportPreset.BuiltIns.Count);
        Assert.Empty(ExportPresetStore.Load());
    }

    // ---- normal maps --------------------------------------------------------------

    [Fact]
    public void ASheetPresetWithANormalMapWritesItBesideTheSheet()
    {
        var run = ExportRunner.Run(
            Walking(), new ExportPreset { Name = "n", NormalMap = true },
            Path.Combine(_dir, "hero.png"));

        Assert.Equal(3, run.Files.Count);
        var map = run.Files[2];
        Assert.EndsWith("hero_normal.png", map);
        Assert.True(File.Exists(map));
        Assert.Contains("normal map", run.Summary);
    }

    [Fact]
    public void ThereIsNoNormalMapUnlessItIsAskedFor()
    {
        // Off by default: it doubles the asset's texture memory and most 2D games do not
        // light their sprites at all.
        var run = ExportRunner.Run(
            Walking(), new ExportPreset { Name = "n" }, Path.Combine(_dir, "plain.png"));

        Assert.Equal(2, run.Files.Count);
        Assert.False(File.Exists(NormalMapWriter.PathFor(Path.Combine(_dir, "plain.png"))));
    }

    [Fact]
    public void TheMapIsTheSameSizeAsTheSheetItCameFrom()
    {
        // Derived from the finished sheet rather than re-composed, so it lines up cell
        // for cell by construction. A map one pixel out of register with its albedo puts
        // a bright rim on every silhouette.
        var run = ExportRunner.Run(
            Walking(4),
            new ExportPreset { Name = "n", NormalMap = true, Trim = SpriteTrim.PerFrame, Pack = SpritePack.Skyline },
            Path.Combine(_dir, "reg.png"));

        using var sheet = SkiaSharp.SKBitmap.Decode(run.Files[0]);
        using var map = SkiaSharp.SKBitmap.Decode(run.Files[2]);

        Assert.Equal(sheet.Width, map.Width);
        Assert.Equal(sheet.Height, map.Height);
    }

    [Fact]
    public void AUnityPresetGetsTheMapTooAndStillKeepsItsBlock()
    {
        var run = ExportRunner.Run(
            Walking(2),
            new ExportPreset { Name = "n", Target = ExportTarget.Unity, NormalMap = true },
            Path.Combine(_dir, "un.png"));

        Assert.Equal(4, run.Files.Count);
        Assert.Contains(run.Files, f => f.EndsWith("un_normal.png"));
        Assert.Contains("\"unity\"", File.ReadAllText(run.Files[1]));
    }

    [AvaloniaFact]
    public void TheMapSettingsAppearOnlyOnceTheMapIsAskedFor()
    {
        // One line until somebody wants four. And absent entirely for a PNG sequence,
        // which has no sheet to map.
        var window = new ExportWindow();

        window.ShowForTests(new ExportPreset { Name = "s", Target = ExportTarget.SpriteSheet });
        Assert.False(window.NormalRowsVisibleForTests);

        window.TickNormalMapForTests(true);
        Assert.True(window.NormalRowsVisibleForTests);

        window.ShowForTests(new ExportPreset
        {
            Name = "seq", Target = ExportTarget.PngSequence, NormalMap = true,
        });
        Assert.False(window.NormalRowsVisibleForTests);
    }

    [AvaloniaFact]
    public void TheGreenConventionRoundTripsThroughTheWindow()
    {
        // The setting a flipped default would silently break, so it is asserted in both
        // directions rather than only on the non-default one.
        var window = new ExportWindow();

        window.ShowForTests(new ExportPreset
        {
            Name = "u", NormalMap = true,
            Normal = new NormalMapOptions(Green: NormalGreen.DirectX, BevelPixels: 3, Strength: 2),
        });
        var dx = window.GatherForTests();
        Assert.Equal(NormalGreen.DirectX, dx.Normal.Green);
        Assert.Equal(3, dx.Normal.BevelPixels, 6);
        Assert.Equal(2, dx.Normal.Strength, 6);

        window.ShowForTests(new ExportPreset { Name = "g", NormalMap = true });
        Assert.Equal(NormalGreen.OpenGl, window.GatherForTests().Normal.Green);
    }

    [AvaloniaFact]
    public void AGarbledNumberFallsBackRatherThanRefusingTheExport()
    {
        // Nobody is served by a dialog that will not let them out over a stray
        // character in a gutter width. Typed as text, because setting the *value* and
        // reading it back would pass on a version that cannot parse anything.
        var window = new ExportWindow();
        window.ShowForTests(new ExportPreset { Name = "x", Padding = 4, WorldHeightUnits = 2 });

        window.TypeForTests(padding: "four", worldHeight: "");
        var recovered = window.GatherForTests();
        Assert.Equal(0, recovered.Padding);
        Assert.Equal(1.0, recovered.WorldHeightUnits, 6);

        // A negative gutter is not a gutter, and a canvas zero units tall is not a
        // canvas — both fall back rather than reaching the exporter.
        window.TypeForTests(padding: "-3", worldHeight: "0");
        var clamped = window.GatherForTests();
        Assert.Equal(0, clamped.Padding);
        Assert.Equal(1.0, clamped.WorldHeightUnits, 6);

        // And a real number still gets through, or the three above would pass on a
        // window that ignored the boxes entirely.
        window.TypeForTests(padding: "6", worldHeight: "2.5");
        var good = window.GatherForTests();
        Assert.Equal(6, good.Padding);
        Assert.Equal(2.5, good.WorldHeightUnits, 6);
    }
}
