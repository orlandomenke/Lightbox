using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Exporting because a document was marked done.
/// </summary>
/// <remarks>
/// <see cref="AutoExport.Decide"/> holds the whole decision and touches no disk, so
/// every branch is checked without a project on disk or a game engine to write into.
/// The tests that do write go through <see cref="AutoExport.Run"/>, which is the only
/// part that can fail for a reason outside the app.
/// </remarks>
[Collection("ExportPresetFile")]
public class AutoExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-auto-" + Guid.NewGuid().ToString("N"));

    public AutoExportTests()
    {
        Directory.CreateDirectory(_dir);
        ExportPresetStore.PathOverride = Path.Combine(_dir, "export.json");
    }

    public void Dispose()
    {
        ExportPresetStore.PathOverride = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static AutoExportSettings On(string folder) => new()
    {
        Enabled = true,
        Trigger = AssetStatus.Ready,
        OutputFolder = folder,
    };

    private static Doc Walking(int frames = 3)
    {
        var doc = DocumentFactory.CreateDoc(100, 80, 12, "#ffffff");
        doc.Scene.FrameCount = frames;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel { Frame = new Frame() });

        for (var i = 0; i < frames; i++)
        {
            if (layer.Cels[i].Frame is not Frame p) continue;
            double x = 20 + i * 6;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#c02040",
                Points =
                [
                    new StrokePoint(x, 20, 1), new StrokePoint(x + 20, 20, 1),
                    new StrokePoint(x + 20, 60, 1), new StrokePoint(x, 60, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }
        return doc;
    }

    // ---- the record ---------------------------------------------------------------

    [Fact]
    public void AProjectThatNeverSetsAStatusWritesNoStatusKey()
    {
        // The camera's rule, and the reason Status is nullable rather than defaulting to
        // Design: a project imported from a folder of loose files has no statuses, and
        // claiming every file is at the start of a pipeline it was never in is a guess.
        var reference = new DocumentRef { Name = "walk", Path = "a/walk.lightbox.json" };
        Assert.False(reference.HasStatus);
        Assert.DoesNotContain("\"status\"", DocJson.Serialize(new Doc()));
    }

    [Fact]
    public void ReopenedIsNotASynonymForInDevelopment()
    {
        // It means "this was Ready and is not any more" — the state a linear pipeline
        // cannot express and the one a producer most needs to see. Asserted as an
        // ordering claim, because the temptation is to slot it between Review and Ready.
        Assert.Equal(AssetStatus.Reopened, AssetStatuses.InOrder[^1]);
        Assert.NotEqual(
            AssetStatuses.Color(AssetStatus.Review), AssetStatuses.Color(AssetStatus.Reopened));
        Assert.Equal("In development", AssetStatuses.Label(AssetStatus.InDevelopment));
    }

    // ---- the decision -------------------------------------------------------------

    [Fact]
    public void NothingHappensWhenItIsSwitchedOff()
    {
        var (folder, outcome) = AutoExport.Decide(
            AssetStatus.Review, AssetStatus.Ready, new AutoExportSettings(), _dir);

        Assert.Null(folder);
        Assert.Equal(AutoExportOutcome.Disabled, outcome);
        // And it says nothing: an artist who has not switched this on should not be told
        // about it on every status change.
        Assert.Empty(AutoExport.Explain(outcome, new AutoExportSettings()));
    }

    [Fact]
    public void ReSelectingTheStatusSomethingAlreadyHasDoesNotExport()
    {
        // The failure this prevents is nasty: opening the menu to check what a document
        // is set to would re-write the engine's files.
        var (folder, outcome) = AutoExport.Decide(
            AssetStatus.Ready, AssetStatus.Ready, On(_dir), _dir);

        Assert.Null(folder);
        Assert.Equal(AutoExportOutcome.Unchanged, outcome);
    }

    [Theory]
    [InlineData(AssetStatus.Review)]
    [InlineData(AssetStatus.Reopened)]
    [InlineData(null)]
    public void ArrivingAtTheTriggerFromAnywhereExports(AssetStatus? before)
    {
        // Review → Ready and Reopened → Ready are both "this is done now", and a
        // never-set document reaching Ready is the first export of a new asset.
        var (folder, outcome) = AutoExport.Decide(before, AssetStatus.Ready, On(_dir), _dir);

        Assert.Equal(_dir, folder);
        Assert.Equal(AutoExportOutcome.Exported, outcome);
    }

    [Fact]
    public void AStatusThatIsNotTheTriggerDoesNotExportAndSaysWhichOneWould()
    {
        var settings = On(_dir);
        var (folder, outcome) = AutoExport.Decide(AssetStatus.Draft, AssetStatus.Review, settings, _dir);

        Assert.Null(folder);
        Assert.Equal(AutoExportOutcome.NotTheTrigger, outcome);
        Assert.Contains("Ready", AutoExport.Explain(outcome, settings));
    }

    [Fact]
    public void TheTriggerIsConfigurableBecauseSomeStudiosReviewInEngine()
    {
        var settings = On(_dir);
        settings.Trigger = AssetStatus.Review;

        Assert.Equal(
            AutoExportOutcome.Exported,
            AutoExport.Decide(AssetStatus.Draft, AssetStatus.Review, settings, _dir).Outcome);
        Assert.Equal(
            AutoExportOutcome.NotTheTrigger,
            AutoExport.Decide(AssetStatus.Review, AssetStatus.Ready, settings, _dir).Outcome);
    }

    [Fact]
    public void ARelativeFolderResolvesAgainstTheProject()
    {
        // The portable case, and the reason relative is allowed at all: a project beside
        // its game stays movable.
        var settings = On(Path.Combine("..", "Game", "Sprites"));
        var root = Path.Combine(_dir, "Knight.lbproj");

        var (folder, outcome) = AutoExport.Decide(null, AssetStatus.Ready, settings, root);

        Assert.Equal(AutoExportOutcome.Exported, outcome);
        Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "Game", "Sprites")), folder);
    }

    [Fact]
    public void ARelativeFolderWithNoProjectIsRefusedRatherThanGuessed()
    {
        // Resolving against the working directory would write files somewhere nobody
        // chose, which is worse than doing nothing.
        var (folder, outcome) = AutoExport.Decide(
            null, AssetStatus.Ready, On("sprites"), projectRoot: null);

        Assert.Null(folder);
        Assert.Equal(AutoExportOutcome.NoDestination, outcome);
    }

    [Fact]
    public void NoFolderIsSaidOutLoudBecauseItIsAMisconfiguration()
    {
        var settings = new AutoExportSettings { Enabled = true };
        var (folder, outcome) = AutoExport.Decide(null, AssetStatus.Ready, settings, _dir);

        Assert.Null(folder);
        Assert.Equal(AutoExportOutcome.NoDestination, outcome);
        // Unlike Disabled, this one is worth a message: the artist switched it on and it
        // is silently doing nothing.
        Assert.Contains("output folder", AutoExport.Explain(outcome, settings));
    }

    // ---- names --------------------------------------------------------------------

    [Fact]
    public void AFileNameComesFromTheDocumentNameAndSurvivesPunctuation()
    {
        Assert.Equal("walk cycle", AutoExport.FileNameFor("  walk cycle  "));
        Assert.Equal("asset", AutoExport.FileNameFor("   "));

        // Replaced rather than dropped, so two documents cannot collapse onto one name by
        // losing different punctuation.
        var cleaned = AutoExport.FileNameFor("run/fast");
        Assert.DoesNotContain('/', cleaned);
        Assert.NotEqual(AutoExport.FileNameFor("runfast"), cleaned);
    }

    // ---- actually running ---------------------------------------------------------

    [Fact]
    public void AnExportLandsInTheFolderNamedAfterTheDocument()
    {
        var target = Path.Combine(_dir, "engine");

        var report = AutoExport.Run(
            Walking(), "walk cycle", AssetStatus.Review, AssetStatus.Ready,
            On(target), _dir, ExportPreset.BuiltIns);

        Assert.Equal(AutoExportOutcome.Exported, report.Outcome);
        Assert.True(File.Exists(Path.Combine(target, "walk cycle.png")));
        Assert.True(File.Exists(Path.Combine(target, "walk cycle.json")));
        Assert.Contains("Auto-exported on Ready", report.Message);
    }

    [Fact]
    public void AMissingFolderIsCreatedRatherThanRefused()
    {
        // A first export into a path that does not exist yet is the ordinary case, not
        // an error.
        var target = Path.Combine(_dir, "new", "deeper");

        var report = AutoExport.Run(
            Walking(2), "idle", null, AssetStatus.Ready, On(target), _dir, ExportPreset.BuiltIns);

        Assert.Equal(AutoExportOutcome.Exported, report.Outcome);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void ItUsesTheNamedPresetAndFallsBackRatherThanFailing()
    {
        var target = Path.Combine(_dir, "packed");
        var settings = On(target);
        settings.PresetName = "Packed atlas";

        var report = AutoExport.Run(
            Walking(4), "run", null, AssetStatus.Ready, settings, _dir, ExportPreset.BuiltIns);

        Assert.Equal(AutoExportOutcome.Exported, report.Outcome);
        var json = File.ReadAllText(Path.Combine(target, "run.json"));
        Assert.Contains("\"pack\": \"skyline\"", json);

        // A preset that has been deleted since it was configured must not stop the
        // export: falling back to a working default beats refusing to ship.
        settings.PresetName = "Gone";
        var fallback = AutoExport.Run(
            Walking(2), "gone", null, AssetStatus.Ready, settings, _dir, ExportPreset.BuiltIns);
        Assert.Equal(AutoExportOutcome.Exported, fallback.Outcome);
    }

    [Fact]
    public void AFailedExportIsAMessageRatherThanAnException()
    {
        // The load-bearing rule: the status change has already been committed by the
        // caller, so this must never throw. A file where the folder should be is the
        // cheapest way to make the write fail for real.
        var blocked = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocked, "not a folder");

        var report = AutoExport.Run(
            Walking(2), "walk", null, AssetStatus.Ready, On(blocked), _dir, ExportPreset.BuiltIns);

        Assert.Equal(AutoExportOutcome.Failed, report.Outcome);
        Assert.Contains("Status saved", report.Message);
    }

    [Fact]
    public void APngSequencePresetGetsAFolderRatherThanAFileName()
    {
        var target = Path.Combine(_dir, "seq");
        var settings = On(target);
        settings.PresetName = "frames";

        var report = AutoExport.Run(
            Walking(3), "walk", null, AssetStatus.Ready, settings, _dir,
            [new ExportPreset { Name = "frames", Target = ExportTarget.PngSequence }]);

        Assert.Equal(AutoExportOutcome.Exported, report.Outcome);
        // A sequence writes into a folder of its own, so the document's name is a
        // directory here and not a .png.
        Assert.True(Directory.Exists(Path.Combine(target, "walk")));
        Assert.Equal(3, report.Run!.Files.Count);
    }

    // ---- settings round trip ------------------------------------------------------

    [Fact]
    public void TheSettingsRoundTripAndAreOffByDefault()
    {
        var fresh = new AppSettings();
        Assert.False(fresh.AutoExport.Enabled);
        Assert.Equal(AssetStatus.Ready, fresh.AutoExport.Trigger);
        Assert.Null(fresh.AutoExport.OutputFolder);

        fresh.AutoExport.Enabled = true;
        fresh.AutoExport.Trigger = AssetStatus.Review;
        fresh.AutoExport.PresetName = "Packed atlas";
        fresh.AutoExport.OutputFolder = "../Game/Sprites";

        var back = AppSettings.Deserialize(fresh.Serialize());

        Assert.True(back.AutoExport.Enabled);
        Assert.Equal(AssetStatus.Review, back.AutoExport.Trigger);
        Assert.Equal("Packed atlas", back.AutoExport.PresetName);
        Assert.Equal("../Game/Sprites", back.AutoExport.OutputFolder);
    }
}
