using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Projects;
using Lightbox.Core.Versioning;

namespace Lightbox.App.ViewModels;

/// <summary>A history line the window can show.</summary>
/// <remarks>
/// <para>
/// <see cref="HasContent"/> is the split between the two kinds of entry: an
/// authored or milestone version keeps bytes and can be reverted to; an audit
/// line ("Reverted to …") records an act and cannot. Both belong in the list —
/// the history is the story of the file, not a menu of files.
/// </para>
/// <para>
/// Top-level rather than nested in the view model for the reason
/// <c>TemplatePullRow</c> records: a nested type cannot be a template's
/// <c>x:DataType</c>.
/// </para>
/// </remarks>
public sealed record VersionRow(VersionEntry Entry, bool HasContent, bool IsActive)
{
    public string Number => $"v{Entry.VersionNumber}";

    public string Label => Entry.Label;

    public string? Notes => Entry.Notes;

    public bool HasNotes => Entry.Notes is { Length: > 0 };

    public string When => Entry.CreatedAt.ToLocalTime().ToString("d MMM yyyy HH:mm");

    public string? Milestone => Entry.MilestoneStatus is { } s ? AssetStatuses.Label(s) : null;

    public bool HasMilestone => Entry.MilestoneStatus is not null;

    /// <summary>
    /// The status board's colour for this milestone, so the badge here and the
    /// column there read as the same fact. Bound rather than written in the
    /// XAML — <c>NoViewInventsItsOwnChromeColour</c> is the guard that sent it
    /// here, and the board's colours are Core data, not chrome.
    /// </summary>
    public string MilestoneColor =>
        Entry.MilestoneStatus is { } s ? AssetStatuses.Color(s) : "#00000000";

    public bool CanRevert => HasContent;

    /// <summary>Greys the record-only lines so the eye reads them as history, not state.</summary>
    public double Opacity => HasContent ? 1.0 : 0.55;
}

/// <summary>
/// One resource's version history, newest first, with revert. Owns no window
/// machinery so a test can drive it dry — the host supplies what happens
/// around a revert (save first, reload after) as delegates, the same seam
/// <see cref="ProjectWindowViewModel"/> uses for its owner.
/// </summary>
public sealed partial class VersionHistoryViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly string _resourceId;
    private readonly string _relativePath;
    private readonly Action? _beforeRevert;
    private readonly Action? _reverted;

    public VersionHistoryViewModel(
        Project project, string resourceId, string relativePath, string name,
        Action? beforeRevert = null, Action? reverted = null)
    {
        _project = project;
        _resourceId = resourceId;
        _relativePath = relativePath;
        _beforeRevert = beforeRevert;
        _reverted = reverted;
        Title = $"{name} — versions";
        Rebuild();
    }

    public string Title { get; }

    public ObservableCollection<VersionRow> Rows { get; } = [];

    public bool HasVersions => Rows.Count > 0;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private VersionRow? _selected;

    private void Rebuild()
    {
        Rows.Clear();
        var store = ProjectVersions.StoreFor(_project);
        var active = store.GetActiveVersion(_resourceId);
        foreach (var entry in store.GetVersions(_resourceId).OrderByDescending(e => e.VersionNumber))
        {
            Rows.Add(new VersionRow(
                entry,
                HasContent: ProjectVersions.ContentPathOf(_project, _resourceId, entry.Id) is not null,
                IsActive: entry.Id == active));
        }
        OnPropertyChanged(nameof(HasVersions));
        Status = Rows.Count == 0
            ? "No versions yet. File ▸ Save version… keeps one."
            : $"{Rows.Count} version{(Rows.Count == 1 ? "" : "s")}. Reverting keeps the current state as a version first.";
    }

    /// <summary>
    /// Put this version's bytes back. Never destructive: the host saves first,
    /// the revert itself versions the current state, and the audit entry
    /// records the act.
    /// </summary>
    [RelayCommand]
    public void Revert(VersionRow? row)
    {
        if (row is not { CanRevert: true }) return;
        try
        {
            _beforeRevert?.Invoke();
            ProjectVersions.Revert(_project, _resourceId, row.Entry.Id, _relativePath);
            _reverted?.Invoke();
            Rebuild();
            Status = $"Reverted to {row.Label}. What was there is kept as “Before revert to {row.Label}”.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Could not revert: {ex.Message}";
        }
    }
}
