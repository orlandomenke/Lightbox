using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.Services;
using Lightbox.Core.Export;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Export sprite sheet…</c> — the window that makes Pillar 5 reachable.
/// </summary>
/// <remarks>
/// <para>
/// Everything this window drives already existed and was already tested: the sheet
/// writer, the packer, the sidecar, the anchors, the collision rectangles, the Unity
/// block and the importer. What did not exist was any way for an artist to press it —
/// <c>SpriteSheetExporter</c> was called by <c>UnityExporter</c> and by tests, and by
/// nothing in the interface. So this is a thin dialog over
/// <see cref="ExportRunner"/> and deliberately holds no export logic at all.
/// </para>
/// <para>
/// <b>Presets are the whole point, not a convenience.</b> "One click" is a claim about
/// the second export: the first one is six decisions, and if there is nowhere to keep
/// them it is six decisions every time. Three built-ins are offered so nobody has to
/// derive "union trim, grid, detect the background" from first principles to get a
/// character out.
/// </para>
/// <para>
/// Controls that do not apply are <b>hidden rather than disabled</b> — a PNG sequence
/// has no cells, no atlas and no sidecar, and offering it a layout picker teaches an
/// artist that the controls lie. Same rule the camera follows.
/// </para>
/// </remarks>
public partial class ExportWindow : Window
{
    private List<ExportPreset> _saved = [];

    /// <summary>The preset the artist settled on, or null if they cancelled.</summary>
    public ExportPreset? Chosen { get; private set; }

    public ExportWindow()
    {
        InitializeComponent();

        TargetBox.ItemsSource = new[]
        {
            "PNG sequence", "Sprite sheet",
            "Sprite sheet + Unity", "Sprite sheet + Godot",
            "Sprite sheet + Unreal", "Strips + GameMaker",
        };
        TrimBox.ItemsSource = new[]
        {
            "None — every cell is the whole canvas",
            "Union — one box for the whole sequence",
            "Per frame — tightest, offsets recorded",
        };
        PackBox.ItemsSource = new[] { "Grid", "Packed (skyline)" };
        BackgroundBox.ItemsSource = new[]
        {
            "Paper only",
            "Detected — also full-canvas fills",
            "Everything — keep the paper",
        };
        BackgroundBox.SelectionChanged += (_, _) => ShowBackgroundHint();
        GreenBox.ItemsSource = new[] { "OpenGL — up (Unity, Godot)", "DirectX — down (Unreal)" };

        Reload();
    }

    /// <summary>Rebuild the preset list, keeping the last-used one selected.</summary>
    private void Reload()
    {
        _saved = ExportPresetStore.Load();
        var all = ExportPreset.BuiltIns.Concat(_saved).ToList();
        PresetBox.ItemsSource = all.Select(Label).ToList();

        var last = ExportPresetStore.LoadLastUsed();
        var index = last is null ? 0 : all.FindIndex(p => p.Name == last);
        PresetBox.SelectedIndex = index < 0 ? 0 : index;
    }

    /// <summary>
    /// A preset's name, with a mark on the artist's own.
    /// </summary>
    /// <remarks>
    /// The same ◈ the symbol browser uses for a global symbol, so "not one of the
    /// built-ins" reads the same way in both places.
    /// </remarks>
    private string Label(ExportPreset preset) =>
        _saved.Any(p => p.Name == preset.Name) ? $"◈ {preset.Name}" : preset.Name;

    private List<ExportPreset> All() => ExportPreset.BuiltIns.Concat(_saved).ToList();

