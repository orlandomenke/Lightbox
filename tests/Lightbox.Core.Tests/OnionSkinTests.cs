using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Which drawings to ghost around the playhead. The interesting mistakes here
/// are about the exposure sheet rather than about pixels — a hold is not a
/// drawing, the same drawing must not be ghosted twice, and a sequence on 3s
/// has its neighbours three frames away.
/// </summary>
public class OnionSkinTests
{
    /// <summary>A layer whose cels are keyed where the string has a letter.</summary>
    /// <example><c>"A--B--C"</c> is three drawings on 3s.</example>
    private static Layer Row(string pattern)
    {
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        var byLetter = new Dictionary<char, Frame>();
        foreach (var c in pattern)
        {
            if (c == '-')
            {
                layer.Cels.Add(new Cel { Frame = null });
                continue;
            }
            if (!byLetter.TryGetValue(c, out var frame))
            {
                byLetter[c] = frame = new PaintedFrame { Id = c.ToString() };
            }
            layer.Cels.Add(new Cel { Frame = frame });
        }
        return layer;
    }

    private static string Names(IEnumerable<OnionGhost> ghosts) =>
        string.Concat(ghosts.Select(g => g.Frame.Id));

    [Fact]
    public void OnOnesTheNeighboursAreTheFramesEitherSide()
    {
        var ghosts = OnionSkin.Ghosts(Row("ABCDE"), index: 2, before: 1, after: 1, keysOnly: false);

        Assert.Equal("BD", Names(ghosts));
        Assert.All(ghosts, g => Assert.Equal(1, g.Steps));
        Assert.True(ghosts[0].Before);
        Assert.False(ghosts[1].Before);
    }

    [Fact]
    public void OnThreesTheNeighboursAreStillTheDrawingsEitherSide()
    {
        // The bug this replaces: the old code read the cel at index ± 1, which
        // on 3s is a hold and yields nothing at all. An animator working on 3s
        // saw no onion skin whatsoever.
        var ghosts = OnionSkin.Ghosts(Row("A--B--C"), index: 3, before: 1, after: 1, keysOnly: false);

        Assert.Equal("AC", Names(ghosts));
    }

    [Fact]
    public void ADrawingIsNeverGhostedAgainstItself()
    {
        // Standing on a hold, the "previous frame" is the same drawing. Ghosting
        // it would tint the current drawing and look like a rendering fault.
        var ghosts = OnionSkin.Ghosts(Row("A--B"), index: 1, before: 2, after: 0, keysOnly: false);

        Assert.DoesNotContain(ghosts, g => g.Frame.Id == "A");
        Assert.Empty(ghosts);
    }

    [Fact]
    public void TheSameDrawingIsNeverGhostedTwice()
    {
        // A cycle can expose one drawing at several indices.
        var ghosts = OnionSkin.Ghosts(Row("ABABC"), index: 4, before: 3, after: 0, keysOnly: false);

        Assert.Equal("BA", Names(ghosts));
    }

    [Fact]
    public void StepsCountDrawingsNotFrames()
    {
        // The nearest ghost is Steps 1 however far away it is, so it gets the
        // strongest opacity. Weighting by frame distance is why onion skin on
        // 2s looks wrong in tools that get this wrong.
        var ghosts = OnionSkin.Ghosts(Row("A--B--C"), index: 6, before: 2, after: 0, keysOnly: false);

        Assert.Equal("BA", Names(ghosts));
        Assert.Equal(1, ghosts[0].Steps);
        Assert.Equal(2, ghosts[1].Steps);
    }

    [Fact]
    public void KeysOnlyWalksKeyedCelsAndIgnoresHolds()
    {
        var ghosts = OnionSkin.Ghosts(Row("A--B--C"), index: 6, before: 2, after: 0, keysOnly: true);

        Assert.Equal("BA", Names(ghosts));
    }

    [Fact]
    public void KeysOnlyStandingOnAHoldFindsTheKeysNotTheHeldDrawing()
    {
        // Index 4 is a hold of B. Keys-only should reach A, not stop at the
        // hold and not offer B, which is already on screen.
        var ghosts = OnionSkin.Ghosts(Row("A--B--C"), index: 4, before: 2, after: 0, keysOnly: true);

        Assert.Equal("A", Names(ghosts));
    }

    [Fact]
    public void BeforeAndAfterAreAskedForSeparately()
    {
        // An animator working forwards wants two behind and none ahead. One
        // control for both makes that impossible to say.
        var ghosts = OnionSkin.Ghosts(Row("ABCDE"), index: 2, before: 2, after: 0, keysOnly: false);

        Assert.Equal("BA", Names(ghosts));
        Assert.All(ghosts, g => Assert.True(g.Before));
    }

    [Fact]
    public void ZeroDepthGhostsNothing()
    {
        Assert.Empty(OnionSkin.Ghosts(Row("ABCDE"), index: 2, before: 0, after: 0, keysOnly: false));
    }

    [Fact]
    public void TheEndsOfTheSequenceSimplyRunOut()
    {
        var ghosts = OnionSkin.Ghosts(Row("ABC"), index: 0, before: 3, after: 3, keysOnly: false);

        Assert.Equal("BC", Names(ghosts));
    }

    // ---- falloff ----------------------------------------------------------------

    [Fact]
    public void FalloffHalvesEachFurtherGhost()
    {
        Assert.Equal(0.4, OnionSkin.OpacityAt(1, 0.4, 0.5), 5);
        Assert.Equal(0.2, OnionSkin.OpacityAt(2, 0.4, 0.5), 5);
        Assert.Equal(0.1, OnionSkin.OpacityAt(3, 0.4, 0.5), 5);
    }

    [Fact]
    public void AFalloffOfOneMakesEveryGhostEquallyVisible()
    {
        // Right when checking registration across a whole sequence, where the
        // ghosts are the subject rather than context for the current drawing.
        Assert.Equal(0.4, OnionSkin.OpacityAt(1, 0.4, 1.0), 5);
        Assert.Equal(0.4, OnionSkin.OpacityAt(4, 0.4, 1.0), 5);
    }
}
