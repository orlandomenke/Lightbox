using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Configure ▸ Library: taking back "don't ask again".
/// </summary>
/// <remarks>
/// <para>
/// B373's dialog can store a preference, and a preference an artist can store
/// but not find again is a trap — so the stored answer is shown here and "Ask
/// every time" is how it is undone.
/// </para>
/// <para>
/// <b>Guarded because the Configure window is the thing this repository has
/// shipped unguarded before.</b> `HOTSPOTS.md` puts its XAML near the top of
/// the risk table, and a whole page once landed there with no test at all.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SymbolPlacementConfigureTests : BrushStateIsolated
{
    private static (ConfigureWindow Window, MainViewModel Vm) Open()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", 320, 240, 12, 72, Scene.DefaultBackgroundColor, false));
        return (new ConfigureWindow(new ShortcutMap(), vm), vm);
    }

    private static ComboBox Box(ConfigureWindow window) =>
        window.FindControl<ComboBox>("SymbolPlacementBox")
        ?? throw new InvalidOperationException(
            "Configure has no SymbolPlacementBox — the stored placement answer cannot be taken back");

    /// <summary>Nothing stored means "Ask every time", which is the first entry.</summary>
    [AvaloniaFact]
    public void ItOpensOnAskWhenNothingIsStored()
    {
        var (window, vm) = Open();
        vm.PlacementPreference = null;

        var box = Box(window);

        Assert.Equal(3, ((IEnumerable<string>)box.ItemsSource!).Count());
        Assert.Equal(0, box.SelectedIndex);
    }

    /// <summary>A stored answer is shown, rather than the page lying about it.</summary>
    [AvaloniaFact]
    public void ItShowsTheAnswerTheDialogStored()
    {
        var (_, vm) = Open();
        vm.PlacementPreference = FrameImportChoice.ImportFrames;

        // Re-opened, because the page reads the stored value when it loads.
        var reopened = new ConfigureWindow(new ShortcutMap(), vm);

        Assert.NotEqual(0, Box(reopened).SelectedIndex);
        Assert.Equal(
            FrameImportChoice.ImportFrames,
            vm.PlacementPreference);
    }

    /// <summary>Choosing here writes the preference.</summary>
    [AvaloniaFact]
    public void ChoosingAnAnswerStoresIt()
    {
        var (window, vm) = Open();
        vm.PlacementPreference = null;

        var box = Box(window);
        box.SelectedIndex = 2;

        Assert.Equal(FrameImportChoice.Reference, vm.PlacementPreference);
    }

    /// <summary>
    /// "Ask every time" clears it, which is the whole reason the control exists.
    /// </summary>
    [AvaloniaFact]
    public void AskEveryTimeTakesTheStoredAnswerBack()
    {
        var (_, vm) = Open();
        vm.PlacementPreference = FrameImportChoice.ImportFrames;

        // Stored *before* the window loads, so the box opens on that answer and
        // moving to "Ask every time" is a real change. Setting it afterwards
        // leaves the box already at index 0, SelectionChanged never fires, and
        // the test passes or fails on its own setup rather than on the control.
        var reopened = new ConfigureWindow(new ShortcutMap(), vm);
        var box = Box(reopened);
        Assert.NotEqual(0, box.SelectedIndex);

        box.SelectedIndex = 0;

        Assert.Null(vm.PlacementPreference);
    }
}
