using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The choices behind <c>File ▸ Save as image…</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>All of the decisions and none of the window</b>, for the reason
/// <see cref="ResizeDialogViewModel"/> gives: the interesting cases here are the
/// ones nobody clicks through by hand — a format that quietly cannot keep the
/// transparency the drawing has, a quality slider on a lossless format, an
/// "every frame" option on a document with one frame.
/// </para>
/// <para>
/// <b>The warning is stated before the save, not after.</b> The roadmap item this
/// implements said it in one line: "JPEG needs a quality control and a warning
/// that it has no alpha, or somebody exports a character on a white box and finds
/// out later." <see cref="ImageSaveResult"/> reports what actually happened from
/// the rendered pixels; this predicts it from the scene so the artist can change
/// their mind while the dialog is still open. The two are deliberately different
/// questions — <see cref="MayLoseTransparency"/> is "this format cannot keep
/// alpha and this document looks like it has some", which is cheap and may be
/// wrong; the result's is measured and is not.
/// </para>
/// </remarks>
public sealed partial class SaveImageDialogViewModel : ObservableObject
{
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;

    public SaveImageDialogViewModel(Scene scene)
    {
        _canvasWidth = Math.Max(1, scene.Width);
        _canvasHeight = Math.Max(1, scene.Height);
        FrameCount = Math.Max(1, scene.FrameCount);
        // A document whose paper is transparent, or which has no opaque paper
        // layer, is one where a format without alpha will change the picture.
        LooksTransparent = scene.TransparentBackground || scene.Layers.Exists(l => l.IsBackground);
    }

    /// <summary>Parameterless for the XAML designer only.</summary>
    public SaveImageDialogViewModel() : this(new Scene()) { }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuality))]
    [NotifyPropertyChangedFor(nameof(KeepsTransparency))]
    [NotifyPropertyChangedFor(nameof(MayLoseTransparency))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private ImageSaveFormat _format = ImageSaveFormat.Png;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _quality = 90;

    /// <summary>Output size as a percentage, so 100 is the document's own size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputWidth))]
    [NotifyPropertyChangedFor(nameof(OutputHeight))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private double _scalePercent = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _allFrames;

    /// <summary>What shows through the transparency in a format that has none.</summary>
    [ObservableProperty]
    private string _matte = "#ffffff";

    /// <summary>How many frames the document has; 1 hides the every-frame option.</summary>
    public int FrameCount { get; }

    public bool IsSequence => FrameCount > 1;

    /// <summary>Whether the document plausibly has transparency to lose.</summary>
    public bool LooksTransparent { get; }

    public IReadOnlyList<ImageSaveFormat> Formats => ImageSaveFormats.All;

    public bool HasQuality => ImageSaveFormats.HasQuality(Format);

    public bool KeepsTransparency => ImageSaveFormats.SupportsAlpha(Format);

    /// <summary>Whether to say something about alpha before the save happens.</summary>
    public bool MayLoseTransparency => !KeepsTransparency && LooksTransparent;

    public int OutputWidth => Math.Max(1, (int)Math.Round(_canvasWidth * Scale));

    public int OutputHeight => Math.Max(1, (int)Math.Round(_canvasHeight * Scale));

    private double Scale => Math.Clamp(ScalePercent, 1, 1600) / 100.0;

    /// <summary>
    /// One sentence naming what is about to be written, including the count when
    /// it is more than one file — "3 files" is the part an artist wants to have
    /// read before they pick a folder rather than after.
    /// </summary>
    public string Summary
    {
        get
        {
            var label = ImageSaveFormats.Label(Format);
            var size = $"{OutputWidth}×{OutputHeight}";
            var count = AllFrames && IsSequence ? $"{FrameCount} files" : "one file";
            var quality = HasQuality ? $", quality {Quality}" : "";
            return $"{count}, {label} at {size}{quality}.";
        }
    }

    public ImageSaveOptions ToOptions() => new(
        Format,
        Math.Clamp(Quality, 1, 100),
        Scale,
        AllFrames && IsSequence,
        Matte);

    /// <summary>The extension the chosen format wants, for the file picker.</summary>
    public string Extension => ImageSaveFormats.Extension(Format);
}
