using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Angle-driven correctives — the shape an artist drew at a joint extreme,
/// eased in as that joint approaches it. Phase 4 of
/// <c>docs/DESIGN-bones.md</c>, under the Q100 decisions.
/// </summary>
/// <remarks>
/// <para>
/// Linear-blend skinning collapses the inside of a sharply bent joint. No
/// weight painting fixes it, because the correct shape at 120° is not a linear
/// blend of anything — so the artist draws it once.
/// </para>
/// <para>
/// The two claims worth guarding hardest: the offsets live in <b>rest
/// space</b>, so a fix rotates with the limb rather than staying stuck where
/// it was authored, and <b>rest is an implicit stop at 0°</b>, so an unbent
/// joint corrects nothing without a stop full of zeroes having to exist in the
/// file.
/// </para>
/// </remarks>
public class CorrectiveTests
{
    private static Corrective Elbow(string strokeId = "s", double at = 90, double dy = -10) => new()
    {
        DriverBoneId = "fore",
        Stops =
        [
            new CorrectiveStop
            {
                AngleDeg = at,
                Strokes = [new StrokeCorrection { StrokeId = strokeId, Offsets = [new PointOffset(0, dy), new PointOffset(0, dy)] }],
            },
        ],
    };

    private static Dictionary<string, BonePose> Bent(double deg) =>
        new() { ["fore"] = new BonePose { RotationDeg = deg } };

    /// <summary>
    /// One point's correction, treating an absent entry as zero.
    /// </summary>
    /// <remarks>
    /// <b>Absence is the honest zero</b> and the tests are written through this
    /// rather than against the dictionary directly: a stop that corrects
    /// nothing names no strokes, so the resolver hands back no entry and
    /// <c>PoseStroke</c> allocates nothing for the overwhelmingly common
    /// unbent case. Asserting a stored zero would have demanded the opposite.
    /// </remarks>
    private static double Fix(Corrective fix, double angle, string id = "s", bool x = false)
    {
        if (!CorrectiveOps.At(fix, angle).TryGetValue(id, out var offsets)) return 0;
        return x ? offsets[0].X : offsets[0].Y;
    }

    // ---- the ramp ------------------------------------------------------------------

    [Fact]
    public void AnUnbentJointCorrectsNothing()
    {
        // Rest is an implicit stop, so the artist authors the fix and not the
        // absence of it — and a drawing at rest is exactly what they drew.
        // Nothing is even named: no entry, so nothing downstream allocates.
        Assert.Empty(CorrectiveOps.At(Elbow(), 0));
        Assert.Equal(0, Fix(Elbow(), 0), 9);
    }

    [Fact]
    public void TheFixEasesInWithTheAngle()
    {
        Assert.Equal(-2.5, Fix(Elbow(), 22.5), 9);
        Assert.Equal(-5, Fix(Elbow(), 45), 9);
        Assert.Equal(-10, Fix(Elbow(), 90), 9);
    }

    [Fact]
    public void PastTheExtremeTheFixHoldsRatherThanSlidingBack()
    {
        // The pose track's semantics. A limb bent further than the artist
        // authored keeps the shape that was right, instead of easing back
        // toward the one that was wrong.
        Assert.Equal(-10, Fix(Elbow(), 200), 9);
    }

    [Fact]
    public void BendingTheOtherWayDoesNotFireAFixDrawnForThisWay()
    {
        // -30° is below the implicit rest stop, so it holds *rest*: an elbow
        // fix must not apply itself in reverse when the joint hyperextends.
        Assert.Equal(0, Fix(Elbow(), -30), 9);
    }

    [Fact]
    public void SeveralStopsMakeARampWithCorners()
    {
        var fix = Elbow(at: 60, dy: -10);
        fix.Stops.Add(new CorrectiveStop
        {
            AngleDeg = 130,
            Strokes = [new StrokeCorrection { StrokeId = "s", Offsets = [new PointOffset(0, 4), new PointOffset(0, 4)] }],
        });

        // Up to -10 by 60, then back through zero and out to +4 by 130 — the
        // elbow that needs one fix mid-bend and a different one at the extreme.
        Assert.Equal(-10, Fix(fix, 60), 9);
        Assert.Equal(-3, Fix(fix, 95), 9);
        Assert.Equal(4, Fix(fix, 130), 9);
    }

