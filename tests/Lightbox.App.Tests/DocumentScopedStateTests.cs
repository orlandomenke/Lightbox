using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B67.</b> What belongs to a document rather than to the process.
/// </summary>
/// <remarks>
/// <para>
/// The report named three things and they are not one feature. Two of the three
/// already behaved — the project docker is gated on <c>HasProject</c> in
/// <c>IsPanelUsable</c>, and the sheet and reference lists read from the active
/// document — so the tests here cover the two that did not, plus controls on the
/// two that did so a later change cannot quietly undo them.
/// </para>
/// <para>
/// The brush is not covered here and its absence is the point: <b>Q9 decided
/// against per-document brush</b> and shipped per-project instead. A test
/// asserting the brush follows the document would contradict a recorded
/// decision, so what is asserted instead is that it deliberately does not.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class DocumentScopedStateTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// Zoom, rotation, mirror and pan come back with the document that was
    /// framed that way, and a document nobody has framed opens fitted.
    /// </summary>
    /// <remarks>
    /// Written against <see cref="CanvasControl.Framing"/> plus the view model's
    /// <c>TabSwitched</c> rather than through the window, because a
    /// <c>MainWindow</c> cannot be constructed in the headless suite. The
    /// handler under test is three lines and is duplicated here in
    /// <c>Carry</c> — which is honest about what this does and does not guard:
    /// it pins the mechanism, not the one subscription line in
    /// <c>MainWindow.axaml.cs</c>.
    /// </remarks>
    [AvaloniaFact]
    public void ZoomIsRememberedPerDocumentRatherThanShared()
    {
        var vm = new MainViewModel(null);
        var canvas = new CanvasControl { Width = 400, Height = 300 };
        canvas.Measure(new Size(400, 300));
        canvas.Arrange(new Rect(0, 0, 400, 300));
        vm.TabSwitched += (leaving, arriving) => Carry(canvas, leaving, arriving);

        var a = vm.Tabs[0];
        canvas.ZoomIn();
        canvas.ZoomIn();
        var framedA = canvas.ZoomPercent;
        Assert.True(framedA > 100, $"the test's own premise: zoomed to {framedA:F0}%");

        // A document nobody has framed opens fitted, not at A's 156%.
        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        output.WriteLine($"A framed at {framedA:F0}%, B opened at {canvas.ZoomPercent:F0}%");
        Assert.Equal(100, canvas.ZoomPercent, 3);

        // ...and B's own framing is B's.
        canvas.ZoomOut();
        var framedB = canvas.ZoomPercent;

        vm.ActiveTab = a;
        output.WriteLine($"back on A: {canvas.ZoomPercent:F0}% (left at {framedA:F0}%)");
        Assert.Equal(framedA, canvas.ZoomPercent, 3);

        vm.ActiveTab = vm.Tabs[1];
        Assert.Equal(framedB, canvas.ZoomPercent, 3);
    }

    /// <summary>Mirror and rotation ride along with the zoom, not just the scale.</summary>
    /// <remarks>
    /// Separate from the zoom test because a fix that carried only <c>_zoom</c>
    /// would pass that one — four fields go into the framing and a partial
    /// restore is the plausible half-fix.
    /// </remarks>
    [AvaloniaFact]
    public void TheWholeFramingTravels_NotOnlyTheZoom()
    {
        var vm = new MainViewModel(null);
        var canvas = new CanvasControl { Width = 400, Height = 300 };
        canvas.Measure(new Size(400, 300));
        canvas.Arrange(new Rect(0, 0, 400, 300));
        vm.TabSwitched += (leaving, arriving) => Carry(canvas, leaving, arriving);

        var a = vm.Tabs[0];
        canvas.ToggleMirror();
        canvas.RotateBy(90);
        Assert.True(canvas.IsMirrored);

        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        Assert.False(canvas.IsMirrored);              // B is not mirrored
        Assert.Equal(0, canvas.Framing.RotationDeg);  // nor rotated

        vm.ActiveTab = a;
        Assert.True(canvas.IsMirrored);
        Assert.Equal(90, canvas.Framing.RotationDeg, 3);
    }

    /// <summary>
    /// A document holding a reference shows it, even after visiting one where a
    /// higher strip was selected.
    /// </summary>
    /// <remarks>
    /// The shared index was not merely untidy. It is bounds-checked against the
    /// <em>active</em> document's strips, so a document with one reference
    /// resolved <c>ActiveReference</c> to null and showed nothing at all — the
    /// reference was there and invisible. The second assertion is the control:
    /// a document with no reference must still show none, which is what the
    /// report actually complained about.
    /// </remarks>
    [AvaloniaFact]
    public void ADocumentWithNoReferenceShowsNoReferencePanel()
    {
        var vm = new MainViewModel(null);
        var a = vm.Tabs[0];
        a.Doc.Scene.References = [Strip("front"), Strip("side"), Strip("back")];
        vm.ActiveReferenceIndex = 2;
        Assert.Equal("back", vm.ActiveReference?.Name);

        // B has one reference. Under the shared index it inherited "2" and
        // showed nothing.
        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        var b = vm.ActiveTab!;
        b.Doc.Scene.References = [Strip("only")];
        vm.ActiveReferenceIndex = 0;
        Assert.Equal("only", vm.ActiveReference?.Name);

        vm.ActiveTab = a;
        output.WriteLine($"A shows {vm.ActiveReference?.Name ?? "<none>"} (left on 'back')");
        Assert.Equal("back", vm.ActiveReference?.Name);

        vm.ActiveTab = b;
        Assert.Equal("only", vm.ActiveReference?.Name);

        // The control the report asked for: a document with nothing shows nothing.
        vm.NewDocument(new NewDocumentSettings("C", 400, 300, 12, 72, "#ffffff", false));
        Assert.Null(vm.ActiveReference);
        Assert.Empty(vm.ReferenceSheetsView);
    }

    /// <summary>Playhead and layer still travel — they did before, in two loose ints.</summary>
    [AvaloniaFact]
    public void SwitchingTabsRestoresThatDocumentsToolState()
    {
        var vm = new MainViewModel(null);
        var a = vm.Tabs[0];
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        var frameA = vm.CurrentFrameIndex;
        Assert.True(frameA > 0, "the test's own premise: the playhead moved");

        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        Assert.Equal(0, vm.CurrentFrameIndex);

        vm.ActiveTab = a;
        Assert.Equal(frameA, vm.CurrentFrameIndex);
        Assert.Equal(a.State.FrameIndex, frameA);
    }

    /// <summary>
    /// The brush does <em>not</em> follow the document, and that is Q9 rather
    /// than an oversight.
    /// </summary>
    /// <remarks>
    /// Guarding a decision rather than a behaviour, which is unusual and is why
    /// it is spelled out. Q9 considered per-document first and rejected it the
    /// same day: it "fixes the page you already drew and leaves the next one
    /// starting from whatever you last used elsewhere". The store is
    /// <c>ProjectManifest.Brush</c>, and with no project the effective scope is
    /// Global. If somebody adds a brush to <see cref="DocumentScopedState"/>
    /// this fails and sends them to <c>DECISIONS.md</c> — which is the whole
    /// job of the test.
    /// </remarks>
    [AvaloniaFact]
    public void TheBrushDoesNotFollowTheDocument_BecauseQ9SaysSo()
    {
        var vm = new MainViewModel(null);
        Assert.Null(vm.ProjectDocker.Project);
        Assert.Equal(BrushScope.Global, vm.BrushScope);

        var a = vm.Tabs[0];
        vm.BrushSize = 77;

        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        Assert.Equal(77, vm.BrushSize);   // the same pencil, deliberately

        vm.BrushSize = 12;
        vm.ActiveTab = a;
        Assert.Equal(12, vm.BrushSize);

        Assert.Null(typeof(DocumentScopedState).GetField("Brush"));
    }

    /// <summary>The window's handler, so the mechanism is exercised end to end.</summary>
    private static void Carry(CanvasControl canvas, DocumentTab? leaving, DocumentTab arriving)
    {
        if (leaving is not null) leaving.State.View = canvas.Framing;
        if (arriving.State.View is { } framed) canvas.Framing = framed;
        else canvas.ResetView();
    }

    private static ReferenceStrip Strip(string name) => new() { Name = name };
}
