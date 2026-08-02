using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The recently-opened list. Everything that goes wrong with one of these is
/// list-shaped — the same file twice, the newest entry at the bottom, a list
/// that grows forever — so none of it needs a window.
/// </summary>
public class RecentItemsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static RecentItems With(params string[] paths)
    {
        var recent = new RecentItems();
        for (var i = 0; i < paths.Length; i++)
        {
            recent.Add(paths[i], "", RecentKind.Document, Noon.AddMinutes(i));
        }
        return recent;
    }

    private static List<string> Names(RecentItems recent) => recent.Items.ConvertAll(i => i.Name);

    [Fact]
    public void TheNewestIsFirst()
    {
        var recent = With("/a/one.lightbox.json", "/a/two.lightbox.json", "/a/three.lightbox.json");

        Assert.Equal(["three", "two", "one"], Names(recent));
    }

    [Fact]
    public void ReopeningMovesItToTheTopRatherThanAddingItAgain()
    {
        // The list is about where you were, not how often you went.
        var recent = With("/a/one.lightbox.json", "/a/two.lightbox.json");

        recent.Add("/a/one.lightbox.json", "", RecentKind.Document, Noon.AddHours(1));

        Assert.Equal(["one", "two"], Names(recent));
    }

    [Fact]
    public void ThePathIsWhatMakesTwoEntriesTheSame()
    {
        // Two characters can both have a "walk"; they are not the same file.
        var recent = new RecentItems();
        recent.Add("/knight/walk.lightbox.json", "walk", RecentKind.Document, Noon);
        recent.Add("/goblin/walk.lightbox.json", "walk", RecentKind.Document, Noon.AddMinutes(1));

        Assert.Equal(2, recent.Items.Count);
        Assert.NotEqual(recent.Items[0].Where, recent.Items[1].Where);
    }

    [Fact]
    public void ATrailingSeparatorIsNotADifferentProject()
    {
        var recent = new RecentItems();
        recent.Add("/art/Knight.lbproj", "Knight", RecentKind.Project, Noon);
        recent.Add("/art/Knight.lbproj" + Path.DirectorySeparatorChar, "Knight", RecentKind.Project, Noon.AddMinutes(1));

        Assert.Single(recent.Items);
    }

    [Fact]
    public void TheListStopsGrowing()
    {
        var recent = new RecentItems();
        for (var i = 0; i < RecentItems.Limit + 8; i++)
        {
            recent.Add($"/a/file{i}.lightbox.json", "", RecentKind.Document, Noon.AddMinutes(i));
        }

        Assert.Equal(RecentItems.Limit, recent.Items.Count);
        // And it is the oldest that fall off, not the newest.
        Assert.Equal($"file{RecentItems.Limit + 7}", recent.Items[0].Name);
        Assert.DoesNotContain(recent.Items, i => i.Name == "file0");
    }

    [Fact]
    public void AnEntryWithNoNameIsNamedAfterItsFile()
    {
        // Both extensions come off. Stripping only the last one leaves
        // "run cycle.lightbox", which is not what anybody called it.
        var recent = With("/a/run cycle.lightbox.json");
        Assert.Equal("run cycle", recent.Items[0].Name);

        var project = new RecentItems();
        project.Add("/art/Knight.lbproj", "", RecentKind.Project, Noon);
        Assert.Equal("Knight", project.Items[0].Name);
    }

    [Fact]
    public void NothingIsRecordedForAnEmptyPath()
    {
        var recent = new RecentItems();

        recent.Add("", "x", RecentKind.Document, Noon);
        recent.Add("   ", "x", RecentKind.Document, Noon);

        Assert.Empty(recent.Items);
    }

    [Fact]
    public void ForgettingRemovesOneAndClearingRemovesAll()
    {
        var recent = With("/a/one.lightbox.json", "/a/two.lightbox.json");

        recent.Forget("/a/one.lightbox.json");
        Assert.Equal(["two"], Names(recent));

        recent.Clear();
        Assert.Empty(recent.Items);
    }

    // ---- what is still there --------------------------------------------------

    [Fact]
    public void OnlyTheEntriesStillOnDiskAreOffered()
    {
        var dir = Directory.CreateTempSubdirectory("lightbox-recent");
        try
        {
            var file = Path.Combine(dir.FullName, "here.lightbox.json");
            File.WriteAllText(file, "{}");
            var project = Path.Combine(dir.FullName, "Knight.lbproj");
            Directory.CreateDirectory(project);

            var recent = new RecentItems();
            recent.Add(file, "here", RecentKind.Document, Noon);
            recent.Add(project, "Knight", RecentKind.Project, Noon.AddMinutes(1));
            recent.Add(Path.Combine(dir.FullName, "gone.lightbox.json"), "gone", RecentKind.Document, Noon.AddMinutes(2));

            Assert.Equal(["Knight", "here"], recent.Existing().Select(i => i.Name).ToList());
            // Filtered on read, not pruned on write: a drive that is unplugged
            // today must not cost you the entry for good.
            Assert.Equal(3, recent.Items.Count);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AProjectIsAFolderAndADocumentIsAFile()
    {
        // Checking the wrong one hides every project in the list.
        var dir = Directory.CreateTempSubdirectory("lightbox-recent-kind");
        try
        {
            var project = Path.Combine(dir.FullName, "Knight.lbproj");
            Directory.CreateDirectory(project);
            var recent = new RecentItems();
            recent.Add(project, "Knight", RecentKind.Project, Noon);

            Assert.Single(recent.Existing());

            // The same path recorded as a document does not exist as a file.
            recent.Clear();
            recent.Add(project, "Knight", RecentKind.Document, Noon);
            Assert.Empty(recent.Existing());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheListSurvivesBeingWrittenAndReadBack()
    {
        var settings = new AppSettings();
        settings.Recent.Add("/a/one.lightbox.json", "one", RecentKind.Document, Noon);
        settings.Recent.Add("/art/Knight.lbproj", "Knight", RecentKind.Project, Noon.AddMinutes(1));

        var back = AppSettings.Deserialize(settings.Serialize());

        Assert.Equal(["Knight", "one"], back.Recent.Items.ConvertAll(i => i.Name));
        Assert.Equal(RecentKind.Project, back.Recent.Items[0].Kind);
        Assert.True(back.ShowStartScreen);
    }
}
