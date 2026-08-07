using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Save has a key, Save as has a key, and neither export nor a status change happens
/// without a file.
/// </summary>
/// <remarks>
/// <para>
/// The reported symptom was "Ctrl+Shift+S does not open the window", and the cause was
/// duller than a broken handler: <c>Ctrl+S</c> was an <c>InputGesture</c> on the Save menu
/// item, so nothing was in the registry, so nobody ever noticed Save as had no gesture at
/// all. Registering both is what makes them appear in the shortcut editor and what let
/// <c>ShortcutRegistrationTests</c> empty its exemption list.
/// </para>
/// <para>
/// The dialogs themselves are not driven here — they need a modal owner, and a test that
/// clicks a button in one proves less than the decision behind it, which is
/// <see cref="SaveRequirementTests"/>. What these cover is the wiring in between: the
/// gestures exist, the menu shows what it will actually respond to, and the gate asks about
/// the right document.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SaveAndStatusGateTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    /// <summary>
    /// A named menu item, through the name scope rather than the visual tree.
    /// </summary>
    /// <remarks>
    /// Menu items are not realised until the menu is opened, so walking the visual tree
    /// finds nothing on a window that has merely been shown.
    /// </remarks>
    private static MenuItem Find(MainWindow window, string name)
    {
        var item = window.FindControl<MenuItem>(name);
        Assert.NotNull(item);
        return item!;
    }

    // ---- the registry ---------------------------------------------------------------

    [Fact]
    public void SaveAndSaveAsAreBothRegisteredWithTheKeysAnArtistExpects()
    {
        var map = new ShortcutMap();

        var save = map.Definitions.Single(d => d.Id == "file.save");
        var saveAs = map.Definitions.Single(d => d.Id == "file.saveAs");

        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), save.Current);
        Assert.Equal(
            new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), saveAs.Current);
        Assert.Equal("File", save.Category);
        Assert.Equal("File", saveAs.Category);
    }

    [Fact]
    public void TheTwoSaveKeysDoNotCollideWithEachOtherOrWithTheSelectTool()
    {
        // Plain S is the select tool, so the modifiers are load-bearing rather than
        // decorative — and Ctrl+S against Ctrl+Shift+S is the pair most likely to be
        // treated as the same gesture by a careless comparison.
        var map = new ShortcutMap();

        Assert.Null(map.ConflictWith("file.save", new KeyGesture(Key.S, KeyModifiers.Control)));
        Assert.Null(map.ConflictWith(
            "file.saveAs", new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift)));

        var clash = map.ConflictWith("file.saveAs", new KeyGesture(Key.S, KeyModifiers.Control));
        Assert.Equal("file.save", clash?.Id);
    }

    [Fact]
    public void BothAreRebindableWhichIsTheWholePointOfRegisteringThem()
    {
        var map = new ShortcutMap { StorePathOverride = Path.Combine(Path.GetTempPath(), $"lb-{Guid.NewGuid():N}.json") };
        map.Assign("file.saveAs", new KeyGesture(Key.F12));

        Assert.Equal(new KeyGesture(Key.F12), map.Definitions.Single(d => d.Id == "file.saveAs").Current);
    }

    // ---- the menu tells the truth ----------------------------------------------------

    [AvaloniaFact]
    public void TheMenuShowsTheGestureItWillActuallyRespondTo()
    {
        // Read from the registry rather than written in the XAML: a label saying Ctrl+S on a
        // command the artist rebound is worse than no label, because they will trust it.
        var (window, _) = Open();

        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), Find(window, "SaveMenu").InputGesture);
        Assert.Equal(
            new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            Find(window, "SaveAsMenu").InputGesture);
    }

    [AvaloniaFact]
    public void NeitherSaveItemCarriesAHardCodedGestureAnyMore()
    {
        // The XAML-side guard lives in ShortcutRegistrationTests; this is the same claim
        // from the built control's side, so removing the registration would fail here even
        // if somebody also re-added an InputGesture attribute to match.
        var (window, _) = Open();
        var map = new ShortcutMap();
        var registered = map.Definitions.Where(d => d.Current is not null)
            .Select(d => d.Current!).ToList();

        foreach (var name in new[] { "SaveMenu", "SaveAsMenu" })
        {
            var gesture = Find(window, name).InputGesture;
            Assert.NotNull(gesture);
            Assert.Contains(gesture!, registered);
        }
    }

    // ---- the gate asks about the right document --------------------------------------

    [AvaloniaFact]
    public void AnUnsavedDocumentIsWhatTheGateSeesForTheDocumentInFront()
    {
        var (_, vm) = Open();
        vm.NewDocument(new NewDocumentSettings("fresh", 64, 64, 12, 72, "#ffffff", false));

        var gate = SaveRequirement.For(vm.SaveTargetTab?.FilePath, vm.SaveTargetTab?.IsDirty ?? false);

        Assert.Equal(SaveGate.AskWhereItGoes, gate);
        Assert.True(SaveRequirement.NeedsTheArtist(gate));
        // And Save has nowhere to write it, so it must reach the picker rather than
        // silently succeeding — which is the other half of the report.
        Assert.False(vm.CanSaveInPlace);
    }

    [AvaloniaFact]
    public void AStatusChangeAsksAboutItsOwnRowRatherThanTheActiveTab()
    {
        // The near-miss this exists for: marking row A Ready while row B is in front would
        // gate on B's file and let A through unsaved, or block A because B is dirty. Either
        // way the artist sees the app refuse for a reason that is not on screen.
        var (_, vm) = Open();

        var row = new ProjectRow(
            new DocumentRef { Name = "walk", Path = "hero/walk.lightbox.json" });
        var facts = vm.SaveFactsFor(row);

        // No project is open, so there is no root to resolve against and the honest answer
        // is "nowhere yet" — not the active tab's path.
        Assert.Null(facts.FilePath);
        Assert.NotEqual("hero/walk.lightbox.json", vm.SaveTargetTab?.FilePath);
        Assert.Equal(SaveGate.AskWhereItGoes, SaveRequirement.For(facts.FilePath, facts.HasUnsavedEdits));
    }

    [AvaloniaFact]
    public void ARowWithNoDocumentAtAllIsNotTreatedAsSaved()
    {
        // A folder row has no document behind it. Reporting "saved" for one
        // would let a status through on nothing.
        var (_, vm) = Open();

        var facts = vm.SaveFactsFor(new ProjectRow(new ProjectFolder { Name = "Hero" }, depth: 0));

        Assert.Null(facts.FilePath);
        Assert.Equal(SaveGate.AskWhereItGoes, SaveRequirement.For(facts.FilePath, facts.HasUnsavedEdits));
    }
}
