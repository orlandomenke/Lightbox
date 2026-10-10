using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A build knows when a file is newer than it writes, so it can say so (Q234).
/// </summary>
/// <remarks>
/// Q230 keeps what a newer build wrote on almost every record, but not inside
/// per-point values or the project manifest, and a save here would drop those.
/// The owner chose a warning on open. It can only work if the version is
/// written and checked before the next format change, which is this.
/// </remarks>
public class NewerVersionTests
{
    [Fact]
    public void ADocumentFromANewerBuildSaysSo()
    {
        var newer = DocJson.Deserialize($$"""{ "version": {{Doc.CurrentVersion + 1}}, "scene": { "layers": [] } }""");
        Assert.True(newer.IsFromANewerBuild);
        Assert.False(new Doc().IsFromANewerBuild);
        Assert.False(DocJson.Deserialize("""{ "version": 1, "scene": { "layers": [] } }""").IsFromANewerBuild);
    }

    /// <summary>The question is asked of the record, never written into it.</summary>
    [Fact]
    public void TheAnswerIsNotWrittenToTheFile()
    {
        var json = DocJson.Serialize(new Doc());
        Assert.DoesNotContain("newerBuild", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AProjectFromANewerBuildSaysSo()
    {
        Assert.True(new ProjectManifest { Version = ProjectManifest.CurrentVersion + 1 }.IsFromANewerBuild);
        Assert.False(new ProjectManifest().IsFromANewerBuild);
    }
}
