using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// B427: File ▸ Open opens the documents this application saves.
/// </summary>
/// <remarks>
/// The dialog read the picked file as text and parsed it, but
/// <see cref="DocJson.Save"/> writes every document as gzip, so it failed on
/// all of them. The start screen and the recents used the gzip-aware loader,
/// which is how it went unnoticed.
/// </remarks>
[Collection("BrushState")]
public sealed class OpenDialogTests : BrushStateIsolated
{
    private static Doc Small()
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Name = "Ink", Cels = [new Cel { Frame = new Frame() }] });
        return doc;
    }

    private static (Lightbox.App.Views.MainWindow Window, MainViewModel Vm) Window()
    {
        var window = new Lightbox.App.Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    [AvaloniaFact]
    public async Task TheOpenDialogOpensADocumentThisApplicationSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-dialog-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(Small(), path); // gzip, as Save writes every document
            var (window, vm) = Window();

            await window.OpenPickedDocumentAsync(path, Path.GetFileName(path), () => Task.FromResult<Stream>(File.OpenRead(path)));

            Assert.Equal(path, vm.ActiveTab?.FilePath);
            Assert.Equal("Ink", vm.Doc.Scene.Layers[^1].Name);
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The same document handed over with no path (a cloud picker): read from the stream.</summary>
    [AvaloniaFact]
    public async Task TheOpenDialogOpensADocumentWithNoPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-dialog-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(Small(), path);
            var (window, vm) = Window();

            await window.OpenPickedDocumentAsync(null, "From the cloud.lightbox.json", () => Task.FromResult<Stream>(File.OpenRead(path)));

            Assert.Null(vm.ActiveTab?.FilePath);
            Assert.Equal("Ink", vm.Doc.Scene.Layers[^1].Name);
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A file that is not a drawing says so rather than taking the window down.</summary>
    [AvaloniaFact]
    public async Task AFileThatIsNotADrawingSaysSo()
    {
        var (window, vm) = Window();

        await window.OpenPickedDocumentAsync(null, "notes.txt",
            () => Task.FromResult<Stream>(new MemoryStream("not json"u8.ToArray())));

        Assert.Contains("Could not open notes.txt", vm.AiStatus);
        window.Close();
    }
}
