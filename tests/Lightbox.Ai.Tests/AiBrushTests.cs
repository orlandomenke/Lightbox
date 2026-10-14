using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

namespace Lightbox.Ai.Tests;

/// <summary>
/// B468: a model is told a stroke's size, hardness and opacity and nothing
/// else about its brush, so what it sends back is a plain round brush. A line
/// drawn with a tapered, textured or pressure-thinned brush came back as a
/// uniform blob — measured on real line art 2026-10-13, where a thin pencil
/// line returned as a thick yellow mass. The brush is the artist's; the motion
/// is the model's.
/// </summary>
public class AiBrushTests
{
    private static Stroke Line(double y, string? label = null) => new()
    {
        Label = label,
        Points = [new(40, y, 0.3), new(216, y, 0.9)],
        Brush = new BrushSettings
        {
            Size = 8,
            TipId = "pencil-grain",
            MinimumDiameter = 0.05,
            TextureId = "paper-cold",
            TextureDepth = 0.6,
            Flow = 0.7,
            Kind = BrushKind.Paint,
        },
        Color = "#202020",
    };

    /// <summary>What a model sends back: the wire's plain brush.</summary>
    private static Stroke Plain(double y, double size = 7) => new()
    {
        Points = [new(40, y, 0.6), new(216, y, 0.6)],
        Brush = new BrushSettings { Size = size, Hardness = 0.8, Opacity = 1, Spacing = 0.15 },
        Color = "#202020",
    };

    private static InbetweenRequest Swing() =>
        new(new SceneInfo(256, 256, 12), [Line(40)], [Line(216)], [0.5], Easing.Linear);

    private sealed class Answering(params Stroke[] strokes) : IAiArtist
    {
        public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
            InbetweenRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<List<InbetweenFrameResult>>.Success(
                [new InbetweenFrameResult(0.5, [.. strokes])]));

