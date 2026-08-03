using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using Lightbox.Raster.Tips;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>Where a library tip came from, and therefore what may be done to it.</summary>
public enum TipScope
{
    /// <summary>Shared by every document in the project.</summary>
    Project,

    /// <summary>The artist's own, following them between projects.</summary>
    User,

    /// <summary>Shipped with the app. Read-only: its id is in old documents.</summary>
    BuiltIn,
}

/// <summary>One tip in the library list.</summary>
public sealed class TipRow(BrushTip tip, TipScope scope, Bitmap? thumbnail)
{
    public BrushTip Tip { get; } = tip;

    public TipScope Scope { get; } = scope;

    public string Name => Tip.Name;

    public Bitmap? Thumbnail { get; } = thumbnail;

    public string Detail =>
        (Scope switch { TipScope.Project => "Project", TipScope.BuiltIn => "Built-in", _ => "User" })
        + (Tip.Recipe is { } r ? $" · {r.Shape} {r.Size}px" : Tip.Source is { } s ? $" · {s}" : "");
}

/// <summary>
/// The brush tip library, its generator, and the scan pipeline — one window off
/// the main menu, the same shape as Configure.
/// </summary>
/// <remarks>
/// <para>
/// A window rather than a docker because making a tip is not something you do
/// while drawing. It is a workshop task with its own controls and its own
/// previews, and giving it panel space in the drawing layout would tax every
/// session that never opens it.
/// </para>
/// <para>
/// <b>Everything here bakes.</b> A tip leaves this window as pixels. Nothing it
/// produces is re-derived at stroke time, at load, or by the inbetweener — the
/// engine resolves an id to a cached raster and has no idea a formula was ever
/// involved.
/// </para>
/// </remarks>
public partial class BrushTipsWindow : Window
{
    private readonly MainViewModel _vm;
    private TipStore.State _user;
    private readonly ObservableCollection<TipRow> _rows = [];
    private readonly List<(string Path, SKBitmap Image)> _scans = [];
    private readonly string? _storePath;
    private bool _syncingName;

    public BrushTipsWindow() : this(new MainViewModel(null)) { }

    public BrushTipsWindow(MainViewModel vm, string? storePath = null)
    {
        _vm = vm;
        _storePath = storePath;
        _user = TipStore.Load(storePath);
        InitializeComponent();
        TipList.ItemsSource = _rows;
        RefreshLibrary();
        RefreshGeneratePreview();
        UpdateShapeRows();
    }

    // ---- library ------------------------------------------------------------

    private void RefreshLibrary()
    {
        var selected = SelectedTip?.Id;
        _rows.Clear();

        foreach (var tip in _vm.ProjectDocker.Project?.Manifest.Tips ?? [])
        {
            _rows.Add(new TipRow(tip, TipScope.Project, Preview(tip)));
        }
        foreach (var tip in _user.Tips)
        {
            _rows.Add(new TipRow(tip, TipScope.User, Preview(tip)));
        }
        foreach (var tip in TipCatalogue.All)
        {
            _rows.Add(new TipRow(tip, TipScope.BuiltIn, Preview(tip)));
        }

        // The catalogue is always there, so the empty state now only means the
        // artist has made nothing yet — which is still worth saying, because
        // otherwise eight shapes they did not choose look like their library.
        EmptyLibrary.IsVisible = _rows.All(r => r.Scope == TipScope.BuiltIn);
        if (selected is not null)
        {
            TipList.SelectedItem = _rows.FirstOrDefault(r => r.Tip.Id == selected);
        }
        UpdateLibraryButtons();
    }

    private BrushTip? SelectedTip => (TipList.SelectedItem as TipRow)?.Tip;

    private void UpdateLibraryButtons()
    {
        var row = TipList.SelectedItem as TipRow;

        // A built-in is read-only, and not because it is precious: its id is
        // recorded in every document that ever painted with it, so deleting or
        // renaming one would make an old file refer to something that no longer
        // means what it meant.
        var mutable = row is not null && row.Scope != TipScope.BuiltIn;
        DeleteButton.IsEnabled = mutable;
        NameBox.IsEnabled = mutable;
        PromoteButton.IsEnabled = row is { Scope: TipScope.User } && _vm.ProjectDocker.Project is not null;
        EditCopyButton.IsEnabled = row?.Tip.Recipe is not null;

        _syncingName = true;
        NameBox.Text = row?.Name ?? "";
        _syncingName = false;
    }

