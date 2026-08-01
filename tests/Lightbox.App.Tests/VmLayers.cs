using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Addressing helpers for tests that care about "the layer I am drawing on".
///
/// They used to write <c>Layers[0]</c> for that, which was true only while the
/// startup document had a single layer. It now opens on paper — a locked
/// Background layer holding the paper colour — so layer 0 is the paper and the
/// drawing layer is the active one. Saying so directly also makes the tests
/// immune to the next change in the default layer stack.
/// </summary>
internal static class VmLayers
{
    /// <summary>
    /// A view model on a transparent document — one ordinary drawing layer and
    /// no paper. For tests whose subject is layer or cel <em>addressing</em>
    /// (folders, ranges, transform scopes, row indices), where a locked
    /// Background layer at index 0 is noise that shifts every number in the
    /// test without changing anything it is checking.
    /// </summary>
    public static MainViewModel BareVm()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Test", 960, 540, 12, 72, "#ffffff", true));
        return vm;
    }

    public static Layer PaintLayer(this MainViewModel vm) => vm.Doc.Scene.Layers[vm.ActiveLayerIndex];

    public static PaintedFrame PaintedCel(this MainViewModel vm, int cel = 0) =>
        (PaintedFrame)vm.PaintLayer().Cels[cel].Frame!;

    public static List<Stroke> PaintStrokes(this MainViewModel vm, int cel = 0) => vm.PaintedCel(cel).Strokes;
}
