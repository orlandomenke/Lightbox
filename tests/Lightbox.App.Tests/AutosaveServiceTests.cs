using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>B187.</b> Autosave serialized the whole document on the UI thread, once
/// a minute, so the hitch grew with the painting and landed mid-stroke.
/// </summary>
/// <remarks>
/// The fix is a snapshot: <see cref="AutosaveService.Flush"/> clones the
/// document on the calling thread — milliseconds, per B142's measurement — and
/// serializes the clone to disk on a background task. What these tests pin is
/// the contract that makes that safe: the flush persists a document that
/// loads, the written file holds the strokes <em>as they were at flush
/// time</em> however the live document moves afterwards, and a clean document
/// writes nothing.
/// </remarks>
public sealed class AutosaveServiceTests : IDisposable
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"lightbox-autosave-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static Doc DocWithStrokes(int count)
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        for (var i = 0; i < count; i++)
        {
            frame.Strokes.Add(new Stroke { Points = [new StrokePoint(i, i * 2, 0.5)] });
        }
        return doc;
    }

    [AvaloniaFact]
    public void AFlushPersistsADocumentThatLoads()
    {
        var doc = DocWithStrokes(3);
        var path = PathFor("autosave.lightbox.json");
        var service = new AutosaveService(() => doc, TimeSpan.Zero, targetPath: path);

        service.MarkDirty();
        service.Flush();
        Assert.True(service.PendingWrite.Wait(WriteTimeout), "background write never finished");

        var recovered = DocJson.Load(path);
        Assert.Equal(3, recovered.Scene.Layers[0].Cels[0].Frame!.Strokes.Count);
    }

    [AvaloniaFact]
    public void TheWrittenFileHoldsTheStrokesAsTheyWereAtFlushTime()
    {
        // The race guard. The write happens off-thread against a clone taken
        // inside Flush, so a stroke added the instant Flush returns must not
        // leak into the recovery copy — if it can, the writer is reading the
        // live document while the artist paints on it.
        var doc = DocWithStrokes(2);
        var path = PathFor("autosave.lightbox.json");
        var service = new AutosaveService(() => doc, TimeSpan.Zero, targetPath: path);

        service.MarkDirty();
        service.Flush();
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(99, 99, 1)] });

        Assert.True(service.PendingWrite.Wait(WriteTimeout), "background write never finished");
        Assert.Equal(2, DocJson.Load(path).Scene.Layers[0].Cels[0].Frame!.Strokes.Count);
    }

    [AvaloniaFact]
    public void ACleanDocumentWritesNothing()
    {
        var doc = DocWithStrokes(1);
        var path = PathFor("autosave.lightbox.json");
        var service = new AutosaveService(() => doc, TimeSpan.Zero, targetPath: path);

        service.Flush();
        Assert.True(service.PendingWrite.Wait(WriteTimeout));
        Assert.False(File.Exists(path));
    }

    [AvaloniaFact]
    public void ASecondDirtyMarkAfterAFlushIsPersistedByTheNextFlush()
    {
        // The skip-while-writing path must not eat work: marking dirty after
        // one flush and flushing again ends with the newer content on disk.
        var doc = DocWithStrokes(1);
        var path = PathFor("autosave.lightbox.json");
        var service = new AutosaveService(() => doc, TimeSpan.Zero, targetPath: path);

        service.MarkDirty();
        service.Flush();
        Assert.True(service.PendingWrite.Wait(WriteTimeout));

        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(5, 5, 0.5)] });
        service.MarkDirty();
        service.Flush();
        Assert.True(service.PendingWrite.Wait(WriteTimeout));

        Assert.Equal(2, DocJson.Load(path).Scene.Layers[0].Cels[0].Frame!.Strokes.Count);
    }
}
