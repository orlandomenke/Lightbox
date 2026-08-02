using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Which set of palettes a node belongs to.
/// </summary>
/// <remarks>
/// The two do not mix. A document palette travels with the file and a project
/// palette is shared by every animation in the project, so dragging one into
/// the other is not a filing move — it is a change of ownership that would
/// silently re-point, or orphan, every stroke that references it.
/// </remarks>
public enum PaletteScope
{
    Document,
    Project,
}

public enum PaletteNodeKind
{
    /// <summary>A "Document" or "Project" heading. Only appears when both exist.</summary>
    Scope,
    Folder,
    Palette,
}

/// <summary>
/// One row of the palette hierarchy: a scope heading, a folder, or a palette.
/// </summary>
/// <remarks>
/// A view of the model rather than a copy of it. The <see cref="Folder"/> and
/// <see cref="Palette"/> here are the document's own objects, so a rename in
/// the tree is the rename — the same reason <see cref="SwatchRow"/> wraps a
/// <see cref="Swatch"/> instead of holding its colour.
/// </remarks>
public sealed partial class PaletteNode : ObservableObject
{
    private readonly Action<PaletteNode, string>? _renamed;

    private PaletteNode(PaletteScope scope, PaletteNodeKind kind, Action<PaletteNode, string>? renamed)
    {
        Scope = scope;
        Kind = kind;
        _renamed = renamed;
    }

    public static PaletteNode ForScope(PaletteScope scope, string name) =>
        new(scope, PaletteNodeKind.Scope, null) { _name = name, IsExpanded = true };

    public static PaletteNode ForFolder(
        PaletteScope scope, PaletteFolder folder, Action<PaletteNode, string> renamed) =>
        new(scope, PaletteNodeKind.Folder, renamed) { Folder = folder, _name = folder.Name };

    public static PaletteNode ForPalette(
        PaletteScope scope, Palette palette, Action<PaletteNode, string> renamed) =>
        new(scope, PaletteNodeKind.Palette, renamed) { Palette = palette, _name = palette.Name };

    public PaletteScope Scope { get; }

    public PaletteNodeKind Kind { get; }

    public PaletteFolder? Folder { get; private init; }

    public Palette? Palette { get; private init; }

    public ObservableCollection<PaletteNode> Children { get; } = [];

    /// <summary>The folder this node's contents live in — null at the top level.</summary>
    public string? FolderId => Kind == PaletteNodeKind.Folder ? Folder!.Id : null;

    public bool IsFolder => Kind == PaletteNodeKind.Folder;

    public bool IsPalette => Kind == PaletteNodeKind.Palette;

    /// <summary>Whether this row can hold others — a folder, or a scope heading.</summary>
    public bool AcceptsDrop => Kind != PaletteNodeKind.Palette;

    /// <summary>Whether the row can be picked up. Scope headings cannot.</summary>
    public bool IsDraggable => Kind != PaletteNodeKind.Scope;

    public string Glyph => Kind switch
    {
        PaletteNodeKind.Scope => "",
        PaletteNodeKind.Folder => "🗀",
        _ => "▤",
    };

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isRenaming;

    private string _name = "";

    /// <summary>
    /// The name, written straight through to the model.
    /// </summary>
    /// <remarks>
    /// A blank name snaps back rather than being recorded: a row you cannot
    /// read is a row you cannot find again, and there is no undo step to
    /// regret it with.
    /// </remarks>
    public string Name
    {
        get => _name;
        set
        {
            var name = value?.Trim() ?? "";
            if (name.Length == 0 || name == _name)
            {
                OnPropertyChanged();
                return;
            }
            _name = name;
            OnPropertyChanged();
            _renamed?.Invoke(this, name);
        }
    }

    /// <summary>Set the name without reporting an edit — the model changed elsewhere.</summary>
    internal void Adopt(string name)
    {
        _name = name;
        OnPropertyChanged(nameof(Name));
    }
}

/// <summary>
/// One entry in the "Assign to ▸" menu: a folder, or the top level.
/// </summary>
/// <remarks>
/// Flattened with its full path rather than nested, because a nested menu of
/// folders and a tree of folders side by side are two ways to say the same
/// thing and the artist has to work out which one they are in. The path —
/// "Characters / Knight" — is also what tells two folders with the same name
/// apart, which a nested menu cannot.
/// </remarks>
public sealed class PaletteAssignTarget
{
    public required PaletteScope Scope { get; init; }

    /// <summary>Null means the top level of that scope.</summary>
    public required string? FolderId { get; init; }

    public required string Label { get; init; }

    public override string ToString() => Label;
}

/// <summary>
/// One palette a colour can be added to, labelled with where it lives.
/// </summary>
/// <remarks>
/// The label carries the folder path — "Characters / Knight / Armour" — for
/// the same reason <see cref="PaletteAssignTarget"/> does: it is what tells
/// two palettes with the same name apart, and a flat list of bare names in a
/// document that has been filed is a list of guesses.
/// </remarks>
public sealed class PaletteTarget
{
    public required string Id { get; init; }

    public required PaletteScope Scope { get; init; }

    public required string Label { get; init; }

    public override string ToString() => Label;
}

/// <summary>
/// Why a colour is being added to a palette, and therefore what a duplicate
/// means.
/// </summary>
/// <remarks>
/// A colour arriving twice is almost always a slip — the wheel moved a little
/// and came back, or the same swatch was picked and re-added — and a palette
/// full of near-identical entries is a palette nobody can use. But not every
/// path means the same thing, so the caller says which it is rather than the
/// docker guessing from the colour alone.
/// </remarks>
public enum PaletteAddIntent
{
    /// <summary>
    /// Refuse a duplicate and say so. The default, and what an anonymous
    /// picker — a gradient stop, a brush's secondary colour — gets.
    /// </summary>
    Deduplicate,

    /// <summary>
    /// Add it anyway. The wheel inside the palette docker: somebody working in
    /// the palette who asks for a second copy of a colour wants one, usually
    /// to file the two under different folders.
    /// </summary>
    Allow,

    /// <summary>
    /// Select the existing swatch instead of making another. The foreground
    /// and background pair: the point of adding is to start painting with a
    /// live colour, and the swatch that is already there does that.
    /// </summary>
    Adopt,
}

/// <summary>A picker asking for its colour to be kept.</summary>
public sealed record PaletteAddRequest(string Hex, string? PaletteId, PaletteAddIntent Intent);

/// <summary>
/// What came of it.
/// </summary>
/// <param name="Swatch">
/// The swatch to link to, new or existing, or null when nothing was added.
/// </param>
/// <param name="Existing">True when this is a swatch that was already there.</param>
/// <param name="Message">What to tell the artist, or empty when there is nothing to say.</param>
public sealed record PaletteAddOutcome(Swatch? Swatch, bool Existing, string Message)
{
    public static readonly PaletteAddOutcome Nothing = new(null, false, "");
}
