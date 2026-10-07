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
        vm.GroupLayersCommand.Execute(null);
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
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, LayerDropTarget.LooseLayer));
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, LayerDropTarget.GroupedLayer));
    }

    /// <summary>
    /// A collapsed folder header is three zones for a layer: beside, into, beside.
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
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, LayerDropTarget.CollapsedFolder));

    /// <summary>
    /// Under an open header is the top of that folder, never "below the folder".
    /// </summary>
    /// <remarks>
    /// The lower quarter used to say Below, drew its line between the header and
    /// the folder's first member, and then dropped the layer under the folder's
    /// last member — wherever that was. The line and the landing disagreed by
    /// the height of the whole folder.
    /// </remarks>
    [Theory]
    [InlineData(0.1, LayerDropHint.Above)]
    [InlineData(0.5, LayerDropHint.Into)]
    [InlineData(0.95, LayerDropHint.Into)]
    public void BelowAnOpenFolderHeaderIsInsideIt(double fraction, LayerDropHint expected) =>
        Assert.Equal(expected, LayerDropPlan.Resolve(fraction, LayerDropTarget.OpenFolder));

    /// <summary>
    /// The gaps between rows and the indent in front of folder members belong
    /// to the nearest row — they used to belong to nothing and refuse the drop.
    /// </summary>
    [Theory]
    [InlineData(-30, 0, 0.0)]   // above the list: the first row's top
    [InlineData(10, 0, 0.5)]
    [InlineData(20.9, 0, 1.0)]  // in the gap, nearer the first row
    [InlineData(21.1, 1, 0.0)]  // in the gap, nearer the second
    [InlineData(32, 1, 0.5)]
    [InlineData(500, 1, 1.0)]   // below the list: the last row's bottom
    public void EveryHeightInTheListBelongsToARow(double y, int index, double fraction)
    {
        var spans = new List<(double, double)> { (0, 20), (22, 42) };
        var (i, f) = LayerDropPlan.Locate(spans, y);
        Assert.Equal(index, i);
        Assert.Equal(fraction, f, 2);
    }

    /// <summary>
    /// A folder in hand goes into another folder like a layer does — and is
    /// never offered its own inside (Q204).
    /// </summary>
    /// <remarks>
    /// This used to read the other way: folders did not nest, so a header never
    /// offered <c>Into</c> to a carried folder. The owner asked for nesting;
    /// the one drop left to refuse is a folder into itself, and that is decided
    /// by trying the move, so the hint and the drop cannot disagree.
    /// </remarks>
    [AvaloniaFact]
    public void AFolderInHandGoesIntoAnotherFolder_ButNeverIntoItself()
    {
        var vm = VmLayers.PaperVm();
        vm.CreateLayerFolderCommand.Execute(null);
        vm.CreateLayerFolderCommand.Execute(null);
        var headers = vm.LayerPanelItems.OfType<GroupRow>().ToList();
        Assert.Equal(2, headers.Count);
        var (upper, lower) = (headers[0], headers[1]);

        var hint = vm.LayerDropHintFor(lower, upper, 0.5);
        Assert.Equal(LayerDropHint.Into, hint);
        vm.DropOnLayerPanel(lower, upper, hint);
        Assert.Equal(upper.Group.Id, lower.Group.ParentId);

        // Now the outer one, carried onto the folder inside it: refused, and
        // no line is drawn for it.
        var inner = vm.LayerPanelItems.OfType<GroupRow>().Single(h => h.Group.Id == lower.Group.Id);
        var outer = vm.LayerPanelItems.OfType<GroupRow>().Single(h => h.Group.Id == upper.Group.Id);
        Assert.Equal(LayerDropHint.None, vm.LayerDropHintFor(outer, inner, 0.5));
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
        vm.GroupLayersCommand.Execute(null);
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
