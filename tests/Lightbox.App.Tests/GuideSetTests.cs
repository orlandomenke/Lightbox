using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Guide sets can finally be made, shared and pulled — the authoring half the
/// scoped-resources design shipped without.
/// </summary>
/// <remarks>
/// <para>
/// <b>The chain existed and nothing fed it</b> (owner's report, 2026-08-13:
/// "guides we are not even able to create or assign"): <see cref="GuideSet"/>
/// was a record, <see cref="GuideScopes.VisibleTo"/> resolved declarations,
/// the project window's share menu listed <c>Manifest.GuideSets</c> — always
/// empty. These drive the three verbs that close the loop: save a document's
/// guides as a named set, see the sets a document's scope offers, and pull one
/// in as ordinary document guides.
/// </para>
/// <para>
/// <b>Copies at both ends, and the tests hold that rather than assume it.</b>
/// A set is a library entry: moving a guide in the document after saving must
/// not edit the library, and moving a pulled guide must not edit the set —
/// otherwise dragging a ruler in one drawing would silently redraw the rig in
/// every drawing that shares it, which is the defect scoping exists to avoid
/// (see <see cref="GuideSet"/>'s own remarks).
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class GuideSetTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-guidesets-{Guid.NewGuid():N}.lbproj");

    private readonly List<MainViewModel> _built = [];

    public new void Dispose()
    {
        foreach (var vm in _built) vm.ProjectDocker.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        _built.Add(vm);
        vm.NewProject(_root, "Production");
        return vm;
    }

    [AvaloniaFact]
    public void AGuideSetCanBeMadeAndNamed()
    {
        var vm = Vm();
        var manifest = vm.ProjectDocker.Project!.Manifest;
        Assert.Null(manifest.GuideSets); // absent until one is made

        vm.AddGuide(GuideKind.Line, 100, 300);
        var eyeline = vm.AddGuide(GuideKind.Line, 100, 120);
        var set = vm.SaveGuidesAsSet("Character height");

        Assert.NotNull(set);
        var kept = Assert.Single(manifest.GuideSets!);
        Assert.Equal("Character height", kept.Name);
        Assert.Equal(2, kept.Guides.Count);
        output.WriteLine($"saved “{kept.Name}” with {kept.Guides.Count} guides");

        // A copy, not a reference: dragging the document's guide afterwards
        // must not silently redraw the library's.
        vm.MoveGuide(eyeline, 0, 40);
        Assert.Equal(120, kept.Guides[1].Y, 3);

        // Saving over it refreshes the guides and keeps the identity the
        // declarations point at.
        vm.AddGuide(GuideKind.Line, 100, 500);
        var again = vm.SaveGuidesAsSet("Character height", overwriteId: kept.Id);
        Assert.Equal(kept.Id, again!.Id);
        Assert.Equal(3, Assert.Single(manifest.GuideSets!).Guides.Count);
    }

    [AvaloniaFact]
    public void ADocumentPullsASharedGuideSetIntoItsOwnGuides()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 100, 300, angle: 0);
        vm.AddGuide(GuideKind.VanishingPoint, 800, 200);
        var set = vm.SaveGuidesAsSet("Street rig")!;

        // A clean canvas, as the next document would be.
        vm.ClearGuidesCommand.Execute(null);
        Assert.False(vm.HasGuides);

        vm.PullGuideSetCommand.Execute(set);

        Assert.True(vm.HasGuides);
        var pulled = vm.Doc.Scene.Guides!;
        Assert.Equal(2, pulled.Count);
        Assert.Equal(GuideKind.VanishingPoint, pulled[1].Kind);
        // Fresh ids: pulling twice is two independent batches, and removing
        // one by id must not take its twin with it.
        Assert.DoesNotContain(pulled, g => set.Guides.Any(s => s.Id == g.Id));

        // Moving the pulled copy leaves the library alone.
        vm.MoveGuide(pulled[0], 0, 60);
        Assert.Equal(300, set.Guides[0].Y, 3);

        // One undoable step, like any other edit — the first undo takes back
        // the move above, the second the pull itself.
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        output.WriteLine($"after undo: HasGuides={vm.HasGuides}");
        Assert.False(vm.HasGuides);
        Assert.Null(vm.Doc.Scene.Guides); // absent, not empty
    }

    [AvaloniaFact]
    public void ADeclaredGuideSetReachesTheDocumentsUnderItsFolder()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        vm.AddGuide(GuideKind.Line, 100, 140);
        var set = vm.SaveGuidesAsSet("Knight height")!;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        var folder = docker.Selected!.Folder!;

        var under = ProjectIo.AddDocument(
            docker.Project!, "Walk", DocumentFactory.CreateDoc(64, 64), folder);
        var elsewhere = ProjectIo.AddDocument(
            docker.Project!, "Goblin idle", DocumentFactory.CreateDoc(64, 64));

        // Before any declaration the project is unscoped: everything offered
        // everywhere — Q30's new-projects-only migration.
        Assert.Contains(vm.GuideSetsVisibleTo(under), s => s.Id == set.Id);
        Assert.Contains(vm.GuideSetsVisibleTo(elsewhere), s => s.Id == set.Id);

        // Shared onto the Knight folder, the project scopes: the walk still
        // sees it, the goblin no longer does.
        docker.ShareGuideSetEntryCommand.Execute(set);
        Assert.True(GuideScopes.AnyDeclared(docker.Project!.Manifest));
        Assert.Contains(vm.GuideSetsVisibleTo(under), s => s.Id == set.Id);
        Assert.DoesNotContain(vm.GuideSetsVisibleTo(elsewhere), s => s.Id == set.Id);
        output.WriteLine("declared on Knight: the walk sees it, the goblin does not");
    }

    /// <summary>
    /// Deleting a set takes its declarations with it — a declaration pointing
    /// at nothing would keep the project scoped while offering air.
    /// </summary>
    [AvaloniaFact]
    public void DeletingASetTakesItsDeclarationsWithIt()
    {
        var vm = Vm();
        var docker = vm.ProjectDocker;
        vm.AddGuide(GuideKind.Line, 100, 140);
        var set = vm.SaveGuidesAsSet("Knight height")!;

        docker.AddItemNamed(ProjectViewModel.NewFolderItem, "Knight");
        docker.Selected = Assert.Single(docker.Rows, r => r.Name == "Knight");
        docker.ShareGuideSetEntryCommand.Execute(set);
        Assert.True(GuideScopes.AnyDeclared(docker.Project!.Manifest));

        vm.DeleteGuideSet(set);

        Assert.Null(docker.Project!.Manifest.GuideSets); // absent, not empty
        Assert.False(GuideScopes.AnyDeclared(docker.Project!.Manifest));
    }

    /// <summary>B163's lesson: a window that cannot construct ships broken.</summary>
    [AvaloniaFact]
    public void TheGuideSetEditorOpens()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Line, 100, 140);
        vm.SaveGuidesAsSet("Height");

        var editor = new GuideSetEditor(vm);
        editor.Show();
        editor.Close();
    }

    /// <summary>
    /// Saving a set records the paper it was drawn on, and pulling it onto
    /// other paper puts it back where it belongs — Q181, the owner's report
    /// that a 4K set arrived on a 1080p document four times too tall.
    /// </summary>
    [AvaloniaFact]
    public void AGuideSetRemembersTheCanvasItWasAuthoredOn()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Knight 4K", 3840, 2160, 12, 72, "#ffffff", false));

        // A six-head knight standing on the floor, filling 70% of the frame.
        var unit = 2160 * 0.7 / 6;
        vm.AddGuide(GuideKind.HeightScale, 1920, 1944, spacing: unit, divisions: 6);
        var set = vm.SaveGuidesAsSet("Knight");

        Assert.NotNull(set!.Canvas);
        Assert.Equal(3840, set.Canvas!.Width);
        Assert.Equal(2160, set.Canvas.Height);
        output.WriteLine($"saved on {set.Canvas.Width}×{set.Canvas.Height}, {unit:0.##} px/head");

        // Same project, half the paper, same ratio — the case the owner named.
        vm.NewDocument(new NewDocumentSettings("Knight HD", 1920, 1080, 12, 72, "#ffffff", false));
        Assert.Empty(vm.Guides);

        vm.PullGuideSetCommand.Execute(set);

        var landed = Assert.Single(vm.Guides);
        Assert.Equal(GuideKind.HeightScale, landed.Kind);
        Assert.Equal(6, landed.Divisions);                 // still six heads
        Assert.Equal(unit / 2, landed.Spacing, 6);         // half the paper, half the head
        Assert.Equal(960, landed.X, 6);
        Assert.Equal(972, landed.Y, 6);
        // Verbatim, it would have stood at y=1944 on 1080 px of paper with its
        // top 570 px above the sky. Fitted, the whole chart is on the page.
        Assert.InRange(landed.Y, 0, 1080);
        Assert.InRange(landed.Y - landed.Spacing * 6, 0, 1080);
        output.WriteLine($"landed at ({landed.X:0.#}, {landed.Y:0.#}), {landed.Spacing:0.##} px/head");
    }

    /// <summary>
    /// The pull is still a copy, and fitting must not have made the library
    /// drift: pulling the same set onto three sizes leaves it as authored.
    /// </summary>
    [AvaloniaFact]
    public void FittingASetOntoThreeDocumentsNeverEditsTheLibrary()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Author", 2000, 1000, 12, 72, "#ffffff", false));
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 100);
        var set = vm.SaveGuidesAsSet("Grid")!;

        foreach (var (w, h) in new[] { (1000, 500), (4000, 2000), (800, 600) })
        {
            vm.NewDocument(new NewDocumentSettings($"{w}", w, h, 12, 72, "#ffffff", false));
            vm.PullGuideSetCommand.Execute(set);
            var landed = Assert.Single(vm.Guides);
            Assert.Equal(100 * (h / 1000.0), landed.Spacing, 6);
        }

        Assert.Equal(100, set.Guides[0].Spacing, 9);
        Assert.Equal(2000, set.Canvas!.Width);
    }
}
