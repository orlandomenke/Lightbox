using Lightbox.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// Steps 3 and 4: what an export would produce, and what a status change asks
/// to be rebuilt.
/// </summary>
public class ExportPlanTests(ITestOutputHelper output)
{
    private readonly Dictionary<string, ExportPreset> _presets = [];

    private ExportPreset Preset(string name, ExportGrouping grouping,
        List<AssetStatus>? include = null, AssetStatus? rebuildOn = null)
    {
        var p = new ExportPreset
        {
            Name = name, Grouping = grouping, IncludeStatuses = include, RebuildOn = rebuildOn,
        };
        _presets[p.Id] = p;
        return p;
    }

    private ExportPreset? Find(string id) => _presets.GetValueOrDefault(id);

    private static (ProjectManifest M, ProjectFolder Characters, ProjectFolder Knight,
                    ProjectFolder Goblin) World()
    {
        var m = new ProjectManifest { Name = "Production" };
        var characters = ProjectFolders.Add(m, "characters");
        var knight = ProjectFolders.Add(m, "knight", characters);
        var goblin = ProjectFolders.Add(m, "goblin", characters);
        return (m, characters, knight, goblin);
    }

    private static DocumentRef Doc(ProjectManifest m, ProjectFolder f, string name, AssetStatus? s = null)
    {
        var d = new DocumentRef { Name = name, Path = $"d/{name}.json", FolderId = f.Id, Status = s };
        m.Documents.Add(d);
        return d;
    }

    /// <summary>A knight declared as one sheet is one file, however deep the tree.</summary>
    [Fact]
    public void OneArtifactPacksTheWholeSubtreeIntoOneFile()
    {
        var (m, _, knight, _) = World();
        var locomotion = ProjectFolders.Add(m, "locomotion", knight);
        Doc(m, knight, "idle");
        Doc(m, locomotion, "walk");
        Doc(m, locomotion, "run");
        ExportScopes.SetPreset(m, knight, Preset("Sheet", ExportGrouping.OneArtifact).Id);

        var plan = ExportPlan.For(m, knight, Find);
        var sheet = Assert.Single(plan);
        output.WriteLine(ExportPlan.Describe(plan));
        Assert.Equal(3, sheet.Documents.Count);
        Assert.Same(knight, sheet.Scope);
    }

