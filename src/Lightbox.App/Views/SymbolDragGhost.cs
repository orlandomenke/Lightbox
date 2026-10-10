using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// The picture of a symbol under the pointer while its tile is dragged over the
/// canvas: what will land, where it will land.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The drag worked and showed nothing — the pointer
/// changed and that was all — so a gesture the manual had promised for months
/// read as broken to the one person using it. A drop that chooses a place owes
/// the artist the sight of the place.
/// </para>
/// <para>
/// <b>It is the placement's picture and the placement's place.</b> The bitmap
/// and the box come from <see cref="SymbolRasterizer.Ghost"/>, which is the
/// render and the matrix the stamp uses, and the box is carried to the screen
/// by the canvas's own view matrix inside a layer that takes the canvas's own
/// margin — so zoom, rotation, mirror and the rulers need no handling here.
/// Only its strength differs, on purpose: it is faded
/// (<see cref="Strength"/>) so the drawing under it can still be aimed at, and
/// what lands is the symbol at its own opacity.
/// </para>
/// <para>
/// <b>An element over the canvas rather than paint inside it.</b> A drag-over
/// arrives per pointer move, and moving an element is a transform on the
/// compositor: the canvas is not invalidated and nothing is recomposed. The
/// symbol is rendered once per drag. The layer is created on the first drag —
/// a window that never drags a symbol never has it.
/// </para>
/// </remarks>
internal sealed class SymbolDragGhost(Panel host, CanvasControl canvas)
{
    /// <summary>
    /// How strongly the ghost is drawn. See-through enough to aim at what is
    /// under it; the drop is not faded.
    /// </summary>
    public const double Strength = 0.6;

    private Image? _image;
    private Panel? _layer;
    private string? _shown;
    private bool _nothingToShow;
    private SKRect _atOrigin;

    /// <summary>Whether the ghost is on screen.</summary>
    public bool IsShown => _image is { IsVisible: true };

    /// <summary>Where the ghost's picture sits, in document coordinates; null when hidden.</summary>
    public SKRect? DocBox { get; private set; }

    /// <summary>The element drawn, for a test to ask where it landed on screen.</summary>
    public Control? Visual => _image;

    /// <summary>Show <paramref name="symbol"/> with its pivot at a document point.</summary>
    /// <param name="celIndex">The frame it is being dropped on, which decides the drawing a cycling symbol shows.</param>
    /// <returns>True when this call put the ghost on screen, false when it only moved it or could not.</returns>
    public bool Show(Symbol symbol, SKImageInfo info, int celIndex, double x, double y)
    {
        var drawing = new SymbolPlacement().FrameIndexAt(celIndex, symbol.FrameCount);
        var key = $"{symbol.Id}|v{symbol.Version}|f{drawing}|{info.Width}x{info.Height}";
        if (_shown != key)
        {
            // Decided once per symbol, the miss included: a symbol with no ink
            // renders the whole scene to find that out, and a drag-over arrives
            // per pointer move.
            _nothingToShow = !Load(symbol, info, celIndex);
            _shown = key;
            if (_nothingToShow) Hide();
        }
        if (_nothingToShow || _image is null || _layer is null) return false;

        // The view matrix is in the canvas's own coordinates, and the rulers
        // push the canvas in from the host's corner.
        _layer.Margin = canvas.Margin;

        var box = _atOrigin;
        box.Offset((float)x, (float)y);
        DocBox = box;
        var origin = canvas.DocumentOrigin;
        _image.RenderTransform = new MatrixTransform(
            Matrix.CreateTranslation(box.Left - origin.X, box.Top - origin.Y) * canvas.ViewMatrix());
        var appeared = !_image.IsVisible;
        _image.IsVisible = true;
        return appeared;
    }

    /// <summary>Take the ghost off screen: the drag left, was dropped, or ended.</summary>
    public void Hide()
    {
        DocBox = null;
        if (_image is not null) _image.IsVisible = false;
    }

    private bool Load(Symbol symbol, SKImageInfo info, int celIndex)
    {
        if (SymbolRasterizer.Ghost(symbol, info, celIndex) is not { } ghost) return false;
        using var bitmap = ghost.Bitmap;
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;

        var image = _image ??= Create();
        (image.Source as IDisposable)?.Dispose();
        image.Source = new Bitmap(stream);
        image.Width = ghost.Box.Width;
        image.Height = ghost.Box.Height;
        _atOrigin = ghost.Box;
        return true;
    }

    private Image Create()
    {
        var image = new Image
        {
            Stretch = Stretch.Fill,
            Opacity = Strength,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
        };
        // Its own clipped layer, directly over the canvas and under the bars
        // that float on it: a ghost dragged to the canvas's edge must not be
        // drawn across the dockers beside it.
        _layer = new Panel { ClipToBounds = true, IsHitTestVisible = false, Children = { image } };
        host.Children.Insert(host.Children.IndexOf(canvas) + 1, _layer);
        return image;
    }
}
