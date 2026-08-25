using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The analysers on shot terms (Q137): the artist's ground line at any angle,
/// the tag under the playhead as the range, loop checks only where the range
/// is meant to repeat, the planted foot's law, the depth hedge, and the
/// camera-space reading. Throughout, the asset lens stays the fallback —
/// every behaviour here is reached by authoring something, never by losing
/// what the sidescroller had.
/// </summary>
public class ShotAnalyserTests
{
    // ---- the ground frame ---------------------------------------------------

    [Fact]
    public void TheArtistsGroundLineWinsAndTheFallbackIsHorizontal()
    {
        var scene = new Scene();
        scene.Guides = [new Guide { Kind = GuideKind.Line, Name = "Ground", X = 0, Y = 100, Angle = 45 }];

        var authored = Ground.Resolve(scene);
        var derived = Ground.Resolve(new Scene());

        Assert.True(authored.Authored);
        Assert.False(derived.Authored);
        // A point straight above the 45° line's anchor (screen-up) is higher
        // ground than the anchor, whatever the slope.
        Assert.True(authored.Elevation(0, 50) > 0);
        Assert.Equal(0, authored.Elevation(0, 100), 9);
        // On the slope itself, elevation stays zero while along advances.
        Assert.Equal(0, authored.Elevation(10, 110), 9);
        Assert.True(authored.Along(10, 110) > 0);
        // The fallback is exactly the asset lens: up is -Y.
        Assert.Equal(-50, derived.Elevation(0, 50), 9);
    }

    // ---- shared scaffolding -------------------------------------------------

