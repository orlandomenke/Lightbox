using System.Reflection;
using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// Scaling the artwork rather than the paper.
/// </summary>
/// <remarks>
/// The interesting failure here is not arithmetic, it is omission: a
/// coordinate the visitor forgets stays at its old scale and lands somewhere
/// wrong, and nothing goes red. <see cref="EveryCoordinateOnTheSceneIsAccountedFor"/>
/// is the guard against that — the same job
/// <c>StrokeCloneTests</c> does for copying.
/// </remarks>
public class ImageResizeTests
{
    /// <summary>Records what it was asked to scale; scaling pixels is Skia's job.</summary>
    private sealed class FakeResampler : IPixelResampler
    {
        public List<(string Png, double X, double Y)> Calls { get; } = [];

        public string? Resample(string pngBase64, double scaleX, double scaleY)
        {
            Calls.Add((pngBase64, scaleX, scaleY));
            return pngBase64 + "@scaled";
        }
    }

    private static Doc DocWithAStroke(out Stroke stroke)
    {
        var doc = new Doc();
        stroke = new Stroke
        {
            Points = [new StrokePoint(100, 50, 1), new StrokePoint(200, 150, 0.5)],
            Brush = new BrushSettings { Size = 10, TextureScale = 12 },
        };
        var captured = stroke;
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame { Strokes = [captured] } }],
        });
        return doc;
    }

    [Fact]
    public void DoublingTheImageDoublesTheGeometryAndTheBrush()
    {
        var doc = DocWithAStroke(out var stroke);

        Assert.True(ImageResize.Apply(doc, 1920, 1080, new FakeResampler()));

        Assert.Equal(1920, doc.Scene.Width);
        Assert.Equal(1080, doc.Scene.Height);
        Assert.Equal(200, stroke.Points[0].X);
        Assert.Equal(100, stroke.Points[0].Y);
        Assert.Equal(400, stroke.Points[1].X);
        // The mark has to grow with the drawing, or a doubled document comes
        // back as the same picture drawn with a pen half the width.
        Assert.Equal(20, stroke.Brush.Size);
        Assert.Equal(24, stroke.Brush.TextureScale);
        // Pressure is not a coordinate.
        Assert.Equal(0.5, stroke.Points[1].Pressure);
    }

    [Fact]
    public void ALayerMaskScalesWithTheDrawingItCarves()
    {
        // The mask lives on the layer, not in a cel, so the cel walk missed
        // it — and an unscaled mask means the doubled content slides out from
        // under its own carve. Caught by CI's coordinate guard growing a
        // property, found to be real here.
        var doc = DocWithAStroke(out _);
        var mask = new LayerMask();
        mask.Frame.Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(100, 50, 1)],
            Brush = new BrushSettings { Size = 10 },
        });
        doc.Scene.Layers[0].Mask = mask;

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        Assert.Equal(200, mask.Frame.Strokes[0].Points[0].X);
        Assert.Equal(100, mask.Frame.Strokes[0].Points[0].Y);
        Assert.Equal(20, mask.Frame.Strokes[0].Brush.Size);
    }

    [Fact]
    public void AnUnlinkedResizeMovesGeometryExactlyAndTheMarkByTheAverage()
    {
        // A dab has one diameter and no axes, so a 2x1 resize cannot scale the
        // mark faithfully. The geometric mean is the stated compromise; what
        // must not happen is the geometry being averaged too.
        var doc = DocWithAStroke(out var stroke);

        ImageResize.Apply(doc, 1920, 540, new FakeResampler());

        Assert.Equal(200, stroke.Points[0].X);
        Assert.Equal(50, stroke.Points[0].Y);
        Assert.Equal(10 * Math.Sqrt(2), stroke.Brush.Size, 6);
    }

    [Fact]
    public void TheOriginScalesWithTheDrawingItOffsets()
    {
        var doc = new Doc();
        CanvasResize.Apply(doc.Scene, 1200, 540, ResizeAnchor.TopRight);
        Assert.Equal(-240, doc.Scene.Left);

        ImageResize.Apply(doc, 2400, 1080, new FakeResampler());

        // Twice the document, twice the margin that was added to its left.
        Assert.Equal(-480, doc.Scene.Left);
        Assert.Equal(2400, doc.Scene.Width);
    }

    [Fact]
    public void ThePixelPayloadsAreHandedToTheResampler()
    {
        var doc = DocWithAStroke(out var stroke);
        stroke.Baked = new BakedSample { PngBase64 = "baked", X = 30, Y = 40 };
        doc.Scene.Layers[0].Cels[0].Frame!.PngBase64 = "baseline";
        var resampler = new FakeResampler();

        ImageResize.Apply(doc, 1920, 1080, resampler);

        Assert.Equal(2, resampler.Calls.Count);
        Assert.All(resampler.Calls, c => Assert.Equal(2.0, c.X));
        Assert.Equal("baked@scaled", stroke.Baked!.PngBase64);
        Assert.Equal(60, stroke.Baked.X);
        Assert.Equal(80, stroke.Baked.Y);
        Assert.Equal("baseline@scaled", doc.Scene.Layers[0].Cels[0].Frame!.PngBase64);
    }

    [Fact]
    public void APayloadThatWillNotResampleIsDroppedRatherThanLeftAtTheOldSize()
    {
        var doc = DocWithAStroke(out var stroke);
        stroke.Baked = new BakedSample { PngBase64 = "corrupt", X = 1, Y = 1 };

        ImageResize.Apply(doc, 1920, 1080, new NullResampler());

        // Keeping it would render a patch of the old scale into the middle of
        // a rescaled drawing, which nobody would trace back to a resize.
        Assert.Null(stroke.Baked);
    }

    private sealed class NullResampler : IPixelResampler
    {
        public string? Resample(string pngBase64, double scaleX, double scaleY) => null;
    }

    [Fact]
    public void SettingTheResolutionAloneResamplesNothing()
    {
        var doc = DocWithAStroke(out var stroke);
        var resampler = new FakeResampler();

        Assert.True(ImageResize.Apply(doc, 960, 540, resampler, ppi: 300));

        Assert.Equal(300, doc.Scene.Ppi);
        Assert.Equal(960, doc.Scene.Width);
        Assert.Equal(100, stroke.Points[0].X);
        Assert.Empty(resampler.Calls);
    }

    [Fact]
    public void AResizeThatAsksForNothingReportsThat()
    {
        var doc = DocWithAStroke(out _);

        Assert.False(ImageResize.Apply(doc, 960, 540, new FakeResampler()));
        Assert.False(ImageResize.Apply(doc, 960, 540, new FakeResampler(), ppi: 72));
        Assert.False(ImageResize.Apply(doc, 0, 540, new FakeResampler()));
    }

    [Fact]
    public void EverythingHangingOffTheSceneMovesTogether()
    {
        var doc = new Doc();
        doc.Scene.Guides = [new Guide { X = 100, Y = 50, Spacing = 32 }];
        doc.Scene.Camera = new Camera { Keys = [new CameraKey { X = 300, Y = 200, Zoom = 2 }] };
        doc.Scene.Pivot = new Pivot { X = 480, Y = 540 };
        doc.ClipRegions["c"] = new ClipRegion
        {
            Contours = [[new StrokePoint(10, 20, 1)]],
            Feather = 4,
        };
        doc.Armature = new Armature
        {
            Bones = [new Bone { Id = "spine", X = 480, Y = 270, Length = 100, RotationDeg = 30 }],
        };
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["spine"] = new BonePose { X = 12, Y = 6, RotationDeg = 15 } } }],
        };
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame
            {
                Placements = [new SymbolPlacement { X = 10, Y = 20, ScaleX = 1, ScaleY = 1 }],
                Anchors = new Dictionary<string, AnchorPoint> { ["hand"] = new(60, 70) },
                Shapes = new Dictionary<string, ShapeBox> { ["body"] = new(1, 2, 3, 4) },
            } }],
        });

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        Assert.Equal(200, doc.Scene.Guides![0].X);
        Assert.Equal(64, doc.Scene.Guides[0].Spacing);
        Assert.Equal(600, doc.Scene.Camera!.Keys[0].X);
        // Zoom is left alone, by the owner's decision (Q257) and against the
        // recommendation. It maps document pixels to output pixels, so after
        // a doubling the same shot frames half the width of the drawing it
        // did; the shots of a resized document are re-keyed by hand.
        Assert.Equal(2, doc.Scene.Camera.Keys[0].Zoom);
        Assert.Equal(960, doc.Scene.Pivot!.X);
        Assert.Equal(20, doc.ClipRegions["c"].Contours[0][0].X);
        Assert.Equal(8, doc.ClipRegions["c"].Feather);

        var bone = doc.Armature!.Bones[0];
        Assert.Equal(960, bone.X);
        Assert.Equal(540, bone.Y);
        Assert.Equal(200, bone.Length);
        // Rotation is not a length and does not scale.
        Assert.Equal(30, bone.RotationDeg);
        var spinePose = doc.Scene.PoseTrack!.Keys[0].Bones["spine"];
        Assert.Equal(24, spinePose.X);
        Assert.Equal(12, spinePose.Y);
        Assert.Equal(15, spinePose.RotationDeg);

        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal(20, frame.Placements![0].X);
        // The symbol keeps its own space — it is shared, and this document
        // rescaling must not reach into it. The placement carries the change.
        Assert.Equal(2, frame.Placements[0].ScaleX);
        Assert.Equal(120, frame.Anchors!["hand"].X);
        Assert.Equal(new ShapeBox(2, 4, 6, 8), frame.Shapes!["body"]);
    }

    /// <summary>
    /// B379, the half that bit through undo: a resize is one snapshot step,
    /// the snapshot is a <c>Doc.Clone</c>, and a stroke's axis used to be
    /// shared between the two — so the resize scaled the snapshot's axis as
    /// well and undo put the points back about a centre that stayed scaled.
    /// </summary>
    [Fact]
    public void UndoingAResizePutsEachStrokesAxisBackWhereItWas()
    {
        var doc = DocWithAStroke(out var stroke);
        stroke.Symmetry = new SymmetryAxis { CenterX = 480, CenterY = 270, AngleDeg = 90, Order = 1, Mirror = true };
        var editor = new DocumentEditor(doc);

        editor.Perform(d => ImageResize.Apply(d, 1920, 1080, new FakeResampler()), "Resize image");
        var scaled = editor.Doc.Scene.Layers[0].Cels[0].Frame!.Strokes[0];
        Assert.Equal(960, scaled.Symmetry!.CenterX);
        Assert.Equal(400, scaled.Points[1].X);

        editor.Undo();
        var restored = editor.Doc.Scene.Layers[0].Cels[0].Frame!.Strokes[0];
        Assert.Equal(200, restored.Points[1].X);
        Assert.Equal(480, restored.Symmetry!.CenterX);
        Assert.Equal(270, restored.Symmetry.CenterY);
    }

    /// <summary>
    /// B380: the baked sample is shared between a stroke and its undo-snapshot
    /// clone on purpose, so a resize must replace it rather than edit it, or
    /// undo brings the old points back under resampled, re-placed pixels.
    /// </summary>
    [Fact]
    public void UndoingAResizeRestoresABakedStrokesPixelsAndPlacement()
    {
        var doc = DocWithAStroke(out var stroke);
        stroke.Baked = new BakedSample { PngBase64 = "ORIGINAL", X = 30, Y = 40 };
        var editor = new DocumentEditor(doc);

        editor.Perform(d => ImageResize.Apply(d, 1920, 1080, new FakeResampler()), "Resize image");
        var scaled = editor.Doc.Scene.Layers[0].Cels[0].Frame!.Strokes[0].Baked!;
        Assert.Equal("ORIGINAL@scaled", scaled.PngBase64);
        Assert.Equal(60, scaled.X);
        Assert.Equal(80, scaled.Y);

        editor.Undo();
        var restored = editor.Doc.Scene.Layers[0].Cels[0].Frame!.Strokes[0].Baked!;
        Assert.Equal("ORIGINAL", restored.PngBase64);
        Assert.Equal(30, restored.X);
        Assert.Equal(40, restored.Y);
    }

    [Fact]
    public void EveryRigIsRescaledNotOnlyTheFirst()
    {
        // B447. Two characters in one document: the resize walked
        // `doc.Armature`, which is the first rig, while the pose keys of
        // every rig were scaled — so the second character's skeleton stood
        // at the old size under a drawing and a pose track at the new one.
        var doc = new Doc
        {
            Armatures =
            [
                new Armature { Name = "Knight", Bones = [new Bone { Id = "k", X = 100, Y = 50, Length = 40 }] },
                new Armature
                {
                    Name = "Dog",
                    Bones =
                    [
                        new Bone { Id = "d", X = 300, Y = 150, Length = 60, RotationDeg = 20 },
                        new Bone { Id = "d-tail", ParentId = "d", X = 10, Y = 4, Length = 30 },
                    ],
                },
            ],
        };
        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["d"] = new BonePose { X = 8, Y = 2 } } }],
        };

        ImageResize.Apply(doc, doc.Scene.Width * 2, doc.Scene.Height * 2, new FakeResampler());

        var knight = doc.Armatures[0].Bones[0];
        Assert.Equal((200.0, 100.0, 80.0), (knight.X, knight.Y, knight.Length));
        var dog = doc.Armatures[1].Bones[0];
        Assert.Equal((600.0, 300.0, 120.0), (dog.X, dog.Y, dog.Length));
        Assert.Equal(20, dog.RotationDeg);
        var tail = doc.Armatures[1].Bones[1];
        Assert.Equal((20.0, 8.0, 60.0), (tail.X, tail.Y, tail.Length));
        // The key was always scaled; now the bone it moves is at the same scale.
        Assert.Equal(16, doc.Scene.PoseTrack.Keys[0].Bones["d"].X);
    }

    [Fact]
    public void AJointZoneTheArtistSetGrowsWithTheDrawing()
    {
        // B447, the same omission one field over. A joint's blend zone is a
        // distance in document pixels; left alone, a doubled arm bends over
        // half the stretch it used to, and every joint-weighted line on it
        // re-poses differently. The default zone needs nothing — it is
        // derived from the bones' lengths, which do scale.
        var doc = new Doc
        {
            Armature = new Armature
            {
                Bones =
                [
                    new Bone { Id = "upper", X = 100, Y = 100, Length = 100 },
                    new Bone { Id = "fore", ParentId = "upper", Connected = true, Length = 100, JointZone = 30 },
                    new Bone { Id = "hand", ParentId = "fore", Connected = true, Length = 40 },
                ],
            },
        };
        var defaultBefore = Skinning.JointZoneOf(doc.Armature, doc.Armature.Bones[2]);

        // Twice as wide and the same height, so the factor matters: a zone
        // is a distance along a limb that may point anywhere, and it takes
        // the one number a bone's length takes — the mark's — not the
        // width's. (Resized evenly, this test could not tell the two apart;
        // the adversary scaled the zone by the width and nothing noticed.)
        ImageResize.Apply(doc, doc.Scene.Width * 2, doc.Scene.Height, new FakeResampler());

        var lengthGrewBy = doc.Armature.Bones[0].Length / 100;
        Assert.Equal(Math.Sqrt(2), lengthGrewBy, 9);
        Assert.Equal(30 * lengthGrewBy, doc.Armature.Bones[1].JointZone!.Value, 9);
        Assert.Null(doc.Armature.Bones[2].JointZone);
        Assert.Equal(defaultBefore * lengthGrewBy, Skinning.JointZoneOf(doc.Armature, doc.Armature.Bones[2]), 9);
    }

    [Fact]
    public void ADrawnJointFixGrowsWithTheLineItCorrects()
    {
        // B447 again: a corrective is offsets from a stroke's rest points, so
        // it is made of distances on the paper. The points were doubled and
        // the fix was not, which leaves the artist's drawn elbow at half
        // strength on every frame that uses it.
        var doc = DocWithAStroke(out var stroke);
        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Correctives =
        [
            new Corrective
            {
                DriverBoneId = "fore",
                Stops =
                [
                    new CorrectiveStop
                    {
                        AngleDeg = 90,
                        Strokes = [new StrokeCorrection { StrokeId = stroke.Id, Offsets = [new PointOffset(3, -4), new PointOffset(0, 5)] }],
                    },
                ],
            },
        ];

        ImageResize.Apply(doc, 1920, 540, new FakeResampler());   // twice as wide, the same height

        var offsets = frame.Correctives[0].Stops[0].Strokes[0].Offsets;
        Assert.Equal(new PointOffset(6, -4), offsets[0]);
        Assert.Equal(new PointOffset(0, 5), offsets[1]);
        Assert.Equal(90, frame.Correctives[0].Stops[0].AngleDeg);
    }

    [Fact]
    public void ADrawingHeldAcrossSeveralCelsIsScaledOnce()
    {
        // B463. A hold is one drawing standing in several cels — that is how
        // an effect baked on 2s is stored — and the resize walked the cels,
        // so it scaled that one drawing once for each cel holding it: twice
        // the image, four times the smoke, on every held frame.
        var held = new Frame
        {
            Strokes =
            [
                new Stroke
                {
                    Points = [new StrokePoint(100, 50, 1), new StrokePoint(200, 150, 1)],
                    Brush = new BrushSettings { Size = 10 },
                    Path = new StrokePath { Nodes = [PathNode.At(100, 50), PathNode.At(200, 150)] },
                    Baked = new BakedSample { PngBase64 = "ink", X = 30, Y = 40 },
                },
            ],
            PngBase64 = "scan",
            Anchors = new Dictionary<string, AnchorPoint> { ["hand"] = new(60, 70) },
        };
        var alone = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(10, 10, 1)] }] };
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = held }, new Cel { Frame = held }, new Cel { Frame = held }, new Cel { Frame = alone }],
        });
        var resampler = new FakeResampler();

        ImageResize.Apply(doc, doc.Scene.Width * 2, doc.Scene.Height * 2, resampler);

        var stroke = held.Strokes[0];
        Assert.Equal((200.0, 100.0), (stroke.Points[0].X, stroke.Points[0].Y));
        Assert.Equal(20, stroke.Brush.Size);
        Assert.Equal(200, stroke.Path!.Nodes[0].X);
        Assert.Equal((60, 80), (stroke.Baked!.X, stroke.Baked.Y));
        Assert.Equal(new AnchorPoint(120, 140), held.Anchors["hand"]);
        // The pixels went through the resampler once each: a scan resampled
        // three times is three times as soft.
        Assert.Equal("scan@scaled", held.PngBase64);
        Assert.Equal("ink@scaled", stroke.Baked.PngBase64);
        Assert.Equal(2, resampler.Calls.Count);
        // And the drawing beside it, held by one cel, is as it always was.
        Assert.Equal(20, alone.Strokes[0].Points[0].X);
    }

    [Fact]
    public void TwoDrawingsThatShareAnIdAreStillTwoDrawings()
    {
        // "Seen it already" is about the object, never the id: a copied cel
        // or a hand-edited file can carry one id twice, and skipping the
        // second would leave a drawing at the old size.
        var first = new Frame { Id = "dup", Strokes = [new Stroke { Points = [new StrokePoint(100, 50, 1)] }] };
        var second = new Frame { Id = "dup", Strokes = [new Stroke { Points = [new StrokePoint(100, 50, 1)] }] };
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = first }, new Cel { Frame = second }] });

        ImageResize.Apply(doc, doc.Scene.Width * 2, doc.Scene.Height * 2, new FakeResampler());

        Assert.Equal(200, first.Strokes[0].Points[0].X);
        Assert.Equal(200, second.Strokes[0].Points[0].X);
    }

    [Fact]
    public void ADrawingThatIsAlsoItsLayersMaskIsScaledOnce()
    {
        // The same walk visits a layer's mask after its cels. Nothing makes
        // one drawing both today; the walk should not depend on that.
        var both = new Frame { Strokes = [new Stroke { Points = [new StrokePoint(100, 50, 1)] }] };
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = both }], Mask = new LayerMask { Frame = both } });

        ImageResize.Apply(doc, doc.Scene.Width * 2, doc.Scene.Height * 2, new FakeResampler());

        Assert.Equal(200, both.Strokes[0].Points[0].X);
    }

    [Fact]
    public void APenPathStaysOnTheLineItDescribes()
    {
        // StrokePath's invariant is absolute: map the nodes with the points,
        // or drop the path. The resize scaled the points and did neither, so
        // the next time the artist moved a node the line was re-drawn from
        // nodes still standing at the old size.
        var doc = DocWithAStroke(out var stroke);
        stroke.Path = new StrokePath
        {
            Nodes =
            [
                new PathNode(100, 50, 1, 0, 0, 30, -10, Corner: false),
                new PathNode(200, 150, 0.5, -20, 15, 0, 0, Corner: false),
            ],
        };
        stroke.Points = Lightbox.Core.Geometry.PathFlattener.Flatten(stroke.Path);

        ImageResize.Apply(doc, 1920, 540, new FakeResampler());   // twice as wide, the same height

        Assert.Equal(new PathNode(200, 50, 1, 0, 0, 60, -10, Corner: false), stroke.Path.Nodes[0]);
        Assert.Equal(new PathNode(400, 150, 0.5, -40, 15, 0, 0, Corner: false), stroke.Path.Nodes[1]);

        // And the two descriptions still agree: the line the nodes draw now
        // starts and ends where the scaled points do, and no point of the
        // drawing is off it by more than the flattening's own tolerance.
        var redrawn = Lightbox.Core.Geometry.PathFlattener.Flatten(stroke.Path);
        Assert.Equal(stroke.Points[0].X, redrawn[0].X, 9);
        Assert.Equal(stroke.Points[^1].X, redrawn[^1].X, 9);
        Assert.Equal(stroke.Points[^1].Y, redrawn[^1].Y, 9);
        var worst = stroke.Points.Max(point => DistanceToPolyline(point, redrawn));
        Assert.True(worst < 1.0, $"a point of the drawing is {worst:F2} off the line its path now describes");
    }

    private static double DistanceToPolyline(StrokePoint p, IReadOnlyList<StrokePoint> line)
    {
        var best = double.MaxValue;
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var (ax, ay, bx, by) = (line[i].X, line[i].Y, line[i + 1].X, line[i + 1].Y);
            var (dx, dy) = (bx - ax, by - ay);
            var length = dx * dx + dy * dy;
            var t = length > 0 ? Math.Clamp(((p.X - ax) * dx + (p.Y - ay) * dy) / length, 0, 1) : 0;
            var (cx, cy) = (ax + dx * t - p.X, ay + dy * t - p.Y);
            best = Math.Min(best, Math.Sqrt(cx * cx + cy * cy));
        }
        return best;
    }

    [Fact]
    public void TypeCanStillBeRetypedWhereItStandsAfterAResize()
    {
        // The glyphs are ordinary strokes and were scaled. The element beside
        // them — what makes the text a sentence somebody can edit — was not,
        // so changing one letter re-set the whole block at the old size in
        // the old place.
        var doc = DocWithAStroke(out _);
        doc.Texts = new Dictionary<string, TextElement>
        {
            ["t"] = new() { Id = "t", Text = "Hello", Size = 48, X = 100, Y = 200, Tracking = 25, LineHeight = 60 },
            ["u"] = new() { Id = "u", Text = "there", Size = 24, X = 10, Y = 20 },
        };

        // Twice as wide and the same height, so each number has to take the
        // right factor: the place its own axis's, the sizes the mark's. (Resized
        // evenly, the adversary swapped the axes and nothing noticed.)
        ImageResize.Apply(doc, 1920, 540, new FakeResampler());
        var mark = Math.Sqrt(2);

        var t = doc.Texts["t"];
        Assert.Equal((200.0, 200.0), (t.X, t.Y));
        Assert.Equal(48 * mark, t.Size, 9);
        Assert.Equal(60 * mark, t.LineHeight!.Value, 9);
        // Thousandths of an em: a ratio, and it means the same at any size.
        Assert.Equal(25, t.Tracking);
        // Leading the typeface still decides stays undecided.
        Assert.Null(doc.Texts["u"].LineHeight);
        Assert.Equal(24 * mark, doc.Texts["u"].Size, 9);
        Assert.Equal((20.0, 20.0), (doc.Texts["u"].X, doc.Texts["u"].Y));
    }

    [Fact]
    public void AnEffectCanBeBakedAgainWhereItStandsAfterAResize()
    {
        // Baked smoke is ordinary strokes and was scaled. The element that
        // bakes it again was not: its grid stayed where it was, at the old
        // size of cell, so a re-bake put small smoke in the old corner.
        var doc = DocWithAStroke(out _);
        doc.Sims = new Dictionary<string, SimElement>
        {
            ["s"] = new()
            {
                Id = "s", OriginX = 100, OriginY = 60, Scale = 10, GridWidth = 64, GridHeight = 36,
                Emitters = [new Emitter { X = 32, Y = 30, X2 = 40, Y2 = 30, Radius = 3 }],
                Particles = new ParticleSpec { Size = 2 },
                OutlineBrush = new BrushSettings { Size = 6, TextureScale = 12 },
            },
        };

        // The middle of the grid, which is where the smoke is.
        var (middleX, middleY) = (100 + 32 * 10.0, 60 + 18 * 10.0);

        ImageResize.Apply(doc, 1920, 540, new FakeResampler());   // twice as wide, the same height
        var mark = Math.Sqrt(2);

        var sim = doc.Sims["s"];
        // A cell is square and has one size; the grid is placed by its
        // middle, which goes where that part of the drawing went — twice as
        // far across and no further down.
        Assert.Equal(10 * mark, sim.Scale, 9);
        Assert.Equal(middleX * 2, sim.OriginX + 32 * sim.Scale, 9);
        Assert.Equal(middleY, sim.OriginY + 18 * sim.Scale, 9);
        // The grid is the same grid: cells are not pixels, and everything
        // measured in them stays.
        Assert.Equal((64, 36), (sim.GridWidth, sim.GridHeight));
        var emitter = sim.Emitters[0];
        Assert.Equal((32.0, 30.0, 40.0, 30.0, 3.0), (emitter.X, emitter.Y, emitter.X2, emitter.Y2, emitter.Radius));
        // What it draws with is in document pixels, like any stroke's brush.
        Assert.Equal(2 * mark, sim.Particles!.Size, 9);
        Assert.Equal(6 * mark, sim.OutlineBrush!.Size, 9);
        Assert.Equal(12 * mark, sim.OutlineBrush.TextureScale, 9);
    }

    [Fact]
    public void AnEvenResizeMovesAnEffectsGridByItsCornerLikeEverythingElse()
    {
        // The even case of the placement above, stated on its own because it
        // is the one almost every resize is: nothing clever, the corner and
        // the cell both double.
        var doc = DocWithAStroke(out _);
        doc.Sims = new Dictionary<string, SimElement>
        {
            ["s"] = new() { Id = "s", OriginX = 100, OriginY = 60, Scale = 10, GridWidth = 64, GridHeight = 36 },
        };

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        var sim = doc.Sims["s"];
        Assert.Equal(200, sim.OriginX, 9);
        Assert.Equal(120, sim.OriginY, 9);
        Assert.Equal(20, sim.Scale, 9);
    }

    [Fact]
    public void AnEffectWithNoPenOfItsOwnIsGivenOneAtTheSizeItDrew()
    {
        // Q257. An effect with no outline pen draws with the default brush,
        // which is six pixels at any size of document. Its outlines on the
        // paper were scaled with everything else; a re-bake would draw them
        // at six again. So the resize gives it the pen it had been using, at
        // the size the drawing now has.
        var doc = DocWithAStroke(out _);
        doc.Sims = new Dictionary<string, SimElement> { ["s"] = new() { Id = "s", Scale = 10 } };
        var usual = new BrushSettings();
        Assert.Null(doc.Sims["s"].OutlineBrush);

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        var pen = doc.Sims["s"].OutlineBrush;
        Assert.NotNull(pen);
        Assert.Equal(usual.Size * 2, pen.Size, 9);
        Assert.Equal(usual.TextureScale * 2, pen.TextureScale, 9);
        // Otherwise it is the default brush, exactly: the bake draws with
        // what it drew with before.
        pen.Size = usual.Size;
        pen.TextureScale = usual.TextureScale;
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(usual, Lightbox.Core.Serialization.DocJson.Options),
            System.Text.Json.JsonSerializer.Serialize(pen, Lightbox.Core.Serialization.DocJson.Options));
    }

    [Fact]
    public void AnEffectThatIsNeverResizedKeepsNoPen()
    {
        // The pen is written because a resize made the default wrong, and
        // only then: optional still means absent.
        var doc = DocWithAStroke(out _);
        doc.Sims = new Dictionary<string, SimElement> { ["s"] = new() { Id = "s" } };

        var json = Lightbox.Core.Serialization.DocJson.Serialize(doc);

        Assert.DoesNotContain("\"outlineBrush\"", json);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1920, 540)]
    public void APaintedStrokesPaperGrainGrowsWithTheMark(int width, int height)
    {
        // Q257. The grain of a simulated medium is in document pixels and is
        // read every time the stroke renders. Left alone, an enlarged
        // painting re-rendered with a finer tooth than it was painted on.
        // It is a size of a mark, so it takes the mark's one factor, as the
        // brush's texture scale does.
        var doc = DocWithAStroke(out var stroke);
        stroke.Brush.Medium = new MediumSettings { Kind = MediumKind.Watercolour, PaperScale = 12 };
        var markBefore = stroke.Brush.Size;

        ImageResize.Apply(doc, width, height, new FakeResampler());

        var grew = stroke.Brush.Size / markBefore;
        Assert.Equal(12 * grew, stroke.Brush.Medium.PaperScale, 9);
    }

    [Fact]
    public void AStrokeWithNoMediumIsNotGivenOneByAResize()
    {
        // Scaling the grain of an untouched medium would touch it, and an
        // untouched medium is what keeps twenty-one keys off every stroke.
        var doc = DocWithAStroke(out var stroke);
        Assert.True(stroke.Brush.Medium.IsUntouched());

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        Assert.True(stroke.Brush.Medium.IsUntouched());
        Assert.Null(stroke.Brush.MediumOnDisk);
    }

    [Fact]
    public void AReferenceNudgeMovesWithTheDrawing()
    {
        // A cell's nudge registers one frame of a reference against the
        // drawing, in document pixels, outside the strip's own scale. The
        // cell's place on the sheet does not move; the nudge does.
        var doc = DocWithAStroke(out _);
        doc.Scene.References =
        [
            new ReferenceStrip
            {
                Scale = 1,
                Cells = [new ReferenceCell { X = 10, Y = 20, Width = 30, Height = 40, Dx = 4, Dy = -3, PivotX = 5, PivotY = 6 }],
            },
        ];

        ImageResize.Apply(doc, 1920, 540, new FakeResampler());   // twice as wide, the same height

        var cell = doc.Scene.References[0].Cells[0];
        Assert.Equal((8.0, -3.0), (cell.Dx, cell.Dy));
        Assert.Equal((10, 20, 30, 40), (cell.X, cell.Y, cell.Width, cell.Height));
        Assert.Equal((5.0, 6.0), (cell.PivotX, cell.PivotY));
    }

    [Fact]
    public void AGroupsStrokesScaleWithTheDrawingTheyArePartOf()
    {
        // Q265. A group holds a merged layer's strokes inside one entry of
        // the list, and the entry has no points of its own — so a resize that
        // reads only the top of the list leaves the whole merged drawing at
        // the old size inside a doubled one.
        var doc = DocWithAStroke(out _);
        Stroke Mark() => new()
        {
            Points = [new StrokePoint(100, 50, 1)],
            Brush = new BrushSettings { Size = 10 },
        };
        var inner = Mark();
        var innermost = Mark();
        var carve = Mark();
        var group = new Stroke
        {
            Tool = ToolKind.Group,
            Group = new StrokeGroup
            {
                Strokes = [inner, new Stroke { Tool = ToolKind.Group, Group = new StrokeGroup { Strokes = [innermost] } }],
                PngBase64 = "under",
                Mask = new LayerMask { Frame = new Frame { Strokes = [carve] } },
            },
        };
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(group);
        var resampler = new FakeResampler();

        ImageResize.Apply(doc, 1920, 1080, resampler);

        Assert.Equal((200.0, 100.0, 20.0), (inner.Points[0].X, inner.Points[0].Y, inner.Brush.Size));
        Assert.Equal((200.0, 20.0), (innermost.Points[0].X, innermost.Brush.Size));
        Assert.Equal((200.0, 20.0), (carve.Points[0].X, carve.Brush.Size));
        Assert.Equal("under@scaled", group.Group.PngBase64);
        Assert.Contains(resampler.Calls, c => c.Png == "under");
    }

    [Fact]
    public void AGroupUndoKeptFromBeforeTheResizeIsNotResizedWithIt()
    {
        var doc = DocWithAStroke(out _);
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Group,
            Group = new StrokeGroup { Strokes = [new Stroke { Points = [new StrokePoint(100, 50, 1)] }] },
        });
        var before = doc.Clone();

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        Assert.Equal(100, before.Scene.Layers[0].Cels[0].Frame!.Strokes[1].Group!.Strokes[0].Points[0].X);
        Assert.Equal(200, doc.Scene.Layers[0].Cels[0].Frame!.Strokes[1].Group!.Strokes[0].Points[0].X);
    }

    [Fact]
    public void WhatUndoKeptFromBeforeTheResizeIsNotResizedWithIt()
    {
        // The resize writes in place, and undo holds a copy of the document
        // from before. Anything the two shared would be scaled in both — the
        // undo would then restore a document that is half old and half new.
        var doc = DocWithAStroke(out var stroke);
        stroke.Path = new StrokePath { Nodes = [PathNode.At(100, 50), PathNode.At(200, 150)] };
        doc.Texts = new Dictionary<string, TextElement> { ["t"] = new() { Id = "t", Size = 48, X = 100, Y = 200 } };
        doc.Sims = new Dictionary<string, SimElement>
        {
            ["s"] = new()
            {
                Id = "s", OriginX = 100, Scale = 10,
                Particles = new ParticleSpec { Size = 2 }, OutlineBrush = new BrushSettings { Size = 6 },
            },
        };
        var before = doc.Clone();

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        var kept = before.Scene.Layers[^1].Cels[0].Frame!.Strokes[0];
        Assert.Equal(100, kept.Path!.Nodes[0].X);
        Assert.Equal((100.0, 48.0), (before.Texts!["t"].X, before.Texts["t"].Size));
        var sim = before.Sims!["s"];
        Assert.Equal((100.0, 10.0, 2.0, 6.0), (sim.OriginX, sim.Scale, sim.Particles!.Size, sim.OutlineBrush!.Size));
        // And the document itself did move, or the lines above prove nothing.
        Assert.Equal(200, stroke.Path.Nodes[0].X);
        Assert.Equal(20, doc.Sims["s"].Scale);
    }

    /// <summary>
    /// Every serialized property of a type, by name — what the omission guards
    /// below compare their lists against.
    /// </summary>
    private static List<string> SerializedProperties(Type type) =>
        [.. type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0
                && !p.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), inherit: true))
            .Select(p => p.Name)];

    public static IEnumerable<object[]> WhereACoordinateCanLive() =>
    [
        [typeof(Doc),
            // Visited by ImageResize.
            new[] { nameof(Doc.Scene), nameof(Doc.ClipRegions), nameof(Doc.Texts), nameof(Doc.Sims), nameof(Doc.Armatures) },
            new[]
            {
                nameof(Doc.Version), nameof(Doc.SavedByFormat), nameof(Doc.Unknown), nameof(Doc.PlayheadFrame),
                nameof(Doc.ActiveLayerId), nameof(Doc.IsTemplate), nameof(Doc.TemplateId), nameof(Doc.Features),
                // The load-only alias of the first rig; it is in Armatures by the time anything runs.
                nameof(Doc.LegacyArmature),
                // Sources with a pixel space of their own: a sheet's cells and
                // a board's tiles are places on *them*, not on the paper.
                nameof(Doc.ReferenceSheets), nameof(Doc.ReferenceBoard),
                // A symbol is drawn in its own space and placed by a
                // SymbolPlacement, which is what scales (ImageResize says why).
                nameof(Doc.Symbols),
                // Libraries of looks: tips, textures, colours, ramps, typefaces.
                nameof(Doc.BrushTips), nameof(Doc.Textures), nameof(Doc.Palettes), nameof(Doc.PaletteFolders),
                nameof(Doc.Gradients), nameof(Doc.Fonts),
                // Ids only, and stroke-widths and ratios by design — "never pixels".
                nameof(Doc.SimGroups), nameof(Doc.LineTreatments),
            }],
        [typeof(Frame),
            new[]
            {
                nameof(Frame.Strokes), nameof(Frame.PngBase64), nameof(Frame.Placements), nameof(Frame.Anchors),
                nameof(Frame.Shapes), nameof(Frame.Correctives),
            },
            new[]
            {
                nameof(Frame.Unknown), nameof(Frame.Id), nameof(Frame.Role), nameof(Frame.Ai),
                // Which lines go together, named by stroke id and piece: no place on the paper (Q271).
                nameof(Frame.Matches),
                // Spacing as fractions between two extremes.
                nameof(Frame.Chart),
                // Derived pixels, trusted only while they match the strokes
                // they were made from — which a resize has just changed.
                nameof(Frame.Checkpoint),
            }],
        [typeof(Stroke),
            new[]
            {
                nameof(Stroke.Points), nameof(Stroke.Holes), nameof(Stroke.RestPoints), nameof(Stroke.Path),
                nameof(Stroke.Symmetry), nameof(Stroke.Wrap), nameof(Stroke.Brush), nameof(Stroke.Baked),
                // A merged layer's drawing, kept inside this entry (Q265).
                nameof(Stroke.Group),
            },
            new[]
            {
                nameof(Stroke.Unknown), nameof(Stroke.Id), nameof(Stroke.Tool), nameof(Stroke.Color),
                nameof(Stroke.ClipId), nameof(Stroke.GradientId), nameof(Stroke.SwatchId), nameof(Stroke.PaletteId),
                nameof(Stroke.AlphaLocked), nameof(Stroke.SimId), nameof(Stroke.TextId), nameof(Stroke.Label),
                // Ratios between bones.
                nameof(Stroke.Weights),
            }],
        [typeof(StrokeGroup),
            new[] { nameof(StrokeGroup.Strokes), nameof(StrokeGroup.PngBase64), nameof(StrokeGroup.Mask) },
            new[]
            {
                nameof(StrokeGroup.Unknown), nameof(StrokeGroup.Blend), nameof(StrokeGroup.Opacity),
                nameof(StrokeGroup.From), nameof(StrokeGroup.Name),
                nameof(StrokeGroup.Clip),
                // The stack a layer had, and treated as a layer's is: a
                // resize does not reach into an effect's parameters.
                nameof(StrokeGroup.Effects),
            }],
        [typeof(Armature),
            new[] { nameof(Armature.Bones), nameof(Armature.Pins), nameof(Armature.PinCell) },
            new[]
            {
                nameof(Armature.Unknown), nameof(Armature.Id), nameof(Armature.Name),
                // Ids, counts, angles and ratios; the lengths they act on are the bones'.
                nameof(Armature.Chains), nameof(Armature.Splines), nameof(Armature.Constraints),
            }],
        [typeof(Bone),
            new[] { nameof(Bone.X), nameof(Bone.Y), nameof(Bone.Length), nameof(Bone.JointZone) },
            new[]
            {
                nameof(Bone.Unknown), nameof(Bone.Id), nameof(Bone.Name), nameof(Bone.ParentId), nameof(Bone.Connected),
                nameof(Bone.RotationDeg), nameof(Bone.Jiggle),
            }],
        [typeof(Pin),
            new[] { nameof(Pin.X), nameof(Pin.Y), nameof(Pin.Extent) },
            new[] { nameof(Pin.Unknown), nameof(Pin.Id), nameof(Pin.Name), nameof(Pin.Kind), nameof(Pin.ParentId) }],
        [typeof(TextElement),
            new[] { nameof(TextElement.X), nameof(TextElement.Y), nameof(TextElement.Size), nameof(TextElement.LineHeight) },
            new[]
            {
                nameof(TextElement.Unknown), nameof(TextElement.Id), nameof(TextElement.Text), nameof(TextElement.Font),
                nameof(TextElement.Align),
                // Thousandths of an em.
                nameof(TextElement.Tracking),
            }],
        [typeof(MediumSettings),
            new[] { nameof(MediumSettings.PaperScale) },
            new[]
            {
                // FlowSteps counts lattice steps, and below a small size a
                // lattice cell is one document pixel, so a wash bleeds a fixed
                // number of pixels. It is a capped integer and cannot follow a
                // resize; it is left by the rule that a rescale may change the
                // mark, not because it holds no size.
                nameof(MediumSettings.Unknown), nameof(MediumSettings.Kind), nameof(MediumSettings.Wetness),
                nameof(MediumSettings.Viscosity), nameof(MediumSettings.Drag), nameof(MediumSettings.FlowSteps),
                nameof(MediumSettings.Absorbency), nameof(MediumSettings.EdgePull), nameof(MediumSettings.PigmentDensity),
                nameof(MediumSettings.Granularity), nameof(MediumSettings.Hiding), nameof(MediumSettings.PhysicalMixing),
                nameof(MediumSettings.Paper), nameof(MediumSettings.PaperInfluence), nameof(MediumSettings.Body),
                nameof(MediumSettings.Relief), nameof(MediumSettings.BristleDrag), nameof(MediumSettings.PaintLoad),
                nameof(MediumSettings.Pickup), nameof(MediumSettings.PressureWater), nameof(MediumSettings.PressureMix),
                nameof(MediumSettings.Rewetting),
            }],
        [typeof(ReferenceCell),
            new[] { nameof(ReferenceCell.Dx), nameof(ReferenceCell.Dy) },
            new[]
            {
                nameof(ReferenceCell.Unknown),
                // Places on the source sheet, not on the paper.
                nameof(ReferenceCell.X), nameof(ReferenceCell.Y), nameof(ReferenceCell.Width), nameof(ReferenceCell.Height),
                nameof(ReferenceCell.PivotX), nameof(ReferenceCell.PivotY),
            }],
        [typeof(CameraKey),
            new[] { nameof(CameraKey.X), nameof(CameraKey.Y) },
            new[]
            {
                nameof(CameraKey.Unknown), nameof(CameraKey.Frame), nameof(CameraKey.RotationDeg), nameof(CameraKey.Ease),
                // Document pixels to output pixels — and left alone by the
                // owner's decision (Q257), not because it holds no size.
                nameof(CameraKey.Zoom),
            }],
        [typeof(Emitter),
            Array.Empty<string>(),
            new[]
            {
                nameof(Emitter.Unknown), nameof(Emitter.Id), nameof(Emitter.Shape), nameof(Emitter.Density),
                nameof(Emitter.Heat), nameof(Emitter.Burst), nameof(Emitter.EmitFrom), nameof(Emitter.EmitUntil),
                nameof(Emitter.MaskLayerId),
                // Cells, and cells per step: the grid's own units, which the
                // effect's cell size turns into pixels.
                nameof(Emitter.X), nameof(Emitter.Y), nameof(Emitter.X2), nameof(Emitter.Y2), nameof(Emitter.Radius),
                nameof(Emitter.VelocityX), nameof(Emitter.VelocityY), nameof(Emitter.MotionX), nameof(Emitter.MotionY),
                nameof(Emitter.Scatter),
            }],
        [typeof(SimElement),
            new[]
            {
                nameof(SimElement.OriginX), nameof(SimElement.OriginY), nameof(SimElement.Scale),
                nameof(SimElement.Particles), nameof(SimElement.OutlineBrush),
            },
            new[]
            {
                nameof(SimElement.Unknown), nameof(SimElement.Id), nameof(SimElement.Kind), nameof(SimElement.Name),
                nameof(SimElement.FirstFrame), nameof(SimElement.FrameCount), nameof(SimElement.ExposeOn),
                nameof(SimElement.Substeps), nameof(SimElement.PreRoll), nameof(SimElement.ObstacleLayerId),
                nameof(SimElement.TreatmentId), nameof(SimElement.OutlineColor), nameof(SimElement.BandColors),
                nameof(SimElement.BandsFromHeat), nameof(SimElement.BandLow), nameof(SimElement.BandHigh),
                // The grid and everything measured on it are in cells: Scale
                // is what says how big a cell is, and Scale is what moves.
                nameof(SimElement.GridWidth), nameof(SimElement.GridHeight), nameof(SimElement.Emitters),
                nameof(SimElement.WindX), nameof(SimElement.WindY), nameof(SimElement.Params),
                // Stroke-widths, angles and ratios (LineTreatment: "never pixels").
                nameof(SimElement.Treatment),
            }],
    ];

    [Theory]
    [MemberData(nameof(WhereACoordinateCanLive))]
    public void EveryPropertyThatCouldHoldAPlaceOnThePaperIsAccountedFor(Type type, string[] scaled, string[] notAPlace)
    {
        // The Scene guard below, for every other type a coordinate can live
        // on. Seven times a place or a length on one of these was left
        // behind by the resize — a stroke's symmetry axis; a second rig, a
        // joint zone and a drawn joint fix; a pen path, a text element and
        // an effect — and each time nothing went red, because the guard only
        // read Scene. A property added to any of these types now fails
        // here until somebody says which list it belongs in.
        var listed = scaled.Concat(notAPlace).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        var actual = SerializedProperties(type);

        var unlisted = actual.Except(listed).ToList();
        var gone = listed.Except(actual).ToList();
        Assert.True(unlisted.Count == 0,
            $"{type.Name} has properties the resize has not been told about: {string.Join(", ", unlisted)}. "
            + "Scale them in ImageResize, or list them here as holding no place on the paper.");
        Assert.True(gone.Count == 0, $"{type.Name} no longer has: {string.Join(", ", gone)}");
    }

    /// <summary>The lengths of the three effects these tests use, as the registry declares them.</summary>
    /// <remarks>The answer is the least a length may be scaled down to; null means "not a length".</remarks>
    private static double? KnownLengths(string kind, string key) => (kind, key) switch
    {
        ("blur.gaussian", "radius") => 0,
        ("style.dropShadow", "distance" or "size") => 0,
        ("detail.edges", "radius") => 2,
        _ => null,
    };

    private static EffectUse Use(string kind, params (string Key, double Value)[] values) => new()
    {
        Kind = kind,
        Params = values.ToDictionary(v => v.Key, v => new Lightbox.Core.Effects.EffectParam(v.Value)),
    };

    [Fact]
    public void AnEffectsRadiusGrowsWithTheDrawing()
    {
        // B466. A blur's radius is in document pixels and re-renders live:
        // left alone, a doubled drawing was half as soft relative to itself,
        // with no edit needed for it to show. Keys of an animated radius
        // scale with it.
        var doc = DocWithAStroke(out _);
        var blur = Use("blur.gaussian", ("radius", 4));
        blur.Params["radius"].Keys = [new EffectKey { Frame = 0, Value = 4 }, new EffectKey { Frame = 10, Value = 9 }];
        doc.Scene.Effects = new EffectStack { Uses = [blur] };

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler(), effectLengths: KnownLengths);

        var radius = doc.Scene.Effects.Uses[0].Params["radius"];
        Assert.Equal(8, radius.Value, 9);
        Assert.Equal([8.0, 18.0], radius.Keys!.Select(k => k.Value));
        Assert.Equal([0, 10], radius.Keys!.Select(k => k.Frame));
    }

    [Fact]
    public void ADropShadowKeepsItsPlaceAgainstTheDrawing()
    {
        // A layer's shadow: its distance and softness are lengths and take
        // the mark's one factor (they point anywhere, so no axis owns them);
        // its angle and opacity are not and stay.
        var doc = DocWithAStroke(out _);
        var layer = doc.Scene.Layers[^1];
        layer.Effects = new EffectStack { Uses = [Use("style.dropShadow", ("distance", 6), ("size", 5), ("angle", 120), ("opacity", 75))] };

        ImageResize.Apply(doc, 1920, 540, new FakeResampler(), effectLengths: KnownLengths);   // twice as wide, the same height
        var mark = Math.Sqrt(2);

        var shadow = layer.Effects.Uses[0].Params;
        Assert.Equal(6 * mark, shadow["distance"].Value, 9);
        Assert.Equal(5 * mark, shadow["size"].Value, 9);
        Assert.Equal(120, shadow["angle"].Value);
        Assert.Equal(75, shadow["opacity"].Value);
    }

    [Fact]
    public void AShrunkLengthStopsAtTheLeastItsEffectWorksAt()
    {
        // Q263. Find edges needs a radius of two: below it the blur underneath
        // does nothing, and the layer renders as a flat white shape with every
        // line gone. Halving the image would take a radius of 2 to 1, and of
        // 3 to 1.5; both stop at 2. A length above the least still scales
        // exactly, and nothing stops one growing past its slider's end.
        var doc = DocWithAStroke(out _);
        var animated = Use("detail.edges", ("radius", 6));
        animated.Params["radius"].Keys = [new EffectKey { Frame = 0, Value = 3 }, new EffectKey { Frame = 8, Value = 10 }];
        doc.Scene.Effects = new EffectStack { Uses = [Use("detail.edges", ("radius", 2)), animated, Use("blur.gaussian", ("radius", 80))] };

        ImageResize.Apply(doc, 480, 270, new FakeResampler(), effectLengths: KnownLengths);

        Assert.Equal(2, doc.Scene.Effects.Uses[0].Params["radius"].Value);
        Assert.Equal(3, doc.Scene.Effects.Uses[1].Params["radius"].Value);
        Assert.Equal([2.0, 5.0], doc.Scene.Effects.Uses[1].Params["radius"].Keys!.Select(k => k.Value));
        Assert.Equal(40, doc.Scene.Effects.Uses[2].Params["radius"].Value);

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler(), effectLengths: KnownLengths);
        Assert.Equal(160, doc.Scene.Effects.Uses[2].Params["radius"].Value);
    }

    [Fact]
    public void ALengthAlreadyBelowTheLeastIsNotRaisedByAResize()
    {
        // The floor stops a resize pushing a working effect below where it
        // works. It does not rewrite a value a file already had there.
        var doc = DocWithAStroke(out _);
        doc.Scene.Effects = new EffectStack { Uses = [Use("detail.edges", ("radius", 1))] };

        ImageResize.Apply(doc, 480, 270, new FakeResampler(), effectLengths: KnownLengths);

        Assert.Equal(0.5, doc.Scene.Effects.Uses[0].Params["radius"].Value);
    }

    [Fact]
    public void AnEffectNobodySaidWasALengthIsLeftAsWritten()
    {
        // An effect this build does not know — a newer document's — and a
        // resize given no word on which parameters are lengths both leave
        // every number alone: a guess would be a wrong one.
        var doc = DocWithAStroke(out _);
        doc.Scene.Effects = new EffectStack { Uses = [Use("future.ripple", ("radius", 4)), Use("blur.gaussian", ("radius", 4))] };

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler(), effectLengths: KnownLengths);
        Assert.Equal(4, doc.Scene.Effects.Uses[0].Params["radius"].Value);
        Assert.Equal(8, doc.Scene.Effects.Uses[1].Params["radius"].Value);

        var told = DocWithAStroke(out _);
        told.Scene.Effects = new EffectStack { Uses = [Use("blur.gaussian", ("radius", 4))] };
        ImageResize.Apply(told, 1920, 1080, new FakeResampler());
        Assert.Equal(4, told.Scene.Effects.Uses[0].Params["radius"].Value);
    }

    [Fact]
    public void EveryCoordinateOnTheSceneIsAccountedFor()
    {
        // The omission guard. A new coordinate-bearing property on Scene is
        // either scaled by ImageResize or listed here as deliberately not —
        // and adding one without doing either fails this test rather than
        // going quiet. Same rule as Stroke.Clone: the list is exhaustive, not
        // "the ones that matter".
        var handled = new HashSet<string>
        {
            // Scaled by ImageResize.
            nameof(Scene.Width), nameof(Scene.Height),
            nameof(Scene.OriginX), nameof(Scene.OriginY),
            nameof(Scene.Layers), nameof(Scene.Guides), nameof(Scene.Camera),
            nameof(Scene.Pivot), nameof(Scene.References), nameof(Scene.Ppi),
            nameof(Scene.PoseTrack), nameof(Scene.Symmetry),
            // Its lengths — a blur's radius, a shadow's distance — when the
            // caller says which they are; the registry knows and Core does not
            // (B466). Grades, angles and opacities hold no place on the paper.
            nameof(Scene.Effects),

            // Carry no document coordinate, on purpose.
            nameof(Scene.Id), nameof(Scene.Name), nameof(Scene.Fps),
            nameof(Scene.FrameCount), nameof(Scene.PlaybackStart), nameof(Scene.PlaybackEnd),
            nameof(Scene.BackgroundColor),
            nameof(Scene.TransparentBackground), nameof(Scene.Markers),
            nameof(Scene.LayerGroups), nameof(Scene.FrameGroups),
            // A link is a relationship between layers, not a place on the
            // paper — nothing in it moves when the image is rescaled.
            nameof(Scene.LayerLinks),
            nameof(Scene.GhostFrames), nameof(Scene.Anchors), nameof(Scene.Tags),
            nameof(Scene.Shapes), nameof(Scene.Audio),
            // What a newer build wrote (Q230): this build cannot know whether
            // any of it is a coordinate, so it is carried as written. A resize
            // here may leave a newer build's own coordinates unscaled — the
            // lesser loss than dropping them, which is what happened before.
            nameof(Scene.Unknown),
            // Which rows the Timeline and the X-sheet show (Q227): a way of
            // looking at the sheet, with no place on the paper in it.
            nameof(Scene.SheetPinnedOnly),
        };

        var actual = typeof(Scene)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)
            .ToList();

        var unaccounted = actual.Where(name => !handled.Contains(name)).ToList();
        Assert.True(
            unaccounted.Count == 0,
            $"Scene grew {string.Join(", ", unaccounted)}. If it holds a document coordinate, "
            + "scale it in ImageResize.Apply; if it does not, say so in this test's list.");
    }
}
