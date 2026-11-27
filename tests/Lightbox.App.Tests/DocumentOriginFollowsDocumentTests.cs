using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The canvas's paper corner follows the document it is showing (B502).
/// </summary>
/// <remarks>
/// <para>
/// <b>The canvas learns the corner by binding, and a binding only re-reads on a
/// notification.</b> <c>DocumentOrigin</c> was announced by the resize and the
/// crop and by nothing else, so a document that arrived already grown — opened
/// from disk, brought back by a tab, restored by an undo — was drawn on by a
/// canvas still holding the previous document's corner. <c>ViewToDoc</c> adds
/// that corner to every pen point, so each mark was recorded the grown distance
/// away from the pen, and the canvas then drew it, correctly, from the record:
/// the stroke vanished from under the pen and turned up somewhere else.
/// </para>
/// <para>
/// <b>Asked of the real window</b>, because the fault is the gap between the view
/// model's answer and the control's copy of it — <see cref="DocumentOriginInViewTests"/>
/// sets the control's property by hand and cannot see it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class DocumentOriginFollowsDocumentTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int Grew = 40;

    private MainWindow? _window;

    public override void Dispose()
    {
        _window?.Close();
        Dispatcher.UIThread.RunJobs();
        base.Dispose();
    }

    private (MainViewModel Vm, CanvasControl Canvas) Open()
    {
        var window = _window = new MainWindow { Width = 1400, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Grown", 200, 120, 12, 72, "#ffffff", false));
        Dispatcher.UIThread.RunJobs();
        return (vm, window.FindControl<CanvasControl>("Canvas")!);
    }

    private static void GrowAroundTheMiddle(MainViewModel vm)
    {
        var choice = new ResizeDialogViewModel(vm.Doc.Scene, ResizeMode.Canvas) { Anchor = ResizeAnchor.Center };
        choice.Width = vm.Doc.Scene.Width + 2 * Grew;
        choice.Height = vm.Doc.Scene.Height + 2 * Grew;
        Assert.True(vm.ApplyResize(choice));
        Dispatcher.UIThread.RunJobs();
    }

    private void Expect(CanvasControl canvas, MainViewModel vm, string when)
    {
        Dispatcher.UIThread.RunJobs();
        var scene = vm.Doc.Scene;
        output.WriteLine($"{when}: document corner ({scene.Left}, {scene.Top}), canvas holds {canvas.DocumentOrigin}");
        Assert.Equal(new PixelPoint(scene.Left, scene.Top), canvas.DocumentOrigin);
    }

    [AvaloniaFact]
    public void TheCanvasCornerFollowsATabSwitch()
    {
        var (vm, canvas) = Open();
        var grown = vm.ActiveTab!;
        GrowAroundTheMiddle(vm);
        Expect(canvas, vm, "grown");
        Assert.Equal(new PixelPoint(-Grew, -Grew), canvas.DocumentOrigin);

        // A second document, never grown: its paper starts at stroke zero.
        vm.NewDocument(new NewDocumentSettings("Flush", 200, 120, 12, 72, "#ffffff", false));
        Expect(canvas, vm, "new flush document");

        // Back to the grown one, which is the owner's report: it arrived with
        // its corner already moved, and nothing said so.
        vm.ActiveTab = grown;
        Expect(canvas, vm, "back on the grown tab");
        Assert.Equal(new PixelPoint(-Grew, -Grew), canvas.DocumentOrigin);
    }

    [AvaloniaFact]
    public void TheCanvasCornerFollowsAnUndoneAndRedoneResize()
    {
        var (vm, canvas) = Open();
        GrowAroundTheMiddle(vm);
        Expect(canvas, vm, "grown");

        vm.UndoCommand.Execute(null);
        Expect(canvas, vm, "resize undone");
        Assert.Equal(new PixelPoint(0, 0), canvas.DocumentOrigin);

        vm.RedoCommand.Execute(null);
        Expect(canvas, vm, "resize redone");
        Assert.Equal(new PixelPoint(-Grew, -Grew), canvas.DocumentOrigin);
    }

    [AvaloniaFact]
    public async Task TheCanvasCornerFollowsAGrownDocumentOpenedFromDisk()
    {
        var (vm, canvas) = Open();
        GrowAroundTheMiddle(vm);
        var path = Path.Combine(Path.GetTempPath(), $"b502-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            Lightbox.Core.Serialization.DocJson.Save(vm.Doc, path);
            vm.NewDocument(new NewDocumentSettings("Flush", 200, 120, 12, 72, "#ffffff", false));
            Expect(canvas, vm, "new flush document");

            Assert.True(await vm.OpenDocumentFileAsync(path));
            Expect(canvas, vm, "grown document opened");
            Assert.Equal(new PixelPoint(-Grew, -Grew), canvas.DocumentOrigin);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