    /// <summary>
    /// A drawing whose foot (lowest ink against the given ground) sits at
    /// ground-position <paramref name="along"/> and height <paramref name="elev"/>,
    /// with a 50-unit body rising from it, and the subject pinned by a pivot.
    /// </summary>
    private static Frame DrawingOn(
        Scene scene, GroundFrame g, string pivotId,
        double along, double elev, double subjElev)
    {
        double X(double a, double e) => g.OriginX + a * g.AlongX + e * g.UpX;
        double Y(double a, double e) => g.OriginY + a * g.AlongY + e * g.UpY;
        return new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Points =
                    [
                        new StrokePoint(X(along - 10, elev + 50), Y(along - 10, elev + 50), 1),
                        new StrokePoint(X(along, elev), Y(along, elev), 1),
                    ],
                },
            ],
            Anchors = new() { [pivotId] = new AnchorPoint(X(along, subjElev), Y(along, subjElev)) },
        };
    }

    private static (Scene Scene, Layer Layer) Walk(
        Scene scene, double[] alongs, double[] footElevs, double[] subjElevs)
    {
        var ground = Ground.Resolve(scene);
        var pivot = Anchors.Declare(scene, "hips", AnchorKind.Pivot);
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        for (var i = 0; i < alongs.Length; i++)
        {
            layer.Cels.Add(new Cel
            {
                Frame = DrawingOn(scene, ground, pivot.Id, alongs[i], footElevs[i], subjElevs[i]),
            });
        }
        return (scene, layer);
    }

    private static readonly double[] CycleFeet = [0, 8, 10, 8, 0, 8, 10, 8];

    private static readonly double[] CycleBody = [50, 54, 56, 54, 50, 54, 56, 54];

    // ---- the slope ----------------------------------------------------------

    [Fact]
    public void AWalkUpASlopeReadsItsContactsAlongTheSlope()
    {
        // The same clean cycle, climbing a 30° hill. The horizontal lens sees
        // ever-rising ink and finds one contact; the artist's ground line sees
        // the feet landing on the hill every fourth frame.
        var sloped = new Scene();
        sloped.Guides = [new Guide { Kind = GuideKind.Line, Name = "ground", X = 0, Y = 200, Angle = -30 }];
        double[] alongs = [0, 15, 30, 45, 60, 75, 90, 105];
        var (scene, layer) = Walk(sloped, alongs, CycleFeet, CycleBody);

        var report = WalkCycleAnalyser.Analyse(scene, layer, 0);

        Assert.NotNull(report);
        Assert.Equal([0, 4], report.ContactFrames);
        Assert.DoesNotContain(report.Findings, f => f.Check == WalkCheck.Contacts);

        // The same drawings with the guide taken away: the horizontal lens
        // cannot find the second footfall on a hill.
        var flat = new Scene { Anchors = scene.Anchors };
        var blind = WalkCycleAnalyser.Analyse(flat, layer, 0);
        Assert.NotNull(blind);
        Assert.Contains(blind.Findings, f => f.Check == WalkCheck.Contacts
            && f.Message.Contains("Fewer than two contacts"));
    }

    // ---- the tag range and the loop gate -------------------------------------

    [Fact]
    public void TheTagUnderThePlayheadScopesTheReadAndNamesIt()
    {
        // A clean 8-frame cycle followed by a settle the cycle checks must
        // not see. The tag says which frames are the walk.
        var scene = new Scene();
        double[] alongs = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        double[] feet = [.. CycleFeet, 30, 30];
        double[] body = [.. CycleBody, 90, 90];
        var (s, layer) = Walk(scene, alongs, feet, body);
        s.Tags = [new AnimationTag { Name = "walk", Start = 0, End = 7, Loop = true }];

        var tagged = WalkCycleAnalyser.Analyse(s, layer, 2);

        Assert.NotNull(tagged);
        Assert.Equal("walk", tagged.RangeName);
        Assert.Equal(8, tagged.Drawings);
        Assert.Empty(tagged.Findings);
    }

    [Fact]
    public void ATagThatDoesNotLoopSkipsTheSeamChecks()
    {
        // A travelling walk whose last drawing is nothing like its first —
        // which is fine, because it is a shot, and the tag says so.
        var scene = new Scene();
        double[] alongs = [0, 15, 30, 45, 60, 75, 90, 200];
        var (s, layer) = Walk(scene, alongs, CycleFeet, CycleBody);
        s.Tags = [new AnimationTag { Name = "cross", Start = 0, End = 7, Loop = false }];

        var report = WalkCycleAnalyser.Analyse(s, layer, 0);

        Assert.NotNull(report);
        Assert.DoesNotContain(report.Findings, f => f.Check == WalkCheck.Loop);
    }

    // ---- the planted foot's law ----------------------------------------------

    [Fact]
    public void ATravellingWalksPlantedFootMustHoldItsPlace()
    {
        // Contacts of two drawings each; in the second contact the foot
        // drifts 12 px along the ground while planted — the slide every
        // audience notices.
        var scene = new Scene();
        double[] alongs = [0, 20, 40, 60, 80, 100, 120, 140];
        double[] feet = [0, 0, 10, 8, 0, 0, 10, 8];
        double[] footAlong = [0, 0, 40, 60, 80, 92, 120, 140];
        var (s, layer) = Walk(scene, alongs, feet, CycleBody);
        // Rebuild the feet with controlled along positions: planted pairs at
        // (0,0) and (80,92) — the second pair slides.
        var ground = Ground.Resolve(s);
        var pivotId = s.Anchors![0].Id;
        for (var i = 0; i < alongs.Length; i++)
        {
            layer.Cels[i].Frame = DrawingOn(s, ground, pivotId, footAlong[i], feet[i], CycleBody[i]);
            // The subject still travels, so the walk reads as travelling.
            layer.Cels[i].Frame!.Anchors![pivotId] = new AnchorPoint(alongs[i], -CycleBody[i]);
        }

        var report = WalkCycleAnalyser.Analyse(s, layer, 0);

        Assert.NotNull(report);
        var finding = Assert.Single(report.Findings, f => f.Check == WalkCheck.Slide);
        Assert.Contains("slides", finding.Message);
        Assert.Equal(5, finding.Frame);
    }

    [Fact]
    public void AnInPlaceTreadMustCarryTheFootAtAConstantRate()
    {
        // In place: one long contact whose foot slips back — but jerkily.
        // 0, -12, -16, -36 is an average rate with a lurch in it.
        var scene = new Scene();
        double[] alongs = [0, 0, 0, 0, 0, 0];
        double[] feet = [0, 0, 0, 0, 10, 10];
        double[] footAlong = [0, -12, -16, -36, 0, 0];
        var (s, layer) = Walk(scene, alongs, feet, [50, 52, 54, 52, 50, 52]);
        var ground = Ground.Resolve(s);
        var pivotId = s.Anchors![0].Id;
        for (var i = 0; i < alongs.Length; i++)
        {
            layer.Cels[i].Frame = DrawingOn(s, ground, pivotId, footAlong[i], feet[i], 50);
            layer.Cels[i].Frame!.Anchors![pivotId] = new AnchorPoint(0, -50 - (i % 2));
        }

        var report = WalkCycleAnalyser.Analyse(s, layer, 0);

        Assert.NotNull(report);
        var finding = Assert.Single(report.Findings, f => f.Check == WalkCheck.Slide);
        Assert.Contains("tread", finding.Message);
    }

    [Fact]
    public void ATravellingWalksLastFootfallOpensNoStrideToJudge()
    {
        // The adversary's find: with three footfalls and a perfectly even bob,
        // the bob check used to wrap the last footfall's stride back to frame
        // 0 — a stride nobody drew — and flag a clean travelling walk as
        // limping. A shot's last contact ends the walk; it opens nothing.
        var scene = new Scene();
        double[] alongs = [0, 15, 30, 45, 60, 75, 90, 105, 120];
        double[] feet = [0, 8, 10, 8, 0, 8, 10, 8, 0];
        double[] body = [50, 54, 56, 54, 50, 54, 56, 54, 50];
        var (s, layer) = Walk(scene, alongs, feet, body);

        var report = WalkCycleAnalyser.Analyse(s, layer, 0);

        Assert.NotNull(report);
        Assert.Equal([0, 4, 8], report.ContactFrames);
        Assert.DoesNotContain(report.Findings, f => f.Check == WalkCheck.Bob);
    }

    // ---- the depth hedge ------------------------------------------------------

    [Fact]
    public void AWalkTowardCameraIsHedgedNotFlagged()
    {
        // Drawings doubling in size read as depth motion: one honest hedge,
        // and none of the flat findings that would otherwise cry wolf.
        var scene = new Scene();
        var pivot = Anchors.Declare(scene, "hips", AnchorKind.Pivot);
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        for (var i = 0; i < 6; i++)
        {
            var size = 40 + i * 12.0;
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes =
                    [
                        new Stroke
                        {
                            Points =
                            [
                                new StrokePoint(100 - size / 4, 200 - size, 1),
                                new StrokePoint(100 + size / 4, 200, 1),
                            ],
                        },
                    ],
                    Anchors = new() { [pivot.Id] = new AnchorPoint(100, 160) },
                },
            });
        }

        var report = WalkCycleAnalyser.Analyse(scene, layer, 0);

        Assert.NotNull(report);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(WalkCheck.Depth, finding.Check);
        Assert.Contains("depth", finding.Message);
    }

    // ---- the ground under the jump ---------------------------------------------

    [Fact]
    public void AJumpOnASlopeFitsTheParabolaItActuallyDescribes()
    {
        var scene = new Scene();
        scene.Guides = [new Guide { Kind = GuideKind.Line, Name = "ground", X = 0, Y = 300, Angle = -30 }];
        var ground = Ground.Resolve(scene);
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        for (var t = 0; t < 6; t++)
        {
            // Ballistic in the slope's frame: along advances evenly, height
            // rises and falls; elev(t) = 40t - 8t².
            var along = 30.0 * t;
            var elev = 40.0 * t - 8.0 * t * t;
            var x = ground.OriginX + along * ground.AlongX + elev * ground.UpX;
            var y = ground.OriginY + along * ground.AlongY + elev * ground.UpY;
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes =
                    [
                        new Stroke { Points = [new StrokePoint(x - 5, y - 5, 1), new StrokePoint(x + 5, y + 5, 1)] },
                    ],
                    Role = t is 0 or 5 ? FrameRole.Key : FrameRole.Inbetween,
                },
            });
        }

        var fit = JumpArcAnalyser.FitRun(scene, layer, 0);

        Assert.NotNull(fit);
        Assert.True(fit.Ballistic, "a ballistic jump on a slope did not read as ballistic");
        Assert.All(fit.Deviations, d => Assert.False(d.OffArc, $"frame {d.Index}: {d.Distance}"));
        Assert.All(fit.Deviations, d => Assert.True(d.Distance < 1e-6));
    }

    // ---- the camera ------------------------------------------------------------

    private static Scene ZoomingCamera()
    {
        var scene = new Scene { Width = 400, Height = 300, FrameCount = 8 };
        scene.Camera = new Camera
        {
            OutputWidth = 400,
            OutputHeight = 300,
            Keys =
            [
                new CameraKey { Frame = 0, X = 200, Y = 150, Zoom = 1 },
                new CameraKey { Frame = 4, X = 200, Y = 150, Zoom = 3 },
            ],
        };
        return scene;
    }

    [Fact]
    public void EvenWorldSpacingCanMissThroughAZoomingCamera()
    {
        // Five drawings evenly spaced in the world; the camera trebles its
        // zoom across them, so on screen the later gaps read three times the
        // earlier ones. World reading: nothing to flag. Camera reading: the
        // spacing misses — and each target maps back to a WORLD position, so
        // the ghost still sits where the artist draws.
        var scene = ZoomingCamera();
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        for (var i = 0; i < 5; i++)
        {
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes =
                    [
                        new Stroke
                        {
                            Points =
                            [
                                new StrokePoint(100 + 25 * i - 10, 140, 1),
                                new StrokePoint(100 + 25 * i + 10, 160, 1),
                            ],
                        },
                    ],
                    Role = i is 0 or 4 ? FrameRole.Key : FrameRole.Inbetween,
                },
            });
        }

        var world = SpacingAssistant.TargetsForRun(scene, layer, 0, Easing.Linear);
        var camera = SpacingAssistant.TargetsForRun(scene, layer, 0, Easing.Linear, throughCamera: true);

        Assert.All(world, t => Assert.False(t.Misses, "even world spacing flagged without the camera"));
        Assert.Contains(camera, t => t.Misses);
        // The camera-space target still hands out world coordinates: inside
        // the drawings' world span, not screen-projected magnitudes.
        Assert.All(camera, t => Assert.InRange(t.TargetX, 90, 210));
    }

    [Fact]
    public void ThroughTheCameraTheJumpVerdictTravelsWithoutACurve()
    {
        var scene = ZoomingCamera();
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        for (var t = 0; t < 6; t++)
        {
            var x = 100.0 + 20 * t;
            var y = 250.0 - 40 * t + 8 * t * t;
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes =
                    [
                        new Stroke { Points = [new StrokePoint(x - 5, y - 5, 1), new StrokePoint(x + 5, y + 5, 1)] },
                    ],
                    Role = t is 0 or 5 ? FrameRole.Key : FrameRole.Inbetween,
                },
            });
        }

        var fit = JumpArcAnalyser.FitRun(scene, layer, 0, throughCamera: true);

        Assert.NotNull(fit);
        Assert.True(fit.ThroughCamera);
        Assert.Empty(fit.Curve);
        // The deviations still carry world positions for the rings.
        Assert.All(fit.Deviations, d => Assert.InRange(d.X, 90, 210));
    }
}
