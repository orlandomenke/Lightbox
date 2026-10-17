using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Reaching a document reference from where the animations are — the project
/// panel — and seeing what it is linked to once it is up: the Reference panel's
/// link line, Refresh now and Open source.
/// </summary>
public sealed class DocumentReferenceLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-doclink-" + Guid.NewGuid().ToString("N"));

    public DocumentReferenceLinkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Doc Run(int frames)
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24, paperColor: "#ffffff");
        doc.Scene.FrameCount = frames;
        var paint = doc.Scene.Layers.Single(l => !l.IsBackground);
        paint.Cels = [];
        for (var i = 0; i < frames; i++)
        {
            var stroke = new Stroke { Color = "#ff0000" };
            stroke.Brush.Size = 16;
            for (var p = 0; p < 6; p++) stroke.Points.Add(new StrokePoint(60 + i * 40 + p, 90, 1.0));
            paint.Cels.Add(new Cel { Frame = new Frame { Strokes = [stroke] } });
        }
        return doc;
    }

    /// <summary>A project with the run saved in it, and the jump open as the current tab.</summary>
    private (MainViewModel Vm, Project Project, DocumentRef RunRef, string RunPath) ProjectWithRun(int frames = 3)
    {
        var vm = new MainViewModel();
        var project = ProjectIo.Create("Demonizer", Path.Combine(_dir, "project"));
        vm.ProjectDocker.Project = project;
        var runRef = new DocumentRef { Name = "run", Path = "maincharacter/run.lightbox.json" };
        var runPath = project.PathOf(runRef);
        Directory.CreateDirectory(Path.GetDirectoryName(runPath)!);
        DocJson.Save(Run(frames), runPath);

        vm.NewDocument(new NewDocumentSettings("jump", 320, 180, 11, 72, Scene.DefaultBackgroundColor, false));
        vm.ActiveTab!.FilePath = Path.Combine(Path.GetDirectoryName(runPath)!, "jump.lightbox.json");
        return (vm, project, runRef, runPath);
    }

    // ---- from the project panel -------------------------------------------------

    [AvaloniaFact]
    public async Task AProjectDocumentCanBeReferencedFromItsRow()
    {
        var (vm, _, runRef, _) = ProjectWithRun(frames: 3);

        var said = await vm.ReferenceProjectDocumentAsync(runRef);

        var strip = Assert.Single(vm.Doc.Scene.References!);
        Assert.True(strip.IsDocument);
        Assert.Equal(3, strip.Cells.Count);
        Assert.Contains("run", said);
    }

    [AvaloniaFact]
    public async Task ADocumentNeverSavedSaysToSaveItFirst()
    {
        var (vm, _, _, _) = ProjectWithRun();
        var unsaved = new DocumentRef { Name = "walk", Path = "maincharacter/walk.lightbox.json" };

        var said = await vm.ReferenceProjectDocumentAsync(unsaved);

        Assert.Contains("Save", said);
        Assert.Null(vm.Doc.Scene.References);
    }

    [AvaloniaFact]
    public async Task TheCurrentDocumentCannotBeReferencedFromItsOwnRow()
    {
        var (vm, project, _, _) = ProjectWithRun();
        var jumpRef = new DocumentRef { Name = "jump", Path = "maincharacter/jump.lightbox.json" };
        DocJson.Save(vm.Doc, project.PathOf(jumpRef));

        var said = await vm.ReferenceProjectDocumentAsync(jumpRef);

        Assert.Contains("itself", said);
        Assert.Null(vm.Doc.Scene.References);
    }

    [AvaloniaFact]
    public async Task ASourceWithUnsavedChangesSaysItIsUsingTheLastSave()
    {
        // The edits in the run's tab are not on disk, and the reference reads
        // the disk. Silence here reads as a bug: say so, once, where it happens.
        var (vm, _, runRef, runPath) = ProjectWithRun();
        var jump = vm.ActiveTab!;
        vm.NewDocument(new NewDocumentSettings("run", 320, 180, 24, 72, Scene.DefaultBackgroundColor, false));
        var runTab = vm.ActiveTab!;
        runTab.FilePath = runPath;
        runTab.Editor.Perform(doc => doc.Scene.FrameCount = 9);
        Assert.True(runTab.IsDirty, "the test needs the run's tab to have unsaved edits");
        vm.ActiveTab = jump;

        var said = await vm.ReferenceProjectDocumentAsync(runRef);

        Assert.Contains("unsaved", said);
        Assert.NotNull(vm.Doc.Scene.References);
        Assert.Contains("unsaved", vm.ReferenceLinkLine);
    }

    // ---- the link line -----------------------------------------------------------

    [AvaloniaFact]
    public async Task TheLinkLineSaysWhatTheReferenceFollowsAndWhichSave()
    {
        var (vm, _, runRef, runPath) = ProjectWithRun();
        await vm.ReferenceProjectDocumentAsync(runRef);

        var line = vm.ReferenceLinkLine;

        Assert.NotNull(line);
        Assert.Contains("run", line);
        var saved = File.GetLastWriteTimeUtc(runPath).ToLocalTime();
        Assert.Contains(saved.ToString("HH:mm"), line);
        Assert.True(vm.ActiveReferenceIsDocument);
    }

    [AvaloniaFact]
    public async Task AMissingSourceSaysTheFramesAreTheLastOnes()
    {
        var (vm, _, runRef, runPath) = ProjectWithRun();
        await vm.ReferenceProjectDocumentAsync(runRef);

        File.Delete(runPath);
        await vm.RefreshDocumentReferencesAsync();

        Assert.Contains("not found", vm.ReferenceLinkLine);
    }

    [AvaloniaFact]
    public void AnImageReferenceHasNoLinkLine()
    {
        var vm = VmLayers.PaperVm();
        vm.Doc.Scene.References = [new ReferenceStrip { Name = "sheet", Cells = [new ReferenceCell { Width = 4, Height = 4 }] }];
        vm.ActiveReferenceIndex = 0;

        Assert.Null(vm.ReferenceLinkLine);
        Assert.False(vm.ActiveReferenceIsDocument);
        Assert.False(vm.RefreshReferenceSourceCommand.CanExecute(null));
        Assert.False(vm.OpenReferenceSourceCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task TheLinkLineSaysFlatteningWhileItIs()
    {
        var (vm, _, runRef, runPath) = ProjectWithRun();
        await vm.ReferenceProjectDocumentAsync(runRef);
        File.SetLastWriteTimeUtc(runPath, DateTime.UtcNow.AddMinutes(1));

        var refresh = vm.RefreshDocumentReferencesAsync();
        Assert.Contains("flattening", vm.ReferenceLinkLine);
        await refresh;

        Assert.DoesNotContain("flattening", vm.ReferenceLinkLine);
    }

    // ---- Refresh now, Open source ---------------------------------------------

    [AvaloniaFact]
    public async Task RefreshNowReflattensEvenWhenTheStampSaysNothingChanged()
    {
        // A copy restored from a backup, a clock that went backwards, a sync
        // client that kept the old time: the stamp is a good gate and not a
        // perfect one, and the artist needs a way past it.
        var (vm, _, runRef, runPath) = ProjectWithRun(frames: 2);
        await vm.ReferenceProjectDocumentAsync(runRef);
        var strip = vm.ActiveReference!;
        var stamp = File.GetLastWriteTimeUtc(runPath);
        DocJson.Save(Run(4), runPath);
        File.SetLastWriteTimeUtc(runPath, stamp);

        Assert.Equal(0, await vm.RefreshDocumentReferencesAsync());
        Assert.Equal(2, strip.Cells.Count);

        Assert.True(vm.RefreshReferenceSourceCommand.CanExecute(null));
        await vm.RefreshReferenceSourceAsync();

        Assert.Equal(4, strip.Cells.Count);
    }

    [AvaloniaFact]
    public async Task OpenSourceOpensTheReferencedDocument()
    {
        var (vm, _, runRef, runPath) = ProjectWithRun();
        await vm.ReferenceProjectDocumentAsync(runRef);
        var jump = vm.ActiveTab;

        Assert.True(vm.OpenReferenceSourceCommand.CanExecute(null));
        await vm.OpenReferenceSourceAsync();

        Assert.NotSame(jump, vm.ActiveTab);
        Assert.Equal(Path.GetFullPath(runPath), Path.GetFullPath(vm.ActiveTab!.FilePath!));
    }

    [AvaloniaFact]
    public async Task OpenSourceComesBackToATabAlreadyOpen_RatherThanASecondCopy()
    {
        var (vm, _, runRef, runPath) = ProjectWithRun();
        var jump = vm.ActiveTab!;
        await vm.OpenDocumentFileAsync(runPath);
        var runTab = vm.ActiveTab!;
        vm.ActiveTab = jump;
        await vm.ReferenceProjectDocumentAsync(runRef);
        var tabs = vm.Tabs.Count;

        await vm.OpenReferenceSourceAsync();

        Assert.Same(runTab, vm.ActiveTab);
        Assert.Equal(tabs, vm.Tabs.Count);
    }
}
