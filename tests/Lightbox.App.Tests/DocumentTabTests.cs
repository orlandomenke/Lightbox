using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class DocumentTabTests
{
    [AvaloniaFact]
    public void StartsWithOneUntitledTabThatSaysItIsNotOnDisk()
    {
        var vm = new MainViewModel(null);
        var tab = Assert.Single(vm.Tabs);
        Assert.Equal("Untitled-1", tab.Title);
        // B97. It badges, because it has never been written — the reporter
        // found the old silence and the badge is the honest answer. What it
        // must NOT do is claim there is work to lose: nothing has been drawn,
        // so closing it asks nothing. Two predicates on purpose.
        Assert.True(tab.IsDirty);
        Assert.True(tab.IsUnsaved);
        Assert.False(tab.HasWorkToLose);
        Assert.True(tab.IsActive);
        Assert.Same(vm.Doc, tab.Doc);
    }

    [AvaloniaFact]
    public void NewDocument_AddsTabWithSettings_AndActivatesIt()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Shot 12", 1920, 1080, 24, 300, "#202020", TransparentBackground: false));

        Assert.Equal(2, vm.Tabs.Count);
        var tab = vm.Tabs[1];
        Assert.Same(tab, vm.ActiveTab);
        Assert.Equal("Shot 12", tab.Title);
        Assert.Equal(1920, vm.Doc.Scene.Width);
        Assert.Equal(24, vm.Doc.Scene.Fps);
        Assert.Equal(300, vm.Doc.Scene.Ppi);
        Assert.Equal("#202020", vm.Doc.Scene.BackgroundColor);
        // Never written, so it badges (B97) — and settings chosen in the New
        // dialog are not work the artist would mourn.
        Assert.True(tab.IsUnsaved);
        Assert.False(tab.HasWorkToLose);
    }

    [AvaloniaFact]
    public void PaintingMarksTheTabDirty_SaveClearsIt()
    {
        var vm = new MainViewModel(null);
        Assert.False(vm.Tabs[0].HasWorkToLose);

        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 30, 0.5);
        vm.EndStroke();
        Assert.True(vm.Tabs[0].IsDirty);
        Assert.True(vm.Tabs[0].HasWorkToLose);
        Assert.EndsWith("•", vm.Tabs[0].DisplayTitle);

        vm.NotifySaved("/somewhere/scene-a.lightbox.json");
        Assert.False(vm.Tabs[0].IsDirty);
        Assert.Equal("scene-a", vm.Tabs[0].Title);
        Assert.Equal("scene-a", vm.Tabs[0].DisplayTitle);
    }

    [AvaloniaFact]
    public void SwitchingTabs_KeepsEachDocumentAndItsUndoHistory()
    {
        var vm = new MainViewModel(null);
        vm.BeginStroke(10, 10, 0.5);
        vm.EndStroke(); // tab 1 has one stroke + one undo step

        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        // The paint layer, not layer 0 — a document with a paper colour puts
        // its locked Background layer at the bottom of the stack.
        Assert.Empty(PaintStrokes(vm)); // fresh document, not the painted one

        vm.ActiveTab = vm.Tabs[0];
        Assert.Single(PaintStrokes(vm));

        // Undo history survived the round trip away and back.
        vm.UndoCommand.Execute(null);
        Assert.Empty(PaintStrokes(vm));
    }

    private static List<Stroke> PaintStrokes(MainViewModel vm)
    {
        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);
        return ((PaintedFrame)layer.Cels[0].Frame!).Strokes;
    }

    [AvaloniaFact]
    public void SwitchingTabs_DoesNotMarkAnythingDirty_AndRestoresPlayhead()
    {
        var vm = new MainViewModel(null);
        vm.AddFrameCommand.Execute(null);
        vm.NotifySaved("/tmp/a.lightbox.json"); // clean state, 2 frames, playhead at 1

        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        Assert.Equal(0, vm.CurrentFrameIndex);

        vm.ActiveTab = vm.Tabs[0];
        Assert.Equal(1, vm.CurrentFrameIndex); // restored
        // Switching is not an edit, so neither tab has work in it. They both
        // still badge as never-written, which is a different claim (B97).
        Assert.False(vm.Tabs[0].HasWorkToLose);
        Assert.False(vm.Tabs[1].HasWorkToLose);
    }

    [AvaloniaFact]
    public void CloseTab_ActivatesNeighbor_AndNeverLeavesZeroTabs()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("B", 400, 300, 12, 72, "#ffffff", false));
        vm.NewDocument(new NewDocumentSettings("C", 400, 300, 12, 72, "#ffffff", false));

        vm.CloseTab(vm.Tabs[2]); // close active "C"
        Assert.Equal(2, vm.Tabs.Count);
        Assert.NotNull(vm.ActiveTab);
        Assert.Contains(vm.ActiveTab!, vm.Tabs);

        vm.CloseTab(vm.Tabs[0]);
        vm.CloseTab(vm.Tabs[0]);
        var last = Assert.Single(vm.Tabs); // a fresh untitled appears
        Assert.Same(last, vm.ActiveTab);
        Assert.False(last.HasWorkToLose);
    }

    [AvaloniaFact]
    public void OpenDocumentTab_UsesFileName_AndKeepsExistingTabs()
    {
        var vm = new MainViewModel(null);
        var doc = Lightbox.Core.Documents.DocumentFactory.CreateDoc(320, 180, 8);
        vm.OpenDocumentTab(DocJson.Clone(doc), "/projects/walk-cycle.lightbox.json");

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Equal("walk-cycle", vm.ActiveTab!.Title);
        Assert.Equal(320, vm.Doc.Scene.Width);
        // Opened from disk, so this one is genuinely clean rather than merely
        // unstarted — the case that separates IsDirty from HasWorkToLose.
        Assert.False(vm.ActiveTab.IsDirty);
        Assert.False(vm.ActiveTab.IsUnsaved);
    }

    /// <summary>
    /// A document with no layers opens instead of taking the application down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// B56, found sideways while writing a B31 test: <c>SyncLayerRows</c> ran
    /// <c>Math.Clamp(ActiveLayerIndex, 0, layers.Count - 1)</c>, and with no layers that is
    /// <c>Clamp(0, 0, -1)</c> — an <c>ArgumentException</c> reading "'0' cannot be greater than
    /// -1", thrown out of a property setter deep under <c>ActiveTab</c>.
    /// </para>
    /// <para>
    /// Reachable rather than theoretical: nothing in <c>DocJson</c> backfills a layer, so any
    /// <c>.lightbox.json</c> whose scene carries <c>"layers": []</c> does this — hand-edited,
    /// written by a script or an agent, or saved by a version that allowed the state. The
    /// message names neither the file nor layers, which is the part that would have cost an
    /// afternoon.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ADocumentWithNoLayersOpensRatherThanThrowing()
    {
        var vm = new MainViewModel(null);
        var layerless = new Doc();   // Scene.Layers is empty and nothing fills it in
        Assert.Empty(layerless.Scene.Layers);

        vm.OpenDocumentTab(layerless, "/projects/hand-edited.lightbox.json");

        Assert.Same(layerless, vm.Doc);
        Assert.Empty(vm.LayerRows);
        // And it is still usable: the next document opens on top of it.
        vm.OpenDocumentTab(DocumentFactory.CreateDoc(120, 90, 4), null);
        Assert.Single(vm.Doc.Scene.Layers);
    }
}

