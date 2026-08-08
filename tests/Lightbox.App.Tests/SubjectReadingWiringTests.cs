using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The taxonomy half, end to end through the view model: reading a character,
/// keeping the answer, and getting it into the next inbetween request.
/// </summary>
/// <remarks>
/// The reading is once per character, not once per frame — that is the entire
/// economic argument for storing it, and these are the tests that hold the
/// storage in place. See <c>docs/DESIGN-subject-reading.md</c>.
/// </remarks>
[Collection("BrushState")]
public class SubjectReadingWiringTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A temp project root that cleans up after itself.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), $"lbsubj-{Guid.NewGuid():N}");

        public string Root => Path.Combine(_parent, "Production.lbproj");

        public void Dispose()
        {
            if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }
    }

    private static SubjectTaxonomy Knight() => new()
    {
        Kind = "biped",
        Parts =
        [
            new SubjectPart { Name = "torso", Depth = 0 },
            new SubjectPart { Name = "near-arm", Parent = "torso", Depth = 2 },
        ],
    };

    private static FakeArtist Reading() => new()
    {
        SubjectResult = AiResult<SubjectTaxonomy>.Success(Knight()),
    };

    /// <summary>A project with one folder that has a sheet the AI can see.</summary>
    /// <remarks>
    /// <b>B114.</b> An ordinary folder, with no reading on it — reading it is
    /// what makes it a character, so requiring one to exist first was the old
    /// model's assumption rather than the feature's.
    /// </remarks>
    private static (MainViewModel Vm, ProjectFolder Character) WithCharacter(
        FakeArtist artist, Scratch scratch)
    {
        // A project forms around the document that is already open, so there has
        // to be one — the application no longer supplies it at startup.
        var vm = VmLayers.PaperVm(artist);
        vm.NewProject(scratch.Root, "Production");
        var project = vm.ProjectDocker.Project!;
        var character = ProjectFolders.Add(project.Manifest, "Knight");
        vm.ProjectDocker.Adopt(project);
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Folder == character);

        // A reference sheet with a visible layer — what CollectReferenceImages
        // sends, and the only thing a reading has to look at.
        var sheet = Assert.IsType<ReferenceSheet>(vm.AddReferenceSheet("Knight sheet"));
        vm.AddReferenceView(sheet);   // a view with a visible layer is what gets sent
        return (vm, character);
    }

    [AvaloniaFact]
    public async Task ReadingACharacterKeepsTheAnswerOnIt()
    {
        using var scratch = new Scratch();
        var artist = Reading();
        var (vm, character) = WithCharacter(artist, scratch);

        await vm.AiReadSubjectCommand.ExecuteAsync(null);

        Assert.Equal("Knight", artist.LastSubjectRequest!.CharacterName);
        Assert.NotEmpty(artist.LastSubjectRequest.ReferenceImages);
        Assert.Equal("biped", character.Taxonomy!.Kind);
        Assert.Equal(2, character.Taxonomy.Parts.Count);
        output.WriteLine(vm.AiStatus);
        Assert.Contains("2 parts", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task AReadingSomebodyEditedIsNotOverwrittenByAReRead()
    {
        // The rig rule pointed at the reading: a guess is a default, never an
        // override of something a person stated. A re-read that silently
        // discarded an artist's corrections would teach them not to make any.
        using var scratch = new Scratch();
        var artist = Reading();
        var (vm, character) = WithCharacter(artist, scratch);

        await vm.AiReadSubjectCommand.ExecuteAsync(null);
        character.Taxonomy!.Reviewed = true;
        character.Taxonomy.Parts[0].Name = "chest";       // the artist's correction
        artist.SubjectResult = AiResult<SubjectTaxonomy>.Success(Knight());

        await vm.AiReadSubjectCommand.ExecuteAsync(null);

        Assert.Equal("chest", character.Taxonomy.Parts[0].Name);
        Assert.Contains("Clear it first", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task WithoutAProjectItSaysSoRatherThanFailingQuietly()
    {
        var artist = Reading();
        var vm = new MainViewModel(artist);

        await vm.AiReadSubjectCommand.ExecuteAsync(null);

        Assert.Null(artist.LastSubjectRequest);
        Assert.Contains("project", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task WithNoSheetToReadItSaysWhatIsMissing()
    {
        using var scratch = new Scratch();
        var artist = Reading();
        var vm = new MainViewModel(artist);
        vm.NewProject(scratch.Root, "Production");
        var project = vm.ProjectDocker.Project!;
        var character = ProjectFolders.Add(project.Manifest, "Knight");
        vm.ProjectDocker.Adopt(project);
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Folder == character);

        await vm.AiReadSubjectCommand.ExecuteAsync(null);

        Assert.Null(artist.LastSubjectRequest);   // no call, no bill
        Assert.Contains("character sheet", vm.AiStatus);
    }

    // ---- and into the inbetween ---------------------------------------------

    [AvaloniaFact]
    public async Task AnInbetweenOnACharactersAnimationCarriesItsTaxonomy()
    {
        using var scratch = new Scratch();
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success([]),
        };
        var vm = new MainViewModel(artist);
        vm.NewProject(scratch.Root, "Production");
        var project = vm.ProjectDocker.Project!;
        var character = ProjectFolders.Add(project.Manifest, "Knight");
        character.Taxonomy = Knight();

        var walk = ProjectIo.AddDocument(project, "walk", vm.Doc, character);
        vm.ProjectDocker.Adopt(project);
        // Open it the way the docker does, so the tab carries the DocumentRef
        // the taxonomy lookup resolves against.
        vm.ProjectDocker.Selected = vm.ProjectDocker.Rows.First(r => r.Animation?.Id == walk.Id);
        vm.ProjectDocker.OpenSelected();

        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 10, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(10, 60, 0.5);
        vm.MoveStroke(30, 60, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Equal("biped", artist.LastInbetweenRequest!.Taxonomy!.Kind);
    }

    [AvaloniaFact]
    public async Task ADocumentWithNoCharacterSendsNoTaxonomyAtAll()
    {
        // The absence half. A loose document must pay nothing for a feature it
        // is not using — not an empty block, not a null-shaped key, nothing.
        var artist = new FakeArtist
        {
            InbetweenResult = AiResult<List<InbetweenFrameResult>>.Success([]),
        };
        var vm = new MainViewModel(artist);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 10, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(10, 60, 0.5);
        vm.MoveStroke(30, 60, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.Null(artist.LastInbetweenRequest!.Taxonomy);
    }
}
