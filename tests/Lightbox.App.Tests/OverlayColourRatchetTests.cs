using System.Text.RegularExpressions;

namespace Lightbox.App.Tests;

/// <summary>
/// Canvas chrome takes its colour from <c>OverlayStyle</c>, and the files that
/// do not yet may not add to what they have (Q249, Q250).
/// </summary>
/// <remarks>
/// <para>
/// <b>A ratchet, because the move is in flight.</b> The inventory of
/// 2026-10-12 found thirty-two overlays with a colour each; the pen, the white
/// arrow, the transform box and the arrow's selection outlines are on the
/// style and the rest follow. Until they have, the honest rule is not "no
/// colour literal in a painter" — which is false today — but "no <em>new</em>
/// one": each file below is held to the number of lines it has now, a file not
/// listed is held to none, and moving an overlay onto the style lowers its
/// number in the same commit.
/// </para>
/// <para>
/// <b>What is counted</b> is a line that writes a colour down:
/// <c>SKColors.X</c>, <c>new SKColor(…)</c>, or a target-typed
/// <c>new(0x…, 0x…</c>. It cannot tell an overlay's colour from a clear to
/// transparent, which is why files that only do the latter are listed with
/// their count rather than exempted by name — the number is the exemption.
/// </para>
/// </remarks>
public partial class OverlayColourRatchetTests(ITestOutputHelper output)
{
    /// <summary>Lines that write a colour literal, per file under Rendering. Only ever lowered.</summary>
    private static readonly Dictionary<string, int> Allowed = new()
    {
        // Where the style's own colours are defined.
        ["OverlayStyle.cs"] = 5,

        // Overlays still in colours of their own — the work that is left.
        ["CanvasControl.cs"] = 12,
        ["GuidePainter.cs"] = 9,
        ["ArmatureOverlayPainter.cs"] = 8,
        ["CanvasControl.Symmetry.cs"] = 5,
        ["RigOverlayPainter.cs"] = 4,
        ["ReferenceBoxPainter.cs"] = 4,
        ["CropOverlayPainter.cs"] = 4,
        ["MotionTrailPainter.cs"] = 3,
        ["FillPreviewPainter.cs"] = 2,
        ["CanvasControl.Selection.cs"] = 2,
        ["BalanceOverlayPainter.cs"] = 2,
        ["AnalysisOverlayPainter.cs"] = 2,
        ["MotionArcPainter.cs"] = 1,

        // The pointer's own ink, which adapts to the artwork and is not chrome
        // you edit: the brush ring, its badge and the eyedropper's ring.
        ["CursorContrast.cs"] = 2,
        ["CursorBadgePainter.cs"] = 2,
        ["PickRing.cs"] = 2,
        ["CanvasControl.Pointer.cs"] = 2,

        // Not overlays: clears, fallbacks and pixels on their way somewhere.
        ["ThumbnailRenderer.cs"] = 4,
        ["PresentedFrame.cs"] = 2,
        ["LayerStackBake.cs"] = 2,
        ["CanvasControl.Theme.cs"] = 2,
        ["OutboundImage.cs"] = 1,
        ["AttachmentOverlay.cs"] = 1,
    };

    [GeneratedRegex(@"SKColors\.|new SKColor\(|new\(0x[0-9a-fA-F]+, 0x")]
    private static partial Regex ColourLiteral();

    [Fact]
    public void EveryCanvasOverlayTakesItsPensFromTheStyle()
    {
        var folder = Path.Combine(RepoRoot(), "src", "Lightbox.App", "Rendering");
        var over = new List<string>();
        var under = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs").Order())
        {
            var name = Path.GetFileName(file);
            var count = File.ReadLines(file).Count(line => ColourLiteral().IsMatch(line));
            var allowed = Allowed.GetValueOrDefault(name);
            if (count > allowed) over.Add($"{name}: {count} lines write a colour, {allowed} allowed");
            if (count < allowed) under.Add($"{name}: {count} now, the ratchet still says {allowed}");
        }

        foreach (var line in under) output.WriteLine("lower me — " + line);
        Assert.True(
            over.Count == 0,
            "Canvas chrome takes its colour from OverlayStyle (DESIGN.md, On the canvas):\n  "
            + string.Join("\n  ", over));
        // A number left high is room for the next literal to hide in.
        Assert.True(under.Count == 0, "Lower the ratchet:\n  " + string.Join("\n  ", under));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lightbox.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
