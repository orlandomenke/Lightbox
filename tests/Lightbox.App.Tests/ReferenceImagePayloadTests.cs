using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// What rides along with an AI request, and what it costs to prepare.
/// </summary>
/// <remarks>
/// <para>
/// B31. <c>CollectReferenceImages</c> composed, PNG-encoded and base64'd every visible
/// character-sheet view on the UI thread before each request — <b>52 ms for one 960×540
/// view</b>, so about 100 ms of stall before every call, producing byte-identical output each
/// time because the sheet had not changed. The per-layer render was already memoised; the
/// expensive half was not.
/// </para>
/// <para>
/// <b>The invalidation is the part worth testing hardest</b>, because getting it wrong fails
/// silently in the worse direction: a stale cache sends a model a picture of art the artist has
/// already changed, and nothing about the result says so. The bug entry proposed hanging it off
/// <c>OnDocumentChanged</c>, which takes an early return for scoped edits — and a stroke commit
/// is exactly that.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ReferenceImagePayloadTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A document with one sheet view carrying real line art.</summary>
    private static MainViewModel WithSheet(int width = 960, int height = 540)
    {
        var vm = new MainViewModel(null);
        var sheet = new ReferenceSheet { Name = "Knight" };
        var layer = new Layer { Name = "Front", Visible = true };
        var frame = new Frame();

        // Dense-ish strokes: the measurement in the bug entry was taken on line art, and an
        // empty view encodes to almost nothing, which would make any timing here meaningless.
        for (var i = 0; i < 24; i++)
        {
            var points = new List<StrokePoint>();
            for (var t = 0; t < 40; t++)
            {
                points.Add(new StrokePoint(20 + t * 22, 30 + i * 20 + Math.Sin(t * 0.3) * 14, 0.8));
            }
            frame.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#101010",
                Brush = new BrushSettings { Size = 4, Hardness = 1, Opacity = 1, Flow = 1 },
                Points = points,
            });
        }

        layer.Cels.Add(new Cel { Frame = frame });
        sheet.Views.Add(new ReferenceView
        {
            Name = "front",
            Width = width,
            Height = height,
            Layers = [layer],
        });
        vm.Doc.ReferenceSheets.Add(sheet);
        return vm;
    }

    private static ReferenceView OnlyView(MainViewModel vm) => vm.Doc.ReferenceSheets[0].Views[0];

    /// <summary>The one view this document would send, decoded.</summary>
    /// <remarks>
    /// Through <see cref="Collect"/> rather than through <c>RenderReferenceViewPng</c>, because
    /// <b>the cap belongs to the request and not to the renderer</b> — and the first version of
    /// these tests got that wrong. Capping inside the parameterless overload also shrank what
    /// the MCP surface answers <c>render_reference_view</c> with, which nobody asked for; the
    /// existing <c>RenderReferenceView_ProducesDecodablePng</c> caught it by asserting the
    /// authored width. A test that reaches past the request path could not have.
    /// </remarks>
    private static SkiaSharp.SKBitmap Sent(MainViewModel vm) =>
        SkiaSharp.SKBitmap.Decode(Convert.FromBase64String(Collect(vm)![0]));

    /// <summary>
    /// What the AI path actually collects, reached the way the request builder reaches it.
    /// </summary>
    /// <remarks>
    /// <c>CollectReferenceImages</c> is private; going through reflection keeps the test on the
    /// real path rather than on a public shim written for it, which would be a second
    /// implementation of the thing under test.
    /// </remarks>
    private static IReadOnlyList<string>? Collect(MainViewModel vm) =>
        (IReadOnlyList<string>?)typeof(MainViewModel)
            .GetMethod("CollectReferenceImages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, null);

    [AvaloniaFact]
    public void ReferenceViewsAreEncodedOnceNotPerCall()
    {
        // The reported bug, as the artist meets it: two AI calls in a row with no edit
        // between them. Timed rather than counted, because the cost is the point — and both
        // numbers get printed, because "the second was faster" means nothing without them.
        var vm = WithSheet();

        var cold = Stopwatch.StartNew();
        var first = Collect(vm);
        var coldMs = cold.Elapsed.TotalMilliseconds;

        var warm = Stopwatch.StartNew();
        var second = Collect(vm);
        var warmMs = warm.Elapsed.TotalMilliseconds;

        Assert.NotNull(first);
        Assert.NotNull(second);
        output.WriteLine($"first call {coldMs:F1} ms, second {warmMs:F2} ms");

        // Same bytes, and cheaply. The equality is what says the cache is not merely fast but
        // returning the same thing the slow path did.
        Assert.Equal(first, second);
        Assert.True(
            warmMs < coldMs / 2,
            $"the second call cost {warmMs:F1} ms against the first's {coldMs:F1} ms — "
            + "the encode is not being reused");
    }

    [AvaloniaFact]
    public void TheEncodedViewIsReusedWhileTheSheetIsUnchanged()
    {
        // The other half of the same property, stated without a clock so it cannot go flaky:
        // repeated collection is byte-identical, which is only interesting because it was
        // already true before the fix — it was identical *and* recomputed. Reference equality
        // on the string is what distinguishes the two.
        var vm = WithSheet();

        var a = Collect(vm)![0];
        var b = Collect(vm)![0];

        Assert.Same(a, b);
    }

    [AvaloniaFact]
    public void EditingTheDrawingThrowsTheEncodedViewAway()
    {
        // The dangerous direction. A cache that survived an edit would hand the model a
        // picture of art that has changed, and the only symptom would be a worse inbetween —
        // which nobody would attribute to a cache.
        var vm = WithSheet();
        var before = Collect(vm)![0];

        // A stroke commit, which is the edit that OnDocumentChanged skips.
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 240, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var after = Collect(vm)![0];
        Assert.NotSame(before, after);
    }

    [AvaloniaFact]
    public void HidingALayerChangesWhatIsSent()
    {
        // Not just "something was edited" — the *content* has to be re-read. Visibility is the
        // cheapest way to prove that, and it is also a real thing an artist does to a sheet
        // before asking for an inbetween.
        var vm = WithSheet();
        var before = Collect(vm)![0];

        OnlyView(vm).Layers[0].Visible = false;
        vm.MarkDocumentEditedForTests();

        var after = Collect(vm);
        output.WriteLine(after is null ? "no visible views, so nothing is sent" : "still sending a view");
        Assert.Null(after);   // the only view has no visible layer left
        Assert.NotEmpty(before);
    }

    [AvaloniaFact]
    public void TheLongEdgeIsCappedOnTheWayOutRatherThanOnTheView()
    {
        // Providers bill by area regardless of file size, so a 4K sheet would be charged as a
        // 4K sheet. The cap belongs to the request; the artist's view stays whatever they drew.
        var vm = WithSheet(1920, 1080);
        var view = OnlyView(vm);

        var capped = Convert.FromBase64String(Collect(vm)![0]);
        var authored = Convert.FromBase64String(vm.RenderReferenceViewPng(view));

        using var cappedImage = SkiaSharp.SKBitmap.Decode(capped);
        using var authoredImage = SkiaSharp.SKBitmap.Decode(authored);

        output.WriteLine(
            $"view {view.Width}×{view.Height} → sent {cappedImage.Width}×{cappedImage.Height} "
            + $"({capped.Length / 1024} KB vs {authored.Length / 1024} KB as drawn)");

        Assert.Equal(768, Math.Max(cappedImage.Width, cappedImage.Height));
        // Aspect kept: a squashed reference is a worse reference, and 16:9 must stay 16:9.
        Assert.Equal(432, Math.Min(cappedImage.Width, cappedImage.Height));
        // The renderer's own overload is uncapped, which is what the MCP surface returns.
        Assert.Equal(1920, authoredImage.Width);
        Assert.True(capped.Length < authored.Length, "the cap did not reduce the payload");
        // The view itself is untouched — the cap is not a document edit.
        Assert.Equal(1920, view.Width);
        Assert.Equal(1080, view.Height);
    }

    [AvaloniaFact]
    public void ASmallViewIsNotUpscaledToTheCap()
    {
        // The cap is a ceiling, not a target. Enlarging a small sheet would spend tokens on
        // pixels the artist never drew, which is the opposite of the point.
        var vm = WithSheet(400, 300);

        using var image = Sent(vm);

        Assert.Equal(400, image.Width);
        Assert.Equal(300, image.Height);
    }

    [AvaloniaFact]
    public void TwoTabsWhoseViewsShareAnIdDoNotShareAPicture()
    {
        // Ids are the cache key and they are NOT unique across tabs: `DocJson.Clone` preserves
        // `ReferenceView.Id`, so opening a template and then "new from template" leaves two
        // documents whose views collide, in one dictionary that lives on the view model rather
        // than on the tab. What saves it is that `MarkDocumentEdited` clears the cache wholesale
        // and every tab activation reaches it — correctness resting on an invariant two methods
        // away, which is exactly the kind that stops being true without anyone noticing.
        var a = WithSheet(400, 300);
        var second = new Doc();
        var sheet = new ReferenceSheet { Name = "Clone" };
        var cloned = new ReferenceView
        {
            Id = OnlyView(a).Id,   // the collision, verbatim
            Name = "front",
            Width = 400,
            Height = 300,
            Layers = [new Layer { Name = "Front", Visible = true, Cels = { new Cel { Frame = new Frame() } } }],
        };
        sheet.Views.Add(cloned);
        second.ReferenceSheets.Add(sheet);

        // Tab A's picture is in the cache under that id before tab B exists.
        var fromA = Collect(a)![0];

        a.OpenDocumentTab(second, filePath: null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var fromB = Collect(a)![0];

        using var drawn = SkiaSharp.SKBitmap.Decode(Convert.FromBase64String(fromA));
        using var empty = SkiaSharp.SKBitmap.Decode(Convert.FromBase64String(fromB));
        output.WriteLine($"tab A {DarkPixels(drawn)} dark pixels, tab B {DarkPixels(empty)}");

        Assert.NotEqual(fromA, fromB);
        // Named, not merely different: tab B's view is empty, so anything dark in what it sends
        // came from tab A. `NotEqual` alone would also pass on two differently-wrong pictures.
        Assert.True(
            DarkPixels(empty) == 0,
            $"the second tab sent {DarkPixels(empty)} dark pixels for an empty view — it is being "
            + "served the first tab's picture, because the two views share an id");
    }

    /// <summary>Pixels dark enough to be a stroke rather than the ground they sit on.</summary>
    /// <remarks>
    /// <b>Darkness, not alpha.</b> The first version of this counted <c>Alpha > 40</c> and
    /// reported 100.0% — the composite is opaque, so every pixel qualified and the assertion
    /// would have passed on a blank sheet just as happily. The number was real and it was
    /// measuring the wrong thing, which is the trap `CLAUDE.md` records for brush measurement
    /// showing up somewhere new. The strokes here are <c>#101010</c> on light ground, so
    /// darkness is what tells them apart.
    /// </remarks>
    private static int DarkPixels(SkiaSharp.SKBitmap image)
    {
        var dark = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).Red < 110) dark++;
            }
        }
        return dark;
    }

    [AvaloniaFact]
    public void TheDownscaledReferenceStillHasTheDrawingInIt()
    {
        // The risk in downscaling line art: thin strokes thin out under minification, and a
        // reference the model cannot read is worse than a big one. Measured against an empty
        // sheet of the same size, so the assertion cannot pass on a blank image — which is
        // exactly what the first version of it did.
        var drawn = WithSheet(1920, 1080);
        var blank = new MainViewModel(null);
        var emptySheet = new ReferenceSheet { Name = "Empty" };
        emptySheet.Views.Add(new ReferenceView
        {
            Name = "front", Width = 1920, Height = 1080,
            Layers = [new Layer { Name = "Front", Visible = true, Cels = { new Cel { Frame = new Frame() } } }],
        });
        blank.Doc.ReferenceSheets.Add(emptySheet);

        using var withArt = Sent(drawn);
        using var without = Sent(blank);

        var ink = DarkPixels(withArt);
        var ground = DarkPixels(without);
        output.WriteLine(
            $"{withArt.Width}×{withArt.Height}: {ink} dark pixels with art, {ground} without");

        Assert.Equal(768, Math.Max(withArt.Width, withArt.Height));
        Assert.True(
            ink > ground + 2000,
            $"the downscaled reference has {ink} dark pixels against {ground} for an empty sheet "
            + "of the same size — the strokes did not survive the minification");
    }
}
