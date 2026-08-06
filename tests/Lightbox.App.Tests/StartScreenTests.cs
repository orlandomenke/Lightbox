using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The screen the application opens on, and the recents behind it.
/// </summary>
/// <remarks>
/// The screen is offered over an already-open untitled document, so the case
/// that matters most is the one where it is answered with nothing: Escape has
/// to leave a usable blank page rather than an empty application.
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

    [AvaloniaFact]
    public async Task EscapeLeavesABlankDocumentRatherThanNothing()
    {
        // The whole reason the screen is shown over a document instead of
        // instead of one. "Open it and draw" stays a keystroke.
        var (w, vm) = Open();
        var tabs = vm.Tabs.Count;

        await Apply(w, StartChoice.Nothing);

        Assert.Equal(tabs, vm.Tabs.Count);
        Assert.NotNull(vm.ActiveTab);
        // Escaping the start screen leaves a blank document, not work — it
        // badges as never-written (B99) and has nothing to lose.
        Assert.False(vm.ActiveTab!.HasWorkToLose);
    }

    [AvaloniaFact]
    public async Task DontShowAgainIsRememberedAndCanBeTurnedBackOn()
    {
        var (w, vm) = Open();
        Assert.True(vm.Settings.ShowStartScreen);

        await Apply(w, StartChoice.Nothing with { DontShowAgain = true });
        Assert.False(vm.Settings.ShowStartScreen);
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

        Assert.NotNull(vm.ActiveTab);
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
    public void SavingSomewhereNewRecordsItToo()
    {
        // A document written for the first time is one you have every reason
        // to come back to; recording only opens means it appears the second
        // time you use it and not the first.
        var dir = Directory.CreateTempSubdirectory("lightbox-recent-save");
        try
        {
            var (_, vm) = Open();
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
