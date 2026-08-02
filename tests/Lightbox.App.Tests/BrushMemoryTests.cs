using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Q9, from the tool bar's side: a document hands back the brush it was
/// painted with.
/// </summary>
/// <remarks>
/// <para>
/// The gripe this answers is not about preference, it is about memory. Come
/// back to a comic page or a game asset after a fortnight and the tool bar
/// says whatever you last used on something else. The document already knows
/// what it was drawn with — every stroke carries its settings — and this is
/// what puts the answer back where you can paint with it.
/// </para>
/// <para>
/// Guarded here rather than only in <c>BrushScopeTests</c> because the record
/// half being right is worth nothing if the tool bar never reads it. See
/// charter O7.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushMemoryTests : BrushStateIsolated
{
    /// <summary>
    /// Paint a real stroke, through the pipeline a pen drives.
    /// </summary>
    /// <remarks>
    /// Not <c>AppendExternalStrokes</c>, which is how the AI and the MCP
    /// server put marks down. That path deliberately does NOT record the
    /// brush: a stroke the artist did not paint is not the brush they were
    /// painting with, and letting an agent rewrite the tool bar's memory would
    /// undo the whole point of remembering it.
    /// </remarks>
    private static void Paint(MainViewModel vm)
    {
        vm.Doc.Scene.Layers[0].Locked = false;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(90, 60, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void GlobalIsStillTheDefaultWithNoProjectOpen()
    {
        // The behaviour the application has always had, and what somebody who
        // opened it to draw one picture gets.
        var vm = new MainViewModel(null);

        Assert.Equal(BrushScope.Global, vm.BrushScope);
        Assert.Equal("Follow the project", vm.BrushMemoryChoice);
    }

    [AvaloniaFact]
    public void ChoosingPerDocumentOverridesWhateverTheProjectWouldSay()
    {
        var vm = new MainViewModel(null);

        vm.BrushMemoryChoice = "Per document";

        Assert.Equal(BrushScope.PerDocument, vm.BrushScope);
    }

    [AvaloniaFact]
    public void UnderGlobalTheDocumentRecordsNothing()
    {
        // The other half of the choice. A file made this way must be
        // indistinguishable from one made before the feature existed.
        var vm = new MainViewModel(null);
        vm.BrushMemoryChoice = "Global";

        Paint(vm);

        Assert.Null(vm.Doc.Brush);
    }

    [AvaloniaFact]
    public void UnderPerDocumentPaintingRecordsTheBrushOnTheDrawing()
    {
        // On commit rather than on save: the session this exists for is the
        // one that ended without a save, because somebody closed the laptop.
        var vm = new MainViewModel(null);
        vm.BrushMemoryChoice = "Per document";
        vm.BrushSize = 41;

        Paint(vm);

        Assert.NotNull(vm.Doc.Brush);
        Assert.Equal(41, vm.Doc.Brush!.Size);
    }

    [AvaloniaFact]
    public void ReopeningTheDocumentPutsThatBrushBackInTheToolBar()
    {
        // The promise, end to end: paint with one brush, go away, come back to
        // a tool bar set to something else, and get your brush returned.
        var vm = new MainViewModel(null);
        vm.BrushMemoryChoice = "Per document";
        vm.BrushSize = 41;
        Paint(vm);
        var painted = vm.Doc;

        // A second document, drawn with something quite different.
        vm.NewDocument(new NewDocumentSettings("Second", 120, 80, 12, 72, "#ffffff", false));
        vm.BrushSize = 7;
        Assert.Equal(7, vm.BrushSize);

        // Back to the first.
        vm.ActiveTab = vm.Tabs.First(t => ReferenceEquals(t.Doc, painted));

        Assert.Equal(41, vm.BrushSize);
    }

    [AvaloniaFact]
    public void ADocumentWithNothingRecordedLeavesTheBrushAlone()
    {
        // Silence is the right answer for an older file, or one made under
        // Global. Resetting somebody's brush to a default every time they open
        // something would be worse than the problem this solves.
        var vm = new MainViewModel(null);
        vm.BrushMemoryChoice = "Per document";
        var first = vm.Doc;
        vm.NewDocument(new NewDocumentSettings("Second", 120, 80, 12, 72, "#ffffff", false));
        vm.BrushSize = 23;

        vm.ActiveTab = vm.Tabs.First(t => ReferenceEquals(t.Doc, first));

        Assert.Equal(23, vm.BrushSize);
    }

    [AvaloniaFact]
    public void SwitchingToPerDocumentMidSessionHandsBackWhatIsAlreadyThere()
    {
        // Turning the setting on should not require a tab change to take
        // effect — the artist just told the application what they wanted.
        var vm = new MainViewModel(null);
        vm.BrushMemoryChoice = "Per document";
        vm.BrushSize = 55;
        Paint(vm);
        vm.BrushMemoryChoice = "Global";
        vm.BrushSize = 9;

        vm.BrushMemoryChoice = "Per document";

        Assert.Equal(55, vm.BrushSize);
    }
}