    [Fact]
    public void AStopAtZeroReplacesTheImplicitOne()
    {
        var fix = Elbow(at: 90);
        fix.Stops.Add(new CorrectiveStop
        {
            AngleDeg = 0,
            Strokes = [new StrokeCorrection { StrokeId = "s", Offsets = [new PointOffset(0, 6), new PointOffset(0, 6)] }],
        });

        // A drawing that needs fixing at rest says so by authoring the stop,
        // and then rest is no longer zero.
        Assert.Equal(6, Fix(fix, 0), 9);
        Assert.Equal(-2, Fix(fix, 45), 9);
    }

    [Fact]
    public void ALineNamedByOnlyOneStopRampsFromNothing()
    {
        var fix = Elbow(at: 60);
        fix.Stops.Add(new CorrectiveStop
        {
            AngleDeg = 120,
            Strokes =
            [
                new StrokeCorrection { StrokeId = "s", Offsets = [new PointOffset(0, -10), new PointOffset(0, -10)] },
                new StrokeCorrection { StrokeId = "other", Offsets = [new PointOffset(3, 0), new PointOffset(3, 0)] },
            ],
        });

        // Adding a second line's fix at a later stop must not require
        // restating it at the first — it ramps in from where it was, which is
        // nowhere.
        Assert.Equal(0, Fix(fix, 60, "other", x: true), 9);
        Assert.Equal(1.5, Fix(fix, 90, "other", x: true), 9);
        Assert.Equal(3, Fix(fix, 120, "other", x: true), 9);
    }

    // ---- several correctives -------------------------------------------------------

    [Fact]
    public void TwoCorrectivesOnOneLineAdd()
    {
        var elbow = Elbow(at: 90, dy: -10);
        var wrist = new Corrective
        {
            DriverBoneId = "hand",
            Stops =
            [
                new CorrectiveStop
                {
                    AngleDeg = 90,
                    Strokes = [new StrokeCorrection { StrokeId = "s", Offsets = [new PointOffset(0, -4), new PointOffset(0, -4)] }],
                },
            ],
        };
        var pose = new Dictionary<string, BonePose>
        {
            ["fore"] = new BonePose { RotationDeg = 90 },
            ["hand"] = new BonePose { RotationDeg = 45 },
        };

        var resolved = CorrectiveOps.Resolve([elbow, wrist], pose);

        // Independent departures, so they sum. Anything else makes one of them
        // quietly conditional on the other.
        Assert.Equal(-12, resolved["s"][0].Y, 9);
    }

    [Fact]
    public void ADrawingWithNoCorrectivesResolvesToNothing()
    {
        Assert.Empty(CorrectiveOps.Resolve(null, Bent(90)));
        Assert.Empty(CorrectiveOps.Resolve([], Bent(90)));
        // And a corrective with no stops is inert rather than throwing.
        Assert.Empty(CorrectiveOps.Resolve([new Corrective { DriverBoneId = "fore" }], Bent(90)));
    }

    // ---- through the skinning ------------------------------------------------------

