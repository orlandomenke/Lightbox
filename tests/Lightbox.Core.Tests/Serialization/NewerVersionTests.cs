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

    /// <summary>
    /// A newer file that holds something this build cannot read at all is
    /// refused in plain words (Q265). Before this the version was only looked
    /// at after the whole document had parsed, so a file with a stroke kind
    /// this build has no name for failed with a conversion error instead.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewerDocumentThisBuildCannotReadSaysItIsNewer(bool gzipped)
    {
        var json = $$"""
            { "version": {{Doc.CurrentVersion + 1}}, "scene": { "layers": [ { "cels": [ { "frame": { "strokes": [ { "tool": "hologram" } ] } } ] } ] } }
            """;

        var e = Assert.ThrowsAny<System.Text.Json.JsonException>(() => Read(json, gzipped));

        Assert.Contains("newer Lightbox", e.Message);
        Assert.Contains($"format {Doc.CurrentVersion + 1}", e.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABrokenDocumentOfThisFormatIsNotBlamedOnItsVersion(bool gzipped)
    {
        var json = $$"""
            { "version": {{Doc.CurrentVersion}}, "scene": { "layers": [ { "cels": [ { "frame": { "strokes": [ { "tool": "hologram" } ] } } ] } ] } }
            """;

        var e = Assert.ThrowsAny<System.Text.Json.JsonException>(() => Read(json, gzipped));

        Assert.DoesNotContain("newer Lightbox", e.Message);
    }

    private static Doc Read(string json, bool gzipped)
    {
        if (!gzipped) return DocJson.Deserialize(json);
        using var file = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(System.Text.Encoding.UTF8.GetBytes(json));
        }
        file.Position = 0;
        return DocJson.Load(file);
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
