using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Guides survive the file. Reported as "reopen a document and the guides are
/// gone" — a guide is document data like the camera, so a save and a reopen
/// must hand back the rig exactly as it was left.
/// </summary>
[Collection("BrushState")]
public class GuidePersistenceTests : BrushStateIsolated
{
    private static MainViewModel Vm() => VmLayers.PaperVm();

    [AvaloniaFact]
    public void AGuideSurvivesSerializeAndDeserialize()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 10, 20, spacing: 48);
        vm.AddGuide(GuideKind.VanishingPoint, 500, 100, divisions: 12);

        var back = DocJson.Deserialize(vm.SerializeDocument());

        Assert.NotNull(back.Scene.Guides);
        Assert.Equal(2, back.Scene.Guides!.Count);
        Assert.Equal(GuideKind.Grid, back.Scene.Guides[0].Kind);
        Assert.Equal(48, back.Scene.Guides[0].Spacing);
    }

    [AvaloniaFact]
    public void AGuideSurvivesSaveAndReopenOfAStandaloneDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-guides-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            var vm = Vm();
            vm.ActiveTab!.FilePath = path;
            vm.AddGuide(GuideKind.Line, 100, 200, angle: 45);
            vm.Save();

            var reopened = Vm();
            reopened.OpenDocumentTab(DocJson.Load(path), path);

            Assert.True(reopened.HasGuides);
            Assert.Equal(GuideKind.Line, reopened.Guides[0].Kind);
            Assert.Equal(100, reopened.Guides[0].X);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The half the round-trip tests above cannot see, and the half the owner
    /// actually reported: the file carried the guides all along, and the canvas
    /// was never told about them on the way back in. <c>OnDocumentChanged</c>
    /// is the funnel a tab switch, a file open and an undo all run through, and
    /// it refreshed layers, audio and markers — everything but guides.
    /// </summary>
    [AvaloniaFact]
    public void ReopeningADocumentWithGuidesDrawsThemOnTheCanvas()
    {
        var window = new Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.FindControl<Lightbox.App.Rendering.CanvasControl>("Canvas")!;

        var doc = DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);
        doc.Scene.Guides = [new Guide { Kind = GuideKind.Line, X = 100, Y = 200 }];
        vm.OpenDocumentTab(doc, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(canvas.Guides);
        Assert.Contains(canvas.Guides!, g => g.Id == doc.Scene.Guides[0].Id);
    }

    [AvaloniaFact]
    public void SwitchingTabsShowsEachDocumentsOwnGuides()
    {
        var window = new Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.FindControl<Lightbox.App.Rendering.CanvasControl>("Canvas")!;

        var bare = DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);
        var guided = DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);
        guided.Scene.Guides = [new Guide { Kind = GuideKind.Grid, Spacing = 32 }];
        vm.OpenDocumentTab(bare, null);
        vm.OpenDocumentTab(guided, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(canvas.Guides);

        // Back to the bare document: its canvas must not keep the other rig.
        vm.ActiveTab = vm.Tabs[^2];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(canvas.Guides);

        vm.ActiveTab = vm.Tabs[^1];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(canvas.Guides);
    }

    [AvaloniaFact]
    public void UndoingAGuideEditRedrawsTheCanvas()
    {
        var window = new Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.FindControl<Lightbox.App.Rendering.CanvasControl>("Canvas")!;

        vm.OpenDocumentTab(DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor), null);
        vm.AddGuide(GuideKind.Line, 50, 50);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(canvas.Guides);

        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Null(canvas.Guides);
    }

    [AvaloniaFact]
    public void AGuideSurvivesSaveAndReopenOfAProjectDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-guides-{Guid.NewGuid():N}.lbproj");
        var vm = Vm();
        var reopened = Vm();
        try
        {
            vm.NewProject(root, "Guided");
            vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 64);
            vm.Save();

            // A fresh session: nothing shared with the one that saved.
            reopened.OpenProject(root);

            Assert.True(reopened.HasGuides);
            Assert.Equal(64, reopened.Guides[0].Spacing);
        }
        finally
        {
            vm.ProjectDocker.Dispose();
            reopened.ProjectDocker.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
