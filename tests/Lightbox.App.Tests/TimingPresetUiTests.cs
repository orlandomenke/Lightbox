using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// Q11's UI half: the picker, the range it applies to, the undo step, and
/// saving an artist's own patterns.
/// </summary>
[Collection("BrushState")]
public class TimingPresetUiTests : BrushStateIsolated
{
    private readonly string _store = Path.Combine(
        Path.GetTempPath(), $"lightbox-timing-{Guid.NewGuid():N}.json");

    public TimingPresetUiTests() => TimingPresetStore.PathOverride = _store;

    public override void Dispose()
    {
        TimingPresetStore.PathOverride = null;
        if (File.Exists(_store)) File.Delete(_store);
        base.Dispose();
    }

    /// <summary>
    /// A view model whose one drawing layer is keyed on 1s for n frames.
    /// </summary>
    /// <remarks>
    /// Built through the view model rather than by filling the model's cel list,
    /// so the timeline rows are in step the way they are for a real artist. A
    /// test that mutated the layer directly would need a sync call no production
    /// path makes, and would then be testing something nobody does.
    /// </remarks>
    private static MainViewModel OnOnes(int drawings)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < drawings; i++) vm.InsertFrameAt(CellAt(vm, i), FrameRole.Key);
        return vm;
    }

    private static FrameCell CellAt(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    private static string Pattern(MainViewModel vm) =>
        string.Concat(vm.Doc.Scene.Layers[0].Cels.Select(c => c.Frame is null ? '.' : 'X'));

    [AvaloniaFact]
    public void ThePickerOffersTheBuiltInsAndStartsOnSomething()
    {
        var vm = new MainViewModel(null);

        Assert.Contains("On 2s", vm.TimingPresets.Select(p => p.Name));
        Assert.Contains("Slow in", vm.TimingPresets.Select(p => p.Name));
        Assert.NotNull(vm.SelectedTimingPreset);
        // Built-ins are not deletable — an artist cannot remove the patterns the
        // manual describes.
        Assert.False(vm.CanDeleteTimingPreset);
    }

    [AvaloniaFact]
    public void ApplyingToASelectedRangeRetimesTheWholeRange()
    {
        var vm = OnOnes(8);

        // Shift-click a range: cel 0 through cel 7.
        vm.SelectFrameCommand.Execute(CellAt(vm, 0));
        vm.RangeSelectTo(CellAt(vm, 7));
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "On 2s");
        vm.ApplyTimingAt(CellAt(vm, 3));

        Assert.Equal("X.X.X.X.X.X.X.X.", Pattern(vm));
    }

    [AvaloniaFact]
    public void WithNoRangeItAppliesToTheCelAlone()
    {
        var vm = OnOnes(4);

        vm.ClearCelRange();
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "On 3s");
        vm.ApplyTimingAt(CellAt(vm, 1));

        // One drawing held for three, so the row grew by two and the drawings
        // after it moved down rather than being lost.
        Assert.Equal("XX..XX", Pattern(vm));
    }

    [AvaloniaFact]
    public void RetimingIsOneUndoStepFromTheViewModel()
    {
        var vm = OnOnes(6);

        vm.SelectFrameCommand.Execute(CellAt(vm, 0));
        vm.RangeSelectTo(CellAt(vm, 5));
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "On 2s");
        vm.ApplyTimingAt(CellAt(vm, 0));
        Assert.Equal(12, vm.Doc.Scene.Layers[0].Cels.Count);

        vm.UndoCommand.Execute(null);

        Assert.Equal(6, vm.Doc.Scene.Layers[0].Cels.Count);
    }

    [AvaloniaFact]
    public void TheRulerFollowsTheNewLength()
    {
        // AfterRetime never notified the frame-count-derived properties, so a
        // re-time that changed the row's length left the ruler and the scrub
        // limit showing the old one until something else refreshed them.
        var vm = OnOnes(6);
        var before = vm.TimelineExtent;

        vm.SelectFrameCommand.Execute(CellAt(vm, 0));
        vm.RangeSelectTo(CellAt(vm, 5));
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "On 3s");
        vm.ApplyTimingAt(CellAt(vm, 0));

        Assert.True(vm.TimelineExtent > before, $"extent stayed at {vm.TimelineExtent}");
        // The scrub limit is the sheet's extent rather than the scene's length
        // since Q90 — the playhead may stand past the end. What this test is
        // actually about is unchanged and still guarded: the derived properties
        // refresh when a re-time changes the length, rather than showing the
        // old one until something else happens to notify them.
        Assert.Equal(vm.TimelineExtent - 1, vm.MaxScrubFrame);
        Assert.True(vm.MaxScrubFrame > before - 1, "the scrub limit did not follow the new length");
    }

    [AvaloniaFact]
    public void ARangeOfNothingSaysSoRatherThanSilentlyDoingNothing()
    {
        // One drawing at cel 0, held out to cel 3 — so cels 1 to 3 are holds
        // with no drawing keyed in them.
        var vm = VmLayers.BareVm();
        for (var i = 0; i < 3; i++) vm.ExtendExposureAt(CellAt(vm, 0));
        Assert.Equal("X...", Pattern(vm));

        vm.SelectFrameCommand.Execute(CellAt(vm, 1));
        vm.RangeSelectTo(CellAt(vm, 3));
        vm.ApplyTimingAt(CellAt(vm, 2));

        Assert.Equal("X...", Pattern(vm));

        Assert.Contains("no drawing", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TheStatusLineSaysWhichWayTheLengthWent()
    {
        // A row silently getting longer is easy to miss on a long timeline.
        var vm = OnOnes(4);
        vm.SelectFrameCommand.Execute(CellAt(vm, 0));
        vm.RangeSelectTo(CellAt(vm, 3));

        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "On 2s");
        vm.ApplyTimingAt(CellAt(vm, 0));
        Assert.Contains("longer", vm.AiStatus);

        vm.SelectFrameCommand.Execute(CellAt(vm, 0));
        vm.RangeSelectTo(CellAt(vm, 7));
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "On 1s");
        vm.ApplyTimingAt(CellAt(vm, 0));
        Assert.Contains("shorter", vm.AiStatus);
    }

    // ---- an artist's own patterns ----------------------------------------------

    [AvaloniaFact]
    public void ASavedPatternPersistsAndComesBackOnTheNextLaunch()
    {
        var vm = new MainViewModel(null);
        vm.NewTimingPresetName = "Studio slow-in";
        vm.NewTimingPresetPattern = "1, 1, 2, 3, 5";

        Assert.True(vm.SaveTimingPreset());
        Assert.Contains("Studio slow-in", vm.TimingPresets.Select(p => p.Name));
        Assert.True(vm.CanDeleteTimingPreset);
        // The form clears, so the next save does not start from the last one.
        Assert.Equal("", vm.NewTimingPresetName);

        var next = new MainViewModel(null);
        var mine = next.TimingPresets.FirstOrDefault(p => p.Name == "Studio slow-in");
        Assert.NotNull(mine);
        Assert.Equal([1, 1, 2, 3, 5], mine!.Holds);
    }

    [AvaloniaFact]
    public void TheBuiltInsAreNotWrittenToTheStore()
    {
        // Storing them would freeze them into every artist's settings file the
        // first time they opened the app, so a later correction reaches nobody.
        var vm = new MainViewModel(null);
        vm.NewTimingPresetName = "Mine";
        vm.NewTimingPresetPattern = "2, 4";
        vm.SaveTimingPreset();

        var text = File.ReadAllText(_store);
        Assert.Contains("Mine", text);
        Assert.DoesNotContain("Slow in", text);
    }

    [AvaloniaFact]
    public void ATypedPatternThatWillNotParseIsRefusedWithAReason()
    {
        var vm = new MainViewModel(null);
        vm.NewTimingPresetName = "Broken";
        vm.NewTimingPresetPattern = "1, 1, x";

        Assert.False(vm.SaveTimingPreset());
        Assert.DoesNotContain("Broken", vm.TimingPresets.Select(p => p.Name));
        Assert.Contains("whole numbers", vm.AiStatus);
        // And nothing was written, so a bad entry cannot accumulate in the file.
        Assert.False(File.Exists(_store));
    }

    [AvaloniaFact]
    public void SavingTheSameNameTwiceReplacesItRatherThanAddingASecond()
    {
        var vm = new MainViewModel(null);
        vm.NewTimingPresetName = "Mine";
        vm.NewTimingPresetPattern = "2";
        vm.SaveTimingPreset();
        vm.NewTimingPresetName = "Mine";
        vm.NewTimingPresetPattern = "3, 3";
        vm.SaveTimingPreset();

        var mine = vm.TimingPresets.Where(p => p.Name == "Mine").ToList();
        Assert.Single(mine);
        Assert.Equal([3, 3], mine[0].Holds);
    }

    [AvaloniaFact]
    public void ABuiltInNameGetsItsOwnEntryRatherThanOverridingTheOne()
    {
        var vm = new MainViewModel(null);
        vm.NewTimingPresetName = "On 2s";
        vm.NewTimingPresetPattern = "2, 2, 4";
        vm.SaveTimingPreset();

        // Two entries called "On 2s": the built-in the manual describes, and
        // the artist's. Silently shadowing the built-in would make the manual
        // wrong for that artist and nothing would say why.
        Assert.Equal(2, vm.TimingPresets.Count(p => p.Name == "On 2s"));
        Assert.Contains(TimingPreset.BuiltIns.First(p => p.Name == "On 2s"), vm.TimingPresets);
    }

    [AvaloniaFact]
    public void DeletingRemovesItFromTheListAndTheFile()
    {
        var vm = new MainViewModel(null);
        vm.NewTimingPresetName = "Mine";
        vm.NewTimingPresetPattern = "2";
        vm.SaveTimingPreset();

        vm.DeleteSelectedTimingPreset();

        Assert.DoesNotContain("Mine", vm.TimingPresets.Select(p => p.Name));
        Assert.DoesNotContain("Mine", File.ReadAllText(_store));
    }

    [AvaloniaFact]
    public void ABuiltInCannotBeDeleted()
    {
        var vm = new MainViewModel(null);
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First();
        var before = vm.TimingPresets.Count;

        vm.DeleteSelectedTimingPreset();

        Assert.Equal(before, vm.TimingPresets.Count);
    }

    [AvaloniaFact]
    public void ACorruptStoreLeavesTheBuiltIns()
    {
        // One bad hand-edit must not cost an artist their patterns, and must
        // certainly not stop the app opening.
        File.WriteAllText(_store, "{ this is not json");

        var vm = new MainViewModel(null);

        Assert.Contains("On 2s", vm.TimingPresets.Select(p => p.Name));
    }

    [AvaloniaFact]
    public void TheCelMenuNamesThePatternTheBarHasChosen()
    {
        var vm = new MainViewModel(null);
        vm.SelectedTimingPreset = TimingPreset.BuiltIns.First(p => p.Name == "Slow in");
        Assert.Equal("Re-time to Slow in", vm.RetimeMenuLabel);
    }
}
