using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The bone chrome's legibility and its onion skin — the 2026-08-16 visual
/// audit's three findings (B235, B234, and the phase-1 armature onion skin).
/// </summary>
/// <remarks>
/// The audit measured rather than squinted: the selected bone is white, and
/// on the white paper most documents are that is a <b>1.00:1</b> contrast
/// ratio — invisible by arithmetic, not by taste. These tests hold the fix
/// the same way: paint on white, read pixels.
/// </remarks>
public class BoneChromeVisualTests
{
    private static SKBitmap OnWhite(IReadOnlyList<BoneChrome> bones)
    {
        var bmp = new SKBitmap(new SKImageInfo(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        ArmatureOverlayPainter.Paint(canvas, bones, scale: 1);
        canvas.Flush();
        return bmp;
    }

    private static byte MinLuminanceAcross(SKBitmap bmp, int x0, int x1, int y)
    {
        byte darkest = 255;
        for (var x = x0; x <= x1; x++)
        {
            var c = bmp.GetPixel(x, y);
            var lum = (byte)((c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000);
            if (lum < darkest) darkest = lum;
        }
        return darkest;
    }

    // ---- B235: the chrome reads on white paper --------------------------------------

    [Fact]
    public void TheSelectedBoneIsVisibleOnWhitePaper()
    {
        using var bmp = OnWhite([new BoneChrome("a", "a", 60, 100, 240, 100, Selected: true)]);

        // Crossing the bone's silhouette must pass through something dark —
        // the rim. Before it, every pixel on this scanline was white-on-white
        // (the audit's 1.00:1), and this walk found nothing below 250.
        var darkest = MinLuminanceAcross(bmp, 60, 240, 100);
        Assert.True(darkest < 120, $"no dark rim found crossing the selected bone: darkest {darkest}");

        // And the selection's identity survives over the rim: the shaft still
        // carries near-white pixels, so selected and unselected stay tellable.
        var white = 0;
        for (var x = 70; x <= 230; x++)
        {
            var c = bmp.GetPixel(x, 100);
            if (c.Red > 220 && c.Green > 220 && c.Blue > 220) white++;
        }
        Assert.True(white > 20, $"the white selection line is gone: {white} white pixels");
    }

    [Fact]
    public void OrdinaryBonesAndHandlesGetTheRimToo()
    {
        using var bmp = OnWhite(
        [
            new BoneChrome("a", "a", 30, 60, 150, 60, Selected: false),
            new BoneChrome("h", "h", 60, 150, 120, 150, Selected: false, IsHandle: true),
        ]);

        Assert.True(MinLuminanceAcross(bmp, 30, 150, 60) < 120, "green bone has no rim on white");
        Assert.True(MinLuminanceAcross(bmp, 60, 120, 150) < 120, "amber handle has no rim on white");
    }

    // ---- the armature's onion skin ---------------------------------------------------

    private static MainViewModel PosedAcrossThreeKeys(out string boneId)
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        boneId = vm.SelectedBoneId!;
        vm.PosingMode = true;

        vm.CurrentFrameIndex = 0;
        vm.PoseBoneTo(boneId, 100, 250);    // key at 0: straight down
        vm.CurrentFrameIndex = 4;
        vm.PoseBoneTo(boneId, 160, 150);    // key at 4: straight right
        vm.CurrentFrameIndex = 8;
        vm.PoseBoneTo(boneId, 100, 50);     // key at 8: straight up
        vm.CurrentFrameIndex = 4;
        return vm;
    }

    [AvaloniaFact]
    public void PosingShowsTheNeighbouringPoseKeysAsGhosts()
    {
        var vm = PosedAcrossThreeKeys(out _);
        vm.Onion.Enabled = true;

        var chrome = vm.BoneChromes;
        var before = chrome.Where(c => c.Ghost == BoneGhost.Before).ToList();
        var after = chrome.Where(c => c.Ghost == BoneGhost.After).ToList();
        var live = chrome.Where(c => c.Ghost == BoneGhost.None).ToList();

        // One bone, three poses on screen: where it came from, where it is,
        // where it goes.
        Assert.Single(before);
        Assert.Single(after);
        Assert.Single(live);
        // The ghosts stand at their keys' poses: down at frame 0, up at 8.
        Assert.Equal(150 + 60, before[0].Y1, 6);
        Assert.Equal(150 - 60, after[0].Y1, 6);
        // Ghosts precede the live bone, so the skeleton draws over them.
        Assert.True(
            chrome.ToList().FindIndex(c => c.Ghost == BoneGhost.None)
                > chrome.ToList().FindLastIndex(c => c.Ghost != BoneGhost.None),
            "the live skeleton must come after its ghosts");
    }

    [AvaloniaFact]
    public void TheGhostsFollowTheOnionSwitchAndTheMode()
    {
        var vm = PosedAcrossThreeKeys(out _);

        vm.Onion.Enabled = false;
        Assert.DoesNotContain(vm.BoneChromes, c => c.Ghost != BoneGhost.None);

        vm.Onion.Enabled = true;
        Assert.Contains(vm.BoneChromes, c => c.Ghost != BoneGhost.None);

        // Bind mode edits the skeleton at rest; a pose ghost there would show
        // a thing no bind drag can relate to.
        vm.PosingMode = false;
        Assert.DoesNotContain(vm.BoneChromes, c => c.Ghost != BoneGhost.None);
    }

    [AvaloniaFact]
    public void AGhostIsNeverHitByAPress()
    {
        var vm = PosedAcrossThreeKeys(out var boneId);
        vm.Onion.Enabled = true;

        // The before-ghost's tip stands at (100, 210) and nothing else is
        // there. A press on it must fall through to empty canvas — a ghost
        // shows a pose, it is not a thing to grab.
        var hit = ArmatureOverlay.Hit(vm.BoneChromes, 100, 210, scale: 1);
        Assert.False(hit.Any, $"a press grabbed the ghost: {hit.Grab}");

        // The live bone (pointing right at frame 4) is still grabbable.
        Assert.Equal(boneId, ArmatureOverlay.Hit(vm.BoneChromes, 160, 150, scale: 1).Id);
    }

    [Fact]
    public void GhostsWearTheOnionTintsAndNoHandles()
    {
        using var bmp = OnWhite(
        [
            new BoneChrome("a", "a", 30, 60, 150, 60, false, false, BoneGhost.Before),
            new BoneChrome("a", "a", 30, 150, 150, 150, false, false, BoneGhost.After),
        ]);

        // The before-ghost reads warm, the after-ghost cool — the drawing
        // onion skin's own vocabulary, so one legend covers both. Scanned
        // across the kite's cross-section rather than sampled at a guessed
        // pixel: the silhouette tapers, so a fixed offset lands on paper.
        static SKColor Strongest(SKBitmap b, int x, int yMid)
        {
            var best = SKColors.White;
            var bestSpread = 0;
            for (var y = yMid - 8; y <= yMid + 8; y++)
            {
                var c = b.GetPixel(x, y);
                var spread = Math.Abs(c.Red - c.Blue);
                if (spread > bestSpread)
                {
                    bestSpread = spread;
                    best = c;
                }
            }
            return best;
        }

        var prev = Strongest(bmp, 90, 60);
        var next = Strongest(bmp, 90, 150);
        Assert.True(prev.Red > prev.Blue + 20, $"before-ghost not warm: {prev}");
        Assert.True(next.Blue > next.Red + 20, $"after-ghost not cool: {next}");

        // No end circles: past the tip there is nothing but paper.
        var pastTip = bmp.GetPixel(150 + 4, 60);
        Assert.Equal(255, pastTip.Red);
        Assert.Equal(255, pastTip.Green);
    }

    // ---- B234: heat belongs to the weight brush -------------------------------------

    [AvaloniaFact]
    public void HeatShowsOnlyWhileTheWeightBrushIsArmed()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var frame = Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        frame.Strokes.Add(new Stroke { Points = [new StrokePoint(120, 150, 1)] });

        // Building the skeleton: no dots on the ink.
        Assert.Empty(vm.HeatPoints);
        vm.PosingMode = true;
        Assert.Empty(vm.HeatPoints);

        // The brush is armed: now the dots answer what it would touch.
        vm.WeightPainting = true;
        Assert.Single(vm.HeatPoints);
    }
}
