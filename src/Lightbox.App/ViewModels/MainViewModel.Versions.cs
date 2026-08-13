using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Core.Versioning;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Version history for project work: what the active tab can version, the
/// authored "Save version…" itself, and putting a tab back together after a
/// revert. The mechanism is Core's (<see cref="ProjectVersions"/>); this is
/// the part that knows about tabs and editors.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// What the active tab would version — a project document or a project
    /// character sheet — or null when there is nothing versionable: no
    /// project, a loose file, a symbol. Loose files have no
    /// <c>versions/</c> folder to write into; saving into a project is what
    /// grants a history, and the menu item's tooltip says so.
    /// </summary>
    public (string Id, string RelativePath, string Name)? VersionableResource
    {
        get
        {
            if (!ProjectDocker.HasProject) return null;
            // A sheet-view tab deliberately reports no SaveTargetTab (the
            // project's save writes it), so the sheet is read off ActiveTab.
            if (ActiveTab?.SheetSource is { } sheet) return (sheet.Id, sheet.Path, sheet.Name);
            if (SaveTargetTab?.Source is { } document) return (document.Id, document.Path, document.Name);
            return null;
        }
    }

    public bool CanSaveVersion => VersionableResource is not null;

    /// <summary>
    /// Keep a version of the active tab's document or sheet: save the project
    /// (what is on disk is what is versioned), then copy the file into the
    /// history under the artist's label.
    /// </summary>
    public VersionEntry? SaveVersionOfActiveTab(string label, string? notes = null)
    {
        if (ProjectDocker.Project is not { } project || VersionableResource is not { } resource)
        {
            AiStatus = "Versions live in a project — save this document into one first.";
            return null;
        }
        try
        {
            SaveProject();
            var entry = ProjectVersions.SaveVersion(
                project, resource.Id, resource.RelativePath, label, notes);
            AiStatus = $"Version {entry.VersionNumber} of “{resource.Name}” kept: {label}.";
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AiStatus = $"Could not keep a version: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// The history of the active tab's resource, wired so a revert saves the
    /// project first (the safety copy must capture what the artist sees, not
    /// a stale file) and reloads whatever is open afterwards.
    /// </summary>
    public VersionHistoryViewModel? HistoryForActiveTab()
    {
        if (ProjectDocker.Project is not { } project || VersionableResource is not { } resource)
        {
            AiStatus = "Versions live in a project — save this document into one first.";
            return null;
        }
        return new VersionHistoryViewModel(
            project, resource.Id, resource.RelativePath, resource.Name,
            beforeRevert: () => SaveProject(),
            reverted: () => ReloadVersionedResource(resource.Id));
    }

    /// <summary>History for a project-window row, same wiring.</summary>
    public VersionHistoryViewModel HistoryFor(string resourceId, string relativePath, string name)
    {
        var project = ProjectDocker.Project!;
        return new VersionHistoryViewModel(
            project, resourceId, relativePath, name,
            beforeRevert: () => SaveProject(),
            reverted: () => ReloadVersionedResource(resourceId));
    }

    /// <summary>
    /// After a revert changed a file on disk, make anything showing it show
    /// the disk. An open document reloads in place, keeping its tab; a sheet's
    /// cached load is dropped and its view tabs closed, so the next open reads
    /// the reverted file.
    /// </summary>
    private void ReloadVersionedResource(string resourceId)
    {
        if (ProjectDocker.Project is not { } project) return;

        if (Tabs.FirstOrDefault(t => t.Source?.Id == resourceId) is { } tab)
        {
            var reference = tab.Source!;
            var doc = DocJson.Load(project.PathOf(reference));
            tab.Editor = new DocumentEditor(doc) { MaxUndo = tab.Editor.MaxUndo };
            tab.State.LayerIndex = FirstPaintableLayer(doc);
            // A different record in the same slot — the framing and playhead
            // of the replaced one are not this one's (B67's reasoning).
            tab.State.View = null;
            tab.MarkSaved();
            if (tab == ActiveTab)
            {
                AttachEditor(tab.Editor);
                ActiveLayerIndex = FirstPaintableLayer(doc);
                CurrentFrameIndex = 0;
                TabSwitched?.Invoke(null, tab);
            }
            return;
        }

        if (project.LoadedSheets.Remove(resourceId))
        {
            // Closing rather than reloading in place: a sheet view's editor
            // wraps a live ReferenceView inside the cached sheet object, and
            // rebinding every open view to the reloaded sheet is B98's
            // registration dance backwards. The tabs were saved a moment ago
            // by the revert's safety pass, so nothing is lost.
            foreach (var open in Tabs.Where(t => t.SheetSource?.Id == resourceId).ToList())
                CloseTab(open);
            OnPropertyChanged(nameof(ReferenceSheetsView));
        }
    }
}
