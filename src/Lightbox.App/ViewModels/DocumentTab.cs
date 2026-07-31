using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>Settings collected by the File → New dialog.</summary>
public sealed record NewDocumentSettings(
    string Name,
    int Width,
    int Height,
    int Fps,
    int Ppi,
    string BackgroundColor,
    bool TransparentBackground);

/// <summary>
/// One open document: its editor (which owns the undo history, so switching
/// tabs never loses it), where it was saved, and whether it has unsaved
/// changes (shown as a • in the tab strip).
/// </summary>
public sealed partial class DocumentTab : ObservableObject
{
    internal DocumentTab(DocumentEditor editor, string title)
    {
        Editor = editor;
        _title = title;
    }

    internal DocumentEditor Editor { get; set; }

    public Doc Doc => Editor.Doc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isActive;

    public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

    public string? FilePath { get; set; }

    /// <summary>Playhead/layer selection remembered while another tab is active.</summary>
    internal int SavedFrameIndex;

    internal int SavedLayerIndex;
}
