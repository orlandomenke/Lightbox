using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// A guide set lands on the paper it arrives at, not on the paper it was drawn
/// on — Q181's first half.
/// </summary>
/// <remarks>
/// <para>
/// The owner's report: a set authored on a 4K document and pulled into a 1080p
/// one arrived at its authored pixel coordinates — four times too tall, anchor
/// off the paper — which made a shared character height chart useless across
/// the resolutions one project actually holds.
/// </para>
/// <para>
/// What these hold is the pair of rules that fixes it without breaking the
/// other guide kinds: <b>positions travel as fractions of each axis, sizes
/// travel by one uniform factor taken from height.</b> The uniform half is the
/// one with teeth — the obvious implementation, scaling x by width and y by
/// height, lands the height chart perfectly and silently tilts every line,
/// un-squares every grid and stops an isometric being isometric.
/// </para>
/// </remarks>
public class GuideSetFitTests(ITestOutputHelper output)
{
    private static GuideSetCanvas Paper(int w, int h) => new() { Width = w, Height = h };

    /// <summary>A six-head chart standing on the floor, 70% of the paper tall.</summary>
    private static GuideSet Chart(int w, int h)
    {
        var headsTall = 6;
        var unit = h * 0.7 / headsTall;
        return new GuideSet
        {
            Name = "Knight",
            Canvas = Paper(w, h),
            Guides =
            [
                new Guide
                {
                    Kind = GuideKind.HeightScale,
                    X = w * 0.5,
                    Y = h * 0.9,       // the ground it stands on
                    Spacing = unit,
                    Divisions = headsTall,
                },
            ],
        };
    }

    [Fact]
    public void PullingA4kSetInto1080pKeepsTheHeightScaleTheSameFractionOfFrame()
    {
        var set = Chart(3840, 2160);
        var authored = set.Guides[0];

        var landed = Assert.Single(GuideSetFit.Onto(set, Paper(1920, 1080)));

        // Same head count — "six heads" is six heads on any paper.
        Assert.Equal(6, landed.Divisions);
        // Same fraction of the frame, top and bottom.
        var authoredFill = authored.Spacing * 6 / 2160;
        var landedFill = landed.Spacing * 6 / 1080;
        output.WriteLine($"4K: {authored.Spacing:0.##} px/head, fills {authoredFill:P1}");
        output.WriteLine($"1080p: {landed.Spacing:0.##} px/head, fills {landedFill:P1}");
        Assert.Equal(authoredFill, landedFill, 6);
        Assert.Equal(960, landed.X, 6);
        Assert.Equal(972, landed.Y, 6);   // 90% down, as authored
        // And it is on the paper at all, which is what the report was about.
        Assert.InRange(landed.Y - landed.Spacing * 6, 0, 1080);
    }

    [Fact]
    public void AGuideSetScalesUniformlySoALinesAngleAndAGridsSquarenessSurvive()
    {
        // Deliberately not the same aspect: 4:3 paper onto 16:9, where a
        // per-axis scale and a uniform one disagree by a third.
        var set = new GuideSet
        {
            Canvas = Paper(1600, 1200),
            Guides =
            [
                new Guide { Kind = GuideKind.Line, X = 800, Y = 600, Angle = 45 },
                new Guide { Kind = GuideKind.Grid, X = 0, Y = 0, Spacing = 60 },
                new Guide { Kind = GuideKind.Isometric, X = 800, Y = 600 },
            ],
        };

        var landed = GuideSetFit.Onto(set, Paper(1920, 1080));

        // The angle is what a per-axis scale would have destroyed: a 45° line
        // through paper squeezed 0.9 one way and 1.2 the other comes out at
        // 39.8°, and nothing in the file would say so.
        Assert.Equal(45, landed[0].Angle, 9);
        // A grid keeps one pitch, so it is still square rather than a lattice
        // of 54 × 72 rectangles.
        Assert.Equal(54, landed[1].Spacing, 9);   // 60 × (1080/1200)
        Assert.Equal(0, landed[1].Angles[0], 9);
        Assert.Equal(90, landed[1].Angles[1], 9);
        // And the isometric still reads as three axes 30° off the horizon.
        Assert.Equal(30, landed[2].Angles[0], 9);
        Assert.Equal(-30, landed[2].Angles[1], 9);
        Assert.Equal(90, landed[2].Angles[2], 9);
        output.WriteLine("angles and pitch survived 4:3 → 16:9");
    }

    [Fact]
    public void ASetWithNoAuthoredCanvasLandsExactlyWhereItWasAuthored()
    {
        // Every set saved before Q181 is this one, and it must keep behaving
        // the way its author left it.
        var set = new GuideSet
        {
            Guides = [new Guide { Kind = GuideKind.Line, X = 3000, Y = 1800, Spacing = 64 }],
        };

        var landed = Assert.Single(GuideSetFit.Onto(set, Paper(1920, 1080)));

        Assert.Equal(3000, landed.X, 9);
        Assert.Equal(1800, landed.Y, 9);
        Assert.Equal(64, landed.Spacing, 9);
    }

    [Fact]
    public void FittingMeasuresFromThePaperRatherThanFromTheCoordinateOrigin()
    {
        // Paper grown leftward moves the origin negative and leaves the
        // drawing alone (Scene.OriginX). A fraction measured from zero rather
        // than from the paper's corner would put the chart outside it.
        var set = new GuideSet
        {
            Canvas = new GuideSetCanvas { Width = 1000, Height = 1000, OriginX = -500, OriginY = -500 },
            Guides = [new Guide { Kind = GuideKind.Line, X = 0, Y = 0 }],  // dead centre
        };

        var landed = Assert.Single(GuideSetFit.Onto(
            set, new GuideSetCanvas { Width = 400, Height = 400, OriginX = 100, OriginY = 100 }));

        Assert.Equal(300, landed.X, 9);   // centre of paper that runs 100..500
        Assert.Equal(300, landed.Y, 9);
    }

    [Fact]
    public void FittingIsACopySoTheLibraryIsNotEditedByThePull()
    {
        var set = Chart(3840, 2160);
        var before = set.Guides[0].Spacing;

        var landed = Assert.Single(GuideSetFit.Onto(set, Paper(1920, 1080)));
        landed.X += 1000;

        Assert.Equal(before, set.Guides[0].Spacing, 9);
        Assert.Equal(1920, set.Guides[0].X, 9);
    }

    [Fact]
    public void AGuideSetThatNeverRecordedItsPaperWritesNoCanvasKey()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        manifest.GuideSets = [new GuideSet { Guides = [new Guide()] }];

        var json = JsonSerializer.Serialize(manifest, DocJson.Options);

        output.WriteLine(json);
        Assert.DoesNotContain("\"canvas\"", json);
        Assert.DoesNotContain("\"left\"", json);
        Assert.DoesNotContain("\"top\"", json);
        Assert.DoesNotContain("\"isUsable\"", json);
    }

    [Fact]
    public void PaperNobodyGrewWritesNoOriginKeys()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        manifest.GuideSets = [new GuideSet { Canvas = Paper(1920, 1080), Guides = [new Guide()] }];

        var json = JsonSerializer.Serialize(manifest, DocJson.Options);

        Assert.Contains("\"canvas\"", json);
        Assert.DoesNotContain("\"originX\"", json);
        Assert.DoesNotContain("\"originY\"", json);
    }
}
