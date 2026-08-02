using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One swatch, as the docker sees it. Wraps the document's <see cref="Swatch"/>
/// rather than copying it, because the whole point of the feature is that the
/// object the engine resolves and the object the artist edits are the same one.
/// </summary>
public sealed partial class SwatchRow : ObservableObject
{
    private readonly Action<SwatchRow, string> _edited;

    /// <param name="edited">Called with the row and the colour it held before the edit.</param>
    public SwatchRow(Swatch model, Action<SwatchRow, string> edited)
    {
        Model = model;
        _edited = edited;
    }

    public Swatch Model { get; }

    public string Id => Model.Id;

    public string Color
    {
        get => Model.Color;
        set
        {
            var hex = HexColor.Normalize(value);
            if (hex is null || hex == Model.Color) return;
            var before = Model.Color;
            Model.Color = hex;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
            OnPropertyChanged(nameof(Label));
            _edited(this, before);
        }
    }

    public string? Name
    {
        get => Model.Name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (name == Model.Name) return;
            Model.Name = name;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
        }
    }

    /// <summary>What the tooltip and the list row show — the name if it has one.</summary>
    public string Label => Model.Name is { Length: > 0 } name ? name : Model.Color;

    public IBrush Fill
    {
        get
        {
            var (r, g, b) = GimpPalette.Rgb(Model.Color);
            return new SolidColorBrush(Avalonia.Media.Color.FromRgb(r, g, b));
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Push a colour set from elsewhere (the picker) without re-entering the setter's guard.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Fill));
        OnPropertyChanged(nameof(Label));
    }
}

/// <summary>
/// The palette docker: the document's palettes, the swatches in the selected
/// one, and the two things an artist does with them — paint with a swatch, and
/// recolour a swatch and watch the art follow.
///
/// State lives here rather than in <see cref="MainViewModel"/> because the
/// docker needs no part of the paint pipeline; it reaches the document through
/// the three callbacks it is handed. Everything that touches pixels or undo
/// stays on the owner's side of that line.
/// </summary>
public sealed partial class PaletteDockerViewModel : ObservableObject
{
    private readonly Action<SwatchRow, string> _swatchRecoloured;
    private readonly Action<Action<Doc>> _edit;
    private readonly Action<string> _paintWith;
    private readonly Func<string> _currentColor;

    private Doc? _doc;
    private bool _loading;

    public PaletteDockerViewModel(
        Action<SwatchRow, string> swatchRecoloured,
        Action<Action<Doc>> edit,
        Action<string> paintWith,
        Func<string> currentColor)
    {
        _swatchRecoloured = swatchRecoloured;
        _edit = edit;
        _paintWith = paintWith;
        _currentColor = currentColor;
    }

    public ObservableCollection<Palette> Palettes { get; } = [];

