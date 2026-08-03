using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Configure ▸ Export: the auto-export settings page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because the heatmap said to.</b> <c>ConfigureWindow.axaml</c> is the third
/// riskiest file in the repository — ten commits, three of them fixes, and until this
/// file only two of its six pages had a test at all. A whole page was added to it and
/// left unguarded, which is precisely the shape <c>HOTSPOTS.md</c> exists to surface.
/// </para>
/// <para>
/// The failure being guarded is the one a settings page fails in: a control that reads
/// its value on load and never writes it back. Every assertion here therefore goes
/// <em>through</em> the control and checks the settings object, rather than checking the
/// control it just set.
/// </para>
/// </remarks>
[Collection("ExportPresetFile")]
public class ExportConfigPageTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-cfgexport-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousSettings;

    public ExportConfigPageTests()
    {
        Directory.CreateDirectory(_dir);
        ExportPresetStore.PathOverride = Path.Combine(_dir, "export.json");
        // Its own settings file, or saving from this page would rewrite the real one.
        _previousSettings = AppSettings.Path;
        AppSettings.Path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        ExportPresetStore.PathOverride = null;
        if (_previousSettings is not null) AppSettings.Path = _previousSettings;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>The window with its view model, on the Export page.</summary>
    private static (ConfigureWindow Window, MainViewModel Vm) OnExportPage()
    {
        var vm = new MainViewModel(artist: null);
        var window = new ConfigureWindow(new ShortcutMap(), vm);
        // Index 5 — Shortcuts, Performance, Guides, Timeline, Drawing, Export, AI.
        window.FindControl<ListBox>("CategoryList")!.SelectedIndex = 5;
        return (window, vm);
    }

    private static T Control<T>(ConfigureWindow window, string name) where T : Control =>
        window.FindControl<T>(name)!;

    [AvaloniaFact]
    public void TheExportPageIsTheOneThatShowsAtItsIndex()
    {
        // An off-by-one in the category switch shows the wrong page and nothing throws,
        // which is why the list order and the visibility switch are asserted together.
        var (window, _) = OnExportPage();

        Assert.True(Control<ScrollViewer>(window, "ExportPage").IsVisible);
        Assert.False(Control<ScrollViewer>(window, "AiPage").IsVisible);
        Assert.False(Control<ScrollViewer>(window, "TimelinePage").IsVisible);
    }

    [AvaloniaFact]
    public void ItOpensShowingWhatIsStoredRatherThanADefault()
    {
        var vm = new MainViewModel(artist: null);
        vm.Settings.AutoExport.Enabled = true;
        vm.Settings.AutoExport.Trigger = AssetStatus.Review;
        vm.Settings.AutoExport.OutputFolder = "../Game/Sprites";

        var window = new ConfigureWindow(new ShortcutMap(), vm);
        window.FindControl<ListBox>("CategoryList")!.SelectedIndex = 5;

        Assert.True(Control<CheckBox>(window, "AutoExportBox").IsChecked);
        Assert.Equal("../Game/Sprites", Control<TextBox>(window, "AutoExportFolderBox").Text);
        Assert.Equal(
            AssetStatuses.InOrder.ToList().IndexOf(AssetStatus.Review),
            Control<ComboBox>(window, "AutoExportStatusBox").SelectedIndex);
    }

    [AvaloniaFact]
    public void TheToggleWritesBackAndPersists()
    {
        // The settings-page failure mode: a control that loads its value and never
        // writes it. Checked against the file, not only the object.
        var (window, vm) = OnExportPage();
        Assert.False(vm.Settings.AutoExport.Enabled);

        Control<CheckBox>(window, "AutoExportBox").IsChecked = true;

        Assert.True(vm.Settings.AutoExport.Enabled);
        Assert.True(AppSettings.Load().AutoExport.Enabled);
    }

    [AvaloniaFact]
    public void ChangingTheTriggerStatusWritesTheStatusAndNotTheIndex()
    {
        // The picker shows labels in production order; storing the selected *index*
        // would silently re-point every artist's trigger if that order ever changed.
        var (window, vm) = OnExportPage();
        var box = Control<ComboBox>(window, "AutoExportStatusBox");

        box.SelectedIndex = AssetStatuses.InOrder.ToList().IndexOf(AssetStatus.Reopened);

        Assert.Equal(AssetStatus.Reopened, vm.Settings.AutoExport.Trigger);
        Assert.Equal(AssetStatus.Reopened, AppSettings.Load().AutoExport.Trigger);
    }

    [AvaloniaFact]
    public void ThePresetPickerOffersTheBuiltInsAndTheArtistsOwn()
    {
        ExportPresetStore.Save([new ExportPreset { Name = "Knight atlas" }]);

        var (window, vm) = OnExportPage();
        var box = Control<ComboBox>(window, "AutoExportPresetBox");
        var names = ((IEnumerable<string>)box.ItemsSource!).ToList();

        Assert.Equal(ExportPreset.BuiltIns.Count + 1, names.Count);
        Assert.Contains("Knight atlas", names);

        box.SelectedIndex = names.IndexOf("Knight atlas");
        // Stored by name, so a preset list that reorders cannot re-point the setting.
        Assert.Equal("Knight atlas", vm.Settings.AutoExport.PresetName);
    }

    [AvaloniaFact]
    public void ThePresetListIsRereadEachTimeThePageIsOpened()
    {
        // The window outlives the export dialog, so a preset saved there while this was
        // open would otherwise never appear.
        var (window, _) = OnExportPage();
        var list = window.FindControl<ListBox>("CategoryList")!;

        ExportPresetStore.Save([new ExportPreset { Name = "Saved later" }]);
        list.SelectedIndex = 0;
        list.SelectedIndex = 5;

        var names = ((IEnumerable<string>)Control<ComboBox>(window, "AutoExportPresetBox")
            .ItemsSource!).ToList();
        Assert.Contains("Saved later", names);
    }

    [AvaloniaFact]
    public void AnEmptyFolderMeansUnsetRatherThanTheWorkingDirectory()
    {
        // The one value that would write files somewhere nobody chose.
        var (window, vm) = OnExportPage();
        var box = Control<TextBox>(window, "AutoExportFolderBox");

        box.Text = "  ../Game/Sprites  ";
        box.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal("../Game/Sprites", vm.Settings.AutoExport.OutputFolder);

        box.Text = "   ";
        box.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));
        Assert.Null(vm.Settings.AutoExport.OutputFolder);
    }

    [AvaloniaFact]
    public void LoadingThePageDoesNotItselfChangeAnything()
    {
        // Every handler is guarded by a loading flag. Without it, populating the combo
        // boxes fires SelectionChanged and writes settings on the way in — which would
        // turn merely opening the page into a configuration change.
        var vm = new MainViewModel(artist: null);
        vm.Settings.AutoExport.PresetName = "Backdrop";
        vm.Settings.AutoExport.Trigger = AssetStatus.Draft;

        var window = new ConfigureWindow(new ShortcutMap(), vm);
        window.FindControl<ListBox>("CategoryList")!.SelectedIndex = 5;

        Assert.Equal("Backdrop", vm.Settings.AutoExport.PresetName);
        Assert.Equal(AssetStatus.Draft, vm.Settings.AutoExport.Trigger);
        Assert.False(File.Exists(AppSettings.Path));
    }
}
