using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>One state the document can stand at, as the History docker shows it.</summary>
/// <remarks>
/// Top-level for the reason <c>TemplatePullRow</c> records: a nested type
/// cannot be a template's <c>x:DataType</c>.
/// </remarks>
public sealed record UndoHistoryRow(long Revision, string Label, bool IsCurrent, bool IsAhead)
{
    /// <summary>Ahead-of-current (undone) rows dim, the way every history panel says "not yet".</summary>
    public double Opacity => IsAhead ? 0.45 : 1.0;
}

/// <summary>
/// The undo record as a timeline: every named step of the active document,
/// with the current state marked, and click-to-jump. A view over
/// <see cref="DocumentEditor.History"/> — it holds no state of its own beyond
/// the rows, which is what lets a tab switch swap the whole thing by
/// re-attaching.
/// </summary>
/// <remarks>
/// The jump goes through the host delegate rather than straight to the
/// editor, because walking states invalidates caches and republishes the
/// canvas — <c>MainViewModel</c> owns that, and the panel knowing about tile
/// caches would be the coupling the docking system exists to avoid.
/// </remarks>
public sealed partial class UndoHistoryViewModel : ObservableObject
{
    private readonly Action<long> _jump;
    private DocumentEditor? _editor;

    public UndoHistoryViewModel(Action<long> jump)
    {
        _jump = jump;
    }

    public ObservableCollection<UndoHistoryRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Follow a different document's editor — the tab switch. Detaches from
    /// the previous one so a closed document's editor is not kept alive by an
    /// event subscription.
    /// </summary>
    public void Attach(DocumentEditor editor)
    {
        if (_editor is not null) _editor.Changed -= Rebuild;
        _editor = editor;
        _editor.Changed += Rebuild;
        Rebuild();
    }

    /// <summary>
    /// Re-read the rows. Runs on every document change, and stays cheap on
    /// purpose: at most MaxUndo + 1 records of strings — no document walk, no
    /// pixels (invariant 6 is about repaint work, and this does none).
    /// </summary>
    private void Rebuild()
    {
        Rows.Clear();
        if (_editor is null) { OnPropertyChanged(nameof(IsEmpty)); return; }

        var current = _editor.Revision;
        // The state before every edit. Only offered while it is reachable —
        // once MaxUndo trims, revision zero is a promise undo cannot keep.
        if (!_editor.HistoryTrimmed)
            Rows.Add(new UndoHistoryRow(0, "As opened", IsCurrent: current == 0, IsAhead: false));

        foreach (var entry in _editor.History)
        {
            Rows.Add(new UndoHistoryRow(
                entry.Revision, entry.Label,
                IsCurrent: entry.Revision == current,
                IsAhead: entry.IsUndone));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Click a row: stand the document at that state.</summary>
    [RelayCommand]
    public void Jump(UndoHistoryRow? row)
    {
        if (row is null || row.IsCurrent || _editor is null) return;
        _jump(row.Revision);
    }
}
