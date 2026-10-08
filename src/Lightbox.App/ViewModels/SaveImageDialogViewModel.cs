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
/// transparency the drawing has, a quality slider on a lossless format.
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
        // A document whose paper is transparent, or which has no opaque paper
        // layer, is one where a format without alpha will change the picture.
        LooksTransparent = scene.TransparentBackground || scene.Layers.Exists(l => l.IsBackground);
    }

    /// <param name="scene">The document's scene.</param>
    /// <param name="svgSurvey">
    /// How to find out what an SVG save would do with each layer. A function
    /// rather than a value because the answer costs a render of every layer,
    /// and a PNG save should not pay for a question it never asks.
    /// </param>
    public SaveImageDialogViewModel(Scene scene, Func<SvgReport>? svgSurvey) : this(scene) =>
        _svgSurvey = svgSurvey;

    private readonly Func<SvgReport>? _svgSurvey;

    /// <summary>
    /// Which layers an SVG save will write as pixels, said before the save —
    /// or null when the format is not SVG, or every layer is paths.
    /// </summary>
    public string? SvgNotice
    {
        get
        {
            if (!ImageSaveFormats.IsVector(Format) || _svgSurvey is null) return null;
            if (!_surveyed)
            {
                // Asked once, whatever comes back. This is read by a binding on
                // the UI thread, where an exception is not marked handled, so a
                // survey that fails becomes a sentence — and is not run again on
                // every read to fail again.
                _surveyed = true;
                try
                {
                    _svgNotice = _svgSurvey().Notice;
                }
                catch (Exception ex)
                {
                    _svgNotice = "This drawing could not be checked for which layers would be saved "
                        + $"as pixels ({ex.Message}). Saving as SVG may not work for it.";
                }
            }
            return _svgNotice;
        }
    }

    private bool _surveyed;
    private string? _svgNotice;

    public bool HasSvgNotice => SvgNotice is not null;

    /// <summary>Parameterless for the XAML designer only.</summary>
    public SaveImageDialogViewModel() : this(new Scene()) { }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuality))]
    [NotifyPropertyChangedFor(nameof(KeepsTransparency))]
    [NotifyPropertyChangedFor(nameof(MayLoseTransparency))]
    [NotifyPropertyChangedFor(nameof(SvgNotice))]
    [NotifyPropertyChangedFor(nameof(HasSvgNotice))]
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

    /// <summary>What shows through the transparency in a format that has none.</summary>
    [ObservableProperty]
    private string _matte = "#ffffff";

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
    /// One sentence naming what is about to be written. One file, always — a
    /// run of frames is <c>Export image sequence…</c> (Q220).
    /// </summary>
    public string Summary
    {
        get
        {
            var label = ImageSaveFormats.Label(Format);
            var size = $"{OutputWidth}×{OutputHeight}";
            var quality = HasQuality ? $", quality {Quality}" : "";
            return $"One file, {label} at {size}{quality}.";
        }
    }

    public ImageSaveOptions ToOptions() => new(
        Format,
        Math.Clamp(Quality, 1, 100),
        Scale,
        Matte);

    /// <summary>The extension the chosen format wants, for the file picker.</summary>
    public string Extension => ImageSaveFormats.Extension(Format);
}
