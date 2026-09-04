using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// A document reopens on the layer it was left on (B358).
/// </summary>
/// <remarks>
/// The same shape as the playhead (Q110/Q111), for the same reasons: nullable
/// so optional means absent, stamped by the save funnel rather than tracked
/// live because which layer is selected is view state (invariant 5), and
/// restored at every open. Stored by <b>id</b> rather than index, because a
/// frame is a number and a layer has identity — an index silently means a
/// different layer once anything is reordered, and restoring the wrong layer
/// is worse than restoring none, since the artist finds out by drawing on it.
/// </remarks>
[Collection("BrushState")]
public class LastLayerTests : BrushStateIsolated
{
    private const int W = 32;
    private const int H = 24;

    private static Doc DocWith(params string[] names)
    {
        var doc = DocumentFactory.CreateDoc(W, H, paperColor: Scene.DefaultBackgroundColor);
        doc.Scene.Layers.RemoveAll(l => !l.IsBackground);
        foreach (var name in names)
        {
            doc.Scene.Layers.Add(new Layer { Name = name, Cels = [new Cel { Frame = new Frame() }] });
        }
        return doc;
    }

    /// <summary>Open a document, select a layer by name, and serialize as a save would.</summary>
    private static string SavedOn(Doc doc, string layerName)
    {
        var vm = VmLayers.PaperVm();
        vm.OpenDocumentTab(doc, null);
        vm.ActiveLayerIndex = doc.Scene.Layers.FindIndex(l => l.Name == layerName);
        return vm.SerializeDocument();
    }

    private static MainViewModel Reopened(string json)
    {
        var vm = VmLayers.PaperVm();
        vm.OpenDocumentTab(DocJson.Deserialize(json), null);
        return vm;
    }

    [AvaloniaFact]
    public void ReopeningLandsOnTheLayerItWasLeftOn()
    {
        var json = SavedOn(DocWith("Rough", "Ink", "Colour"), "Colour");

        Assert.Equal("Colour", Reopened(json).PaintLayer().Name);
    }

    [AvaloniaFact]
    public void TheLayerItWouldHaveOpenedOnAnywayWritesNoKey()
    {
        // Optional means absent. Most documents are left on the layer the open
        // would have chosen, and those must carry nothing — checked by dumping
        // the JSON rather than by reading the model.
        var json = SavedOn(DocWith("Rough", "Ink"), "Rough");

        Assert.DoesNotContain("\"activeLayerId\"", json);
        Assert.Equal("Rough", Reopened(json).PaintLayer().Name);
    }

    [AvaloniaFact]
    public void ADeletedLayerIsNotHonoured()
    {
        // An id can name a layer that is gone. Falling back is the point: the
        // open must not land somewhere that does not exist.
        var doc = DocWith("Rough", "Ink");
        var json = SavedOn(doc, "Ink");
        var back = DocJson.Deserialize(json);
        back.Scene.Layers.RemoveAll(l => l.Name == "Ink");

        var vm = VmLayers.PaperVm();
        vm.OpenDocumentTab(back, null);

        Assert.Equal("Rough", vm.PaintLayer().Name);
    }

    [AvaloniaFact]
    public void ALayerLockedSinceTheSaveIsNotHonouredEither()
    {
        // B357 through the back door, and the reason the resolver asks
        // Paintable rather than merely "does this id exist": honouring a
        // locked layer would put the caret where the first stroke goes nowhere.
        var doc = DocWith("Rough", "Ink");
        var json = SavedOn(doc, "Ink");
        var back = DocJson.Deserialize(json);
        back.Scene.Layers.First(l => l.Name == "Ink").Locked = true;

        var vm = VmLayers.PaperVm();
        vm.OpenDocumentTab(back, null);

        Assert.Equal("Rough", vm.PaintLayer().Name);
    }

    [AvaloniaFact]
    public void TheLayerRidesAlongsideThePlayheadNotInsteadOfIt()
    {
        // Both are stamped by the one funnel, so a document put down on frame 3
        // of its third layer comes back on both — not one or the other.
        var doc = DocWith("Rough", "Ink", "Colour");
        var vm = VmLayers.PaperVm();
        vm.OpenDocumentTab(doc, null);
        vm.ActiveLayerIndex = doc.Scene.Layers.FindIndex(l => l.Name == "Colour");
        vm.CurrentFrameIndex = 3;
        var json = vm.SerializeDocument();

        var reopened = Reopened(json);

        Assert.Equal("Colour", reopened.PaintLayer().Name);
        Assert.Equal(3, reopened.CurrentFrameIndex);
    }
}
