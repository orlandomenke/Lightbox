using Avalonia.Headless.XUnit;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// One test's panel arrangement must not become the next test's starting
/// layout.
/// </summary>
/// <remarks>
/// <para>
/// <b>B376.</b> <c>WorkspaceViewModel.PersistSession</c> writes the session to
/// disk on <em>every</em> layout change — deliberately, so the app reopens as it
/// was left (B288) — and <c>WorkspaceStore.Load</c> binds each store it hands
/// out to the path it read. In the suite that path was one scratch file shared
/// by every test, so any test that hid a panel left it hidden for everything
/// that ran afterwards.
/// </para>
/// <para>
/// It surfaced as <c>SidebarTests.ToggleTimeline_FlipsVisibility</c> and
/// <c>ReferenceTabTests.AddView_OpensReferenceTab_TimelineHidden</c> failing in
/// one full run of three and passing in the two either side and in isolation —
/// both assert a fresh <c>MainViewModel</c> opens with the timeline showing.
/// xUnit promises no order between classes, so which side of the polluting test
/// they landed on varied by run.
/// </para>
/// <para>
/// <b>Not a parallelism bug, which is what it looked like.</b> This assembly
/// carries <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c>
/// — the first guess was a race on the static path and it was wrong. The tests
/// below are written against the leak that was actually reproduced: hide a
/// panel in one view model, build another, and see what it starts with.
/// </para>
/// </remarks>
public sealed class WorkspaceLeakTests(ITestOutputHelper output)
{
    /// <summary>
    /// The reproduction, as a test. Before the fix the second view model opened
    /// with the timeline hidden.
    /// </summary>
    [AvaloniaFact]
    public void HidingAPanelInOneViewModelDoesNotReachTheNextOne()
    {
        var first = new MainViewModel(null);
        Assert.True(first.TimelineVisible);
        first.ToggleTimelineCommand.Execute(null);
        Assert.False(first.TimelineVisible);

        var second = new MainViewModel(null);
        output.WriteLine($"a view model built after one that hid the timeline sees: {second.TimelineVisible}");
        Assert.True(second.TimelineVisible);
    }

    /// <summary>
    /// And the mechanism that makes it true, named rather than left implicit:
    /// a store loaded anywhere in the suite has nowhere to write itself.
    /// </summary>
    /// <remarks>
    /// Asserted on <c>File</c> rather than on the static path, because <c>File</c>
    /// is what <c>Save</c> actually consults — a future change that gave the
    /// suite a real path again would have to break this to do it.
    /// </remarks>
    [Fact]
    public void AStoreLoadedInTheSuiteHasNowhereToSave()
    {
        var store = WorkspaceStore.Load();
        output.WriteLine($"store.File = \"{store.File}\"");
        Assert.True(string.IsNullOrEmpty(store.File));
    }

    /// <summary>
    /// A test that wants persistence still gets it, by setting a path of its
    /// own — so the fix removes the sharing and not the capability.
    /// </summary>
    [Fact]
    public void ATestThatAsksForAPathStillPersists()
    {
        var previous = WorkspaceStore.Path;
        var mine = Path.Combine(Path.GetTempPath(), $"lightbox-b376-{Guid.NewGuid():N}.json");
        try
        {
            WorkspaceStore.Path = mine;
            var store = WorkspaceStore.Load();
            store.Session = store.Workspaces[0].Layout.Clone();
            store.Session.Hide(DockPanelId.Timeline);
            store.Save();

            Assert.True(File.Exists(mine));
            Assert.False(WorkspaceStore.Load().Session!.IsVisible(DockPanelId.Timeline));
        }
        finally
        {
            WorkspaceStore.Path = previous;
            if (File.Exists(mine)) File.Delete(mine);
        }
    }
}
