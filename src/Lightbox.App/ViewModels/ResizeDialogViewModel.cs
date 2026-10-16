using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>Which of the two resizes the dialog is doing.</summary>
/// <remarks>
/// One dialog rather than two, because an artist opening it knows the size they
/// want and not which of two menu items owns it — but the <em>mode</em> is not
/// cosmetic. It decides whether the marks are allowed to change, which is the
/// whole design (Q61).
/// </remarks>
public enum ResizeMode
{
    /// <summary>More paper, or less. Nothing drawn moves.</summary>
    Canvas,

    /// <summary>The artwork scales, and the grain re-rolls with it.</summary>
    Image,
}

/// <summary>
/// The resize dialog's outline preview: the paper as it is and as it will be,
/// placed by the anchor, in a unit space the control only has to fit.
/// </summary>
/// <param name="Old">The current paper.</param>
/// <param name="New">The paper after the resize.</param>
/// <param name="Kept">The part of the current paper that survives — all of it
/// unless the resize crops; the rest of <paramref name="Old"/> is what is cut away.</param>
/// <param name="Extent">The union of the two, whose larger side is 1. Every
/// rectangle lies inside <c>(0, 0, Extent)</c>.</param>
/// <param name="Scale">Units per pixel, so a test can read the picture back in pixels.</param>
/// <param name="Crops">Whether any of the current paper is cut away.</param>
public sealed record ResizeOutline(Rect Old, Rect New, Rect Kept, Size Extent, double Scale, bool Crops);

/// <summary>
/// Resize canvas and resize image, as an artist asks for them.
/// </summary>
/// <remarks>
/// <para>
/// <b>All of the arithmetic and none of the window</b>, so the linking, the
/// clamping and the summary are testable without one — which matters here
/// because the interesting cases are the ones nobody clicks through by hand: a
/// link that divides by zero, a crop to a single pixel, a PPI change that is not
/// a resize at all.
/// </para>
/// <para>
/// <b>The two modes differ in what they are allowed to do to the mark</b>, and
/// the dialog says so rather than assuming the artist remembers. Resizing the
/// paper moves no coordinate — <c>Scene.OriginX</c> shifts instead — so the
/// render is bit-identical outside the new margin. Rescaling the artwork
/// multiplies every coordinate and the brush sizes with them, and every dab
/// dynamic seeded from a position through <c>Hash01</c> re-rolls. That is
/// allowed, because the artist asked for different art; it is not allowed when
/// they only asked for more paper.
/// </para>
/// </remarks>
public sealed partial class ResizeDialogViewModel : ObservableObject
{
    private readonly int _startWidth;
    private readonly int _startHeight;
    private readonly int _startPpi;
    private bool _linking;

    public ResizeDialogViewModel(Scene scene, ResizeMode mode)
    {
        Mode = mode;
        _startWidth = scene.Width;
        _startHeight = scene.Height;
        _startPpi = scene.Ppi;
        _width = scene.Width;
        _height = scene.Height;
        _ppi = scene.Ppi;
    }

    public ResizeMode Mode { get; }

    public bool IsImage => Mode == ResizeMode.Image;

    public bool IsCanvas => Mode == ResizeMode.Canvas;

    /// <summary>The size the document started at, for the summary and for Reset.</summary>
    public int StartWidth => _startWidth;

