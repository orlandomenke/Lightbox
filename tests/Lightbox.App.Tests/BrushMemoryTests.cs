using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Q9, from the tool bar's side: a project hands back the brush its work is
/// painted with.
/// </summary>
/// <remarks>
/// <para>
/// The gripe this answers is not about preference, it is about memory. Come
/// back to a comic or a set of game assets after a fortnight and the tool bar
/// says whatever you last used on something else.
/// </para>
/// <para>
/// The <b>project</b> rather than the document, because the answer has to
/// reach the pages that do not exist yet: page one remembering its own brush
/// leaves page eleven starting from scratch, which is the same problem one
/// file later.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushMemoryTests : BrushStateIsolated
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-brushmem-{Guid.NewGuid():N}.lbproj");

    /// <summary>Also tears down the shared brush store; see the base class.</summary>
    public override void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private MainViewModel WithProject(ProjectType? type = ProjectType.Comic)
    {
        var vm = new MainViewModel(null);
        vm.ProjectDocker.Project = ProjectIo.Create("Knight", _root, type);
        return vm;
    }

    /// <summary>
    /// Paint a real stroke, through the pipeline a pen drives.
    /// </summary>
    /// <remarks>
    /// Not <c>AppendExternalStrokes</c>, which is how the AI and the MCP
    /// server put marks down. That path deliberately does NOT record the
    /// brush: a stroke the artist did not paint is not the brush they were
    /// painting with, and letting an agent rewrite the memory would undo the
    /// point of having one.
    /// </remarks>
    private static void Paint(MainViewModel vm)
    {
        vm.Doc.Scene.Layers[0].Locked = false;
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(90, 60, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void WithNoProjectOpenTheBrushBelongsToTheTool()
    {
        // Somebody who opened the app to draw one picture gets exactly what
        // the application always did — and there is nowhere to keep a project
        // brush, so even asking for one cannot change that.
        var vm = new MainViewModel(null);

        Assert.Equal(BrushScope.Global, vm.BrushScope);
        vm.BrushMemoryChoice = "Per project";
        Assert.Equal(BrushScope.Global, vm.BrushScope);
    }

    [AvaloniaFact]
    public void AComicKeepsTheBrushWithTheProjectWithoutBeingAsked()
    {
        // The default doing its job: the artist never opens Configure.
        var vm = WithProject(ProjectType.Comic);

        Assert.Equal(BrushScope.PerProject, vm.BrushScope);
    }

    [AvaloniaFact]
    public void AStoryboardKeepsOneBrushForTheTool()
    {
        var vm = WithProject(ProjectType.Storyboard);

        Assert.Equal(BrushScope.Global, vm.BrushScope);
    }

    [AvaloniaFact]
    public void AChosenScopeOverridesTheProjectType()
    {
        var vm = WithProject(ProjectType.Storyboard);

        vm.BrushMemoryChoice = "Per project";

        Assert.Equal(BrushScope.PerProject, vm.BrushScope);
    }

    [AvaloniaFact]
    public void UnderGlobalTheProjectRecordsNothing()
    {
        // The other half of the choice. A project made this way must be
        // indistinguishable from one made before the feature existed.
        var vm = WithProject();
        vm.BrushMemoryChoice = "Global";

        Paint(vm);

        Assert.Null(vm.ProjectDocker.Project!.Manifest.Brush);
    }

    [AvaloniaFact]
    public void PaintingRecordsTheBrushOnTheProject()
    {
        // On commit rather than on save: the session this exists for is the
        // one that ended without a save, because somebody closed the laptop.
        var vm = WithProject();
        vm.BrushSize = 41;

        Paint(vm);

        Assert.NotNull(vm.ProjectDocker.Project!.Manifest.Brush);
        Assert.Equal(41, vm.ProjectDocker.Project.Manifest.Brush!.Size);
    }

    [AvaloniaFact]
    public void AnAgentsStrokeDoesNotRewriteIt()
    {
        // AppendExternalStrokes is the AI and MCP path. A stroke the artist
        // did not paint is not the brush they were painting with.
        var vm = WithProject();
        vm.Doc.Scene.Layers[0].Locked = false;

        vm.AppendExternalStrokes(vm.Doc.Scene.Layers[0].Id, 0, [new Stroke
        {
            Tool = ToolKind.Brush,
            Points = [new StrokePoint(5, 5, 1), new StrokePoint(50, 50, 1)],
            Brush = new BrushSettings { Size = 99 },
        }]);

        Assert.Null(vm.ProjectDocker.Project!.Manifest.Brush);
    }

    [AvaloniaFact]
    public void ANewDocumentInTheProjectIsFedThatBrush()
    {
        // The reason this is per project rather than per document: the brush
        // has to reach the page that did not exist when it was chosen.
        var vm = WithProject();
        vm.BrushSize = 41;
        Paint(vm);

        vm.NewDocument(new NewDocumentSettings("Page 2", 120, 80, 12, 72, "#ffffff", false));
        vm.BrushSize = 7;
        // Coming back is what a tab switch does; a freshly opened project
        // document goes through the same recall.
        vm.ActiveTab = vm.Tabs[0];

        Assert.Equal(41, vm.BrushSize);
    }

    [AvaloniaFact]
    public void AProjectWithNothingRecordedLeavesTheBrushAlone()
    {
        // Silence is the right answer for an older project, or one worked on
        // under Global. Resetting somebody's brush to a default every time
        // they open something would be worse than the problem this solves.
        var vm = WithProject();
        vm.BrushSize = 23;

        vm.ActiveTab = vm.Tabs[0];

        Assert.Equal(23, vm.BrushSize);
    }

    [AvaloniaFact]
    public void SwitchingToPerProjectMidSessionHandsBackWhatIsAlreadyThere()
    {
        // Turning the setting on should not require a tab change to take
        // effect — the artist just told the application what they wanted.
        var vm = WithProject();
        vm.BrushSize = 55;
        Paint(vm);
        vm.BrushMemoryChoice = "Global";
        vm.BrushSize = 9;

        vm.BrushMemoryChoice = "Per project";

        Assert.Equal(55, vm.BrushSize);
    }
}
