using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A document an older build saved over a newer one's says so (Q237), so the
/// newer build can warn that some of what it wrote may be missing.
/// </summary>
public class SavedByOlderBuildTests
{
    [Fact]
    public void ANewerDocumentSavedHereIsMarkedWithThisBuildsFormat()
    {
        var json = DocJson.Serialize(new Doc { Version = Doc.CurrentVersion + 1 });
        Assert.Contains($"\"savedByFormat\": {Doc.CurrentVersion}", json);
    }

    /// <summary>Absent unless used: an ordinary document writes nothing new.</summary>
    [Fact]
    public void ADocumentOfThisBuildsFormatWritesNoMarker()
    {
        Assert.DoesNotContain("savedByFormat", DocJson.Serialize(new Doc()));
    }

    [Fact]
    public void TheBuildThatWroteTheFormatSeesTheMarkerAndClearsIt()
    {
        // As the newer build would read it: its own format, saved by an older one.
        var read = DocJson.Deserialize($$"""{ "version": {{Doc.CurrentVersion}}, "savedByFormat": {{Doc.CurrentVersion - 1}}, "scene": { "layers": [] } }""");

        Assert.True(read.WasLastSavedByAnOlderBuild);
        // Its own next save says nothing more: the loss, if any, has happened.
        Assert.DoesNotContain("savedByFormat", DocJson.Serialize(read));
    }

    [Fact]
    public void AnOrdinaryDocumentWasNotSavedByAnOlderBuild()
    {
        Assert.False(new Doc().WasLastSavedByAnOlderBuild);
        Assert.False(DocJson.Deserialize("""{ "version": 1, "scene": { "layers": [] } }""").WasLastSavedByAnOlderBuild);
    }

    /// <summary>A clone keeps what was read, so an undo snapshot does not forget it.</summary>
    [Fact]
    public void ACloneRemembersWhatWasRead()
    {
        var read = DocJson.Deserialize($$"""{ "version": {{Doc.CurrentVersion}}, "savedByFormat": {{Doc.CurrentVersion - 1}}, "scene": { "layers": [] } }""");
        Assert.True(read.Clone().WasLastSavedByAnOlderBuild);
    }
}
