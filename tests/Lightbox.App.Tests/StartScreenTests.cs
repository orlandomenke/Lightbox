using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The screen the application opens on, and the recents behind it.
/// </summary>
/// <remarks>
/// <para>
/// The screen used to be offered over an already-open untitled document, and the
/// case that mattered most was answering it with nothing: Escape left that
/// document behind as a usable blank page.
/// </para>
/// <para>
/// <b>That is the behaviour this file now pins the reverse of</b>, on request.
/// Escaping adopted a canvas nobody chose — a 960×540 at 12 fps you find out
/// about after drawing on it — and there was no way to decline. The screen is
/// now offered over an empty application, and answering it with nothing leaves
/// the application empty.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class StartScreenTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static async Task Apply(MainWindow w, StartChoice choice)
    {
        await w.ApplyStartChoiceAsync(choice);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // ---- the fast path ---------------------------------------------------------

    /// <summary>
    /// Declining the start screen declines the document as well.
    /// </summary>
    /// <remarks>
    /// The assertion here used to be its own opposite, and the reason it changed
    /// is worth keeping: escaping left a canvas the artist had not chosen, and
    /// the next stroke committed them to it. Nothing open is a state you can
    /// leave by choosing, which "a blank 960×540" is not.
    /// </remarks>
    [AvaloniaFact]
    public async Task EscapeLeavesNothingOpenRatherThanACanvasNobodyChose()
    {
        var (w, vm) = Open();
        Assert.Empty(vm.Tabs);

        await Apply(w, StartChoice.Nothing);

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTab);
        Assert.False(vm.HasDocument);
    }

    /// <summary>
    /// The Edit-menu toggle is the one switch for the start screen, and it
    /// persists both ways.
    /// </summary>
    /// <remarks>
    /// This test used to drive a "Don't show this again" checkbox on the screen
    /// itself. That checkbox existed while the screen sat over an already-open
    /// blank document, so skipping it cost nothing; the application now starts
    /// empty, and opting out on the way past would mean staring at an empty
    /// workspace every launch. The checkbox is gone — the preference lives only
    /// under Edit, where turning it off is a decision.
    /// </remarks>
    [AvaloniaFact]
    public void TheStartScreenToggleLivesInTheEditMenuAndPersists()
    {
        var (_, vm) = Open();
        Assert.True(vm.Settings.ShowStartScreen);

        vm.ShowStartScreen = false;
        Assert.False(AppSettings.Load().ShowStartScreen);

        // And there is a way back, or it is a setting you can only switch off.
        vm.ShowStartScreen = true;
        Assert.True(AppSettings.Load().ShowStartScreen);
    }

    [AvaloniaFact]
    public async Task OfferingTheScreenDoesNothingWhenItIsTurnedOff()
    {
        var (w, vm) = Open();
        vm.ShowStartScreen = false;

        // Would block on a modal dialog if it opened one.
        await w.OfferStartScreenAsync();

        // And it invents nothing on the way past: turning the screen off is a
        // preference about the screen, not a standing order to make a document.
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTab);
    }

    // ---- what it makes ---------------------------------------------------------

    [AvaloniaFact]
    public async Task NewFileUsesTheValuesTheScreenCollected()
    {
        var (w, vm) = Open();

        await Apply(w, new StartChoice
        {
            Document = new NewDocumentSettings(
                "Run cycle", 800, 600, 24, 72, "#101010", false, null, WorkspaceChoice.Keep),
        });

        Assert.Equal("Run cycle", vm.ActiveTab!.Title);
        Assert.Equal(800, vm.Doc.Scene.Width);
        Assert.Equal(600, vm.Doc.Scene.Height);
        Assert.Equal(24, vm.Doc.Scene.Fps);
    }

    [AvaloniaFact]
    public async Task OpeningARecentDocumentOpensIt()
    {
        var dir = Directory.CreateTempSubdirectory("lightbox-start");
        try
        {
            var (w, vm) = Open();
            var path = Path.Combine(dir.FullName, "walk.lightbox.json");
            File.WriteAllText(path, vm.SerializeDocument());

            await Apply(w, new StartChoice { Open = path, OpenKind = RecentKind.Document });

            Assert.Equal("walk", vm.ActiveTab!.Title);
            Assert.Equal(path, vm.ActiveTab.FilePath);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task AFileThatHasMovedSaysSoRatherThanDoingNothing()
    {
        var (w, vm) = Open();
        var before = vm.Tabs.Count;

        await Apply(w, new StartChoice
        {
            Open = Path.Combine(Path.GetTempPath(), "gone.lightbox.json"),
            OpenKind = RecentKind.Document,
        });

        Assert.Equal(before, vm.Tabs.Count);
        Assert.Contains("no longer", vm.AiStatus);
    }

    // ---- the recents list ------------------------------------------------------

    [AvaloniaFact]
    public void OpeningADocumentPutsItInTheRecents()
    {
        var dir = Directory.CreateTempSubdirectory("lightbox-recent-open");
        try
        {
            var (_, vm) = Open();
            var path = Path.Combine(dir.FullName, "walk.lightbox.json");
            File.WriteAllText(path, vm.SerializeDocument());

            vm.OpenRecent(new RecentItem { Path = path, Name = "walk", Kind = RecentKind.Document });

            Assert.Contains(vm.RecentEntries, i => i.Path == path);
            Assert.True(vm.HasRecents);
            // And it survives the application, not just the session.
            Assert.Contains(AppSettings.Load().Recent.Items, i => i.Path == path);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task SavingSomewhereNewRecordsItToo()
    {
        // A document written for the first time is one you have every reason
        // to come back to; recording only opens means it appears the second
        // time you use it and not the first.
        var dir = Directory.CreateTempSubdirectory("lightbox-recent-save");
        try
        {
            var (w, vm) = Open();
            // Something to save. The window used to come up with one; it now
            // comes up empty, so the test asks the screen for it — which is also
            // the route an artist takes.
            await Apply(w, new StartChoice
            {
                Document = new NewDocumentSettings(
                    "fresh", 960, 540, 12, 72, "#ffffff", false, null, WorkspaceChoice.Keep),
            });
            var path = Path.Combine(dir.FullName, "fresh.lightbox.json");
            File.WriteAllText(path, vm.SerializeDocument());

            vm.NotifySaved(path);

            Assert.Contains(vm.RecentEntries, i => i.Path == path);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void ClearingTheListEmptiesItOnDiskAsWell()
    {
        var dir = Directory.CreateTempSubdirectory("lightbox-recent-clear");
        try
        {
            var (_, vm) = Open();
            var path = Path.Combine(dir.FullName, "walk.lightbox.json");
            File.WriteAllText(path, vm.SerializeDocument());
            vm.Remember(path, RecentKind.Document);
            Assert.True(vm.HasRecents);

            vm.ForgetRecentsCommand.Execute(null);

            Assert.False(vm.HasRecents);
            Assert.Empty(AppSettings.Load().Recent.Items);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void OnlyWhatIsStillOnDiskIsOffered()
    {
        var (_, vm) = Open();

        vm.Remember(Path.Combine(Path.GetTempPath(), "vanished.lightbox.json"), RecentKind.Document);

        Assert.DoesNotContain(vm.RecentEntries, i => i.Name == "vanished");
        // Filtered on read, not pruned on write: an unplugged drive today must
        // not cost the entry for good.
        Assert.Contains(vm.Settings.Recent.Items, i => i.Name == "vanished");
    }
}