public class BackgroundColorTests
{
    [AvaloniaFact]
    public void SceneBackground_RoundTrips_AndTintsTheSnapshot()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Dark", 100, 100, 12, 72, "#336699", false));

        var restored = DocJson.Deserialize(DocJson.Serialize(vm.Doc));
        Assert.Equal("#336699", restored.Scene.BackgroundColor);
        Assert.False(restored.Scene.TransparentBackground);
        Assert.Equal(72, restored.Scene.Ppi);

        Lightbox.App.Rendering.RenderSnapshot? last = null;
        vm.SnapshotChanged += s => last = s;
        vm.PublishSnapshot();
        using var bmp = SKBitmap.FromImage(last!.Image);
        Assert.Equal(new SKColor(0x33, 0x66, 0x99), bmp.GetPixel(50, 50));
    }

    [AvaloniaFact]
    public void TransparentBackground_RendersTransparentPixels()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Alpha", 100, 100, 12, 72, "#ffffff", TransparentBackground: true));

        Lightbox.App.Rendering.RenderSnapshot? last = null;
        vm.SnapshotChanged += s => last = s;
        vm.PublishSnapshot();
        using var bmp = SKBitmap.FromImage(last!.Image);
        Assert.Equal(0, bmp.GetPixel(50, 50).Alpha);
    }
}

public class ColorWheelFidelityTests
{
    [AvaloniaFact]
    public void WheelValue_IsNotRewrittenWhileDragging()
    {
        var picker = new MainViewModel(null).ColorPicker;

        // Simulate spectrum drag updates: each set must stick exactly —
        // write-back of recomputed values is what made the thumb snap away.
        var drag = new Avalonia.Media.HsvColor(1, 123.437, 0.5678, 0.9123);
        picker.WheelHsv = drag;
        Assert.Equal(drag.H, picker.WheelHsv.H, 10);
        Assert.Equal(drag.S, picker.WheelHsv.S, 10);
        Assert.Equal(drag.V, picker.WheelHsv.V, 10);

        var drag2 = new Avalonia.Media.HsvColor(1, 123.501, 0.5702, 0.9123);
        picker.WheelHsv = drag2;
        Assert.Equal(drag2.H, picker.WheelHsv.H, 10);
        Assert.Equal(drag2.S, picker.WheelHsv.S, 10);
    }

    [AvaloniaFact]
    public void SliderChannels_AreNotRewrittenWhileEditing()
    {
        var picker = new MainViewModel(null).ColorPicker;
        picker.HsvValue = 37.5;
        Assert.Equal(37.5, picker.HsvValue, 10); // kept verbatim, not round-tripped

        picker.Red = 123;
        Assert.Equal(123, picker.Red, 10);
        // ...while the other representations did follow.
        Assert.NotEqual(0, picker.Cyan + picker.Magenta + picker.Yellow + picker.Black);
    }
}