    /// <summary>
    /// Shared settings, separate deliverables — the case that would otherwise
    /// force one declaration per character.
    /// </summary>
    [Fact]
    public void PerChildFolderSharesSettingsAndSplitsTheOutput()
    {
        var (m, characters, knight, goblin) = World();
        Doc(m, knight, "walk");
        Doc(m, knight, "run");
        Doc(m, goblin, "lurk");
        ExportScopes.SetPreset(m, characters, Preset("Sheet", ExportGrouping.PerChildFolder).Id);

        var plan = ExportPlan.For(m, characters, Find);
        output.WriteLine(ExportPlan.Describe(plan));
        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, a => a.Documents.Count == 2);
        Assert.Contains(plan, a => a.Documents.Count == 1);
    }

    /// <summary>A nearer declaration splits an artifact out of a larger one.</summary>
    [Fact]
    public void ANearerDeclarationMakesItsOwnArtifact()
    {
        var (m, _, knight, _) = World();
        var combat = ProjectFolders.Add(m, "combat", knight);
        Doc(m, knight, "idle");
        Doc(m, combat, "attack");
        ExportScopes.SetPreset(m, knight, Preset("Knight", ExportGrouping.OneArtifact).Id);
        Assert.Single(ExportPlan.For(m, knight, Find));

        ExportScopes.SetPreset(m, combat, Preset("Combat", ExportGrouping.OneArtifact).Id);
        var plan = ExportPlan.For(m, knight, Find);
        output.WriteLine($"after declaring on combat: {ExportPlan.Describe(plan)}");
        Assert.Equal(2, plan.Count);
    }

    /// <summary>Work in progress stays out, and is reported rather than dropped.</summary>
    /// <remarks>
    /// A scope where most things are still Draft is a legitimate state. An
    /// artist not told will read the small number as a bug, so the count says so.
    /// </remarks>
    [Fact]
    public void TheStatusFilterHoldsWorkBackAndSaysSo()
    {
        var (m, _, knight, _) = World();
        Doc(m, knight, "walk", AssetStatus.Ready);
        Doc(m, knight, "run", AssetStatus.Draft);
        Doc(m, knight, "jump");
        ExportScopes.SetPreset(m, knight,
            Preset("Shipped", ExportGrouping.OneArtifact, [AssetStatus.Ready]).Id);

        var sheet = Assert.Single(ExportPlan.For(m, knight, Find));
        output.WriteLine(ExportPlan.Describe([sheet]));
        Assert.Single(sheet.Documents);
        Assert.Equal(2, sheet.Excluded.Count);
        Assert.Contains("held back by status", ExportPlan.Describe([sheet]));
    }

    /// <summary>An artifact where nothing qualifies is named, not hidden.</summary>
    [Fact]
    public void AnEmptyArtifactIsReportedRatherThanSkipped()
    {
        var (m, _, knight, _) = World();
        Doc(m, knight, "walk", AssetStatus.Draft);
        ExportScopes.SetPreset(m, knight,
            Preset("Shipped", ExportGrouping.OneArtifact, [AssetStatus.Ready]).Id);

        var sheet = Assert.Single(ExportPlan.For(m, knight, Find));
        Assert.True(sheet.IsEmpty);
        Assert.Contains("empty", ExportPlan.Describe([sheet]));
    }

    /// <summary>A project that scopes nothing exports one file per document.</summary>
    [Fact]
    public void AnUnscopedProjectExportsPerDocument()
    {
        var (m, _, knight, _) = World();
        Doc(m, knight, "walk");
        Doc(m, knight, "run");

        var plan = ExportPlan.For(m, knight, Find);
        Assert.Equal(2, plan.Count);
        Assert.All(plan, a => Assert.Single(a.Documents));
    }

    /// <summary>Staleness catches edits, additions and removals — not just edits.</summary>
    /// <remarks>
    /// Reporting only edits is the version of this that looks right and quietly
    /// ships a sheet with a deleted animation still in it.
    /// </remarks>
    [Fact]
    public void StalenessCatchesEditsAdditionsAndRemovals()
    {
        var (m, _, knight, _) = World();
        var walk = Doc(m, knight, "walk");
        var run = Doc(m, knight, "run");
        ExportScopes.SetPreset(m, knight, Preset("Sheet", ExportGrouping.OneArtifact).Id);

        var built = Assert.Single(ExportPlan.For(m, knight, Find));
        var record = ExportStaleness.RecordOf(built, "knight.png");
        Assert.False(ExportStaleness.IsStale(record, built));

        walk.Version++;                                   // edited
        Assert.Contains(walk.Id, ExportStaleness.Drifted(record, Assert.Single(ExportPlan.For(m, knight, Find))));

        var jump = Doc(m, knight, "jump");                // added
        Assert.Contains(jump.Id, ExportStaleness.Drifted(record, Assert.Single(ExportPlan.For(m, knight, Find))));

        m.Documents.Remove(run);                          // removed
        var drifted = ExportStaleness.Drifted(record, Assert.Single(ExportPlan.For(m, knight, Find)));
        output.WriteLine($"{drifted.Count} documents drifted since the sheet was written");
        Assert.Contains(run.Id, drifted);
    }

    /// <summary>
    /// Reopening a shipped animation makes the sheet stale rather than emptying it.
    /// </summary>
    /// <remarks>
    /// The case the whole staleness design exists for. Removing work from a
    /// deliverable because somebody reopened it for polish is the kind of
    /// helpfulness that breaks a build; the sheet keeps what it had and says so.
    /// </remarks>
    [Fact]
    public void ReopeningAnAnimationMakesTheSheetStaleNotEmpty()
    {
        var (m, _, knight, _) = World();
        var walk = Doc(m, knight, "walk", AssetStatus.Ready);
        Doc(m, knight, "run", AssetStatus.Ready);
        ExportScopes.SetPreset(m, knight,
            Preset("Shipped", ExportGrouping.OneArtifact, [AssetStatus.Ready]).Id);

        var record = ExportStaleness.RecordOf(Assert.Single(ExportPlan.For(m, knight, Find)));
        walk.Status = AssetStatus.Reopened;

        var now = Assert.Single(ExportPlan.For(m, knight, Find));
        Assert.True(ExportStaleness.IsStale(record, now));
        Assert.Contains(walk.Id, ExportStaleness.Drifted(record, now));
        // The record still holds both, which is what "keeps what it had" means.
        Assert.Equal(2, record.SeenVersions.Count);
    }

    /// <summary>Reaching the trigger status rebuilds the whole artifact.</summary>
    /// <remarks>
    /// Per document in, per artifact out. The siblings have not changed and they
    /// are still in the sheet, so the sheet is rebuilt entire.
    /// </remarks>
    [Fact]
    public void ReachingReadyRebuildsTheArtifactHoldingIt()
    {
        var (m, _, knight, goblin) = World();
        var walk = Doc(m, knight, "walk");
        Doc(m, knight, "run");
        Doc(m, goblin, "lurk");
        ExportScopes.SetPreset(m, knight,
            Preset("Knight", ExportGrouping.OneArtifact, rebuildOn: AssetStatus.Ready).Id);

        // A status nobody asked for fires nothing.
        Assert.Empty(ExportTriggers.Fired(m, walk, AssetStatus.Draft, Find));

        var fired = ExportTriggers.Fired(m, walk, AssetStatus.Ready, Find);
        var sheet = Assert.Single(fired);
        output.WriteLine($"walk reaching Ready rebuilds {sheet.Documents.Count} documents");
        Assert.Equal(2, sheet.Documents.Count);   // the whole knight, not just walk
        Assert.DoesNotContain(sheet.Documents, d => d.Name == "lurk");
    }

    /// <summary>A trigger is opt-in; a preset that names none fires nothing.</summary>
    [Fact]
    public void APresetWithNoTriggerNeverFires()
    {
        var (m, _, knight, _) = World();
        var walk = Doc(m, knight, "walk");
        ExportScopes.SetPreset(m, knight, Preset("Sheet", ExportGrouping.OneArtifact).Id);
        Assert.Empty(ExportTriggers.Fired(m, walk, AssetStatus.Ready, Find));
    }
}