    private ExportPreset? SelectedPreset()
    {
        var all = All();
        return PresetBox.SelectedIndex >= 0 && PresetBox.SelectedIndex < all.Count
            ? all[PresetBox.SelectedIndex]
            : null;
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedPreset() is { } preset) Show(preset);
        DeletePresetButton.IsEnabled = SelectedPreset() is { } p && _saved.Any(s => s.Name == p.Name);
    }

    /// <summary>Push a preset's values into the controls.</summary>
    private void Show(ExportPreset preset)
    {
        TargetBox.SelectedIndex = preset.Target switch
        {
            ExportTarget.PngSequence => 0,
            ExportTarget.Unity => 2,
            ExportTarget.Godot => 3,
            ExportTarget.Unreal => 4,
            ExportTarget.GameMaker => 5,
            _ => 1,
        };
        TrimBox.SelectedIndex = preset.Trim switch
        {
            SpriteTrim.None => 0,
            SpriteTrim.PerFrame => 2,
            _ => 1,
        };
        PackBox.SelectedIndex = preset.Pack == SpritePack.Skyline ? 1 : 0;
        PaddingBox.Text = preset.Padding.ToString();
        BackgroundBox.SelectedIndex = preset.Background switch
        {
            BackgroundHandling.Detected => 1,
            BackgroundHandling.Everything => 2,
            _ => 0,
        };
        WorldHeightBox.Text = preset.WorldHeightUnits.ToString("0.###");
        WriteImporterBox.IsChecked = preset.WriteImporter;

        NormalMapBox.IsChecked = preset.NormalMap;
        BevelBox.Text = preset.Normal.BevelPixels.ToString("0.###");
        NormalStrengthBox.Text = preset.Normal.Strength.ToString("0.###");
        GreenBox.SelectedIndex = preset.Normal.Green == NormalGreen.DirectX ? 1 : 0;

        ApplyVisibility();
        ShowBackgroundHint();
    }

    /// <summary>Read the controls back into a preset.</summary>
    /// <param name="name">
    /// The name to give it. The window never invents one — an unnamed preset would
    /// show as a blank row and could not be picked again.
    /// </param>
    internal ExportPreset Gather(string name) => new()
    {
        Name = name,
        Target = TargetBox.SelectedIndex switch
        {
            0 => ExportTarget.PngSequence,
            2 => ExportTarget.Unity,
            3 => ExportTarget.Godot,
            4 => ExportTarget.Unreal,
            5 => ExportTarget.GameMaker,
            _ => ExportTarget.SpriteSheet,
        },
        Trim = TrimBox.SelectedIndex switch
        {
            0 => SpriteTrim.None,
            2 => SpriteTrim.PerFrame,
            _ => SpriteTrim.Union,
        },
        Pack = PackBox.SelectedIndex == 1 ? SpritePack.Skyline : SpritePack.Grid,
        // A typo falls back to the safe value rather than refusing the export. Nobody
        // is served by a dialog that will not let them out over a stray character in
        // a gutter width.
        Padding = int.TryParse(PaddingBox.Text, out var pad) && pad >= 0 ? pad : 0,
        Background = BackgroundBox.SelectedIndex switch
        {
            1 => BackgroundHandling.Detected,
            2 => BackgroundHandling.Everything,
            _ => BackgroundHandling.PaperOnly,
        },
        WorldHeightUnits = double.TryParse(WorldHeightBox.Text, out var h) && h > 0 ? h : 1.0,
        WriteImporter = WriteImporterBox.IsChecked == true,
        NormalMap = NormalMapBox.IsChecked == true,
        Normal = new NormalMapOptions(
            BevelPixels: double.TryParse(BevelBox.Text, out var bevel) && bevel > 0 ? bevel : 6,
            Strength: double.TryParse(NormalStrengthBox.Text, out var st) && st > 0 ? st : 1.0,
            Green: GreenBox.SelectedIndex == 1 ? NormalGreen.DirectX : NormalGreen.OpenGl),
    };

    private void OnNormalMapChanged(object? sender, RoutedEventArgs e) => ApplyVisibility();

    private void OnTargetChanged(object? sender, SelectionChangedEventArgs e) => ApplyVisibility();

    /// <summary>Hide what does not apply, rather than greying it out.</summary>
    private void ApplyVisibility()
    {
        // Gather with a throwaway name purely to reuse the two "does this apply"
        // properties on the record, so the window cannot disagree with it.
        var preset = Gather("probe");

        foreach (var control in new Control[]
                 { TrimLabel, TrimBox, BackgroundLabel, BackgroundBox, BackgroundHint })
        {
            control.IsVisible = preset.UsesSheetSettings;
        }

        // Packing, columns and padding are the three the strip convention decides for
        // itself, so they go away rather than being shown and then overridden. The trim
        // stays, because none-versus-union is still a real choice for a strip.
        foreach (var control in new Control[] { PackLabel, PackBox, PaddingLabel, PaddingBox })
        {
            control.IsVisible = preset.UsesSheetSettings && !preset.UsesStripLayout;
        }

        EnginePanel.IsVisible = preset.UsesEngineSettings;

        // A PNG sequence has no sheet to map. The map's own settings appear only once it
        // is asked for, so the panel is one line until somebody wants four.
        NormalPanel.IsVisible = preset.UsesSheetSettings;
        NormalRows.IsVisible = preset.UsesSheetSettings && NormalMapBox.IsChecked == true;
    }

    private void ShowBackgroundHint()
    {
        BackgroundHint.Text = BackgroundBox.SelectedIndex switch
        {
            1 => "Leaves out any layer that covers the whole canvas on every drawing it "
                 + "shows — the grey layer under the line. Reads pixels, not names, and the "
                 + "export lists what it left out.",
            2 => "Keeps everything, paper included. For the asset whose background is the "
                 + "point: a backdrop, a sky, a tiling floor.",
            _ => "Leaves out the document's Background layer and nothing else. What Lightbox "
                 + "has always done.",
        };
    }

    private void OnSavePreset(object? sender, RoutedEventArgs e)
    {
        // An inline name field rather than a second dialog, matching how a timing
        // pattern is saved. A blank name does nothing: a nameless preset would show as
        // a blank row and could never be picked again.
        var name = PresetNameBox.Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        var preset = Gather(name.Trim());
        // Replace by name rather than adding a duplicate: two rows reading "Atlas" is
        // worse than losing the older one, and the artist just typed the name.
        _saved.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        _saved.Add(preset);
        ExportPresetStore.Save(_saved, preset.Name);

        Reload();
        var all = All();
        var index = all.FindIndex(p => p.Name == preset.Name);
        if (index >= 0) PresetBox.SelectedIndex = index;
        PresetNameBox.Text = "";
    }

    /// <summary>Test seam for the save path, which needs a name typed into the box.</summary>
    internal void SavePresetForTests(string name)
    {
        PresetNameBox.Text = name;
        OnSavePreset(null, new RoutedEventArgs());
    }

    internal void DeletePresetForTests() => OnDeletePreset(null, new RoutedEventArgs());

    internal void SelectPresetForTests(int index) => PresetBox.SelectedIndex = index;

    private void OnDeletePreset(object? sender, RoutedEventArgs e)
    {
        if (SelectedPreset() is not { } preset) return;
        if (!_saved.Any(p => p.Name == preset.Name)) return;   // built-ins stay

        _saved.RemoveAll(p => p.Name == preset.Name);
        ExportPresetStore.Save(_saved);
        Reload();
    }

    private void OnExport(object? sender, RoutedEventArgs e)
    {
        // Named after the preset that was selected, so saving and exporting keep the
        // same identity. A hand-edited built-in exports under the built-in's name and
        // does not silently become a fourth preset.
        Chosen = Gather(SelectedPreset()?.Name ?? "Export");
        ExportPresetStore.Save(_saved, Chosen.Name);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Chosen = null;
        Close();
    }

    // ---- test seams --------------------------------------------------------------
    // The window cannot be driven by synthetic clicks reliably in this environment, so
    // the two paths that matter are reachable directly.

    internal void ShowForTests(ExportPreset preset) => Show(preset);

    internal ExportPreset GatherForTests(string name = "test") => Gather(name);

    internal void TypeForTests(string? padding = null, string? worldHeight = null)
    {
        if (padding is not null) PaddingBox.Text = padding;
        if (worldHeight is not null) WorldHeightBox.Text = worldHeight;
    }

    internal bool SheetRowsVisibleForTests => TrimBox.IsVisible;

    internal bool EngineRowsVisibleForTests => EnginePanel.IsVisible;

    internal bool NormalRowsVisibleForTests => NormalRows.IsVisible;

    internal void TickNormalMapForTests(bool on)
    {
        NormalMapBox.IsChecked = on;
        ApplyVisibility();
    }
}
