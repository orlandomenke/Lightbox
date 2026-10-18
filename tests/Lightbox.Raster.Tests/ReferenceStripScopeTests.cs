using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B490. Decoded reference sheets belong to the document that registered them,
/// not to the strip id: ids repeat across a copy, a template and the same file
/// opened twice, and a reopened file is a different document from the tab it
/// replaced.
/// </summary>
public class ReferenceStripScopeTests
{
    private const string Id = "ref_shared";

    private static string Png(SKColor colour)
    {
        using var bmp = new SKBitmap(4, 4);
        bmp.Erase(colour);
        return PngCodec.Encode(bmp);
    }

    private static SKBitmap Sheet(SKColor colour)
    {
        var bmp = new SKBitmap(4, 4);
        bmp.Erase(colour);
        return bmp;
    }

    private static Doc DocWithStrip(SKColor colour)
    {
        var doc = new Doc { Scene = new Scene { Width = 8, Height = 8, FrameCount = 1 } };
        doc.Scene.References = [new ReferenceStrip { Id = Id, Png = Png(colour) }];
        return doc;
    }

    private static void RegisterAll(Doc doc) =>
        ReferenceStripRegistry.Register(doc.Scene, doc.Scene.References!.Select(s => (s.Id, s.Png)));

    private static SKColor Colour(Scene scene) =>
        ReferenceStripRegistry.Resolve(scene, Id) is { } sheet ? sheet.GetPixel(0, 0) : SKColors.Transparent;

    [Fact]
    public void TwoDocumentsWithTheSameStripIdHoldTheirOwnSheets()
    {
        var red = new DocumentEditor(DocWithStrip(SKColors.Red));
        var blue = new DocumentEditor(DocWithStrip(SKColors.Blue));
        RegisterAll(red.Doc);
        RegisterAll(blue.Doc);

        Assert.Equal(SKColors.Red, Colour(red.Doc.Scene));
        Assert.Equal(SKColors.Blue, Colour(blue.Doc.Scene));

        ReferenceStripRegistry.ForgetScope(red);
        ReferenceStripRegistry.ForgetScope(blue);
    }

    [Fact]
    public void ReplacingASheetInOneDocumentLeavesTheOtherAlone()
    {
        var original = new DocumentEditor(DocWithStrip(SKColors.Red));
        var copy = new DocumentEditor(original.Doc.Clone());
        RegisterAll(original.Doc);
        RegisterAll(copy.Doc);

        ReferenceStripRegistry.Replace(copy.Doc.Scene, Id, Sheet(SKColors.Green));

        Assert.Equal(SKColors.Red, Colour(original.Doc.Scene));
        Assert.Equal(SKColors.Green, Colour(copy.Doc.Scene));

        ReferenceStripRegistry.ForgetScope(original);
        ReferenceStripRegistry.ForgetScope(copy);
    }

    [Fact]
    public void AnUndoCopyOfTheSameDocumentSeesTheSameSheet()
    {
        // Undo hands back a clone of the document, and a render snapshot is
        // one too; both must find what the live document registered.
        var editor = new DocumentEditor(DocWithStrip(SKColors.Red));
        RegisterAll(editor.Doc);

        var snapshot = editor.Doc.Clone();

        Assert.Same(
            ReferenceStripRegistry.Resolve(editor.Doc.Scene, Id),
            ReferenceStripRegistry.Resolve(snapshot.Scene, Id));

        ReferenceStripRegistry.ForgetScope(editor);
    }

    [Fact]
    public void ForgettingADocumentDropsOnlyItsSheets()
    {
        var closed = new DocumentEditor(DocWithStrip(SKColors.Red));
        var open = new DocumentEditor(DocWithStrip(SKColors.Blue));
        RegisterAll(closed.Doc);
        RegisterAll(open.Doc);
        var retired = new List<SKBitmap>();

        ReferenceStripRegistry.ForgetScope(closed, retired.Add);

        Assert.Null(ReferenceStripRegistry.Resolve(closed.Doc.Scene, Id));
        Assert.Equal(SKColors.Blue, Colour(open.Doc.Scene));
        Assert.Single(retired);

        ReferenceStripRegistry.ForgetScope(open);
    }

    [Fact]
    public void AReplacedSheetGoesToTheRetirerRatherThanTheFinaliser()
    {
        var editor = new DocumentEditor(DocWithStrip(SKColors.Red));
        RegisterAll(editor.Doc);
        var before = ReferenceStripRegistry.Resolve(editor.Doc.Scene, Id);
        var retired = new List<SKBitmap>();

        ReferenceStripRegistry.Replace(editor.Doc.Scene, Id, Sheet(SKColors.Green), retired.Add);

        Assert.Same(before, Assert.Single(retired));
        ReferenceStripRegistry.ForgetScope(editor);
    }

    [Fact]
    public void WhileAnExportIsReadingADisplacedSheetIsLeftAlone()
    {
        // An export draws on its own thread and pins nothing, so a sheet it
        // may be drawing must not be freed under it.
        var editor = new DocumentEditor(DocWithStrip(SKColors.Red));
        RegisterAll(editor.Doc);
        var before = ReferenceStripRegistry.Resolve(editor.Doc.Scene, Id)!;
        var retired = new List<SKBitmap>();

        using (ReferenceStripRegistry.Reading())
        {
            ReferenceStripRegistry.Replace(editor.Doc.Scene, Id, Sheet(SKColors.Green), retired.Add);
        }

        Assert.Empty(retired);
        Assert.NotEqual(IntPtr.Zero, before.Handle);
        ReferenceStripRegistry.ForgetScope(editor);
    }

    [Fact]
    public void TheScopeIsNeverWritten()
    {
        var editor = new DocumentEditor(DocWithStrip(SKColors.Red));
        Assert.Same(editor, editor.Doc.Scene.ResourceScope);

        var json = DocJson.Serialize(editor.Doc);

        Assert.DoesNotContain("resourceScope", json, StringComparison.OrdinalIgnoreCase);
    }
}
