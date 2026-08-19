using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// <c>Effects ▸ Fluid effects…</c> — where fire, smoke and water are authored.
/// </summary>
/// <remarks>
/// <para>
/// <b>A window of its own rather than a docker, and that is the one design
/// decision here</b> (Q123). Tuning an effect is a loop of look-and-adjust
/// against a preview, and the preview has to be large enough to judge a
/// silhouette by — which is exactly the room a docker does not have. It also
/// keeps the feature out of <c>MainWindow.axaml</c>, the hottest file in
/// <c>HOTSPOTS.md</c>: the whole of this arrives as one menu item, one
/// shortcut and one field.
/// </para>
/// <para>
/// <b>Simulate and Bake are separate buttons because they cost different
/// things by a factor of forty.</b> A style edit redraws from the solve
/// already in hand and previews as the slider moves; a fluid edit marks the
/// picture stale and waits to be asked. Hiding that would make every edit feel
/// like the slow one — and worse, would make an artist afraid to touch the
/// cheap half.
/// </para>
/// <para>
/// The window holds no effects logic at all: it is bindings over
/// <see cref="FluidEffectsViewModel"/>, which is what lets the bake, the cache
/// and the treatment cascade be tested with no window attached.
/// </para>
/// </remarks>
public partial class FluidEffectsWindow : Window
{
    private readonly FluidEffectsViewModel _model;

    public FluidEffectsWindow()
    {
        // The designer's parameterless path. A window with no view model binds
        // to nothing and shows an empty list, which is the right thing to see.
        InitializeComponent();
        _model = null!;
    }

    public FluidEffectsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _model = new FluidEffectsViewModel(vm);
        DataContext = _model;
        _model.PreviewChanged += RepaintPreview;
        Closed += (_, _) => _model.PreviewChanged -= RepaintPreview;
        SyncFrameRange();
    }

    /// <summary>The model, for a test that wants to drive the window's buttons.</summary>
    internal FluidEffectsViewModel Model => _model;

    private void OnNewFire(object? sender, RoutedEventArgs e) => New("fire");

    private void OnNewSmoke(object? sender, RoutedEventArgs e) => New("smoke");

    private void New(string kind)
    {
        _model.NewElement(kind);
        SyncFrameRange();
        _model.Resolve();
    }

    private void OnDeleteElement(object? sender, RoutedEventArgs e)
    {
        _model.DeleteElement();
        RepaintPreview();
    }

    private void OnNewEmitter(object? sender, RoutedEventArgs e) => _model.NewEmitter();

    private void OnDeleteEmitter(object? sender, RoutedEventArgs e) => _model.DeleteEmitter();

    private void OnUseCurrentBrush(object? sender, RoutedEventArgs e) => _model.UseCurrentBrush();

    private void OnClearBrush(object? sender, RoutedEventArgs e) => _model.ClearBrush();

    private void OnRevertTreatment(object? sender, RoutedEventArgs e) => _model.RevertTreatment();

    private void OnRevertField(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EffectFieldRow row) row.Revert();
    }

    private void OnSimulate(object? sender, RoutedEventArgs e)
    {
        SyncFrameRange();
        _model.Resolve();
    }

    private void OnBake(object? sender, RoutedEventArgs e) => _model.Bake();

    private void OnClearBake(object? sender, RoutedEventArgs e) => _model.ClearBake();

    /// <summary>Keep the scrubber inside the element's own frame range.</summary>
    private void SyncFrameRange()
    {
        if (_model.Element is not { } element) return;
        FrameSlider.Minimum = element.FirstFrame;
        FrameSlider.Maximum = element.FirstFrame + Math.Max(0, element.FrameCount - 1);
    }

    /// <summary>
    /// Draw the preview at the size the box actually is.
    /// </summary>
    /// <remarks>
    /// The bitmap is re-made rather than kept: the preview changes on every
    /// slider move, and a cache keyed by nothing is how a window ends up
    /// showing the last effect's picture.
    /// </remarks>
    private void RepaintPreview()
    {
        var box = PreviewImage.Bounds;
        var width = (int)Math.Round(box.Width > 8 ? box.Width : 320);
        var height = (int)Math.Round(box.Height > 8 ? box.Height : 320);

        var old = PreviewImage.Source as Bitmap;
        using var rendered = _model.RenderPreview(_model.PreviewFrame, width, height);
        PreviewImage.Source = rendered is null ? null : Encode(rendered);
        old?.Dispose();
    }

    private static Bitmap Encode(SKBitmap picture)
    {
        using var image = SKImage.FromBitmap(picture)
            ?? throw new InvalidOperationException("Could not snapshot the effect preview.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
