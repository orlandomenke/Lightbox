using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Opening a document reads the file on a worker, and the window says it is
/// opening instead of freezing (Q229).
/// </summary>
/// <remarks>
/// <para>
/// A 30-layer, 200-drawing document took about 2.5 s to parse cold, all of it
/// on the UI thread: the window did not repaint, and a click went nowhere.
/// </para>
/// <para>
/// <b>The owner's choice was "an Opening… tab with no canvas", and the way it
/// is built is chosen so nothing can be lost.</b> There is no stand-in document
/// while the file loads — a blank one could be drawn on and then thrown away
/// when the real one arrives. The tab strip shows the name as opening, the
/// canvas is covered, and the document becomes a real tab only once it exists.
/// </para>
/// <para>
/// The loader is a seam (<see cref="MainViewModel.LoadDocument"/>) so a test
/// can hold a load open and look at the window in between.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class OpenOffTheUiThreadTests : BrushStateIsolated
{
    private readonly Func<string, Doc> _loader = MainViewModel.LoadDocument;

    public override void Dispose()
    {
        MainViewModel.LoadDocument = _loader;
        base.Dispose();
    }

    private static Doc Small()
    {
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Name = "Ink", Cels = [new Cel { Frame = new Frame() }] });
        return doc;
    }

    [AvaloniaFact]
    public async Task WhileTheFileLoadsTheWindowSaysSoAndTheOldDocumentIsUntouched()
    {
        var vm = VmLayers.PaperVm();
        var before = vm.ActiveTab;
        var gate = new TaskCompletionSource();
        MainViewModel.LoadDocument = _ =>
        {
            gate.Task.Wait();
            return Small();
        };

        var opening = vm.OpenDocumentFileAsync(Path.Combine(Path.GetTempPath(), "Big scene.lightbox.json"));

        Assert.False(opening.IsCompleted, "the load ran on the UI thread");
        Assert.True(vm.IsOpeningDocument);
        Assert.Equal("Big scene", Assert.Single(vm.OpeningDocuments).Title);
        Assert.Same(before, vm.ActiveTab);

        gate.SetResult();
        Assert.True(await opening);

        Assert.False(vm.IsOpeningDocument);
        Assert.Empty(vm.OpeningDocuments);
        Assert.NotSame(before, vm.ActiveTab);
        Assert.Equal("Big scene", vm.ActiveTab!.Title);
        Assert.Equal("Ink", vm.Doc.Scene.Layers[^1].Name);
    }

    [AvaloniaFact]
    public async Task AFileThatWillNotOpenSaysWhyAndLeavesNothingBehind()
    {
        var vm = VmLayers.PaperVm();
        var tabs = vm.Tabs.Count;
        MainViewModel.LoadDocument = _ => throw new System.Text.Json.JsonException("not a drawing");

        Assert.False(await vm.OpenDocumentFileAsync(Path.Combine(Path.GetTempPath(), "Broken.lightbox.json")));

        Assert.False(vm.IsOpeningDocument);
        Assert.Equal(tabs, vm.Tabs.Count);
        Assert.Contains("Broken", vm.AiStatus);
        Assert.Contains("not a drawing", vm.AiStatus);
    }

    /// <summary>A real file, through the real loader: the seam changes nothing about what opens.</summary>
    [AvaloniaFact]
    public async Task ARealFileOpensThroughTheWorker()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-async-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(Small(), path);
            var vm = VmLayers.PaperVm();

            Assert.True(await vm.OpenDocumentFileAsync(path));

            Assert.Equal(path, vm.ActiveTab!.FilePath);
            Assert.Equal("Ink", vm.Doc.Scene.Layers[^1].Name);
            Assert.False(vm.ActiveTab.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// File ▸ Open reads a document this application saved. It read the picked
    /// file as text and parsed that, but a saved document is gzip, so the dialog
    /// could not open the app's own files (found 2026-10-09 while moving opening
    /// off the UI thread; the start screen and the recents used the gzip-aware
    /// loader and never showed it).
    /// </summary>
    [AvaloniaFact]
    public async Task TheOpenDialogOpensADocumentThisApplicationSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-dialog-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(Small(), path); // gzip, as Save writes every document
            var window = new Lightbox.App.Views.MainWindow();
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var vm = (MainViewModel)window.DataContext!;

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

    /// <summary>The same document handed over with no path (a cloud picker): read from the stream, gzip or not.</summary>
    [AvaloniaFact]
    public async Task TheOpenDialogOpensADocumentWithNoPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-dialog-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            DocJson.Save(Small(), path);
            var window = new Lightbox.App.Views.MainWindow();
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var vm = (MainViewModel)window.DataContext!;

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

    /// <summary>
    /// While a file opens the canvas is covered with its name and a moving
    /// indicator, the menus wait, and a key does nothing — the window answers,
    /// but nothing can be edited while it is not the document the artist asked
    /// for. Through the real window: MainWindow.axaml had no test of its own.
    /// </summary>
    [AvaloniaFact]
    public async Task WhileAFileOpensTheCanvasIsCoveredAndTheKeysWait()
    {
        var window = new Lightbox.App.Views.MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < 6) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 2;
        Pump();

        var gate = new TaskCompletionSource();
        MainViewModel.LoadDocument = _ =>
        {
            gate.Task.Wait();
            return Small();
        };
        var opening = vm.OpenDocumentFileAsync(Path.Combine(Path.GetTempPath(), "Big scene.lightbox.json"));
        Pump();

        var cover = window.FindControl<Avalonia.Controls.Border>("OpeningDocument")!;
        Assert.True(cover.IsVisible, "the canvas was not covered while the file opened");
        Assert.Contains(cover.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.Text?.Contains("Big scene") == true);
        Assert.Contains(cover.GetVisualDescendants().OfType<Avalonia.Controls.ProgressBar>(), p => p.IsIndeterminate);
        Assert.False(window.FindControl<Avalonia.Controls.Menu>("MainMenu")!.IsEnabled, "the menus took commands while a file opened");

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.ArrowRight, Avalonia.Input.RawInputModifiers.None);
        window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.ArrowRight, Avalonia.Input.RawInputModifiers.None);
        Pump();
        Assert.Equal(2, vm.CurrentFrameIndex);

        gate.SetResult();
        Assert.True(await opening);
        Pump();
        Assert.False(cover.IsVisible);
        Assert.True(window.FindControl<Avalonia.Controls.Menu>("MainMenu")!.IsEnabled);
        window.Close();
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
