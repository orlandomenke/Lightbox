using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Q111: a document reopens showing what it showed when it was put down. The
/// playhead rides into the file at save (absent at frame 0 — optional means
/// absent) and is restored, clamped, on open.
/// </summary>
public class PlayheadPersistenceTests
{
    private static MainViewModel FreshVm()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Scene", 400, 300, 12, 72, "#ffffff", false));
        return vm;
    }

    [AvaloniaFact]
    public void TheDocumentReopensAtTheFrameItWasSavedOn()
    {
        var vm = FreshVm();
        vm.CurrentFrameIndex = 7;

        var loaded = DocJson.Deserialize(vm.SerializeDocument());
        Assert.Equal(7, loaded.PlayheadFrame);

        vm.ReplaceDocument(loaded);
        Assert.Equal(7, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void ADocumentSavedAtTheStartWritesNoPlayheadKey()
    {
        var vm = FreshVm();
        var json = vm.SerializeDocument();
        Assert.DoesNotContain("\"playheadFrame\"", json);
    }

    [AvaloniaFact]
    public void AHandEditedNegativePlayheadLandsAtTheStart()
    {
        // Negative-proofed only: past the end is a place the playhead is
        // allowed to stand (PlayheadPastTheEnd), so a large value restores
        // as-is and only nonsense below zero is repaired.
        var vm = FreshVm();
        var loaded = DocJson.Deserialize(vm.SerializeDocument());
        loaded.PlayheadFrame = -5;

        vm.ReplaceDocument(loaded);
        Assert.Equal(0, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void OpeningInANewTabParksTheTabWhereTheFileSays()
    {
        var vm = FreshVm();
        vm.CurrentFrameIndex = 5;
        var loaded = DocJson.Deserialize(vm.SerializeDocument());

        vm.OpenDocumentTab(loaded, null);
        Assert.Equal(5, vm.CurrentFrameIndex);
    }
}
