using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The rig library: save a drawing's skeleton as a named set, see the sets a
/// drawing's scope offers, and put one on at the right size — Q181.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built with its verbs rather than a release after them.</b> Guide sets
/// shipped as a record, a resolver and a share menu that nothing could feed,
/// and were reachable only from tests until somebody noticed. These drive the
/// same three verbs through the view model, and the menu and editor that reach
/// them exist in the same commit.
/// </para>
/// <para>
/// The arithmetic lives in <c>ArmatureFitTests</c>, which does not need a
/// window. What is here is the wiring: that a save measures the head count
/// against this drawing's chart, that a pull becomes the document's armature,
/// and that a pull onto a skeleton something is already bound to is refused.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class RigSetTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-rigsets-{Guid.NewGuid():N}.lbproj");

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

    /// <summary>A figure standing on the ground, that many pixels of it.</summary>
    private static Armature Figure(double x, double groundY, double tall) => new()
    {
        Bones =
        [
            new Bone { Id = "spine", Name = "Spine", X = x, Y = groundY, RotationDeg = -90, Length = tall },
        ],
    };

    [AvaloniaFact]
    public void ARigSetCanBeMadeAndNamed()
    {
        var vm = Vm();
        var manifest = vm.ProjectDocker.Project!.Manifest;
        Assert.Null(manifest.RigSets);   // absent until one is made

        vm.NewDocument(new NewDocumentSettings("Goblin", 1000, 1000, 12, 72, "#ffffff", false));
        vm.Doc.Armature = Figure(x: 500, groundY: 900, tall: 300);
        var set = vm.SaveArmatureAsSet("Goblin");

        Assert.NotNull(set);
        var kept = Assert.Single(manifest.RigSets!);
        Assert.Equal("Goblin", kept.Name);
        Assert.Single(kept.Armature.Bones);

        // A copy, not a reference: re-proportioning the drawing's bone after
        // the save must not silently redraw every character built from it.
        vm.Doc.Armature!.Bones[0].Length = 999;
        Assert.Equal(300, kept.Armature.Bones[0].Length, 6);
    }

    [AvaloniaFact]
    public void ARigRemembersHowManyHeadsTallItWas()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Goblin", 1000, 1000, 12, 72, "#ffffff", false));
        // A hundred-pixel head, and a goblin four and a half of them tall.
        vm.AddGuide(GuideKind.HeightScale, 500, 900, spacing: 100, divisions: 8);
        vm.Doc.Armature = Figure(x: 500, groundY: 900, tall: 450);

        var set = vm.SaveArmatureAsSet("Goblin")!;

        Assert.Equal(4.5, set.Heads!.Value, 6);
        Assert.Equal(1000, set.Canvas!.Height);
        output.WriteLine($"saved “{set.Name}” at {set.Heads:0.##} heads");
    }

    [AvaloniaFact]
    public void ARigSavedWithNoHeightScaleHasNoHeadCountAndSaysSo()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Dog", 1000, 1000, 12, 72, "#ffffff", false));
        vm.Doc.Armature = Figure(x: 500, groundY: 900, tall: 300);

        var set = vm.SaveArmatureAsSet("Dog")!;

        // Null rather than a number derived from the canvas: a guess dressed
        // as a proportion is worse than an honest absence.
        Assert.Null(set.Heads);
        Assert.Contains("no character height scale", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.DocumentHasHeightScale);
    }

    [AvaloniaFact]
    public void PullingARigAgainstAHeightScaleStandsItOnTheAnchorAtItsHeadCount()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Goblin 4K", 3840, 2160, 12, 72, "#ffffff", false));
        vm.AddGuide(GuideKind.HeightScale, 1920, 2000, spacing: 100, divisions: 8);
        vm.Doc.Armature = Figure(x: 1920, groundY: 2000, tall: 450);
        var set = vm.SaveArmatureAsSet("Goblin")!;

        // Another drawing, half the size, a fifty-pixel head at (400, 900).
        vm.NewDocument(new NewDocumentSettings("walk", 1920, 1080, 12, 72, "#ffffff", false));
        vm.AddGuide(GuideKind.HeightScale, 400, 900, spacing: 50, divisions: 8);
        Assert.False(vm.HasArmature);

        vm.PullRigSetCommand.Execute(set);

        Assert.True(vm.HasArmature);
        var box = ArmatureFit.BindBounds(vm.Doc.Armature!)!.Value;
        Assert.Equal(4.5 * 50, box.MaxY - box.MinY, 6);
        Assert.Equal(900, box.MaxY, 6);
        Assert.Equal(400, (box.MinX + box.MaxX) / 2, 6);
        output.WriteLine($"landed {box.MaxY - box.MinY:0.##} px tall on a 50 px head");

        // One undoable step, and the drawing had no skeleton before it.
        vm.UndoCommand.Execute(null);
        Assert.False(vm.HasArmature);
    }

    [AvaloniaFact]
    public void TheMenuSaysHowManyHeadsEachSetIs()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Chart", 1000, 1000, 12, 72, "#ffffff", false));
        vm.AddGuide(GuideKind.HeightScale, 500, 900, spacing: 100, divisions: 8);

        vm.Doc.Armature = Figure(500, 900, 750);
        vm.SaveArmatureAsSet("Human");
        vm.Doc.Armature = Figure(500, 900, 450);
        vm.SaveArmatureAsSet("Goblin");
        vm.NotifyRigSetOffers();

        var labels = vm.PullRigSetMenu.Select(e => e.Label).ToList();
        output.WriteLine(string.Join(" | ", labels));
        Assert.Contains("Human — 7.5 heads", labels);
        Assert.Contains("Goblin — 4.5 heads", labels);
    }

    /// <summary>
    /// <c>docs/DESIGN-bones.md</c>'s "one trap": the bind pose is the space
    /// dab dynamics seed from, so swapping the skeleton under bound art
    /// re-rolls every dab and the character boils.
    /// </summary>
    [AvaloniaFact]
    public void PullingOntoASkeletonSomethingIsBoundToIsRefused()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Rigged", 1000, 1000, 12, 72, "#ffffff", false));
        vm.AddGuide(GuideKind.HeightScale, 500, 900, spacing: 100, divisions: 8);
        vm.Doc.Armature = Figure(500, 900, 450);
        var set = vm.SaveArmatureAsSet("Goblin")!;

        // A layer that follows the skeleton is enough to make the bind pose
        // load-bearing.
        vm.Doc.Scene.Layers[^1].BoneId = "spine";
        Assert.True(vm.ArmatureIsBound);
        Assert.False(vm.CanPullRigSet);

        var lengthBefore = vm.Doc.Armature!.Bones[0].Length;
        vm.PullRigSetCommand.Execute(set);

        Assert.Equal(lengthBefore, vm.Doc.Armature!.Bones[0].Length, 9);
        Assert.Contains("boil", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
        output.WriteLine(vm.AiStatus);
    }

    [AvaloniaFact]
    public void PullingOntoAPosedSkeletonIsRefused()
    {
        var vm = Vm();
        vm.NewDocument(new NewDocumentSettings("Posed", 1000, 1000, 12, 72, "#ffffff", false));
        vm.Doc.Armature = Figure(500, 900, 450);
        var set = vm.SaveArmatureAsSet("Goblin")!;

        vm.Doc.Scene.PoseTrack = new PoseTrack { Keys = [new PoseKey { Frame = 0 }] };

        Assert.False(vm.CanPullRigSet);
        vm.PullRigSetCommand.Execute(set);
        Assert.Contains("posed", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ADeclaredRigSetReachesTheDocumentsUnderItsFolder()
    {
        var vm = Vm();
        var manifest = vm.ProjectDocker.Project!.Manifest;
        var knight = ProjectFolders.Add(manifest, "Knight");

        vm.NewDocument(new NewDocumentSettings("Rigs", 1000, 1000, 12, 72, "#ffffff", false));
        vm.Doc.Armature = Figure(500, 900, 450);
        var goblin = vm.SaveArmatureAsSet("Goblin")!;
        vm.Doc.Armature = Figure(500, 900, 750);
        var human = vm.SaveArmatureAsSet("Human")!;

        // Unscoped: everything is offered to everyone (Q30's migration).
        Assert.Equal(2, vm.RigSetsVisibleTo(vm.ActiveTab!.Source).Count);

        // Declared on the knight: a document elsewhere is offered neither.
        ResourceScopes.Declare(manifest, knight, RigScopes.Kind, human.Id);
        Assert.Empty(vm.RigSetsVisibleTo(vm.ActiveTab!.Source));
        var under = new DocumentRef { Name = "walk", FolderId = knight.Id };
        Assert.Equal(human.Id, Assert.Single(vm.RigSetsVisibleTo(under)).Id);
        Assert.DoesNotContain(vm.RigSetsVisibleTo(under), s => s.Id == goblin.Id);
    }

    [AvaloniaFact]
    public void DeletingASetTakesBackItsShares()
    {
        var vm = Vm();
        var manifest = vm.ProjectDocker.Project!.Manifest;
        var knight = ProjectFolders.Add(manifest, "Knight");
        vm.NewDocument(new NewDocumentSettings("Rigs", 1000, 1000, 12, 72, "#ffffff", false));
        vm.Doc.Armature = Figure(500, 900, 450);
        var set = vm.SaveArmatureAsSet("Goblin")!;
        ResourceScopes.Declare(manifest, knight, RigScopes.Kind, set.Id);

        vm.DeleteRigSet(set);

        // Absent, not empty — and no declaration left pointing at nothing,
        // which would scope the kind and offer air.
        Assert.Null(manifest.RigSets);
        Assert.DoesNotContain(knight.Resources ?? [], r => r.Kind == RigScopes.Kind);
    }

    /// <summary>The editor opens and closes; the verbs it drives are above.</summary>
    [AvaloniaFact]
    public void TheRigSetEditorOpens()
    {
        var vm = Vm();
        var editor = new RigSetEditor(vm);
        editor.Show();
        editor.Close();
    }
}