    /// <inheritdoc cref="StartWidth"/>
    public int StartHeight => _startHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(Outline))]
    private int _width;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(Outline))]
    private int _height;

    /// <summary>
    /// Pixels per inch. Image mode only, because paper does not change how big
    /// a pixel prints.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private int _ppi;

    /// <summary>
    /// Where the existing artwork sits in the new paper. Canvas mode only.
    /// </summary>
    /// <remarks>
    /// Centre by default, which is what "add a margin" almost always means. The
    /// grid is nine buttons rather than a dropdown because the answer is a
    /// direction, and a direction is faster to point at than to read.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(Outline))]
    private ResizeAnchor _anchor = ResizeAnchor.Center;

    /// <summary>Keep the aspect ratio: editing one dimension moves the other.</summary>
    /// <remarks>
    /// <b>On by default for an image and off for a canvas</b>, because they are
    /// different questions. Rescaling artwork non-uniformly distorts it, and
    /// almost nobody means to; adding paper to one side is the ordinary reason
    /// to open the canvas mode at all.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _linked;

    /// <summary>
    /// Start the link in the state each mode wants. Separate from the field's
    /// initialiser so a caller can override it before the dialog is shown.
    /// </summary>
    public ResizeDialogViewModel WithDefaults()
    {
        Linked = IsImage;
        return this;
    }

    partial void OnWidthChanged(int value)
    {
        if (!Linked || _linking || _startWidth <= 0 || _startHeight <= 0) return;
        _linking = true;
        Height = Math.Max(1, (int)Math.Round(value * (double)_startHeight / _startWidth));
        _linking = false;
    }

    partial void OnHeightChanged(int value)
    {
        if (!Linked || _linking || _startWidth <= 0 || _startHeight <= 0) return;
        _linking = true;
        Width = Math.Max(1, (int)Math.Round(value * (double)_startWidth / _startHeight));
        _linking = false;
    }

    /// <summary>Whether the button does anything.</summary>
    /// <remarks>
    /// A dialog that accepts a no-op and reports success is the same lie as a
    /// tool that silently does nothing — and both <c>CanvasResize.Apply</c> and
    /// <c>ImageResize.Apply</c> already return false for it, so this only says
    /// out loud what they would decide.
    /// </remarks>
    public bool CanApply
    {
        get
        {
            if (Width < 1 || Height < 1) return false;
            if (IsImage && Ppi < 1) return false;
            var sizeChanged = Width != _startWidth || Height != _startHeight;
            var ppiChanged = IsImage && Ppi != _startPpi;
            return sizeChanged || ppiChanged;
        }
    }

    /// <summary>
    /// What is about to happen, in the artist's terms — the preview the roadmap
    /// asks for, as a sentence rather than a picture.
    /// </summary>
    /// <remarks>
    /// It names the consequence that cannot be undone by eye: whether the marks
    /// are going to change. An artist who wanted more paper and got rescaled
    /// artwork has lost the grain of every stroke in the document, and the only
    /// moment that is cheap to prevent is before they press the button.
    /// </remarks>
    public string Summary
    {
        get
        {
            if (Width < 1 || Height < 1) return "Give a width and a height of at least 1 pixel.";
            if (IsImage && Ppi < 1) return "PPI has to be at least 1.";

            var size = Width == _startWidth && Height == _startHeight
                ? $"{Width} × {Height}, unchanged"
                : $"{_startWidth} × {_startHeight} → {Width} × {Height}";

            if (IsImage)
            {
                var ppi = Ppi == _startPpi ? $"{Ppi} PPI" : $"{_startPpi} → {Ppi} PPI";
                return $"{size}, {ppi}. The artwork scales, so the marks change.";
            }

            var dw = Width - _startWidth;
            var dh = Height - _startHeight;
            string change;
            if (dw == 0 && dh == 0) change = "no change to the paper";
            else if (dw >= 0 && dh >= 0) change = $"{Describe(dw, dh)} added";
            else if (dw <= 0 && dh <= 0) change = $"{Describe(-dw, -dh)} cropped";
            // Wider and shorter, or the other way round: one axis gains paper
            // and the other loses it, so neither word on its own is true.
            else change = $"{Describe(Math.Abs(dw), Math.Abs(dh))} changed";
            return $"{size} — {change}, anchored {Human(Anchor)}. Nothing drawn moves.";
        }
    }

    private static string Describe(int dw, int dh) => (dw, dh) switch
    {
        (0, _) => $"{dh} px vertically",
        (_, 0) => $"{dw} px horizontally",
        _ => $"{dw} × {dh} px",
    };

    private static string Human(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => "top-left",
        ResizeAnchor.Top => "top",
        ResizeAnchor.TopRight => "top-right",
        ResizeAnchor.Left => "left",
        ResizeAnchor.Right => "right",
        ResizeAnchor.BottomLeft => "bottom-left",
        ResizeAnchor.Bottom => "bottom",
        ResizeAnchor.BottomRight => "bottom-right",
        _ => "centre",
    };

    /// <summary>
    /// The preview as a picture: where the old paper sits in the new, so the
    /// direction the paper grows is visible before the button rather than after.
    /// </summary>
    /// <remarks>
    /// <b>Placed by <see cref="CanvasResize.OriginShift"/>, the same call the
    /// edit makes</b>, so the picture and the result cannot disagree — not even
    /// about which side a centred anchor's spare pixel goes to. A size below
    /// one pixel (a field mid-edit) shows the old paper alone rather than a
    /// rectangle that is not a size.
    /// </remarks>
    public ResizeOutline Outline
    {
        get
        {
            var oldW = Math.Max(1, _startWidth);
            var oldH = Math.Max(1, _startHeight);
            var isSize = Width >= 1 && Height >= 1;
            var (newW, newH) = isSize ? (Width, Height) : (oldW, oldH);
            var (dx, dy) = isSize
                ? CanvasResize.OriginShift(oldW, oldH, newW, newH, Anchor)
                : (0, 0);

            // In pixels, relative to the old paper's top-left.
            var oldPx = new Rect(0, 0, oldW, oldH);
            var newPx = new Rect(dx, dy, newW, newH);
            var union = oldPx.Union(newPx);
            var scale = 1.0 / Math.Max(union.Width, union.Height);

            Rect Unit(Rect r) => new(
                (r.X - union.X) * scale, (r.Y - union.Y) * scale, r.Width * scale, r.Height * scale);

            var kept = oldPx.Intersect(newPx);
            return new ResizeOutline(
                Unit(oldPx), Unit(newPx), Unit(kept),
                new Size(union.Width * scale, union.Height * scale),
                scale,
                Crops: kept != oldPx);
        }
    }

    /// <summary>Pick the anchor from the nine-square grid.</summary>
    public void SetAnchor(ResizeAnchor anchor) => Anchor = anchor;

    /// <summary>
    /// Trade width for height — portrait to landscape in one press.
    /// </summary>
    /// <remarks>
    /// Deliberately steps around the link: a swap changes the aspect ratio by
    /// definition, and a linked field that "corrected" the other half back
    /// would turn the button into a no-op.
    /// </remarks>
    public void Swap()
    {
        _linking = true;
        (Width, Height) = (Height, Width);
        _linking = false;
    }

    /// <summary>Put the fields back to the size the document already is.</summary>
    public void Reset()
    {
        _linking = true;
        Width = _startWidth;
        Height = _startHeight;
        Ppi = _startPpi;
        _linking = false;
    }

    /// <summary>
    /// Do it. Returns false and changes nothing when the request is a no-op.
    /// </summary>
    /// <remarks>
    /// <b>The caller wraps this in one <c>DocumentEditor.Perform</c></b>, so a
    /// resize is a single undo step however many coordinates it touched. That is
    /// not a detail: an image resize walks every stroke, every clip contour,
    /// every guide, camera key, placement and collision box in the document, and
    /// an artist who has to press undo once per stroke has effectively lost the
    /// drawing.
    /// </remarks>
    public bool Apply(Doc doc, IPixelResampler resampler) =>
        IsImage
            ? ImageResize.Apply(doc, Width, Height, resampler, Ppi, Lightbox.Raster.Effects.EffectRegistry.LengthFloor)
            : CanvasResize.Apply(doc.Scene, Width, Height, Anchor);
}
