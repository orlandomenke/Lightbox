using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One symbol, as the browser shows it.
/// </summary>
public sealed partial class SymbolRow : ObservableObject
{
    public SymbolRow(Symbol model)
    {
        Model = model;
        _name = model.Name;
    }

    public Symbol Model { get; }

    [ObservableProperty]
    private string _name;

    partial void OnNameChanged(string value)
    {
        // Renaming does not bump the version: the drawing did not change, and
        // "the sword changed since you placed this" must not fire because
        // somebody fixed a typo.
        Model.Name = value;
    }

    public SymbolKind Kind => Model.Kind;

    public string KindLabel => Model.Kind.ToString();

    /// <summary>Tags as one editable line, which is how they are thought about.</summary>
    public string Tags
    {
        get => string.Join(", ", Model.Tags);
        set
        {
            Model.Tags = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            OnPropertyChanged();
        }
    }

    /// <summary>Frame count, shown only when there is more than one.</summary>
    public string? Length => Model.Frames.Count > 1 ? $"{Model.Frames.Count}f" : null;

    [ObservableProperty]
    private Bitmap? _thumb;

    /// <summary>The version the thumbnail was made from, so an edit refreshes it.</summary>
    public int ThumbVersion { get; set; } = -1;
}

/// <summary>
/// The symbol browser: what a project has, and how to find one.
/// </summary>
/// <remarks>
/// <para>
/// Pillar 3 lists a pose library, an expression library, a hand library, a face
/// library, a prop library and an FX library. They are not six features — they
/// are <see cref="SymbolKind"/> values and the filter below, which is why they
/// land together and why adding a seventh costs an enum entry.
/// </para>
/// <para>
/// Absent unless a project is open, like the project panel and for the same
/// reason: a symbol lives above the animations that place it, so without a
/// project there is nowhere for one to live and no panel to show.
/// </para>
/// </remarks>
public sealed partial class SymbolBrowserViewModel : ObservableObject
{
    private readonly Func<Project?> _project;
    private readonly Func<(int Width, int Height)> _canvasSize;

    public SymbolBrowserViewModel(Func<Project?> project, Func<(int, int)> canvasSize)
    {
        _project = project;
        _canvasSize = canvasSize;
    }

    /// <summary>What the grid shows: the project's symbols, filtered.</summary>
    public ObservableCollection<SymbolRow> Rows { get; } = [];

    [ObservableProperty]
    private SymbolRow? _selected;

    /// <summary>
    /// The kind being shown, or null for all of them.
    /// </summary>
    /// <remarks>
    /// Null is the default and the common case. A filing system is only worth
    /// using once there is too much to look at, and a project with six props
    /// should not open on a filtered view of two of them.
    /// </remarks>
    [ObservableProperty]
    private SymbolKind? _kindFilter;

    /// <summary>Name and tag search. Empty shows everything.</summary>
    [ObservableProperty]
    private string _search = "";

    public IReadOnlyList<SymbolKind?> KindChoices { get; } =
        [null, .. Enum.GetValues<SymbolKind>().Cast<SymbolKind?>()];

    partial void OnKindFilterChanged(SymbolKind? value) => Refresh();

    partial void OnSearchChanged(string value) => Refresh();

    /// <summary>Whether the project has any symbols at all, filter aside.</summary>
    public bool HasAny => _project()?.Symbols.Count > 0;

    /// <summary>What to say when the grid is empty, or null when it is not.</summary>
    /// <remarks>
    /// Two different emptinesses, and telling them apart is the whole value of
    /// the message: a project with no symbols needs to know how to make one, a
    /// filter that matches nothing needs to know it is a filter.
    /// </remarks>
    public string? EmptyMessage => Rows.Count > 0
        ? null
        : HasAny
            ? "No symbol matches that."
            : "No symbols yet. Draw something, then Make symbol.";

    /// <summary>Rebuild the grid from the project.</summary>
    public void Refresh()
    {
        Rows.Clear();
        if (_project() is { } project)
        {
            foreach (var symbol in project.Symbols.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (KindFilter is { } kind && symbol.Kind != kind) continue;
                if (!Matches(symbol, Search)) continue;
                Rows.Add(new SymbolRow(symbol));
            }
        }
        RefreshThumbs();
        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>Name first, then tags — both case-insensitive, both substrings.</summary>
    private static bool Matches(Symbol symbol, string query)
    {
        if (query.Length == 0) return true;
        var q = query.Trim();
        if (q.Length == 0) return true;
        return symbol.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || symbol.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Render the thumbnails that are missing or out of date.
    /// </summary>
    /// <remarks>
    /// Keyed on the symbol's version, so editing one refreshes exactly its own
    /// tile. A symbol is a handful of strokes and the grid holds what a project
    /// has, so this is not the frame cache and does not need to be.
    /// </remarks>
    public void RefreshThumbs()
    {
        var (w, h) = _canvasSize();
        var info = new SKImageInfo(Math.Max(1, w), Math.Max(1, h), SKColorType.Rgba8888, SKAlphaType.Premul);
        foreach (var row in Rows)
        {
            if (row.ThumbVersion == row.Model.Version && row.Thumb is not null) continue;
            row.Thumb = RenderThumb(row.Model, info);
            row.ThumbVersion = row.Model.Version;
        }
    }

    private static Bitmap? RenderThumb(Symbol symbol, SKImageInfo info)
    {
        if (symbol.Frames.Count == 0) return null;
        // Through the ordinary pixel path, at the ordinary place: the tile is a
        // picture of what the symbol will actually look like, not a second
        // renderer's opinion of it.
        using var bitmap = symbol.Frames[0] switch
        {
            PaintedFrame p => FrameRasterizer.Materialize(p, info.Width, info.Height),
            VectorFrame v => FrameRasterizer.Rasterize(v.Strokes, info.Width, info.Height),
            _ => null,
        };
        return bitmap is null ? null : ThumbnailRenderer.RenderChecker(bitmap, 64, 48);
    }
}
