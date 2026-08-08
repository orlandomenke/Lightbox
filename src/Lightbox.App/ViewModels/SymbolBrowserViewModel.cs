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
    public SymbolRow(Symbol model, SymbolScope scope = SymbolScope.Project)
    {
        Model = model;
        Scope = scope;
        _name = model.Name;
    }

    public Symbol Model { get; }

    /// <summary>Which library this tile came from.</summary>
    /// <remarks>
    /// Shown on the tile because it changes what placing it does: a project
    /// symbol is placed, a global one is <em>copied into the project</em> first.
    /// An artist who cannot see the difference cannot predict either.
    /// </remarks>
    public SymbolScope Scope { get; }

    public bool IsGlobal => Scope == SymbolScope.Global;

    /// <summary>A badge, so the two scopes are told apart without reading a menu.</summary>
    public string ScopeBadge => IsGlobal ? "◈" : "";

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

    private readonly Func<IReadOnlyDictionary<string, Symbol>> _library;

    private readonly Func<DocumentRef?> _inView;

    /// <param name="inView">
    /// The document the grid is showing symbols <em>for</em>, so a scoped
    /// project can narrow the list to what that document may place. Optional,
    /// because a project that scopes no symbols — every project until somebody
    /// says otherwise — needs no answer from it.
    /// </param>
    public SymbolBrowserViewModel(
        Func<Project?> project,
        Func<(int, int)> canvasSize,
        Func<IReadOnlyDictionary<string, Symbol>>? library = null,
        Func<DocumentRef?>? inView = null)
    {
        _project = project;
        _canvasSize = canvasSize;
        _inView = inView ?? (() => null);
        // Injected so the grid can be tested without a settings file, and so the
        // view model never learns where the library lives.
        _library = library ?? (() => new Dictionary<string, Symbol>());
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

    /// <summary>Which library to show, or null for both.</summary>
    /// <remarks>
    /// Null by default and for the same reason the kind filter is: an artist with
    /// four symbols should not open on a filtered view of two. It becomes useful
    /// once a personal library has grown past what fits on screen.
    /// </remarks>
    [ObservableProperty]
    private SymbolScope? _scopeFilter;

    public IReadOnlyList<SymbolScope?> ScopeChoices { get; } =
        [null, SymbolScope.Project, SymbolScope.Global];

    partial void OnScopeFilterChanged(SymbolScope? value) => Refresh();

    partial void OnKindFilterChanged(SymbolKind? value) => Refresh();

    partial void OnSearchChanged(string value) => Refresh();

    /// <summary>Whether the project has any symbols at all, filter aside.</summary>
    public bool HasAny => _project()?.Symbols.Count > 0 || _library().Count > 0;

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
            : _project() is null
                // Without a project the only thing here can be the artist's own
                // library, so "Make symbol" is not the advice — it needs a
                // project to make one *in*, and saying so is better than an
                // instruction that will be refused.
                ? "No symbols in your library yet. Open a project to make one, then Promote it."
                : "No symbols yet. Draw something, then Make symbol.";

    /// <summary>Rebuild the grid from the project and the artist's own library.</summary>
    /// <remarks>
    /// <para>
    /// Both scopes in one grid rather than two panels, because the artist's
    /// question is "which sword do I want", not "which folder is it in" — the
    /// filter is there for when it becomes the second question.
    /// </para>
    /// <para>
    /// <b>A library symbol already adopted appears once, as a project symbol.</b>
    /// It is the same id and the same drawing, and showing it twice would ask the
    /// artist to choose between a thing and itself. The project's copy wins
    /// because it is the one a placement resolves to.
    /// </para>
    /// </remarks>
    public void Refresh()
    {
        Rows.Clear();
        var project = _project();
        var mine = project?.Symbols;

        // Q30 step 5. Null when the project scopes no symbols, which is every
        // project that has not asked — and the reason this is a narrowing rather
        // than a widening, so the null has to mean "everything" rather than
        // "nothing declared, so nothing shown".
        var visible = project is null
            ? null
            : SymbolScopes.VisibleTo(project.Manifest, _inView());

        var candidates = new List<(Symbol Symbol, SymbolScope Scope)>();
        if (mine is not null)
        {
            foreach (var symbol in mine.Values)
            {
                // The project's own symbols are what a scope governs. The global
                // library is the artist's, always theirs to reach for, and
                // placing one adopts it into the project — where it can then be
                // declared like anything else.
                if (visible is not null && !visible.Contains(symbol.Id)) continue;
                candidates.Add((symbol, SymbolScope.Project));
            }
        }
        foreach (var (id, symbol) in _library())
        {
            if (mine?.ContainsKey(id) == true) continue;
            candidates.Add((symbol, SymbolScope.Global));
        }

        foreach (var (symbol, scope) in candidates.OrderBy(c => c.Symbol.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (ScopeFilter is { } want && scope != want) continue;
            if (KindFilter is { } kind && symbol.Kind != kind) continue;
            if (!Matches(symbol, Search)) continue;
            Rows.Add(new SymbolRow(symbol, scope));
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
        using var bitmap = FrameRasterizer.Materialize(symbol.Frames[0], info.Width, info.Height);
        return bitmap is null ? null : ThumbnailRenderer.RenderChecker(bitmap, 64, 48);
    }
}
