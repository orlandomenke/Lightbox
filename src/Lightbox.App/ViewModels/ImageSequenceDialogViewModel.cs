using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The choices behind <c>File ▸ Export image sequence…</c> (Q220).
/// </summary>
/// <remarks>
/// <para>
/// <b>All of the decisions and none of the window</b>, as
/// <see cref="SaveImageDialogViewModel"/> is and for its reason: the cases
/// worth checking are the ones nobody clicks through by hand.
/// </para>
/// <para>
/// <b>Frame numbers here count from one</b>, the way the timeline shows them
/// and the way the files are numbered. <see cref="ToSettings"/> is the only
/// place they become the zero-based indices the exporter counts in.
/// </para>
/// </remarks>
public sealed partial class ImageSequenceDialogViewModel : ObservableObject
{
    private readonly Scene _scene;
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private bool _applyingTag;

    public ImageSequenceDialogViewModel(Scene scene)
    {
        _scene = scene;
        // What the camera saw when there is one, the canvas otherwise — the
        // same answer the exporter gives, asked of the same function.
        (_outputWidth, _outputHeight) = SequenceExporter.OutputSize(scene);
        LastFrame = Math.Max(1, scene.FrameCount);
        _toFrame = LastFrame;
        Tags = scene.Tags is { Count: > 0 } tags ? tags.ToArray() : [];
        LooksTransparent = scene.TransparentBackground || scene.Layers.Exists(l => l.IsBackground);
    }

    /// <summary>Parameterless for the XAML designer only.</summary>
    public ImageSequenceDialogViewModel() : this(new Scene()) { }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuality))]
    [NotifyPropertyChangedFor(nameof(OffersTransparentPaper))]
    [NotifyPropertyChangedFor(nameof(HasMatte))]
    [NotifyPropertyChangedFor(nameof(MayLoseTransparency))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private ImageSaveFormat _format = ImageSaveFormat.Png;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _quality = 90;

    /// <summary>Output size as a percentage, so 100 is the document's own size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private double _scalePercent = 100;

    /// <summary>First frame written, counted from one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _fromFrame = 1;

    /// <summary>Last frame written, counted from one, inclusive.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _toFrame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _step = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _uniqueOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartNumber))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _timelineNumbers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _prefix = ImageSequenceSettings.DefaultPrefix;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _digits = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _startNumber = 1;

    [ObservableProperty]
    private bool _transparentPaper;

    /// <summary>What shows through the transparency in a format that has none.</summary>
    [ObservableProperty]
    private string _matte = "#ffffff";

    /// <summary>
    /// The tag the range was taken from, or null once the range has been typed.
    /// </summary>
    [ObservableProperty]
    private AnimationTag? _selectedTag;

    /// <summary>The timeline's last frame, counted from one.</summary>
    public int LastFrame { get; }

    public IReadOnlyList<AnimationTag> Tags { get; }

    /// <summary>A document with no tags shows no picker rather than an empty one.</summary>
    public bool HasTags => Tags.Count > 0;

    public IReadOnlyList<ImageSaveFormat> Formats => ImageSaveFormats.All;

    public bool HasQuality => ImageSaveFormats.HasQuality(Format);

    /// <summary>
    /// False while the timeline does the numbering: a start number and the
    /// timeline's own numbers answer the same question, and only one can win.
    /// </summary>
    public bool HasStartNumber => !TimelineNumbers;

    /// <summary>Leaving the paper out needs somewhere to put the transparency.</summary>
    public bool OffersTransparentPaper => ImageSaveFormats.SupportsAlpha(Format);

    public bool HasMatte => !ImageSaveFormats.SupportsAlpha(Format);

    /// <summary>Whether the document plausibly has transparency to lose.</summary>
    public bool LooksTransparent { get; }

    /// <summary>Whether to say something about alpha before the export happens.</summary>
    public bool MayLoseTransparency => HasMatte && LooksTransparent;

    private double Scale => Math.Clamp(ScalePercent, 1, 1600) / 100.0;

    public int OutputWidth => Math.Max(1, (int)Math.Round(_outputWidth * Scale));

    public int OutputHeight => Math.Max(1, (int)Math.Round(_outputHeight * Scale));

    partial void OnSelectedTagChanged(AnimationTag? value)
    {
        if (value is null) return;
        _applyingTag = true;
        try
        {
            FromFrame = Math.Clamp(value.Start + 1, 1, LastFrame);
            ToFrame = Math.Clamp(value.End + 1, FromFrame, LastFrame);
        }
        finally
        {
            _applyingTag = false;
        }
    }

    // A range typed over a tag's is no longer that tag's, and a picker still
    // saying "walk" above it would be a label on the wrong thing.
    partial void OnFromFrameChanged(int value)
    {
        if (!_applyingTag) SelectedTag = null;
    }

    partial void OnToFrameChanged(int value)
    {
        if (!_applyingTag) SelectedTag = null;
    }

    /// <summary>
    /// One sentence naming what is about to be written: how many files, what
    /// they are, and what the first one will be called.
    /// </summary>
    /// <remarks>
    /// With unique frames the count is a ceiling and says so — which pictures
    /// repeat is only known once they have been rendered, and a number that
    /// turned out wrong would be worse than an honest "up to".
    /// </remarks>
    public string Summary
    {
        get
        {
            var settings = ToSettings();
            var frames = settings.Frames(_scene);
            var files = frames.Count == 1 ? "1 file" : $"{frames.Count} files";
            var count = UniqueOnly ? $"Writes up to {files} and a timing file" : $"Writes {files}";
            var quality = HasQuality ? $", quality {Quality}" : "";
            if (frames.Count == 0) return "This document has no frames to export.";
            return $"{count}, {ImageSaveFormats.Label(Format)} at {OutputWidth}×{OutputHeight}{quality}. "
                + $"The first is {settings.FileName(0, frames[0])}.";
        }
    }

    public ImageSequenceSettings ToSettings() => new(
        Format,
        Math.Clamp(Quality, 1, 100),
        Scale,
        FromFrame - 1,
        ToFrame - 1,
        Math.Max(1, Step),
        UniqueOnly,
        TimelineNumbers,
        Prefix,
        Digits,
        StartNumber,
        TransparentPaper && OffersTransparentPaper,
        Matte);
}