    public ObservableCollection<SwatchRow> Swatches { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPalette))]
    private Palette? _selectedPalette;

    public bool HasPalette => SelectedPalette is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSwatch))]
    private SwatchRow? _selectedSwatch;

    public bool HasSwatch => SelectedSwatch is not null;

    /// <summary>
    /// When on, the colour picker edits the selected swatch instead of only
    /// setting the paint colour — Toon Boom's colour editor, and the mode in
    /// which dragging the wheel recolours the drawing live. Off by default:
    /// an artist reaching for the wheel means "paint in this colour" far more
    /// often than "repaint everything I have already drawn in it".
    /// </summary>
    [ObservableProperty]
    private bool _editSelectedSwatch;

    /// <summary>
    /// A run of colour edits to one swatch has finished. Dragging the wheel
    /// produces an edit per pointer event; the owner coalesces them into a
    /// single undo step and this is where it closes one off.
    /// </summary>
    public event Action? SwatchEditRunEnded;

    partial void OnEditSelectedSwatchChanged(bool value) => SwatchEditRunEnded?.Invoke();

    /// <summary>Status line for the docker's bottom bar — import/export results, mostly.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    partial void OnSelectedPaletteChanged(Palette? value) => RebuildSwatches();

    partial void OnSelectedSwatchChanged(SwatchRow? oldValue, SwatchRow? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
            // Not while reloading: the rows are rebuilt from the same document
            // and the "change" is the same swatch under a new wrapper.
            if (!_loading) SwatchEditRunEnded?.Invoke();
        }
        if (newValue is null) return;
        newValue.IsSelected = true;
        // Selecting a swatch is how you paint with it. The owner records the
        // link, so the next stroke references the swatch rather than copying
        // the colour out of it.
        if (!_loading) _paintWith(newValue.Id);
    }

    /// <summary>Point the docker at a document. Idempotent; called on every document change.</summary>
    public void Load(Doc doc)
    {
        _doc = doc;
        var keepPalette = SelectedPalette?.Id;
        var keepSwatch = SelectedSwatch?.Id;

        _loading = true;
        try
        {
            // A palette filed under a folder that is no longer there has to
            // appear somewhere, or it colours the art from a place nobody can
            // find in the panel.
            if (doc.PaletteFolders is { } folders) PaletteTree.Prune(folders, doc.Palettes);

            Palettes.Clear();
            foreach (var palette in doc.Palettes) Palettes.Add(palette);
            foreach (var palette in ProjectPalettes) Palettes.Add(palette);
            SelectedPalette = Palettes.FirstOrDefault(p => p.Id == keepPalette) ?? Palettes.FirstOrDefault();
            RebuildSwatches();
            SelectedSwatch = Swatches.FirstOrDefault(s => s.Id == keepSwatch);
            RebuildTree();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildSwatches()
    {
        var keep = SelectedSwatch?.Id;
        Swatches.Clear();
        foreach (var swatch in SelectedPalette?.Swatches ?? []) Swatches.Add(new SwatchRow(swatch, OnSwatchEdited));
        SelectedSwatch = Swatches.FirstOrDefault(s => s.Id == keep);
    }

    private void OnSwatchEdited(SwatchRow row, string before) => _swatchRecoloured(row, before);

    /// <summary>
    /// Called by the owner when the colour picker moves and
    /// <see cref="EditSelectedSwatch"/> is on. Returns true if it consumed the
    /// colour, so the owner knows not to treat it as a plain paint-colour change.
    /// </summary>
    public bool ApplyPickerColor(string hex)
    {
        if (!EditSelectedSwatch || SelectedSwatch is not { } row) return false;
        row.Color = hex;
        return true;
    }

    // ---- the hierarchy --------------------------------------------------------

    /// <summary>
    /// The open project, if there is one.
    /// </summary>
    /// <remarks>
    /// Properties rather than constructor arguments so that a docker built
    /// without a project — which is every test that is not about one, and the
    /// ordinary single-file case — needs no ceremony to say so.
    /// </remarks>
    public Func<Project?>? ProjectSource { get; set; }

    /// <summary>Called when a project palette or folder changed and wants saving.</summary>
    public Action? ProjectEdited { get; set; }

    private Project? Project => ProjectSource?.Invoke();

    private IReadOnlyList<Palette> ProjectPalettes => Project?.Palettes ?? [];

    private IReadOnlyList<PaletteFolder> FoldersOf(PaletteScope scope) => scope == PaletteScope.Project
        ? Project?.PaletteFolders ?? []
        : _doc?.PaletteFolders ?? [];

    private IReadOnlyList<Palette> PalettesOf(PaletteScope scope) => scope == PaletteScope.Project
        ? ProjectPalettes
        : _doc?.Palettes ?? [];

    /// <summary>Which store a palette lives in.</summary>
    public PaletteScope ScopeOf(Palette palette) =>
        ProjectPalettes.Any(p => p.Id == palette.Id) ? PaletteScope.Project : PaletteScope.Document;

    /// <summary>The palette hierarchy, as rows.</summary>
    public ObservableCollection<PaletteNode> Tree { get; } = [];

    /// <summary>Every folder a node could be assigned to, flattened with its path.</summary>
    public ObservableCollection<PaletteAssignTarget> AssignTargets { get; } = [];

    [ObservableProperty]
    private PaletteNode? _selectedNode;

    partial void OnSelectedNodeChanged(PaletteNode? oldValue, PaletteNode? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is null) return;
        newValue.IsSelected = true;
        // Clicking a palette in the tree is how you select it. Clicking a
        // folder is not: a folder has no colours, and clearing the swatch grid
        // to open one would make the tree a worse way to reach a palette than
        // the list it replaced.
        if (newValue.Palette is { } palette) SelectedPalette = palette;
    }

    /// <summary>
    /// Rebuild the tree from the two stores.
    /// </summary>
    /// <remarks>
    /// The scope headings only appear when a project is open. Without one there
    /// is a single set of palettes, and a "Document" heading above all of them
    /// would be a row that says nothing and costs an indent — the camera's rule
    /// again: optional means absent, not present and empty.
    /// </remarks>
    private void RebuildTree()
    {
        // A rename changes no row's place, so rebuilding for one would swap
        // every node for a new object mid-edit: the text box the artist is
        // typing into is gone, and so is which folders were open.
        if (_renaming) return;

        var keep = SelectedNode is { } node
            ? node.Palette?.Id ?? node.Folder?.Id
            : SelectedPalette?.Id;

        Tree.Clear();

        foreach (var scope in Scopes)
        {
            var host = Scopes.Length == 1
                ? null
                : PaletteNode.ForScope(scope, ScopeName(scope));
            if (host is not null) Tree.Add(host);
            Fill(host?.Children ?? Tree, scope, null);
        }

        RebuildAssignTargets();
        SelectedNode = keep is null ? null : Find(Tree, keep);
    }

    private PaletteScope[] Scopes => Project is null
        ? [PaletteScope.Document]
        : [PaletteScope.Document, PaletteScope.Project];

    private static string ScopeName(PaletteScope scope) =>
        scope == PaletteScope.Project ? "Project" : "Document";

    /// <summary>
    /// The "Assign to ▸" entries, rebuilt separately from the rows.
    /// </summary>
    /// <remarks>
    /// Separately because a rename changes no row's place but does change what
    /// the menu should say. Rebuilding the tree for it would swap every node
    /// mid-edit; leaving the menu alone would offer the folder's old name.
    /// </remarks>
    private void RebuildAssignTargets()
    {
        AssignTargets.Clear();
        foreach (var scope in Scopes)
        {
            AssignTargets.Add(new PaletteAssignTarget
            {
                Scope = scope,
                FolderId = null,
                Label = Scopes.Length == 1 ? "(top level)" : $"{ScopeName(scope)} — (top level)",
            });
            foreach (var folder in FoldersOf(scope))
            {
                AssignTargets.Add(new PaletteAssignTarget
                {
                    Scope = scope,
                    FolderId = folder.Id,
                    Label = PaletteTree.PathOf(FoldersOf(scope), folder.Id),
                });
            }
        }
    }

    private void Fill(IList<PaletteNode> into, PaletteScope scope, string? parentId)
    {
        foreach (var folder in PaletteTree.FoldersIn(FoldersOf(scope), parentId))
        {
            var node = PaletteNode.ForFolder(scope, folder, OnNodeRenamed);
            into.Add(node);
            Fill(node.Children, scope, folder.Id);
        }
        foreach (var palette in PaletteTree.PalettesIn(PalettesOf(scope), parentId))
        {
            into.Add(PaletteNode.ForPalette(scope, palette, OnNodeRenamed));
        }
    }

    private static PaletteNode? Find(IEnumerable<PaletteNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Palette?.Id == id || node.Folder?.Id == id) return node;
            if (Find(node.Children, id) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>
    /// Run a change against whichever store the scope names.
    /// </summary>
    /// <remarks>
    /// The document's goes through the undo stack; the project's does not,
    /// because a project spans documents and there is no single history for it
    /// to land in. That asymmetry is real and worth being explicit about rather
    /// than pretending both are undoable.
    /// </remarks>
    private void InScope(PaletteScope scope, Action<Doc> onDocument, Action onProject)
    {
        if (scope == PaletteScope.Project)
        {
            if (Project is null) return;
            onProject();
            ProjectEdited?.Invoke();
            if (_doc is not null) Load(_doc);
            return;
        }
        _edit(onDocument);
    }

    /// <summary>
    /// Change one palette, wherever it lives. Looked up by id inside the edit
    /// rather than captured, because the document the change lands on is the
    /// one the undo stack hands back, not necessarily the object we started
    /// from.
    /// </summary>
    private void EditPalette(Palette palette, Action<Palette> mutate)
    {
        var id = palette.Id;
        InScope(
            ScopeOf(palette),
            d =>
            {
                if (d.Palettes.FirstOrDefault(p => p.Id == id) is { } target) mutate(target);
            },
            () =>
            {
                if (Project!.Palettes.FirstOrDefault(p => p.Id == id) is { } target) mutate(target);
            });
    }

    private bool _renaming;

    private void OnNodeRenamed(PaletteNode node, string name)
    {
        _renaming = true;
        try
        {
            if (node.Folder is { } folder)
            {
                InScope(node.Scope, _ => folder.Name = name, () => folder.Name = name);
            }
            else if (node.Palette is { } palette)
            {
                InScope(node.Scope, _ => palette.Name = name, () => palette.Name = name);
            }
        }
        finally
        {
            _renaming = false;
        }
        // The rows did not move, but the menu now says the wrong name.
        RebuildAssignTargets();
    }

    /// <summary>
    /// A new folder, inside whatever is selected.
    /// </summary>
    /// <remarks>
    /// Inside the selected folder, or beside the selected palette — the two
    /// readings of "here" that a file manager gives, and the ones that mean
    /// the new folder appears where the artist was looking rather than at the
    /// bottom of the panel.
    /// </remarks>
    [RelayCommand]
    private void AddFolder()
    {
        if (_doc is null) return;
        var scope = SelectedNode?.Scope ?? PaletteScope.Document;
        var parent = SelectedNode?.Kind switch
        {
            PaletteNodeKind.Folder => SelectedNode.Folder!.Id,
            PaletteNodeKind.Palette => SelectedNode.Palette!.FolderId,
            _ => null,
        };
        var folder = new PaletteFolder
        {
            Name = $"Folder {FoldersOf(scope).Count + 1}",
            ParentId = parent,
        };
        InScope(
            scope,
            d => (d.PaletteFolders ??= []).Add(folder),
            () => Project!.PaletteFolders.Add(folder));
        SelectedNode = Find(Tree, folder.Id);
    }

    /// <summary>
    /// File a palette or a folder somewhere else. Used by both the drag and the
    /// "Assign to ▸" menu, so the two can never disagree.
    /// </summary>
    public bool Assign(PaletteNode? node, PaletteAssignTarget? target)
    {
        if (node is null || target is null || !CanAssign(node, target)) return false;

        if (node.Folder is { } folder)
        {
            var folders = FoldersOf(node.Scope);
            InScope(
                node.Scope,
                _ => PaletteTree.Reparent(folders, folder.Id, target.FolderId),
                () => PaletteTree.Reparent(folders, folder.Id, target.FolderId));
            return true;
        }
        if (node.Palette is { } palette)
        {
            var to = target.FolderId;
            InScope(node.Scope, _ => palette.FolderId = to, () => palette.FolderId = to);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a move is allowed.
    /// </summary>
    /// <remarks>
    /// Two refusals. A folder cannot go inside itself or its own descendant —
    /// the pair would still reference each other, so nothing reads as corrupt,
    /// and both would simply stop appearing. And nothing crosses between the
    /// document and the project: that is not filing, it is a change of
    /// ownership, and it would re-point or orphan every stroke that references
    /// the palette.
    /// </remarks>
    public bool CanAssign(PaletteNode? node, PaletteAssignTarget? target)
    {
        if (node is null || target is null || node.Kind == PaletteNodeKind.Scope) return false;
        if (node.Scope != target.Scope) return false;
        if (node.Folder is not { } folder) return true;
        return target.FolderId is not { } to
            || !PaletteTree.IsSelfOrDescendant(FoldersOf(node.Scope), to, folder.Id);
    }

    /// <summary>Drop one row onto another. A palette row means "beside it".</summary>
    public bool Drop(PaletteNode? source, PaletteNode? onto)
    {
        if (source is null || onto is null || ReferenceEquals(source, onto)) return false;
        var folderId = onto.Kind switch
        {
            PaletteNodeKind.Folder => onto.Folder!.Id,
            PaletteNodeKind.Palette => onto.Palette!.FolderId,
            _ => null,
        };
        return Assign(source, new PaletteAssignTarget
        {
            Scope = onto.Scope,
            FolderId = folderId,
            Label = "",
        });
    }

    // ---- commands -----------------------------------------------------------

    [RelayCommand]
    private void AddPalette()
    {
        if (_doc is null) return;
        var scope = SelectedNode?.Scope ?? PaletteScope.Document;
        var folderId = SelectedNode?.Kind switch
        {
            PaletteNodeKind.Folder => SelectedNode.Folder!.Id,
            PaletteNodeKind.Palette => SelectedNode.Palette!.FolderId,
            _ => null,
        };
        var palette = new Palette
        {
            Name = $"Palette {PalettesOf(scope).Count + 1}",
            FolderId = folderId,
        };
        InScope(scope, d => d.Palettes.Add(palette), () => Project!.Palettes.Add(palette));
        SelectedPalette = Palettes.FirstOrDefault(p => p.Id == palette.Id);
    }

    [RelayCommand]
    private void RemovePalette()
    {
        // The tree's ✕ removes whatever is selected — a folder is as much a
        // thing you made by mistake as a palette is.
        if (SelectedNode is { Kind: PaletteNodeKind.Folder } node)
        {
            RemoveFolder(node);
            return;
        }
        if (SelectedPalette is not { } palette) return;
        var id = palette.Id;
        InScope(
            ScopeOf(palette),
            d => d.Palettes.RemoveAll(p => p.Id == id),
            () => Project!.Palettes.RemoveAll(p => p.Id == id));
    }

    /// <summary>
    /// Delete a folder. The palettes inside it survive, one level up.
    /// </summary>
    /// <remarks>
    /// Tidying up must not be the fastest way to lose an afternoon's work —
    /// every stroke painted with one of those swatches would go dead with it.
    /// </remarks>
    private void RemoveFolder(PaletteNode node)
    {
        if (node.Folder is not { } folder) return;
        InScope(
            node.Scope,
            d =>
            {
                if (d.PaletteFolders is not { } folders) return;
                PaletteTree.RemoveFolder(folders, d.Palettes, folder.Id);
                // Back to absent once the last one goes, so a document that
                // ends up with no folders writes no folder key again.
                if (folders.Count == 0) d.PaletteFolders = null;
            },
            () => PaletteTree.RemoveFolder(Project!.PaletteFolders, Project!.Palettes, folder.Id));
    }

    /// <summary>Add the colour currently in the picker as a new swatch.</summary>
    [RelayCommand]
    private void AddSwatch()
    {
        if (SelectedPalette is not { } palette) return;
        var swatch = new Swatch { Color = _currentColor() };
        EditPalette(palette, target => target.Swatches.Add(swatch));
        SelectedSwatch = Swatches.FirstOrDefault(s => s.Id == swatch.Id);
    }

    /// <summary>
    /// Add a colour that arrived from somewhere other than the docker — the
    /// wheel's "Add to palette", wherever that wheel happens to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="AddSwatch"/> this does not require a palette to be
    /// selected: with none, it makes one. Refusing here would mean the artist
    /// had to go and find the palette docker, create a palette and come back —
    /// at which point the colour they wanted to keep is whatever the wheel has
    /// moved to since.
    /// </para>
    /// <para>
    /// One undo step covers the palette and the swatch together, because
    /// undoing "add a colour" halfway and leaving an empty palette behind is
    /// not a state anybody asked for.
    /// </para>
    /// <para>
    /// The new swatch is deliberately <i>not</i> selected here. Selecting one
    /// in this docker means "paint with it", and the colour might have come
    /// from the background half of the pair or from a gradient stop — none of
    /// which should change what the brush is loaded with. The caller knows
    /// whose colour it was and does the linking.
    /// </para>
    /// </remarks>
    public Swatch? AddColor(string hex)
    {
        if (_doc is null) return null;
        var swatch = new Swatch { Color = hex };
        if (SelectedPalette is { } selected)
        {
            EditPalette(selected, target => target.Swatches.Add(swatch));
            return swatch;
        }
        // Nothing selected: the document gets a palette, because that is the
        // scope that always exists.
        _edit(d =>
        {
            var palette = new Palette { Name = $"Palette {d.Palettes.Count + 1}" };
            palette.Swatches.Add(swatch);
            d.Palettes.Add(palette);
        });
        return swatch;
    }

    /// <summary>
    /// Remove a swatch from the palette. Art that referenced it keeps the
    /// literal colour recorded on the stroke — the reference goes dead rather
    /// than the drawing going black.
    /// </summary>
    [RelayCommand]
    private void RemoveSwatch()
    {
        if (SelectedPalette is not { } palette || SelectedSwatch is not { } row) return;
        var swatchId = row.Id;
        EditPalette(palette, target => target.Swatches.RemoveAll(s => s.Id == swatchId));
    }

    public void ImportGpl(string path)
    {
        if (_doc is null) return;
        try
        {
            var palette = GimpPalette.Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            // Filed where the artist was looking, the same as a new palette.
            // An import that always lands at the top level is an import you
            // then have to go and put away.
            if (SelectedNode is { Scope: PaletteScope.Document } node)
            {
                palette.FolderId = node.Folder?.Id ?? node.Palette?.FolderId;
            }
            _edit(d => d.Palettes.Add(palette));
            SelectedPalette = Palettes.FirstOrDefault(p => p.Id == palette.Id);
            Status = $"Imported {palette.Swatches.Count} swatches from {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    public void ExportGpl(string path)
    {
        if (SelectedPalette is not { } palette) return;
        try
        {
            File.WriteAllText(path, GimpPalette.Write(palette));
            Status = $"Wrote {palette.Swatches.Count} swatches to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not write {Path.GetFileName(path)}: {ex.Message}";
        }
    }
}