        public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(SubjectRequest request, CancellationToken ct) =>
            Task.FromResult(AiResult<SubjectTaxonomy>.Refused("not under test"));
    }

    [Fact]
    public async Task AnAcceptedStrokeWearsTheBrushOfTheKeyStrokeItCameFrom()
    {
        var run = await InbetweenRepair.RunAsync(new Answering(Plain(128)), Swing(), CancellationToken.None);

        var stroke = Assert.Single(run.Value!.Frames[0].Strokes!);
        Assert.Equal("pencil-grain", stroke.Brush.TipId);
        Assert.Equal("paper-cold", stroke.Brush.TextureId);
        Assert.Equal(0.6, stroke.Brush.TextureDepth);
        Assert.Equal(0.05, stroke.Brush.MinimumDiameter);
        Assert.Equal(0.7, stroke.Brush.Flow);
    }

    [Fact]
    public async Task AnAlphaLockedStrokeComesBackAlphaLocked()
    {
        // The case that was measured: a line coloured by alpha-locked strokes
        // over black ink. The wire calls every stroke "brush", so the colouring
        // came back as ordinary paint — a size-25 stroke laid down in the open
        // instead of tinting the line, a solid blob several times its width.
        var key = (Stroke s) => { s.AlphaLocked = true; s.Brush.Size = 25; return s; };
        var request = new InbetweenRequest(
            new SceneInfo(256, 256, 12), [key(Line(40))], [key(Line(216))], [0.5], Easing.Linear);

        var run = await InbetweenRepair.RunAsync(new Answering(Plain(128, size: 25)), request, CancellationToken.None);

        Assert.True(run.Value!.Frames[0].Strokes![0].AlphaLocked);
    }

    [Fact]
    public async Task TheSizeTheModelChoseIsKept()
    {
        // The model blends size between the keys; that is part of the motion,
        // and the one brush number it was asked for.
        var run = await InbetweenRepair.RunAsync(new Answering(Plain(128, size: 7)), Swing(), CancellationToken.None);

        Assert.Equal(7, run.Value!.Frames[0].Strokes![0].Brush.Size);
    }

    [Fact]
    public async Task TheKeysBrushIsCopiedNotShared()
    {
        // A shared BrushSettings would let an edit to the inbetween's brush
        // change the key's — and the key is the artist's drawing.
        var request = Swing();
        var run = await InbetweenRepair.RunAsync(new Answering(Plain(128)), request, CancellationToken.None);

        Assert.NotSame(request.KeyframeA[0].Brush, run.Value!.Frames[0].Strokes![0].Brush);
    }

    private static Stroke Brushed(double x, double y, string tip, double opacity = 1) => new()
    {
        Points = [new(x, y, 0.5), new(x + 60, y, 0.5)],
        Brush = new BrushSettings { Size = 6, TipId = tip, Opacity = opacity },
    };

    private static Stroke PlainAt(double x, double y, double opacity = 1) => new()
    {
        Points = [new(x, y, 0.5), new(x + 60, y, 0.5)],
        Brush = new BrushSettings { Size = 6, Hardness = 0.8, Opacity = opacity, Spacing = 0.15 },
    };

    private static readonly SceneInfo Scene = new(1000, 1000, 12);

    [Fact]
    public void TwoStrokesWithDifferentBrushesEachKeepTheirOwn()
    {
        // A pairing that swapped them would pass a one-stroke test.
        var keyA = new List<Stroke> { Brushed(100, 100, "pencil"), Brushed(100, 600, "ink") };
        var keyB = new List<Stroke> { Brushed(140, 120, "pencil"), Brushed(140, 620, "ink") };

        var restored = AiBrushes.Restore([PlainAt(120, 610), PlainAt(120, 110)], keyA, keyB, Scene, 0.5);

        Assert.Equal("ink", restored[0].Brush.TipId);
        Assert.Equal("pencil", restored[1].Brush.TipId);
    }

    [Fact]
    public void AStrokeThatOnlyKeyBHasStillFindsItsBrush()
    {
        var restored = AiBrushes.Restore([PlainAt(120, 110)], [], [Brushed(140, 120, "ink")], Scene, 0.5);

        Assert.Equal("ink", restored[0].Brush.TipId);
    }

    [Fact]
    public void PastHalfwayTheBrushIsKeyBs()
    {
        // A stroke that changes brush from pencil to ink should not wear the
        // pencil all the way and pop to ink on the key.
        var early = AiBrushes.Restore([PlainAt(120, 110)], [Brushed(100, 100, "pencil")], [Brushed(140, 120, "ink")], Scene, 0.25);
        var late = AiBrushes.Restore([PlainAt(120, 110)], [Brushed(100, 100, "pencil")], [Brushed(140, 120, "ink")], Scene, 0.75);

        Assert.Equal("pencil", early[0].Brush.TipId);
        Assert.Equal("ink", late[0].Brush.TipId);
    }

    [Fact]
    public void AFadeTheModelDrewIsKept()
    {
        // The keys disagree about opacity, so the model's in-between value is
        // part of the motion, not noise to overwrite.
        var restored = AiBrushes.Restore(
            [PlainAt(120, 110, opacity: 0.5)], [Brushed(100, 100, "ink", 1)], [Brushed(140, 120, "ink", 0)], Scene, 0.5);

        Assert.Equal(0.5, restored[0].Brush.Opacity);
    }

    [Fact]
    public void AFarStrokeIsNotABrushDonor()
    {
        // The matcher pairs whatever is left; a revealed line in one corner must
        // not borrow the brush — or the alpha lock — of a stroke across the page.
        var far = Brushed(900, 900, "ink");
        far.AlphaLocked = true;

        var restored = AiBrushes.Restore([PlainAt(20, 20)], [far], [far], Scene, 0.5);

        Assert.Null(restored[0].Brush.TipId);
        Assert.False(restored[0].AlphaLocked);
    }

    [Fact]
    public void AFillIsNotABrushDonor()
    {
        // A fill is described to the model as a brush outline; the stroke that
        // comes back is a brush, and a fill's settings would be the wrong ones.
        var fill = Brushed(100, 100, "fill-tip");
        fill.Tool = ToolKind.Fill;

        var restored = AiBrushes.Restore([PlainAt(110, 105)], [fill], [fill], Scene, 0.5);

        Assert.Null(restored[0].Brush.TipId);
    }

    [Fact]
    public async Task AStrokeWithNothingToMatchKeepsThePlainBrush()
    {
        // Ink the keys do not explain — a revealed line — has no brush to
        // inherit, and inventing one would be a guess.
        var keys = new InbetweenRequest(new SceneInfo(256, 256, 12), [], [], [0.5], Easing.Linear);
        var plain = Plain(128);

        var answer = await new Answering(plain).GenerateInbetweensAsync(keys, CancellationToken.None);
        var restored = AiBrushes.Restore(answer.Value![0].Strokes, keys.KeyframeA, keys.KeyframeB, keys.Scene, 0.5);

        Assert.Null(Assert.Single(restored).Brush.TipId);
    }
}
