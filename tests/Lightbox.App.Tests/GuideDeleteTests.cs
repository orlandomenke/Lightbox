using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Delete removes the guide you have selected (B355).
/// </summary>
/// <remarks>
/// <para>
/// Reported as *"I am unable to remove single guides. I expected to be able to
/// select any guide, press delete and it would be removed. But I am only able
/// to clear all guides."* The command existed and worked —
/// <c>RemoveSelectedGuideCommand</c> — wired to exactly one thing: a Remove
/// button in <c>GuideOptionsPanel.axaml</c>. No key reached it, and it is not
/// in <c>ShortcutMap</c>, so it could not be found, searched or bound either.
/// </para>
/// <para>
/// The fix goes in the command Delete already runs, not in a new binding.
/// That is B173's rule — a branch on <c>Key.Delete</c> in the view is
/// invisible to the Configure window, so an artist who rebound Delete would
/// not have rebound this — and it is why there is no new entry in the map.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class GuideDeleteTests : BrushStateIsolated
{
    private static MainViewModel Vm() => VmLayers.PaperVm();

    [AvaloniaFact]
    public void DeleteRemovesTheSelectedGuideAndLeavesTheOthers()
    {
        var vm = Vm();
        var keep = vm.AddGuide(GuideKind.Line, 40, 0, angle: 90);
        var doomed = vm.AddGuide(GuideKind.Line, 120, 0, angle: 90);
        vm.SelectGuide(doomed.Id);
        Assert.True(vm.HasSelectedGuide);

        vm.DeleteSelectionContentsCommand.Execute(null);

        Assert.Single(vm.Guides);
        Assert.Equal(keep.Id, vm.Guides[0].Id);
        Assert.False(vm.HasSelectedGuide);
    }

    [AvaloniaFact]
    public void TheHeightScaleGoesTheSameWay()
    {
        // The kind that prompted the report, and the one with the most chrome
        // on screen — so the one an artist is most likely to try this on.
        var vm = Vm();
        var scale = vm.AddGuide(GuideKind.HeightScale, 200, 400, spacing: 40, divisions: 6);
        vm.SelectGuide(scale.Id);

        vm.DeleteSelectionContentsCommand.Execute(null);

        Assert.Empty(vm.Guides);
    }

    [AvaloniaFact]
    public void DeleteWithNoGuideSelectedIsUnchanged()
    {
        // The guard against fixing this too greedily: guides only win the key
        // when one is actually selected, or Delete would stop clearing pixels.
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 40, 0, angle: 90);
        Assert.False(vm.HasSelectedGuide);

        vm.DeleteSelectionContentsCommand.Execute(null);

        Assert.Single(vm.Guides);
    }

    [AvaloniaFact]
    public void RemovingAGuideIsUndoable()
    {
        // It goes through the same RemoveGuide the panel button uses, so it is
        // one recorded step rather than a quiet mutation of the scene.
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.Line, 90, 0, angle: 90);
        vm.SelectGuide(guide.Id);

        vm.DeleteSelectionContentsCommand.Execute(null);
        Assert.Empty(vm.Guides);

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.Guides);
        Assert.Equal(90, vm.Guides[0].X);
    }
}
