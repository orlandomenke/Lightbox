using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The walk reader (Q134): a clean cycle reports nothing, a seam that jumps
/// names the loop, footfalls landing unevenly name the stride, and a lopsided
/// bob names both steps. The interesting property throughout is that a
/// CORRECT cycle's last drawing differs from its first — by one step — so
/// none of the checks may punish that.
/// </summary>
public class WalkCycleAnalyserTests
{
    /// <summary>
    /// A cycle whose feet and body are authored separately: the ink is a
    /// 20×50 figure whose lowest point is <paramref name="bottoms"/>[i], and
    /// the subject is a pivot anchor at (40, <paramref name="bodyYs"/>[i]) —
    /// so the bob and the contacts can disagree the way a real drawing's do.
    /// </summary>
    private static (Scene Scene, Layer Layer) Cycle(double[] bottoms, double[] bodyYs)
    {
        var scene = new Scene();
        var pivot = Anchors.Declare(scene, "hips", AnchorKind.Pivot);
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        for (var i = 0; i < bottoms.Length; i++)
        {
            var frame = new Frame
            {
                Strokes =
                [
                    new Stroke
                    {
                        Points =
                        [
                            new StrokePoint(30, bottoms[i] - 50, 1),
                            new StrokePoint(50, bottoms[i], 1),
                        ],
                    },
                ],
                Anchors = new() { [pivot.Id] = new AnchorPoint(40, bodyYs[i]) },
            };
            layer.Cels.Add(new Cel { Frame = frame });
        }
        return (scene, layer);
    }

    /// <summary>Contacts on 0 and 4, an even bob rising between them.</summary>
    private static readonly double[] CleanBottoms = [100, 92, 90, 92, 100, 92, 90, 92];

    private static readonly double[] CleanBodyYs = [50, 46, 44, 46, 50, 46, 44, 46];

    [Fact]
    public void ACleanCycleReportsNoFindings()
    {
        var (scene, layer) = Cycle(CleanBottoms, CleanBodyYs);

        var report = WalkCycleAnalyser.Analyse(scene, layer);

        Assert.NotNull(report);
        Assert.Equal(8, report.Drawings);
        Assert.Equal([0, 4], report.ContactFrames);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void ASeamThatJumpsNamesTheLoop()
    {
        // The last drawing ends 20 px low where no internal step exceeds 4 —
        // the pop an artist sees the third time the loop comes round.
        var bodyYs = (double[])CleanBodyYs.Clone();
        bodyYs[^1] = 66;
        var (scene, layer) = Cycle(CleanBottoms, bodyYs);

        var report = WalkCycleAnalyser.Analyse(scene, layer);

        Assert.NotNull(report);
        var finding = Assert.Single(report.Findings, f => f.Check == WalkCheck.Loop);
        Assert.Equal(7, finding.Frame);
        Assert.Contains("does not close", finding.Message);
    }

    [Fact]
    public void UnevenContactsNameTheStride()
    {
        // Footfalls at 0 and 3 in an eight-frame cycle: strides of 3 and 5.
        double[] bottoms = [100, 92, 90, 100, 92, 90, 88, 90];
        var (scene, layer) = Cycle(bottoms, CleanBodyYs);

        var report = WalkCycleAnalyser.Analyse(scene, layer);

        Assert.NotNull(report);
        Assert.Equal([0, 3], report.ContactFrames);
        var finding = Assert.Single(report.Findings, f => f.Check == WalkCheck.Contacts);
        Assert.Contains("unevenly", finding.Message);
        Assert.Contains("frames 1, 4", finding.Message);
    }

    [Fact]
    public void ALopsidedBobNamesBothSteps()
    {
        // The first stride rises six pixels, the second walks dead level.
        double[] bodyYs = [50, 46, 44, 46, 50, 50, 50, 50];
        var (scene, layer) = Cycle(CleanBottoms, bodyYs);

        var report = WalkCycleAnalyser.Analyse(scene, layer);

        Assert.NotNull(report);
        var finding = Assert.Single(report.Findings, f => f.Check == WalkCheck.Bob);
        Assert.Contains("uneven between steps", finding.Message);
        // Both numbers in the message, the measuring rule: 6 px against 0 px
        // is a limp, and the artist should see the sizes rather than a flag.
        Assert.Contains("6 px", finding.Message);
    }

    [Fact]
    public void ADeadLevelWalkReadsAsFloating()
    {
        double[] flat = [50, 50, 50, 50, 50, 50, 50, 50];
        var (scene, layer) = Cycle(CleanBottoms, flat);

        var report = WalkCycleAnalyser.Analyse(scene, layer);

        Assert.NotNull(report);
        var finding = Assert.Single(report.Findings, f => f.Check == WalkCheck.Bob);
        Assert.Contains("floating", finding.Message);
    }

    [Fact]
    public void TooFewDrawingsIsNotAnAnalysis()
    {
        var (scene, layer) = Cycle([100, 92, 90], [50, 46, 44]);

        Assert.Null(WalkCycleAnalyser.Analyse(scene, layer));
    }

    [Fact]
    public void AFrameWithoutInkIsSkippedRatherThanCounted()
    {
        var (scene, layer) = Cycle(CleanBottoms, CleanBodyYs);
        layer.Cels.Add(new Cel { Frame = new Frame() });

        var report = WalkCycleAnalyser.Analyse(scene, layer);

        Assert.NotNull(report);
        Assert.Equal(8, report.Drawings);
    }
}
