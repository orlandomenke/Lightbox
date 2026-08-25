using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// P5b: named anchors — sockets and extra pivots, declared once and positioned
/// per drawing.
/// </summary>
public class AnchorTests
{
    private static Scene OnOnes(int drawings)
    {
        var scene = new Scene { FrameCount = drawings };
        var layer = new Layer { Name = "Anim" };
        for (var i = 0; i < drawings; i++) layer.Cels.Add(new Cel { Frame = new Frame() });
        scene.Layers.Add(layer);
        return scene;
    }

    // ---- absence ---------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoAnchorsCarriesNoAnchorKeys()
    {
        // The camera's rule, and the one this repo has been bitten by twice.
        var json = DocJson.Serialize(new Doc());

        Assert.DoesNotContain("\"anchors\"", json);
    }

    [Fact]
    public void RemovingTheLastAnchorLeavesTheDocumentAsItWas()
    {
        var doc = new Doc();
        doc.Scene = OnOnes(3);
        var anchor = Anchors.Declare(doc.Scene, "Left hand");
        Anchors.SetAcross(doc.Scene.Layers[0], 0, 3, anchor.Id, new AnchorPoint(10, 20));
        Assert.Contains("\"anchors\"", DocJson.Serialize(doc));

        Assert.True(Anchors.Remove(doc.Scene, anchor.Id));

        // Both halves back to null: the declaration and every position of it.
        Assert.Null(doc.Scene.Anchors);
        Assert.DoesNotContain("\"anchors\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void DeletingADeclarationClearsItsPositionsToo()
    {
        // Leaving them would keep entries nothing can name — invisible, exported,
        // and impossible to find.
        var scene = OnOnes(2);
        var keep = Anchors.Declare(scene, "Keep");
        var drop = Anchors.Declare(scene, "Drop");
        Anchors.SetAcross(scene.Layers[0], 0, 2, keep.Id, new AnchorPoint(1, 1));
        Anchors.SetAcross(scene.Layers[0], 0, 2, drop.Id, new AnchorPoint(2, 2));

        Anchors.Remove(scene, drop.Id);

        var frame = scene.Layers[0].Cels[0].Frame!;
        Assert.NotNull(Anchors.At(frame, keep.Id));
        Assert.Null(Anchors.At(frame, drop.Id));
    }

    // ---- positioning across a range --------------------------------------------

    [Fact]
    public void SettingAcrossARangeTouchesEveryDrawingInIt()
    {
        var scene = OnOnes(6);
        var anchor = Anchors.Declare(scene, "Muzzle");

        var changed = Anchors.SetAcross(scene.Layers[0], 1, 4, anchor.Id, new AnchorPoint(30, 40));

        Assert.Equal(4, changed);
        Assert.Null(Anchors.At(scene.Layers[0].Cels[0].Frame!, anchor.Id));
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(new AnchorPoint(30, 40), Anchors.At(scene.Layers[0].Cels[i].Frame!, anchor.Id));
        }
        Assert.Null(Anchors.At(scene.Layers[0].Cels[5].Frame!, anchor.Id));
    }

    [Fact]
    public void AHeldDrawingIsVisitedOnceRatherThanTwice()
    {
        // The range works on *drawings*, not cels. On 2s a range holds each
        // drawing twice, and setting the same anchor twice on one drawing is the
        // same edit done twice — so the count would lie.
        var scene = new Scene { FrameCount = 4 };
        var layer = new Layer();
        layer.Cels.Add(new Cel { Frame = new Frame() });
        layer.Cels.Add(new Cel());
        layer.Cels.Add(new Cel { Frame = new Frame() });
        layer.Cels.Add(new Cel());
        scene.Layers.Add(layer);
        var anchor = Anchors.Declare(scene, "Hand");

        var changed = Anchors.SetAcross(layer, 0, 4, anchor.Id, new AnchorPoint(5, 6));

        Assert.Equal(2, changed);
        // And the hold resolves to the drawing it is holding, so the anchor is
        // there on every frame the artist sees.
        Assert.NotNull(Anchors.At(layer.Cels[0].Frame!, anchor.Id));
        Assert.NotNull(Anchors.At(layer.Cels[2].Frame!, anchor.Id));
    }

    [Fact]
    public void ARangeStartingOnAHoldStillSetsTheDrawingItShows()
    {
        var scene = new Scene { FrameCount = 3 };
        var layer = new Layer();
        layer.Cels.Add(new Cel { Frame = new Frame() });
        layer.Cels.Add(new Cel());
        layer.Cels.Add(new Cel());
        scene.Layers.Add(layer);
        var anchor = Anchors.Declare(scene, "Hand");

        Assert.Equal(1, Anchors.SetAcross(layer, 1, 2, anchor.Id, new AnchorPoint(7, 8)));
        Assert.Equal(new AnchorPoint(7, 8), Anchors.At(layer.Cels[0].Frame!, anchor.Id));
    }

    [Fact]
    public void ClearingAcrossARangeRemovesItAndTheKeyWithIt()
    {
        var scene = OnOnes(4);
        var anchor = Anchors.Declare(scene, "Hand");
        Anchors.SetAcross(scene.Layers[0], 0, 4, anchor.Id, new AnchorPoint(1, 2));

        var cleared = Anchors.ClearAcross(scene.Layers[0], 1, 2, anchor.Id);

        Assert.Equal(2, cleared);
        Assert.NotNull(Anchors.At(scene.Layers[0].Cels[0].Frame!, anchor.Id));
        // The dictionary goes back to null, so a drawing with no anchors writes
        // no key.
        Assert.Null(scene.Layers[0].Cels[1].Frame!.Anchors);
        Assert.NotNull(Anchors.At(scene.Layers[0].Cels[3].Frame!, anchor.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void ANonRangeIsANoOp(int count)
    {
        var scene = OnOnes(3);
        var anchor = Anchors.Declare(scene, "Hand");
        Assert.Equal(0, Anchors.SetAcross(scene.Layers[0], 0, count, anchor.Id, new AnchorPoint(1, 1)));
    }

    // ---- the property that made "on the frame" the right choice -----------------

    [Fact]
    public void AnAnchorTravelsWithItsDrawingWhenTheTimingChanges()
    {
        // The reason positions are on the frame rather than in a table keyed by
        // frame index: a re-time moves drawings around the sheet, and an
        // index-keyed table would silently point at the wrong one.
        var scene = OnOnes(4);
        var anchor = Anchors.Declare(scene, "Hand");
        var layer = scene.Layers[0];
        var third = layer.Cels[2].Frame!;
        Anchors.SetAcross(layer, 2, 1, anchor.Id, new AnchorPoint(99, 99));

        Lightbox.Core.Timeline.ExposureSheet.ApplyTiming(
            layer, 0, 4, new Lightbox.Core.Timeline.TimingPreset("On 2s", [2]));

        // The drawing is somewhere else now, and its anchor went with it.
        var moved = layer.Cels.First(c => ReferenceEquals(c.Frame, third));
        Assert.Equal(new AnchorPoint(99, 99), Anchors.At(moved.Frame!, anchor.Id));
    }

    [Fact]
    public void AnAnchorRoundTripsThroughTheFile()
    {
        var doc = new Doc { Scene = OnOnes(2) };
        var anchor = Anchors.Declare(doc.Scene, "Left hand", AnchorKind.Socket);
        Anchors.SetAcross(doc.Scene.Layers[0], 0, 2, anchor.Id, new AnchorPoint(12.5, 34.5));

        var back = DocJson.Clone(doc);

        var declared = Assert.Single(back.Scene.Anchors!);
        Assert.Equal("Left hand", declared.Name);
        Assert.Equal(AnchorKind.Socket, declared.Kind);
        Assert.Equal(new AnchorPoint(12.5, 34.5), Anchors.At(back.Scene.Layers[0].Cels[0].Frame!, declared.Id));
    }

    // ---- direction (Q144) --------------------------------------------------------

    [Fact]
    public void AnAnchorWithNoAngleWritesNoAngleKey()
    {
        // Q144's absent-until-used half: the angle is nullable so a document
        // whose anchors never turn serializes exactly as it did before the
        // field existed — the camera's rule, applied again.
        var doc = new Doc { Scene = OnOnes(1) };
        var anchor = Anchors.Declare(doc.Scene, "Hand");
        Anchors.SetAcross(doc.Scene.Layers[0], 0, 1, anchor.Id, new AnchorPoint(10, 20));

        var json = System.Text.Json.JsonSerializer.Serialize(doc, DocJson.Options);

        Assert.DoesNotContain("\"angleDeg\"", json);
    }

    [Fact]
    public void AnAimedAnchorRoundTripsItsAngle()
    {
        var doc = new Doc { Scene = OnOnes(1) };
        var anchor = Anchors.Declare(doc.Scene, "Muzzle");
        Anchors.SetAcross(doc.Scene.Layers[0], 0, 1, anchor.Id, new AnchorPoint(10, 20, -45));

        var back = DocJson.Clone(doc);

        var point = Anchors.At(back.Scene.Layers[0].Cels[0].Frame!, doc.Scene.Anchors![0].Id);
        Assert.Equal(new AnchorPoint(10, 20, -45), point);
    }

    [Fact]
    public void TheAngleTravelsWithTheDrawingThroughARetime()
    {
        // The same property the position has, for the same reason: direction is
        // per drawing, and an index-keyed table would aim the wrong one after
        // any re-time.
        var scene = OnOnes(4);
        var anchor = Anchors.Declare(scene, "Hand");
        var layer = scene.Layers[0];
        var third = layer.Cels[2].Frame!;
        Anchors.SetAcross(layer, 2, 1, anchor.Id, new AnchorPoint(99, 99, 30));

        Lightbox.Core.Timeline.ExposureSheet.ApplyTiming(
            layer, 0, 4, new Lightbox.Core.Timeline.TimingPreset("On 2s", [2]));

        var moved = layer.Cels.First(c => ReferenceEquals(c.Frame, third));
        Assert.Equal(new AnchorPoint(99, 99, 30), Anchors.At(moved.Frame!, anchor.Id));
    }

    // ---- resolving for the exporter --------------------------------------------

    [Fact]
    public void ResolvingAtAFrameReadsThroughHolds()
    {
        var scene = new Scene { FrameCount = 3 };
        var layer = new Layer();
        layer.Cels.Add(new Cel { Frame = new Frame() });
        layer.Cels.Add(new Cel());
        scene.Layers.Add(layer);
        var anchor = Anchors.Declare(scene, "Hand");
        Anchors.SetAcross(layer, 0, 1, anchor.Id, new AnchorPoint(4, 5));

        // A socket that vanished on every other frame would read as a rigging bug
        // in the engine.
        Assert.Equal(new AnchorPoint(4, 5), Anchors.ResolvedAt(scene, 1)[anchor.Id]);
    }

    [Fact]
    public void AnUpperLayerWinsWhenTwoPlaceTheSameAnchor()
    {
        // The same precedence the layer stack already means everywhere else.
        var scene = OnOnes(1);
        var upper = new Layer { Name = "Upper" };
        upper.Cels.Add(new Cel { Frame = new Frame() });
        scene.Layers.Add(upper);
        var anchor = Anchors.Declare(scene, "Hand");

        Anchors.SetAcross(scene.Layers[0], 0, 1, anchor.Id, new AnchorPoint(1, 1));
        Anchors.SetAcross(upper, 0, 1, anchor.Id, new AnchorPoint(2, 2));

        Assert.Equal(new AnchorPoint(2, 2), Anchors.ResolvedAt(scene, 0)[anchor.Id]);
    }

    [Fact]
    public void NoDeclarationsMeansNothingToResolve()
    {
        Assert.Empty(Anchors.ResolvedAt(OnOnes(2), 0));
    }
}

/// <summary>
/// P5d: a frame event is a marker with a flag; a tag is genuinely new, because a
/// marker is a point and a tag is a range.
/// </summary>
public class AnimationTagTests
{
    [Fact]
    public void AnUntouchedMarkerWritesNoEventKey()
    {
        // A flag rather than a parallel FrameEvent record — adding one would have
        // been the mistake Q11's "reusable animation presets" was struck for.
        var doc = new Doc();
        doc.Scene.Markers.Add(new FrameMarker { Frame = 1, Label = "contact" });

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"isEvent\"", json);
        Assert.False(doc.Scene.Markers[0].ExportsAsEvent);
        // And the convenience getter must not reintroduce the key under a second name.
        Assert.DoesNotContain("\"exportsAsEvent\"", json);
    }

    [Fact]
    public void ADocumentWithNoTagsWritesNoTagKey()
    {
        Assert.DoesNotContain("\"tags\"", DocJson.Serialize(new Doc()));
    }

    [Fact]
    public void ATagRoundTripsWithItsDirectionAndLoop()
    {
        var doc = new Doc();
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "run", Start = 4, End = 11, Direction = TagDirection.PingPong, Loop = false },
        ];

        var tag = Assert.Single(DocJson.Clone(doc).Scene.Tags!);

        Assert.Equal("run", tag.Name);
        Assert.Equal(TagDirection.PingPong, tag.Direction);
        Assert.False(tag.Loop);
        Assert.Equal(8, tag.Length);
    }

    [Fact]
    public void ATagsLengthCountsBothEndsAndSurvivesBeingBackwards()
    {
        Assert.Equal(4, new AnimationTag { Start = 2, End = 5 }.Length);
        // A range typed the wrong way round still names four frames rather than
        // minus two, because the artist meant a range either way.
        Assert.Equal(4, new AnimationTag { Start = 5, End = 2 }.Length);
        Assert.Equal(1, new AnimationTag { Start = 3, End = 3 }.Length);
    }

    [Fact]
    public void AMarkerMarkedAsAnEventRoundTrips()
    {
        var doc = new Doc();
        doc.Scene.Markers.Add(new FrameMarker { Frame = 2, Label = "OnFootstep", IsEvent = true });

        var back = DocJson.Clone(doc);

        Assert.True(back.Scene.Markers[0].ExportsAsEvent);
        Assert.Equal("OnFootstep", back.Scene.Markers[0].Label);
    }
}
