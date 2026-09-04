using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// A saved skeleton travels in head units, so the goblin stays shorter than
/// the human on any paper — Q181's second half.
/// </summary>
/// <remarks>
/// <para>
/// The owner's ask: build a rig for a human and a dog and a goblin, then use
/// them as a proportion tool on the next character. That only works if the rig
/// carries a unit that survives a change of resolution, and the head unit is
/// the one an animator already thinks in.
/// </para>
/// <para>
/// <see cref="GuideKind.HeightScale"/> is the exchange rate, and it was shaped
/// for the job before anybody asked: its <c>(X, Y)</c> is the ground and its
/// <see cref="Guide.Spacing"/> is one head. So the arithmetic these hold is
/// <c>heads × spacing</c>, feet on the anchor, and nothing has to know the
/// resolution.
/// </para>
/// </remarks>
public class ArmatureFitTests(ITestOutputHelper output)
{
    /// <summary>
    /// A figure standing on <paramref name="groundY"/>, <paramref name="tall"/>
    /// pixels of it, with a head bone across the top so the rig is more than
    /// one segment.
    /// </summary>
    private static Armature Figure(double x, double groundY, double tall)
    {
        var spine = new Bone
        {
            Id = "spine", Name = "Spine",
            X = x, Y = groundY, RotationDeg = -90, Length = tall,
        };
        // A child in the parent's frame: straight on, half a head long.
        var head = new Bone
        {
            Id = "head", Name = "Head", ParentId = "spine",
            X = tall, Y = 0, RotationDeg = 0, Length = 0,
        };
        return new Armature { Bones = [spine, head] };
    }

    private static Guide HeightScale(double x, double groundY, double head, int heads) => new()
    {
        Kind = GuideKind.HeightScale, X = x, Y = groundY, Spacing = head, Divisions = heads,
    };

    private static AuthoredCanvas Paper(int w, int h) => new() { Width = w, Height = h };

    [Fact]
    public void ARigRemembersHowManyHeadsTallItWas()
    {
        var rig = Figure(x: 100, groundY: 500, tall: 300);

        Assert.Equal(300, ArmatureFit.BindHeight(rig), 6);
        // Six heads of 50, or three of 100 — the rig does not care which, it
        // measures itself against whatever chart is standing beside it.
        Assert.Equal(6, ArmatureFit.HeadsOn(rig, HeightScale(0, 500, head: 50, heads: 6))!.Value, 6);
        Assert.Equal(3, ArmatureFit.HeadsOn(rig, HeightScale(0, 500, head: 100, heads: 3))!.Value, 6);
        // And with nothing to measure against there is no head count to invent.
        Assert.Null(ArmatureFit.HeadsOn(rig, null));
        Assert.Null(ArmatureFit.HeadsOn(rig, new Guide { Kind = GuideKind.Line }));
    }

    [Fact]
    public void PullingARigAgainstAHeightScaleStandsItOnTheAnchorAtItsHeadCount()
    {
        // Saved on 4K: 300 px tall against a 100 px head — three heads.
        var set = new RigSet
        {
            Name = "Goblin",
            Armature = Figure(x: 100, groundY: 500, tall: 300),
            Canvas = Paper(3840, 2160),
            Heads = 3,
        };

        // Landing on a document whose head is 50 px, standing at (400, 900).
        var landed = ArmatureFit.Onto(
            set, Paper(1920, 1080), HeightScale(400, 900, head: 50, heads: 8), RigFit.Heads);

        var box = ArmatureFit.BindBounds(landed)!.Value;
        output.WriteLine($"landed {box.MaxY - box.MinY:0.##} px tall, feet at ({box.MinX:0.#}, {box.MaxY:0.#})");
        Assert.Equal(150, box.MaxY - box.MinY, 6);   // three heads of fifty
        Assert.Equal(900, box.MaxY, 6);              // feet on the ground the scale names
        Assert.Equal(400, (box.MinX + box.MaxX) / 2, 6);
    }

    /// <summary>
    /// The whole point, stated as the owner stated it: two characters saved
    /// against one chart keep their relationship on a document neither was
    /// drawn on.
    /// </summary>
    [Fact]
    public void TwoRigsSavedAgainstOneChartKeepTheirRelativeHeights()
    {
        var chart = HeightScale(0, 1000, head: 100, heads: 8);
        var human = Figure(x: 100, groundY: 1000, tall: 750);   // 7.5 heads
        var goblin = Figure(x: 100, groundY: 1000, tall: 450);  // 4.5 heads

        var humanSet = new RigSet
        {
            Name = "Human", Armature = human, Canvas = Paper(1920, 1080),
            Heads = ArmatureFit.HeadsOn(human, chart),
        };
        var goblinSet = new RigSet
        {
            Name = "Goblin", Armature = goblin, Canvas = Paper(1920, 1080),
            Heads = ArmatureFit.HeadsOn(goblin, chart),
        };
        Assert.Equal(7.5, humanSet.Heads!.Value, 6);
        Assert.Equal(4.5, goblinSet.Heads!.Value, 6);

        // A different document, a different resolution, a different head size.
        var elsewhere = HeightScale(500, 300, head: 24, heads: 8);
        var landedHuman = ArmatureFit.Onto(humanSet, Paper(960, 540), elsewhere, RigFit.Heads);
        var landedGoblin = ArmatureFit.Onto(goblinSet, Paper(960, 540), elsewhere, RigFit.Heads);

        var h = ArmatureFit.BindHeight(landedHuman);
        var g = ArmatureFit.BindHeight(landedGoblin);
        output.WriteLine($"human {h:0.##} px, goblin {g:0.##} px, ratio {g / h:0.###}");
        Assert.Equal(7.5 * 24, h, 6);
        Assert.Equal(4.5 * 24, g, 6);
        // The relationship, which is the thing that had to survive the move.
        Assert.Equal(4.5 / 7.5, g / h, 6);
    }

