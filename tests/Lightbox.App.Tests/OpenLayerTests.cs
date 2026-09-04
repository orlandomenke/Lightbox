using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Opening a document puts the caret on a layer you can actually paint on
/// (B357).
/// </summary>
/// <remarks>
/// <para>
/// B136 already fixed half of this: index 0 is the locked paper on any
/// document that has one, and opening on it was reported as *"unable to draw
/// on the last build"*. That fix asked <c>!IsBackground</c> — which is the
/// paper and nothing else. A document whose first real layer is **locked**,
/// or hidden, or inside a locked folder, still opened with that layer selected
/// and the first stroke still went nowhere.
/// </para>
/// <para>
/// <c>CanEdit</c> has always known the answer and asks it before every mark:
/// visible, and editable — where editable accounts for the enclosing folder.
/// Two places asking "can this be painted on" two different ways is the whole
/// defect, so there is one predicate now and both use it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class OpenLayerTests : BrushStateIsolated
{
    private const int W = 32;
    private const int H = 24;

    /// <summary>A paper layer plus the named layers above it, in order.</summary>
    private static Doc DocWith(params string[] names)
    {
        var doc = DocumentFactory.CreateDoc(W, H, paperColor: Scene.DefaultBackgroundColor);
        // The factory ships a paper layer and one "Paint" layer. The paper
        // stays — it is the thing B136 is about — and the default paint layer
        // goes, so the named ones below are the only candidates.
        doc.Scene.Layers.RemoveAll(l => !l.IsBackground);
        foreach (var name in names)
        {
            doc.Scene.Layers.Add(new Layer { Name = name, Cels = [new Cel { Frame = new Frame() }] });
        }
        return doc;
    }

    private static Layer Named(Doc doc, string name) => doc.Scene.Layers.First(l => l.Name == name);

    /// <summary>Open it the way the report does: from its serialized form.</summary>
    private static MainViewModel Reopened(Doc doc)
    {
        var vm = VmLayers.PaperVm();
        vm.OpenDocumentTab(DocJson.Deserialize(DocJson.Serialize(doc)), null);
        return vm;
    }

    [AvaloniaFact]
    public void ALockedFirstLayerIsNotWhereTheCaretLands()
    {
        var doc = DocWith("Locked ink", "Free paint");
        Named(doc, "Locked ink").Locked = true;

        var vm = Reopened(doc);

        Assert.Equal("Free paint", vm.PaintLayer().Name);
    }

    [AvaloniaFact]
    public void AHiddenFirstLayerIsNotEither()
    {
        // The other half of what CanEdit refuses, and equally invisible to a
        // not-the-paper test.
        var doc = DocWith("Hidden rough", "Free paint");
        Named(doc, "Hidden rough").Visible = false;

        var vm = Reopened(doc);

        Assert.Equal("Free paint", vm.PaintLayer().Name);
    }

    [AvaloniaFact]
    public void WithNothingPaintableTheCaretIsStillOffThePaper()
    {
        // The second fallback. Landing on the paper would trade one silent
        // failure for another; landing on a real layer lets CanEdit say why.
        var doc = DocWith("Only layer");
        Named(doc, "Only layer").Locked = true;

        var vm = Reopened(doc);

        Assert.Equal("Only layer", vm.PaintLayer().Name);
        Assert.False(vm.PaintLayer().IsBackground);
    }

    [AvaloniaFact]
    public void AnUnlockedDocumentIsUnchanged()
    {
        // The guard against fixing this too broadly: with everything paintable
        // the answer is still the first layer above the paper.
        var doc = DocWith("First", "Second");

        var vm = Reopened(doc);

        Assert.Equal("First", vm.PaintLayer().Name);
    }

    [AvaloniaFact]
    public void ThePaperIsStillNeverTheAnswer()
    {
        // B136's own case, kept here so this file covers the whole rule rather
        // than only the part that was still broken.
        var doc = DocWith("Ink");

        var vm = Reopened(doc);

        Assert.False(vm.PaintLayer().IsBackground);
        Assert.Equal("Ink", vm.PaintLayer().Name);
    }
}