    private void OnTipSelected(object? sender, SelectionChangedEventArgs e) => UpdateLibraryButtons();

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LibraryPage is null) return; // fires once during InitializeComponent
        var index = CategoryList.SelectedIndex;
        LibraryPage.IsVisible = index == 0;
        GeneratePage.IsVisible = index == 1;
        ScanPage.IsVisible = index == 2;
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (TipList.SelectedItem is not TipRow row) return;

        // Only the library copy goes. Any drawing that painted with it holds
        // its own raster in Doc.BrushTips, so deleting here can never reach
        // back and change a picture — which is the whole reason a tip travels
        // with the document rather than being referenced out of a library.
        if (row.Scope == TipScope.BuiltIn) return;
        if (row.Scope == TipScope.Project) _vm.ProjectDocker.Project?.Manifest.Tips?.RemoveAll(t => t.Id == row.Tip.Id);
        else _user.Tips.RemoveAll(t => t.Id == row.Tip.Id);

        Persist();
        RefreshLibrary();
    }

    /// <summary>
    /// Renaming is the name field, not a modal. Typing writes straight to the
    /// tip and saves; rebuilding the list on every keystroke would fight the
    /// caret, so the row's label catches up when the selection next moves.
    /// </summary>
    private void OnRenamed(object? sender, TextChangedEventArgs e)
    {
        if (_syncingName || TipList.SelectedItem is not TipRow { Scope: not TipScope.BuiltIn, Tip: var tip }) return;
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name) || name == tip.Name) return;
        tip.Name = name;
        Persist();
    }

    private void OnPromote(object? sender, RoutedEventArgs e)
    {
        if (TipList.SelectedItem is not TipRow { Scope: TipScope.User } row) return;
        if (_vm.ProjectDocker.Project is not { } project) return;

        project.Manifest.Tips ??= [];
        project.Manifest.Tips.Add(row.Tip.Clone());
        _user.Tips.RemoveAll(t => t.Id == row.Tip.Id);
        Persist();
        RefreshLibrary();
    }

    // ---- generate -----------------------------------------------------------

    private TipRecipe CurrentRecipe() => new()
    {
        Shape = (TipShape)Math.Max(0, ShapeBox.SelectedIndex),
        Size = SizeBox.SelectedIndex switch { 0 => 128, 2 => 512, 3 => 1024, _ => 256 },
        Hardness = HardnessSlider.Value,
        InnerRadius = InnerSlider.Value,
        Roundness = RoundnessSlider.Value,
        Angle = AngleSlider.Value,
        Spacing = SpacingSlider.Value,
        LineWidth = LineWidthSlider.Value,
        Crossed = CrossedBox.IsChecked == true,
        Count = (int)Math.Round(CountSlider.Value),
        Sharpness = SharpnessSlider.Value,
    };

    /// <summary>
    /// Put a recipe back on the controls. The inverse of
    /// <see cref="CurrentRecipe"/>, which is what makes a tip re-openable —
    /// the reason <see cref="BrushTip.Recipe"/> is stored at all.
    /// </summary>
    private void LoadRecipe(TipRecipe recipe)
    {
        ShapeBox.SelectedIndex = (int)recipe.Shape;
        SizeBox.SelectedIndex = recipe.Size switch { 128 => 0, 512 => 2, 1024 => 3, _ => 1 };
        HardnessSlider.Value = Math.Clamp(recipe.Hardness, HardnessSlider.Minimum, HardnessSlider.Maximum);
        InnerSlider.Value = Math.Clamp(recipe.InnerRadius, InnerSlider.Minimum, InnerSlider.Maximum);
        RoundnessSlider.Value = Math.Clamp(recipe.Roundness, RoundnessSlider.Minimum, RoundnessSlider.Maximum);
        AngleSlider.Value = Math.Clamp(recipe.Angle, AngleSlider.Minimum, AngleSlider.Maximum);
        SpacingSlider.Value = Math.Clamp(recipe.Spacing, SpacingSlider.Minimum, SpacingSlider.Maximum);
        LineWidthSlider.Value = Math.Clamp(recipe.LineWidth, LineWidthSlider.Minimum, LineWidthSlider.Maximum);
        CrossedBox.IsChecked = recipe.Crossed;
        CountSlider.Value = Math.Clamp(recipe.Count, CountSlider.Minimum, CountSlider.Maximum);
        SharpnessSlider.Value = Math.Clamp(recipe.Sharpness, SharpnessSlider.Minimum, SharpnessSlider.Maximum);
        OnRecipeChanged();
    }

    /// <summary>Load the selected tip's recipe into the generator. Test seam for the click.</summary>
    internal void EditCopyForTest() => OnEditCopy(null, new RoutedEventArgs());

    /// <summary>
    /// Open the selected tip's recipe in the generator. A copy, always: baking
    /// over an existing tip would change the pixels under every drawing that
    /// used it, which is invariant 1 read from the wrong end.
    /// </summary>
    private void OnEditCopy(object? sender, RoutedEventArgs e)
    {
        if (SelectedTip is not { Recipe: { } recipe } tip) return;
        LoadRecipe(recipe);
        GenerateName.Text = $"{tip.Name} copy";
        CategoryList.SelectedIndex = 1;
    }

    private void OnRecipeChanged(object? sender, RoutedEventArgs e) => OnRecipeChanged();

    private void OnRecipeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        OnRecipeChanged();

    private void OnRecipeChanged(object? sender, SelectionChangedEventArgs e) => OnRecipeChanged();

    private void OnRecipeChanged()
    {
        if (GeneratePreview is null) return; // during InitializeComponent
        UpdateShapeRows();
        RefreshGeneratePreview();
    }

    /// <summary>
    /// Only the controls the chosen shape reads. A slider that does nothing is
    /// worse than an absent one — the same rule the brush cost badge follows.
    /// </summary>
    private void UpdateShapeRows()
    {
        var shape = (TipShape)Math.Max(0, ShapeBox.SelectedIndex);
        HardnessRow.IsVisible = shape == TipShape.SoftCircle;
        InnerRow.IsVisible = shape == TipShape.Ring;
        RoundnessRow.IsVisible = shape is TipShape.Chisel or TipShape.Superellipse;
        HatchRows.IsVisible = shape == TipShape.Hatch;

        // Count and Sharpness are one control each and four meanings each, so
        // they are relabelled rather than duplicated. A slider called
        // "Sharpness" that actually sets grain coverage is the same lie as a
        // slider that does nothing.
        CountRow.IsVisible = shape is TipShape.Bristle or TipShape.Polygon or TipShape.Spatter;
        CountLabel.Text = shape switch
        {
            TipShape.Bristle => "Bristles",
            TipShape.Polygon => "Sides",
            // Cells across, not grain size: more cells is *smaller* grains, and
            // a label that reads the other way round makes the slider feel
            // broken to the one person who bothers to check.
            _ => "Grains across",
        };

        SharpnessRow.IsVisible = shape is TipShape.Bristle or TipShape.Superellipse
            or TipShape.Polygon or TipShape.Spatter or TipShape.Halo;
        SharpnessLabel.Text = shape switch
        {
            TipShape.Bristle => "Channel depth",
            TipShape.Superellipse => "Squareness",
            TipShape.Polygon => "Corner sharpness",
            TipShape.Spatter => "Coverage",
            _ => "Rim strength",
        };

        // Sides top out at 12 and bristles want more than a polygon does.
        CountSlider.Maximum = shape switch
        {
            TipShape.Polygon => 12,
            TipShape.Spatter => 24,
            _ => 32,
        };
    }

    private void RefreshGeneratePreview()
    {
        // Previews bake at a fixed small size regardless of the chosen output,
        // so dragging a slider does not silently start baking 1024² per frame.
        var recipe = CurrentRecipe();
        recipe.Size = 192;
        GeneratePreview.Source = Preview(TipGenerator.Coverage(recipe, recipe.Size), recipe.Size);
    }

    /// <summary>Add the current recipe to the library. Test seam for the click.</summary>
    internal void OnGenerateForTest() => OnGenerate(null, new RoutedEventArgs());

    private void OnGenerate(object? sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(GenerateName.Text) ? "Tip" : GenerateName.Text.Trim();
        _user.Tips.Add(TipGenerator.Create(CurrentRecipe(), name));
        Persist();
        CategoryList.SelectedIndex = 0;
        RefreshLibrary();
    }

    // ---- scans --------------------------------------------------------------

    private TipImageSettings ScanSettings() => new()
    {
        BlackPoint = BlackSlider.Value,
        WhitePoint = WhiteSlider.Value,
        Invert = InvertBox.IsChecked == true,
        CentreOnMass = CentreBox.IsChecked == true,
        Size = ScanSizeBox.SelectedIndex switch { 0 => 256, 2 => 1024, 3 => 2048, _ => 512 },
    };

    private async void OnChooseImages(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scanned stamps",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        foreach (var (_, image) in _scans) image.Dispose();
        _scans.Clear();

        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;
            if (SKBitmap.Decode(path) is { } bitmap) _scans.Add((path, bitmap));
        }

        ScanCount.Text = _scans.Count switch
        {
            0 => "No images loaded.",
            1 => "1 image. One set of levels applies to it.",
            var n => $"{n} images. One set of levels applies to all of them.",
        };
        ScanAddButton.IsEnabled = _scans.Count > 0;
        OnScanChanged();
    }

    private void OnScanChanged(object? sender, RoutedEventArgs e) => OnScanChanged();

    private void OnScanChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        OnScanChanged();

    private void OnScanChanged(object? sender, SelectionChangedEventArgs e) => OnScanChanged();

    private void OnScanChanged()
    {
        if (ScanPreview is null || _scans.Count == 0) return;

        // Preview the first of the batch at a small size — the levels are what
        // is being judged, and they are the same for every image in it.
        var settings = ScanSettings();
        settings.Size = 192;
        var result = TipFromImage.Create(_scans[0].Image, settings, "preview");

        ScanWarning.IsVisible = result.Rejection is not null;
        ScanWarningText.Text = result.Rejection ?? "";
        ScanPreview.Source = result.Tip is null ? null : Preview(result.Tip);
    }

    private void OnAddScans(object? sender, RoutedEventArgs e)
    {
        var settings = ScanSettings();
        var rejected = new List<string>();
        var added = 0;

        foreach (var (path, image) in _scans)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var result = TipFromImage.Create(image, settings, name);
            if (result.Tip is { } tip)
            {
                tip.Source = Path.GetFileName(path);
                _user.Tips.Add(tip);
                added++;
            }
            else
            {
                rejected.Add(name);
            }
        }

        Persist();

        if (rejected.Count > 0)
        {
            // Named, not counted. "Two were rejected" sends the artist looking
            // through the whole batch; naming them says which scans to re-crop.
            ScanWarning.IsVisible = true;
            ScanWarningText.Text =
                $"Added {added}. The mark runs off the crop in: {string.Join(", ", rejected)} — " +
                "re-crop those with clear paper all the way round, or they will stamp a box down every stroke.";
        }
        else
        {
            CategoryList.SelectedIndex = 0;
        }

        RefreshLibrary();
    }

    // ---- shared -------------------------------------------------------------

    private void Persist()
    {
        TipStore.Save(_user, _storePath);
        if (_vm.ProjectDocker.Project is not null) _vm.SaveProject();
    }

    private static Bitmap? Preview(BrushTip tip)
    {
        try
        {
            using var bitmap = PngCodec.Decode(tip.Png);
            return Preview(bitmap);
        }
        catch
        {
            return null; // a malformed tip shows no thumbnail rather than throwing
        }
    }

    /// <summary>
    /// The tip as ink on paper, which is how an artist reads a stamp — the
    /// shape lives in alpha, and rendering alpha straight onto a dark panel
    /// would show a bright blob where a mark should be.
    /// </summary>
    private static Bitmap Preview(SKBitmap tip)
    {
        var info = new SKImageInfo(tip.Width, tip.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create a tip preview surface.");
        surface.Canvas.Clear(new SKColor(0xf2, 0xf0, 0xec));
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0x1a, 0x1a, 0x1a), SKBlendMode.SrcIn),
        };
        surface.Canvas.DrawBitmap(tip, 0, 0, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    private static Bitmap Preview(float[] alpha, int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var bytes = new byte[size * size * 4];
        for (int i = 0, o = 0; i < alpha.Length; i++, o += 4)
        {
            bytes[o] = 255;
            bytes[o + 1] = 255;
            bytes[o + 2] = 255;
            bytes[o + 3] = (byte)(Math.Clamp(alpha[i], 0f, 1f) * 255f + 0.5f);
        }
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return Preview(bitmap);
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var (_, image) in _scans) image.Dispose();
        _scans.Clear();
        base.OnClosed(e);
    }
}