    [Fact]
    public void ARigWithNoHeadCountFallsBackToTheCanvasFraction()
    {
        // Saved on a document that had no height scale, so nothing measured it.
        var set = new RigSet
        {
            Name = "Dog",
            Armature = Figure(x: 500, groundY: 800, tall: 400),
            Canvas = Paper(1000, 1000),
            Heads = null,
        };

        var landed = ArmatureFit.Onto(set, Paper(500, 500), HeightScale(0, 0, 50, 8), RigFit.Heads);

        Assert.Equal(RigFit.Canvas, ArmatureFit.LandedAs(set, Paper(500, 500), HeightScale(0, 0, 50, 8), RigFit.Heads));
        var box = ArmatureFit.BindBounds(landed)!.Value;
        Assert.Equal(200, box.MaxY - box.MinY, 6);   // half the paper, half the dog
        Assert.Equal(400, box.MaxY, 6);              // its feet at the same 80% down
        Assert.Equal(250, (box.MinX + box.MaxX) / 2, 6);
        output.WriteLine("asked for heads, had nothing to measure, landed by canvas — and said so");
    }

    [Fact]
    public void OriginalSizeLandsTheBindPoseUntouched()
    {
        // The goblin being short is data. A tool that always fits to the frame
        // cannot draw a size comparison at all.
        var set = new RigSet
        {
            Armature = Figure(x: 100, groundY: 500, tall: 300),
            Canvas = Paper(3840, 2160),
            Heads = 3,
        };

        var landed = ArmatureFit.Onto(set, Paper(400, 400), HeightScale(9, 9, 20, 8), RigFit.Original);

        var box = ArmatureFit.BindBounds(landed)!.Value;
        Assert.Equal(300, box.MaxY - box.MinY, 9);
        Assert.Equal(500, box.MaxY, 9);
        Assert.Equal(100, box.MinX, 9);
    }

    [Fact]
    public void ScalingARigChangesLengthsAndNeverAngles()
    {
        var rig = Figure(x: 100, groundY: 500, tall: 300);
        rig.Bones[1].RotationDeg = 37.5;

        ArmatureFit.Scale(rig, 0.25);

        Assert.Equal(75, rig.Bones[0].Length, 9);
        Assert.Equal(-90, rig.Bones[0].RotationDeg, 9);
        Assert.Equal(37.5, rig.Bones[1].RotationDeg, 9);
        // A child's offset is in the parent's frame and scales with it.
        Assert.Equal(75, rig.Bones[1].X, 9);
    }

    [Fact]
    public void MovingARigMovesItsRootsAndNotItsChildrenTwice()
    {
        var rig = Figure(x: 100, groundY: 500, tall: 300);
        var childOffset = rig.Bones[1].X;

        ArmatureFit.MoveBy(rig, 40, -25);

        Assert.Equal(140, rig.Bones[0].X, 9);
        Assert.Equal(475, rig.Bones[0].Y, 9);
        Assert.Equal(childOffset, rig.Bones[1].X, 9);
    }

    [Fact]
    public void FittingIsACopySoTheLibraryIsNotEditedByThePull()
    {
        var set = new RigSet
        {
            Armature = Figure(x: 100, groundY: 500, tall: 300),
            Canvas = Paper(3840, 2160),
            Heads = 3,
        };

        ArmatureFit.Onto(set, Paper(1920, 1080), HeightScale(400, 900, 50, 8), RigFit.Heads);

        Assert.Equal(300, set.Armature.Bones[0].Length, 9);
        Assert.Equal(500, set.Armature.Bones[0].Y, 9);
    }

    [Fact]
    public void ARigSetThatMeasuredNoHeadsWritesNoHeadsKey()
    {
        var manifest = new ProjectManifest { Name = "Production" };
        manifest.RigSets = [new RigSet { Armature = Figure(0, 10, 10) }];

        var json = JsonSerializer.Serialize(manifest, DocJson.Options);

        output.WriteLine(json);
        Assert.DoesNotContain("\"heads\"", json);
        Assert.DoesNotContain("\"canvas\"", json);
    }

    [Fact]
    public void AProjectWithNoSkeletonsWritesNoRigSetsKey()
    {
        var json = JsonSerializer.Serialize(new ProjectManifest { Name = "Production" }, DocJson.Options);

        Assert.DoesNotContain("\"rigSets\"", json);
    }
}
