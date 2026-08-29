using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Dragging a folder, and saying where a drop would land before it happens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Folders could be dropped on and never picked up.</b> A header has been a
/// drop target since folders landed, which teaches that dragging works in this
/// docker — and then the one row an artist most wants to move, the whole block
/// at once, refused to move at all. Neither kind showed anything while it was
/// being carried: no ghost saying what was in hand, no line saying where it
/// would go.
/// </para>
/// <para>
/// <b>Asserted against the rule and the record, not against synthetic drags.</b>
/// <c>MANUAL_TESTING.md</c>'s warning applies with full force here — a dropped
/// pointer event through Xvfb looks exactly like a wrong answer — so the
/// decision lives in <see cref="LayerDropPlan"/> where it can be read, and what
/// the drop then does is checked on <c>Scene.Layers</c>.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class FolderDragDropTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// Bottom to top: paper, loose, [folder: two layers], loose.
    /// </summary>
    private static MainViewModel Stacked()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 5) vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        layers[1].Name = "under";
        layers[2].Name = "in a";
        layers[3].Name = "in b";
        layers[4].Name = "over";

        // Built through the app's own two paths rather than by writing GroupId
        // directly: those are what rebuild the panel, and a test that reaches
        // past them is testing a docker nobody will ever see.
        vm.ActiveLayerIndex = 2;
        vm.CreateLayerFolderCommand.Execute(null);
        vm.MoveLayerIntoGroup(layers.Single(l => l.Name == "in b"), vm.Doc.Scene.LayerGroups[0]);
        return vm;
    }

    /// <summary>The stack bottom-first, by name — what an assertion can read.</summary>
    private static string Order(MainViewModel vm) =>
        string.Join(",", vm.Doc.Scene.Layers.Select(l => l.Name));

    private static LayerGroup Folder(MainViewModel vm) => vm.Doc.Scene.LayerGroups[0];

    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerPanelItems.OfType<LayerRow>().Single(r => r.Layer.Name == name);

    private static GroupRow Header(MainViewModel vm) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single();

    // ---- the rule ------------------------------------------------------------

    /// <summary>A layer row is two halves, whatever is being carried.</summary>
    [Theory]
    [InlineData(0.0, LayerDropHint.Above)]
    [InlineData(0.49, LayerDropHint.Above)]
    [InlineData(0.51, LayerDropHint.Below)]
    [InlineData(1.0, LayerDropHint.Below)]
    public void ALayerRowSplitsInHalf(double fraction, LayerDropHint expected)
    {
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, targetIsFolder: false, draggingFolder: false));
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, targetIsFolder: false, draggingFolder: true));
    }

    /// <summary>
    /// A folder header is three zones for a layer: beside, into, beside.
    /// </summary>
    /// <remarks>
    /// The middle half files into the folder because that is the common
    /// gesture — you point at the folder you mean. The quarters at each end
    /// exist because before them a header could only swallow what was dropped
    /// on it, so putting a layer immediately above a folder meant aiming at
    /// whatever row happened to be above it instead.
    /// </remarks>
    [Theory]
    [InlineData(0.1, LayerDropHint.Above)]
    [InlineData(0.24, LayerDropHint.Above)]
    [InlineData(0.5, LayerDropHint.Into)]
    [InlineData(0.76, LayerDropHint.Below)]
    [InlineData(0.95, LayerDropHint.Below)]
    public void AFolderHeaderOffersInsideAndBeside(double fraction, LayerDropHint expected) =>
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, targetIsFolder: true, draggingFolder: false));

    /// <summary>
    /// A folder in hand never sees an <c>Into</c>, because folders do not nest.
    /// </summary>
    /// <remarks>
    /// <c>Layer.GroupId</c> is a single id with no parent of its own, so there
    /// is nothing for a folder to be filed into. Offering the zone would be a
    /// gesture the drop has to refuse — feedback that promises something the
    /// record cannot hold is worse than none.
    /// </remarks>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void AFolderInHandIsNeverOfferedAFolderToGoInside(double fraction)
    {
        var hint = LayerDropPlan.Resolve(fraction, targetIsFolder: true, draggingFolder: true);
        Assert.NotEqual(LayerDropHint.Into, hint);
    }

    // ---- moving the block ----------------------------------------------------

    [AvaloniaFact]
    public void TheStackStartsWhereTheTestsThinkItDoes()
    {
        var vm = Stacked();
        Assert.Equal("Background,under,in a,in b,over", Order(vm));
    }

    /// <summary>The whole folder moves, keeping its own order.</summary>
    [AvaloniaFact]
    public void DroppingAFolderAboveARowLiftsEveryLayerInIt()
    {
        var vm = Stacked();
        vm.DropGroupBeside(Folder(vm), Row(vm, "over"), above: true);
        output.WriteLine(Order(vm));
        Assert.Equal("Background,under,over,in a,in b", Order(vm));
    }

    [AvaloniaFact]
    public void DroppingAFolderBelowARowPutsTheBlockUnderIt()
    {
        var vm = Stacked();
        vm.DropGroupBeside(Folder(vm), Row(vm, "under"), above: false);
        output.WriteLine(Order(vm));
        Assert.Equal("Background,in a,in b,under,over", Order(vm));
    }

    /// <summary>
    /// The folder stays one run, which is what the panel is built on.
    /// </summary>
    /// <remarks>
    /// <c>RebuildLayerPanel</c> emits a folder's header where its first member
    /// appears and its members after it, so a folder split around some other
    /// layer would draw one header with its rows scattered beneath. Landing
    /// beside a <em>member</em> of another folder therefore has to mean beside
    /// that folder — this is that, checked on the record rather than argued.
    /// </remarks>
    [AvaloniaFact]
    public void AFolderDroppedOnAMemberOfAnotherFolderLandsBesideIt()
    {
        var vm = Stacked();
        // A second folder holding "over", made the way the app makes one.
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Name == "over");
        vm.CreateLayerFolderCommand.Execute(null);
        var second = vm.Doc.Scene.LayerGroups[^1];

        vm.DropGroupBeside(second, Row(vm, "in a"), above: false);
        output.WriteLine(Order(vm));
        Assert.Equal("Background,under,over,in a,in b", Order(vm));

        var ids = vm.Doc.Scene.Layers.Select(l => l.GroupId).ToList();
        var first = ids.IndexOf(Folder(vm).Id);
        var last = ids.LastIndexOf(Folder(vm).Id);
        Assert.Equal(last - first + 1, ids.Count(id => id == Folder(vm).Id));
    }

    [AvaloniaFact]
    public void AFolderDroppedOnItselfChangesNothingAndIsNotAnUndoStep()
    {
        var vm = Stacked();
        var before = Order(vm);
        var undos = vm.RecordedStepCount;

        vm.DropGroupBeside(Folder(vm), Row(vm, "in b"), above: true);

        Assert.Equal(before, Order(vm));
        Assert.Equal(undos, vm.RecordedStepCount);
    }

    /// <summary>The paper stays at the bottom, folders included.</summary>
    [AvaloniaFact]
    public void AFolderCannotBeFiledUnderThePaper()
    {
        var vm = Stacked();
        var before = Order(vm);
        vm.DropGroupBeside(Folder(vm), Row(vm, "Background"), above: false);
        output.WriteLine($"{before} → {Order(vm)}; {vm.AiStatus}");
        Assert.Equal(before, Order(vm));
    }

    [AvaloniaFact]
    public void MovingAFolderIsOneUndoStepAndComesBack()
    {
        var vm = Stacked();
        var before = Order(vm);
        vm.DropGroupBeside(Folder(vm), Row(vm, "over"), above: true);
        Assert.NotEqual(before, Order(vm));

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, Order(vm));
    }

    // ---- beside a folder, rather than into it --------------------------------

    /// <summary>
    /// A layer dropped on a header's outer quarter goes beside the folder and
    /// comes out of whatever folder it was in.
    /// </summary>
    [AvaloniaFact]
    public void ALayerDroppedOnTheEdgeOfAHeaderLandsBesideTheFolder()
    {
        var vm = Stacked();
        vm.DropLayerBesideGroup(Row(vm, "under"), Header(vm), above: true);
        output.WriteLine(Order(vm));
        Assert.Equal("Background,in a,in b,under,over", Order(vm));
        Assert.Null(vm.Doc.Scene.Layers.Single(l => l.Name == "under").GroupId);
    }

    /// <summary>And the middle of the header still files it away.</summary>
    [AvaloniaFact]
    public void ALayerDroppedOnTheMiddleOfAHeaderStillJoinsTheFolder()
    {
        var vm = Stacked();
        vm.MoveLayerIntoGroup(vm.Doc.Scene.Layers.Single(l => l.Name == "over"), Folder(vm));
        Assert.Equal(Folder(vm).Id, vm.Doc.Scene.Layers.Single(l => l.Name == "over").GroupId);
    }

    // ---- the hint is shown on one row and taken down again -------------------

    [AvaloniaFact]
    public void OnlyOneRowShowsAHintAtATime()
    {
        var vm = Stacked();
        vm.ShowLayerDropHint(Row(vm, "over"), LayerDropHint.Above);
        Assert.Equal(LayerDropHint.Above, Row(vm, "over").DropHint);
        Assert.Equal(LayerDropHint.None, Row(vm, "under").DropHint);

        vm.ShowLayerDropHint(Header(vm), LayerDropHint.Into);
        Assert.Equal(LayerDropHint.None, Row(vm, "over").DropHint);
        Assert.Equal(LayerDropHint.Into, Header(vm).DropHint);
        Assert.True(Header(vm).DropInto);

        vm.ClearLayerDropHints();
        Assert.All(
            vm.LayerPanelItems,
            item => Assert.Equal(
                LayerDropHint.None,
                item is LayerRow r ? r.DropHint : ((GroupRow)item).DropHint));
    }
}
