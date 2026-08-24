using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The two bars that float over the canvas, and whether what is in them fits.
/// </summary>
/// <remarks>
/// <para>
/// Both faults here were reported as "the icons are cut off" and "the zoom value is
/// unreadable", and neither is visible to a view model: the commands worked, the bindings
/// worked, and the content was being squeezed by a box. So these are geometry assertions
/// against the real template, which is the only place the answer lives.
/// </para>
/// <para>
/// The cut-off glyphs had <em>two</em> causes stacked, and the second only surfaced because
/// these tests were written before the fix was believed. Every shortcut button set
/// <c>Padding="6,2"</c> inline, which beats any style — 12 px off a 26 px tile leaves 14 px,
/// and the wide emoji (🎥, 🔒) need more. Removing it made the tiles <em>worse</em>: the
/// overlay-bar rule in <c>Density.axaml</c> sat <b>above</b> the plain <c>Button</c> and
/// <c>ToggleButton</c> rules, and <b>Avalonia resolves competing styles by order, not by
/// specificity</b> — so the generic <c>Padding="8,0"</c> won and the content box shrank to
/// 10 px. The authoritative-looking selector counted for nothing; only its position did.
/// </para>
/// <para>
/// Which is why these assert the <em>measured content box</em> rather than "the padding is
/// zero". A rule about where the setter lives would have passed on the broken build; a rule
/// about whether the glyph fits could not. It also explains why raising the tile from 24 to
/// 26 never fixed the clipping it was aimed at — the tile grew and the content box did not.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class OverlayBarLayoutTests : BrushStateIsolated
{
    private static MainWindow Open()
    {
        var window = new MainWindow { Width = 1600, Height = 1000 };
        window.Show();
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static CanvasOverlayBar Bar(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<CanvasOverlayBar>().First(b => b.Name == name);

    /// <summary>Every button an artist presses in a bar — not the bar's own chrome.</summary>
    /// <remarks>
    /// The collapse and close buttons come from the control's template and are styled
    /// separately, so including them would measure something nobody complained about.
    /// </remarks>
    private static List<Button> Tiles(CanvasOverlayBar bar) =>
        bar.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Name is not ("PART_Collapse" or "PART_Close"))
            .ToList();

    /// <summary>The tiles that are actually on screen, with their measured boxes.</summary>
    /// <remarks>
    /// Visible ones only, and the first version of this got it wrong. Half the shortcut bar
    /// is absent by design — no camera button without a camera, no guide toggles without
    /// rulers — and a collapsed control measures 0 × 0, so those reported a *negative*
    /// content box and drowned the two real failures in noise. A hidden tile has no geometry
    /// to be wrong about.
    /// </remarks>
    private static List<ContentControl> OnScreen(CanvasOverlayBar bar) =>
        bar.GetVisualDescendants().OfType<ContentControl>()
            .Where(c => c is Button or ToggleButton)
            .Where(c => c.Name is not ("PART_Collapse" or "PART_Close"))
            .Where(c => c.IsVisible && c.Bounds.Width > 0)
            .ToList();

    [AvaloniaFact]
    public void EveryShortcutTileLeavesRoomForItsGlyph()
    {
        // The reported bug, as the measurement that would have caught it. A 26 px tile with
        // 6 px of padding on each side leaves 14 px of content box, and the wide emoji —
        // 🎥 and 🔒 — need more than that, so they were clipped while the geometric glyphs
        // fitted. Stated as a floor on the content box rather than as "padding must be zero",
        // because the thing that matters is whether the glyph fits.
        var window = Open();
        var bar = Bar(window, "ShortcutBar");

        var cramped = new List<string>();
        foreach (var tile in OnScreen(bar))
        {
            var inner = tile.Bounds.Width - tile.Padding.Left - tile.Padding.Right;
            if (inner < 20) cramped.Add($"{tile.Content} ({inner:0.#} px of {tile.Bounds.Width:0.#})");
        }

        Assert.True(
            cramped.Count == 0,
            "these tiles do not leave 20 px for a glyph: " + string.Join(", ", cramped));
    }

    [AvaloniaFact]
    public void TheShortcutTilesDoNotOverrideTheOnePlaceThatSizesThem()
    {
        // Stronger and cheaper than measuring: the padding is decided in Density.axaml, and
        // an inline value is how it came to be wrong. A tile that sets its own is a tile the
        // next size change will not reach.
        var window = Open();
        var bar = Bar(window, "ShortcutBar");

        var overriding = OnScreen(bar)
            .Where(c => c.Padding != default)
            .Select(c => $"{c.Content} → {c.Padding}")
            .ToList();

        Assert.True(
            overriding.Count == 0,
            "these set their own padding instead of taking the overlay tile's: "
            + string.Join(", ", overriding));
    }

    [AvaloniaFact]
    public void TheZoomReadoutIsWideEnoughToReadAtFullPercent()
    {
        // "100%" is the widest thing it shows and the value it shows most, so it is the one
        // to size for. On a top or bottom bar the slot runs across, and a 26 px square — what
        // every other tile gets — fits about two characters.
        var window = Open();
        var readout = window.FindControl<Button>("ZoomLabel");
        var text = window.FindControl<TextBlock>("ZoomLabelText");

        Assert.NotNull(readout);
        Assert.NotNull(text);
        Assert.True(
            readout!.Bounds.Width >= 40,
            $"the zoom readout is {readout.Bounds.Width:0.#} px wide — not enough for \"100%\"");
        Assert.True(
            text!.Bounds.Width >= 40,
            $"the readout's text box is {text.Bounds.Width:0.#} px — the string is being squeezed");
    }

    [AvaloniaFact]
    public void TheZoomReadoutTurnsAndGrowsTheOtherWayOnASideBar()
    {
        // The half a RenderTransform cannot do on its own. Rotating the readout to lie along
        // a narrow bar was already in the XAML, but rotation happens after layout — the text
        // had been measured and clipped inside a 26 px square before it was turned, so it was
        // unreadable at an angle. The slot has to grow along the bar, and the bar's own width
        // must not grow with it.
        var window = Open();
        var vm = (MainViewModel)window.DataContext!;
        var readout = window.FindControl<Button>("ZoomLabel")!;
        var acrossBefore = readout.Bounds.Width;

        vm.Workspace.PlaceOverlay(OverlayId.View, CanvasEdge.Left, 0.3);
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(
            readout.Bounds.Height >= 40,
            $"on a side bar the readout is {readout.Bounds.Height:0.#} px tall — the string still has nowhere to go");
        Assert.True(
            readout.Bounds.Width <= OverlayConverters.OverlayTile,
            $"the readout is {readout.Bounds.Width:0.#} px across, wider than the {OverlayConverters.OverlayTile} px "
            + "tile — the whole bar bulges around one control");
        Assert.True(acrossBefore >= 40, "the readout was not wide on a horizontal bar to begin with");
    }

    [AvaloniaFact]
    public void TheReadoutKeepsTheTileWidthAcrossTheBar()
    {
        // The tile size is a style setter, which code cannot read, so the readout's cross
        // axis carries a copy of the number. This is what stops the copy drifting silently
        // if the tile is ever resized again — it was raised from 24 to 26 once already.
        var window = Open();
        var bar = Bar(window, "ShortcutBar");
        var tile = Tiles(bar).First();

        Assert.Equal(OverlayConverters.OverlayTile, tile.Bounds.Width);
        Assert.Equal(OverlayConverters.OverlayTile, tile.Bounds.Height);
    }

    [AvaloniaFact]
    public void ChangingTheZoomKeepsTheReadoutsOwnLayout()
    {
        // The trap in the fix. The readout's content is now a TextBlock that carries the
        // width and the rotation, so the old `ZoomLabel.Content = "…"` would have replaced it
        // with a bare string and put the squeezed version straight back — silently, and only
        // after the artist zoomed once.
        var window = Open();
        var text = window.FindControl<TextBlock>("ZoomLabelText")!;

        window.Canvas.ZoomAt(new Avalonia.Point(400, 300), 2.5);
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotEqual("100%", text.Text);
        Assert.EndsWith("%", text.Text);
        Assert.True(
            text.Bounds.Width >= 40,
            $"after a zoom the text box is {text.Bounds.Width:0.#} px — its own layout was replaced");
    }
}