    private static (Doc Doc, Frame Frame, Stroke Stroke) Limb()
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Armature = new Armature
        {
            Bones =
            [
                new Bone { Id = "upper", X = 100, Y = 100, Length = 50 },
                new Bone { Id = "fore", ParentId = "upper", X = 50, Connected = true, Length = 50 },
            ],
        };
        var stroke = new Stroke { Points = [new StrokePoint(150, 100, 1), new StrokePoint(190, 100, 1)] };
        Skinning.AssignAll(stroke, "fore");
        var frame = new Frame { Strokes = [stroke] };
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = frame }] });
        return (doc, frame, stroke);
    }

    [Fact]
    public void TheFixIsInRestSpaceSoItTurnsWithTheLimb()
    {
        var (doc, _, stroke) = Limb();
        // Push the line 10px along rest -y. The forearm lies along +x at rest,
        // so at rest the fix is straight up.
        var correction = new PointOffset[] { new(0, -10), new(0, -10) };

        var pose = Bent(90);
        var posed = Skinning.PoseStroke(stroke, doc.Armature!, pose, null, correction);

        // Bent 90° the limb points down, so "up in rest space" is now +x in
        // document space. A posed-space offset would still be pointing at -y,
        // which is the whole reason the record stores rest space.
        var unfixed = Skinning.PoseStroke(stroke, doc.Armature!, pose);
        Assert.Equal(10, posed.Points[0].X - unfixed.Points[0].X, 6);
        Assert.Equal(0, posed.Points[0].Y - unfixed.Points[0].Y, 6);
    }

    [Fact]
    public void ACorrectionWhoseCountNoLongerMatchesIsDroppedRatherThanSlid()
    {
        var (doc, _, stroke) = Limb();
        // The artist re-shaped the line since drawing the fix: three points now.
        stroke.Points.Add(new StrokePoint(230, 100, 1));

        var stale = new PointOffset[] { new(0, -10), new(0, -10) };
        var withStale = Skinning.PoseStroke(stroke, doc.Armature!, Bent(90), null, stale);
        var plain = Skinning.PoseStroke(stroke, doc.Armature!, Bent(90));

        // Applying two offsets to the first two of three points would move the
        // wrong part of the line — a wrong drawing rather than an uncorrected
        // one, and silently.
        Assert.Equal(plain.Points.Count, withStale.Points.Count);
        for (var i = 0; i < plain.Points.Count; i++)
            Assert.Equal(plain.Points[i].X, withStale.Points[i].X, 9);
    }

    [Fact]
    public void ThePosedRenderCarriesTheDrawingsCorrective()
    {
        var (doc, frame, stroke) = Limb();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["fore"] = new BonePose { RotationDeg = 90 } } }],
        };
        frame.Correctives = [Elbow(stroke.Id, at: 90, dy: -10)];

        var posed = Skinning.PoseFrameForRender(doc, frame, 0);
        var without = Skinning.PoseFrameForRender(doc, new Frame { Strokes = [stroke.Clone(newId: false)] }, 0);

        Assert.NotEqual(without.Strokes[0].Points[0].X, posed.Strokes[0].Points[0].X, 6);
        // Invariant 1: the record is untouched — a corrective decides where
        // marks are STAMPED, exactly as a pose does.
        Assert.Equal(150, frame.Strokes[0].Points[0].X, 9);
    }

    [Fact]
    public void BakingFreezesWhatWasOnScreenCorrectiveIncluded()
    {
        var (doc, frame, stroke) = Limb();
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["fore"] = new BonePose { RotationDeg = 90 } } }],
        };
        frame.Correctives = [Elbow(stroke.Id, at: 90, dy: -10)];
        var live = Skinning.PoseFrameForRender(doc, frame, 0);

        Skinning.BakeFrame(frame, doc.Armature!, ArmatureOps.PoseAt(doc.Scene.PoseTrack, 0));

        // A freeze that dropped the fix would give back the collapsed joint
        // the artist drew it to cure.
        for (var i = 0; i < live.Strokes[0].Points.Count; i++)
        {
            Assert.Equal(live.Strokes[0].Points[i].X, frame.Strokes[0].Points[i].X, 9);
            Assert.Equal(live.Strokes[0].Points[i].Y, frame.Strokes[0].Points[i].Y, 9);
        }
    }

    // ---- the record ----------------------------------------------------------------

    [Fact]
    public void ADrawingWithNoCorrectiveWritesNoKeys()
    {
        var json = DocJson.Serialize(DocumentFactory.CreateDoc());

        Assert.DoesNotContain("\"correctives\"", json);
        Assert.DoesNotContain("\"hasCorrectives\"", json);
    }

    [Fact]
    public void ACorrectiveSurvivesASaveAndLoadAndSolvesTheSame()
    {
        var (doc, frame, stroke) = Limb();
        frame.Correctives = [Elbow(stroke.Id, at: 90, dy: -10)];

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var reloaded = back.Scene.Layers[^1].Cels[0].Frame!;

        var fix = Assert.Single(reloaded.Correctives!);
        Assert.Equal("fore", fix.DriverBoneId);
        Assert.Equal(90, Assert.Single(fix.Stops).AngleDeg);
        // Bit-identical, not merely close: a corrective is replayed on load, on
        // undo and by the inbetweener.
        Assert.Equal(
            CorrectiveOps.At(frame.Correctives[0], 45)[stroke.Id][0].Y,
            CorrectiveOps.At(fix, 45)[stroke.Id][0].Y);
    }

    [Fact]
    public void CloningADrawingCopiesItsCorrectivesDeeply()
    {
        var (_, frame, stroke) = Limb();
        frame.Correctives = [Elbow(stroke.Id)];

        var copy = frame.Clone();
        copy.Correctives![0].Stops[0].Strokes[0].Offsets[0] = new PointOffset(99, 99);
        copy.Correctives[0].Stops.Add(new CorrectiveStop { AngleDeg = 1 });

        Assert.Single(frame.Correctives[0].Stops);
        Assert.Equal(0, frame.Correctives[0].Stops[0].Strokes[0].Offsets[0].X, 9);
    }

    // ---- authoring: posed edit back to rest space -----------------------------------

    [Fact]
    public void ADragInPosedSpaceComesBackAsTheRestOffsetThatProducesIt()
    {
        var (doc, _, stroke) = Limb();
        var pose = Bent(90);

        // What the artist sees, and where they drag the first point to.
        var posed = Skinning.PoseControlPoints(stroke, doc.Armature!, pose);
        var target = new List<StrokePoint>(posed) { [0] = posed[0] with { X = posed[0].X + 7, Y = posed[0].Y - 3 } };

        var offsets = Skinning.RestOffsetsFor(stroke, doc.Armature!, pose, target);

        // The round trip is the claim: feeding those offsets back through the
        // pose must land the point exactly where it was dragged. Anything less
        // and a captured fix is not the fix the artist drew.
        var again = Skinning.PoseControlPoints(stroke, doc.Armature!, pose, null, offsets);
        Assert.Equal(target[0].X, again[0].X, 6);
        Assert.Equal(target[0].Y, again[0].Y, 6);
        // And the points nobody dragged did not move.
        Assert.Equal(posed[1].X, again[1].X, 6);
        Assert.Equal(posed[1].Y, again[1].Y, 6);
    }

    [Fact]
    public void TheOffsetIsExpressedInRestSpaceNotInWhatWasDragged()
    {
        var (doc, _, stroke) = Limb();
        var pose = Bent(90);
        var posed = Skinning.PoseControlPoints(stroke, doc.Armature!, pose);
        // Dragged 10px along document +x, with the forearm bent to point down.
        var target = new List<StrokePoint>(posed) { [0] = posed[0] with { X = posed[0].X + 10 } };

        var offsets = Skinning.RestOffsetsFor(stroke, doc.Armature!, pose, target);

        // Rest space has the forearm along +x, so a document-space +x drag at
        // 90° is a rest-space -y offset. Storing the raw drag would have given
        // (10, 0) and been wrong the moment the shoulder moved.
        Assert.Equal(0, offsets[0].X, 6);
        Assert.Equal(-10, offsets[0].Y, 6);
    }

    [Fact]
    public void AnUnboundPointKeepsTheDragItWasGiven()
    {
        var (doc, _, stroke) = Limb();
        stroke.Weights = null;   // nothing moves it, so rest space IS document space
        var posed = Skinning.PoseControlPoints(stroke, doc.Armature!, Bent(90));
        var target = new List<StrokePoint>(posed) { [0] = posed[0] with { X = posed[0].X + 5 } };

        var offsets = Skinning.RestOffsetsFor(stroke, doc.Armature!, Bent(90), target);

        // The blend is singular here — dividing by its determinant would have
        // thrown or produced infinities.
        Assert.Equal(5, offsets[0].X, 6);
        Assert.Equal(0, offsets[0].Y, 6);
    }

    [Fact]
    public void PosingControlPointsGivesOneOutputPerInput()
    {
        var (doc, _, stroke) = Limb();

        // The authoring path must not densify, or the diff at the end of a
        // capture would compare lists of different lengths.
        var authoring = Skinning.PoseControlPoints(stroke, doc.Armature!, Bent(90));
        Assert.Equal(stroke.Points.Count, authoring.Count);

        // And it must agree with the drawing path where they both have a
        // point, or the shape the artist edits during a capture is not the
        // shape they will get.
        var drawn = Skinning.PoseStroke(stroke, doc.Armature!, Bent(90));
        Assert.Equal(authoring[0].X, drawn.Points[0].X, 9);
        Assert.Equal(authoring[0].Y, drawn.Points[0].Y, 9);
        Assert.Equal(authoring[^1].X, drawn.Points[^1].X, 9);
        Assert.Equal(authoring[^1].Y, drawn.Points[^1].Y, 9);
    }
}
