using Avalonia.Headless.XUnit;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B490's review. Reference sheets are held per editor, so a tab that is given
/// a new editor in place — a replace from the MCP surface or a test, a version
/// restore, File ▸ New over the blank tab — must register the new document's
/// sheets and let the old editor's go.
/// </summary>
[Collection("BrushState")]
public class ReferenceSheetsFollowTheEditorTests : BrushStateIsolated
{
    private static Doc DocWithAReference(out string id)
    {
        var vm = VmLayers.PaperVm();
        using var bmp = new SKBitmap(8, 8);
        bmp.Erase(SKColors.Red);
        var strip = new ReferenceStrip
        {
            Name = "ref",
            Png = Lightbox.Raster.PngCodec.Encode(bmp),
            SheetWidth = 8,
            SheetHeight = 8,
            Cells = [new ReferenceCell { Width = 8, Height = 8 }],
            Pinned = true,
            Opacity = 1.0,
            Scale = 1.0,
        };
        id = strip.Id;
        var doc = DocJson.Clone(vm.Doc);
        doc.Scene.References = [strip];
        return doc;
    }

    [AvaloniaFact]
    public void ADocumentReplacedIntoAnOpenTabDrawsItsReferences()
    {
        var vm = VmLayers.PaperVm();
        var replaced = vm.ActiveTab!.Editor;

        vm.ReplaceDocument(DocWithAReference(out var id));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotSame(replaced, vm.ActiveTab!.Editor);
        Assert.NotNull(Lightbox.Raster.ReferenceStripRegistry.Resolve(vm.Doc.Scene, id));
    }

    [AvaloniaFact]
    public void TheReplacedEditorsSheetsAreLetGo()
    {
        var vm = VmLayers.PaperVm();
        vm.ReplaceDocument(DocWithAReference(out var id));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var first = vm.Doc.Scene;
        Assert.NotNull(Lightbox.Raster.ReferenceStripRegistry.Resolve(first, id));

        vm.ReplaceDocument(DocWithAReference(out _));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(Lightbox.Raster.ReferenceStripRegistry.Resolve(first, id));
    }

    /// <summary>
    /// The sensitivity review's case: production footage on a document whose
    /// tab has closed. The project's loaded copy still exports, and the closed
    /// editor's sheets have been forgotten, so the export decodes its own.
    /// </summary>
    [AvaloniaFact]
    public void ADocumentExportedAfterItsTabClosedStillCarriesItsFootage()
    {
        var vm = VmLayers.PaperVm();
        var doc = DocWithAReference(out var id);
        doc.Scene.References![0].RendersInExport = true;
        vm.OpenDocumentTab(doc, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(Lightbox.Raster.ReferenceStripRegistry.Resolve(doc.Scene, id));
        vm.CloseTab(vm.ActiveTab!);
        Assert.Null(Lightbox.Raster.ReferenceStripRegistry.Resolve(doc.Scene, id));

        using var cache = new Lightbox.Raster.FrameBitmapCache();
        using var image = Lightbox.App.Services.SequenceExporter.RenderFrame(doc, cache, 0);
        using var raster = SKBitmap.FromImage(image);

        Assert.Equal(SKColors.Red, raster.GetPixel(2, 2));
    }
}
