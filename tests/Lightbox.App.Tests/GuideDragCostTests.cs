using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A guide being dragged costs the view a redraw and nothing else (B353).
/// </summary>
/// <remarks>
/// <para>
/// Reported as the height guide being "really lagging/slow in adjusting or
/// moving". Every pointer event during a guide drag called
/// <c>NotifyGuides</c>, whose recording half publishes a frame through the
/// whole compose pipeline and then calls <c>MarkDocumentEdited</c> — which
/// marks autosave dirty, invalidates the reference-view cache, and
/// <b>re-flattens every taped reference strip</b>. Once per mouse move.
/// </para>
/// <para>
/// The split already existed and was unused: <c>NotifyGuidesView</c> is
/// documented as "tell everything that shows guides to re-read them, without
/// recording an edit", and had exactly one caller — <c>NotifyGuides</c>
/// itself. A guide is chrome; the canvas redraws it from <c>GuidesChanged</c>,
/// and the gesture's end records the whole move as one step.
/// </para>
/// <para>
/// Counted rather than timed, deliberately. A duration would measure this box
/// (see B259's long tail of contention flakes); the number of document edits a
/// drag causes is exact, is the thing that was wrong, and cannot flake.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class GuideDragCostTests : BrushStateIsolated
{
    private static MainViewModel Vm() => VmLayers.PaperVm();

    [AvaloniaFact]
    public void DraggingAGuideEditsTheDocumentOnceForTheWholeGesture()
    {
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.Line, 100, 100, angle: 90);

        var edits = 0;
        vm.DocumentEdited += () => edits++;

        // A gesture is many pointer events. Twenty is a short one.
        for (var i = 0; i < 20; i++) vm.DragGuide(guide, 1, 0);

        Assert.Equal(0, edits);

        vm.EndGuideDrag(guide);

        // Exactly one, now that B354 has landed: the editor's own change is
        // the single source of the mark, and MoveGuide no longer adds a second.
        Assert.Equal(1, edits);
        Assert.Equal(120, guide.X);
    }

    [AvaloniaFact]
    public void ResizingAHeightScaleEditsTheDocumentOnceForTheWholeGesture()
    {
        // The one that was reported. Its own remark already promised this:
        // "nothing is recorded until EndHeightScaleResize closes the gesture
        // into one step" — and it called the recording notifier every move.
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.HeightScale, 200, 400, spacing: 40, divisions: 6);

        var edits = 0;
        vm.DocumentEdited += () => edits++;

        for (var i = 0; i < 20; i++) vm.DragHeightScaleTop(guide, 1);

        Assert.Equal(0, edits);

        vm.EndHeightScaleResize(guide);

        Assert.Equal(1, edits);
    }

    [AvaloniaFact]
    public void TheCanvasIsStillToldOnEveryMove()
    {
        // The other half, and the one that would make this fix a regression if
        // it were wrong: cheap must not mean invisible. The guide has to follow
        // the pointer, which is what GuidesChanged drives.
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.Line, 100, 100, angle: 90);

        var redraws = 0;
        vm.GuidesChanged += () => redraws++;

        for (var i = 0; i < 20; i++) vm.DragGuide(guide, 1, 0);

        Assert.Equal(20, redraws);
    }

    [AvaloniaFact]
    public void DraggingSeveralGuidesAtOnceIsNoDifferent()
    {
        // The group case, and the one that nearly shipped unfixed:
        // UpdateGuidesMove's own remark says it is "DragGuide for a group",
        // and it had the same per-pointer-event NotifyGuides. A multi-select
        // drag is the worst version of the report, because the work was
        // per event rather than per guide and the artist had picked up more.
        var vm = Vm();
        var first = vm.AddGuide(GuideKind.Line, 40, 0, angle: 90);
        var second = vm.AddGuide(GuideKind.Line, 120, 0, angle: 90);
        vm.SelectGuide(first.Id);
        vm.SelectGuide(second.Id, shift: true);

        var edits = 0;
        var redraws = 0;
        vm.DocumentEdited += () => edits++;
        vm.GuidesChanged += () => redraws++;

        for (var i = 0; i < 20; i++) vm.UpdateGuidesMove(1, 0);

        Assert.Equal(0, edits);
        Assert.Equal(20, redraws);

        vm.EndGuidesMove();

        Assert.Equal(1, edits);
        Assert.Equal(60, first.X);
        Assert.Equal(140, second.X);
    }
}
