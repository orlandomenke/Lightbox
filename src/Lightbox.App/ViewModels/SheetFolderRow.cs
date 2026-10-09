using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// A folder's row on the Timeline and the X-sheet (Q227): its name, whether it
/// is folded there, and one cell per frame marking where anything inside it —
/// at any depth — has a drawing.
/// </summary>
/// <remarks>
/// <para>
/// Not <see cref="GroupRow"/>, the Layers docker's header for the same folder,
/// because the two fold separately: the owner's case is a character kept as
/// sketch, line and colour layers that stay open in the Layers docker, where
/// they are worked on, while the sheet shows the one row.
/// </para>
/// <para>
/// The cells are a summary and belong to no layer, so their
/// <see cref="FrameCell.LayerIndex"/> is -1 and no cel verb can be aimed at one.
/// </para>
/// </remarks>
public sealed partial class SheetFolderRow : ObservableObject
{
    private readonly MainViewModel _owner;

    public SheetFolderRow(MainViewModel owner, LayerGroup group)
    {
        _owner = owner;
        Group = group;
    }

    /// <summary>The folder this row stands for. Settable for <see cref="GroupRow.Group"/>'s reason.</summary>
    public LayerGroup Group { get; internal set; }

    [ObservableProperty]
    private string _name = "";

    /// <summary>Folded on the sheet. The Layers docker's own state is <see cref="LayerGroup.Collapsed"/>.</summary>
    [ObservableProperty]
    private bool _collapsed;

    /// <summary>Pinned to the sheet: with Pinned only on, the folder and everything inside keep their rows.</summary>
    [ObservableProperty]
    private bool _pinned;

    /// <summary>Set while the row is being brought in line with the folder, so that is not mistaken for a click.</summary>
    internal bool Syncing { get; set; }

    partial void OnPinnedChanged(bool value)
    {
        if (!Syncing) _owner.SetSheetPinned(Group, value);
    }

    /// <summary>How many folders this one is inside.</summary>
    [ObservableProperty]
    private int _depth;

    /// <summary>The colour the folder shows, its own or inherited (Q226).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TintBrush))]
    private string _color = LayerGroup.DefaultColor;

    /// <summary>The folder's colour as a wash over the row, as its header has in the Layers docker.</summary>
    public Avalonia.Media.IBrush? TintBrush => LayerRow.Wash(Color, GroupRow.HeaderWashAlpha);

    /// <summary>One per frame of the sheet: keyed where anything inside the folder is drawn.</summary>
    public ObservableCollection<FrameCell> Cells { get; } = [];

    [RelayCommand]
    private void ToggleFold() => _owner.ToggleSheetFold(this);
}
