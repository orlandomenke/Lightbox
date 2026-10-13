using System.Reflection;
using Lightbox.Core.Documents;
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
        // Zoom is a ratio against a scene that scaled with it, so the shot
        // frames the same part of the drawing.
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

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        var t = doc.Texts["t"];
        Assert.Equal((200.0, 400.0, 96.0), (t.X, t.Y, t.Size));
        Assert.Equal(120, t.LineHeight!.Value, 9);
        // Thousandths of an em: a ratio, and it means the same at any size.
        Assert.Equal(25, t.Tracking);
        // Leading the typeface still decides stays undecided.
        Assert.Null(doc.Texts["u"].LineHeight);
        Assert.Equal(48, doc.Texts["u"].Size);
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

        ImageResize.Apply(doc, 1920, 1080, new FakeResampler());

        var sim = doc.Sims["s"];
        Assert.Equal((200.0, 120.0, 20.0), (sim.OriginX, sim.OriginY, sim.Scale));
        // The grid is the same grid, covering the same part of the drawing:
        // cells are not pixels, and everything measured in them stays.
        Assert.Equal((64, 36), (sim.GridWidth, sim.GridHeight));
        var emitter = sim.Emitters[0];
        Assert.Equal((32.0, 30.0, 40.0, 30.0, 3.0), (emitter.X, emitter.Y, emitter.X2, emitter.Y2, emitter.Radius));
        // What it draws with is in document pixels, like any stroke's brush.
        Assert.Equal(4, sim.Particles!.Size, 9);
        Assert.Equal(12, sim.OutlineBrush!.Size, 9);
        Assert.Equal(24, sim.OutlineBrush.TextureScale, 9);
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
            },
            new[]
            {
                nameof(Stroke.Unknown), nameof(Stroke.Id), nameof(Stroke.Tool), nameof(Stroke.Color),
                nameof(Stroke.ClipId), nameof(Stroke.GradientId), nameof(Stroke.SwatchId), nameof(Stroke.PaletteId),
                nameof(Stroke.AlphaLocked), nameof(Stroke.SimId), nameof(Stroke.TextId), nameof(Stroke.Label),
                // Ratios between bones.
                nameof(Stroke.Weights),
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
        // on. Four times a length was added to one of these and the resize
        // was not told — a second rig, a joint zone, a drawn joint fix, a
        // symmetry axis — and each time nothing went red, because the guard
        // only read Scene. A property added to any of these types now fails
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

            // Carry no document coordinate, on purpose.
            nameof(Scene.Id), nameof(Scene.Name), nameof(Scene.Fps),
            nameof(Scene.FrameCount), nameof(Scene.BackgroundColor),
            nameof(Scene.TransparentBackground), nameof(Scene.Markers),
            nameof(Scene.LayerGroups), nameof(Scene.FrameGroups),
            // A link is a relationship between layers, not a place on the
            // paper — nothing in it moves when the image is rescaled.
            nameof(Scene.LayerLinks),
            nameof(Scene.GhostFrames), nameof(Scene.Anchors), nameof(Scene.Tags),
            nameof(Scene.Shapes), nameof(Scene.Audio),
            // Effect parameters are values, not places — nothing in a grade
            // names a point on the paper. The one spatial number, a blur's
            // radius, is deliberately left alone by a resize for the brush
            // rule's reason turned around: the roadmap's open effects work
            // carries rescaling kernel params with the registry's reach
            // knowledge, which Core does not have.
            nameof(Scene.Effects),
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
