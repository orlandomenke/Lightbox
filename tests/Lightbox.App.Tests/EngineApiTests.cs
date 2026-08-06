using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

using Lightbox.Core.Projects;
namespace Lightbox.App.Tests;

/// <summary>
/// The engine API record, checked against the importers it describes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The point of this file is that the record cannot drift from the code.</b> A note in
/// a document saying "the Unity importer uses <c>ISpriteEditorDataProvider</c>" is true
/// until somebody changes a call, and then it is a lie nobody notices — which is precisely
/// the failure mode that shipped an importer using an API removed two years earlier.
/// </para>
/// <para>
/// So every symbol in <see cref="EngineApiNotes"/> is asserted to appear in the importer
/// it belongs to. That catches both directions: a note claiming an API the code does not
/// use, and code switching API without the note following.
/// </para>
/// </remarks>
public class EngineApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-api-" + Guid.NewGuid().ToString("N"));

    public EngineApiTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// The shipped importer for an engine, or null where there is no script.
    /// </summary>
    private static string? SourceFor(EngineTarget engine) => engine switch
    {
        EngineTarget.Unity => UnityExporter.ImporterSource,
        EngineTarget.Godot => GodotExporter.ImporterSource,
        EngineTarget.Unreal => UnrealExporter.ImporterSource,
        // GameMaker has no script by design — its "API" is a filename rule, checked
        // behaviourally below instead of as text.
        _ => null,
    };

    // ---- the record and the code cannot disagree ------------------------------------

    [Theory]
    [InlineData(EngineTarget.Unity)]
    [InlineData(EngineTarget.Godot)]
    [InlineData(EngineTarget.Unreal)]
    public void EverySymbolTheRecordNamesIsActuallyCalledByThatImporter(EngineTarget engine)
    {
        var note = EngineApiNotes.For(engine);
        var source = SourceFor(engine);
        Assert.NotNull(source);

        foreach (var symbol in note.ApiSurface)
        {
            Assert.True(
                source!.Contains(symbol, StringComparison.Ordinal),
                $"{engine}'s record names {symbol}, but the shipped importer never calls it — "
                + "either the note is wrong or the importer changed without it.");
        }
    }

    [Fact]
    public void TheGameMakerRecordIsCheckedAgainstBehaviourBecauseThereIsNoScript()
    {
        // Its whole API surface is the filename rule, so the honest check is that an
        // export actually produces a name carrying it.
        var note = EngineApiNotes.For(EngineTarget.GameMaker);
        Assert.Equal(ImportMechanism.FilenameConvention, note.Mechanism);
        Assert.Null(SourceFor(EngineTarget.GameMaker));

        var doc = DocumentFactory.CreateDoc(64, 64, 12, null);
        doc.Scene.FrameCount = 3;
        var result = GameMakerExporter.Export(doc, Path.Combine(_dir, "gm.png"));

        var name = Path.GetFileName(Assert.Single(result.StripPaths));
        Assert.All(note.ApiSurface, s => Assert.Contains(s, name));
        Assert.Equal("gm_strip3.png", name);
    }

    // ---- no engine ships without a record ------------------------------------------

    [Fact]
    public void EveryEngineTargetHasARecord()
    {
        // The guard that makes this file worth having: a sixth engine cannot land without
        // its API surface being written down.
        var recorded = EngineApiNotes.All.Select(n => n.Engine).ToList();

        Assert.Equal(
            Enum.GetValues<EngineTarget>().OrderBy(e => e).ToList(),
            recorded.OrderBy(e => e).ToList());
        Assert.Equal(recorded.Count, recorded.Distinct().Count());
    }

    [Fact]
    public void EveryEngineExportTargetMapsToAnEngineWithARecord()
    {
        // The other side of the same guard, from the export window's enum rather than
        // from this one — the two lists are maintained separately and must not diverge.
        var engineTargets = new[]
        {
            ExportTarget.Unity, ExportTarget.Godot, ExportTarget.Unreal, ExportTarget.GameMaker,
        };

        foreach (var target in engineTargets)
        {
            Assert.True(
                Enum.TryParse<EngineTarget>(target.ToString(), out var engine),
                $"{target} has no matching EngineTarget, so it can have no API record.");
            Assert.Contains(engine, EngineApiNotes.All.Select(n => n.Engine));
        }

        // And nothing in the record is an engine we do not actually export for.
        foreach (var note in EngineApiNotes.All)
        {
            Assert.True(
                Enum.TryParse<ExportTarget>(note.Engine.ToString(), out _),
                $"{note.Engine} has a record but no export target.");
        }
    }

    [Fact]
    public void NoRecordIsEmptyOrVague()
    {
        // A note that says nothing passes a "there is a note" check and helps nobody.
        foreach (var note in EngineApiNotes.All)
        {
            Assert.NotEmpty(note.ApiSurface);
            Assert.All(note.ApiSurface, s => Assert.False(string.IsNullOrWhiteSpace(s)));
            Assert.True(note.MinimumVersion.Length > 0, $"{note.Engine} names no minimum version");
            Assert.True(
                note.OnAnOlderVersion.Length > 40,
                $"{note.Engine} does not say what happens on an older version");
            Assert.True(
                note.DeprecationRisk.Length > 40,
                $"{note.Engine} does not say what could stop working");
        }
    }

    // ---- the version each importer claims, said in the importer too ----------------

    [Fact]
    public void TheImporterItselfNamesTheVersionItNeeds()
    {
        // Because the person who hits the problem is reading the script, not this record.
        Assert.Contains("2021.2", UnityExporter.ImporterSource);
        Assert.Contains("Godot 4", GodotExporter.ImporterSource);
        Assert.Contains("experimental", UnrealExporter.ImporterSource);
    }

    [Fact]
    public void UnitysVersionBranchIsCompiledRatherThanCheckedAtRuntime()
    {
        // The fallback has to be unreachable on a new engine and vice versa: a runtime
        // check would compile the removed API and fail to build on 2022.2 and up.
        var source = UnityExporter.ImporterSource;

        Assert.Contains("#if UNITY_2021_2_OR_NEWER", source);
        Assert.Contains("#else", source);
        // And the old API is still present, in the branch that is right for old engines.
        Assert.Contains("importer.spritesheet", source);
    }

    // ---- the gap, recorded rather than implied --------------------------------------

    [Fact]
    public void NoImporterHasBeenRunAgainstTheRealEngineAndTheRecordSaysSo()
    {
        // This test is meant to be edited. When somebody does a real import and records
        // it, they flip the flag and this assertion moves — which is the friction that
        // keeps the claim honest, rather than a caveat in a document nobody opens.
        Assert.Equal(EngineApiNotes.All.Count, EngineApiNotes.Unverified.Count);
        Assert.All(EngineApiNotes.All, n => Assert.False(n.VerifiedByRealImport));
    }

    [Fact]
    public void TheUnverifiedQueryNarrowsRatherThanBeingAllOrNothing()
    {
        // So a partially verified state is expressible at all, and closing the gap for one
        // engine does not need the others re-checked. Tested over a supplied set, because
        // over the real one "unverified" and "all" are the same list today — an assertion
        // that cannot tell them apart would pass on a query that ignored the flag.
        var partly = EngineApiNotes.All
            .Select(n => n.Engine == EngineTarget.Godot ? n with { VerifiedByRealImport = true } : n)
            .ToList();

        var left = EngineApiNotes.UnverifiedIn(partly);

        Assert.Equal(EngineApiNotes.All.Count - 1, left.Count);
        Assert.DoesNotContain(EngineTarget.Godot, left.Select(n => n.Engine));
        Assert.Contains(EngineTarget.Unreal, left.Select(n => n.Engine));
    }
}
