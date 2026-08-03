using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// P5c: collision shapes — hurtboxes, hitboxes and physics bodies, declared once
/// and sized per drawing.
/// </summary>
/// <remarks>
/// Deliberately the same test shape as <see cref="AnchorTests"/>, because it is the
/// same kind of data and the claim being protected is that they behave identically.
/// Where a test here has no counterpart there, it is guarding something a
/// rectangle has and a point does not — a size, a centre, and absence as the off
/// state.
/// </remarks>
public class CollisionShapeTests
{
    private static Scene OnOnes(int drawings)
    {
        var scene = new Scene { FrameCount = drawings };
        var layer = new Layer { Name = "Anim" };
        for (var i = 0; i < drawings; i++) layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
        scene.Layers.Add(layer);
        return scene;
    }

    // ---- absence ---------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoShapesCarriesNoShapeKeys()
    {
        // The camera's rule. FrameConverter names every property it writes, so this
        // is also what proves the new field was actually added to WriteShared —
        // without it the round-trip test below fails and this one passes.
        var json = DocJson.Serialize(new Doc());

        Assert.DoesNotContain("\"shapes\"", json);
    }

    [Fact]
    public void TheCentreIsNotSerializedAlongsideTheRectangle()
    {
        // A read-only property serializes like any other one, and two derived keys
        // on every shape of every frame is the mistake BlendOrNormal made.
        var doc = new Doc { Scene = OnOnes(1) };
        var shape = CollisionShapes.Declare(doc.Scene, "Body");
        CollisionShapes.SetAcross(doc.Scene.Layers[0], 0, 1, shape.Id, new ShapeBox(1, 2, 3, 4));

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"shapes\"", json);
        Assert.DoesNotContain("centre", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovingTheLastShapeLeavesTheDocumentAsItWas()
    {
        var doc = new Doc { Scene = OnOnes(3) };
        var shape = CollisionShapes.Declare(doc.Scene, "Body");
        CollisionShapes.SetAcross(doc.Scene.Layers[0], 0, 3, shape.Id, new ShapeBox(10, 20, 30, 40));
        Assert.Contains("\"shapes\"", DocJson.Serialize(doc));

        Assert.True(CollisionShapes.Remove(doc.Scene, shape.Id));

        Assert.Null(doc.Scene.Shapes);
        Assert.DoesNotContain("\"shapes\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void DeletingADeclarationClearsItsRectanglesToo()
    {
        var scene = OnOnes(2);
        var keep = CollisionShapes.Declare(scene, "Body");
        var drop = CollisionShapes.Declare(scene, "Sword", ShapeRole.Hitbox);
        CollisionShapes.SetAcross(scene.Layers[0], 0, 2, keep.Id, new ShapeBox(1, 1, 8, 8));
        CollisionShapes.SetAcross(scene.Layers[0], 0, 2, drop.Id, new ShapeBox(2, 2, 4, 4));

        CollisionShapes.Remove(scene, drop.Id);

        var frame = scene.Layers[0].Cels[0].Frame!;
        Assert.NotNull(CollisionShapes.At(frame, keep.Id));
        Assert.Null(CollisionShapes.At(frame, drop.Id));
    }

    // ---- sizing across a range -------------------------------------------------

    [Fact]
    public void SettingAcrossARangeTouchesEveryDrawingInIt()
    {
        var scene = OnOnes(6);
        var shape = CollisionShapes.Declare(scene, "Body");

        var changed = CollisionShapes.SetAcross(
            scene.Layers[0], 1, 4, shape.Id, new ShapeBox(30, 40, 20, 60));

        Assert.Equal(4, changed);
        Assert.Null(CollisionShapes.At(scene.Layers[0].Cels[0].Frame!, shape.Id));
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(
                new ShapeBox(30, 40, 20, 60),
                CollisionShapes.At(scene.Layers[0].Cels[i].Frame!, shape.Id));
        }
        Assert.Null(CollisionShapes.At(scene.Layers[0].Cels[5].Frame!, shape.Id));
    }

    [Fact]
    public void AHeldDrawingIsVisitedOnceRatherThanTwice()
    {
        // On 2s a range holds each drawing twice, and sizing the same shape twice on
        // one drawing is the same edit done twice — so the count would lie.
        var scene = new Scene { FrameCount = 4 };
        var layer = new Layer();
        layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
        layer.Cels.Add(new Cel());
        layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
        layer.Cels.Add(new Cel());
        scene.Layers.Add(layer);
        var shape = CollisionShapes.Declare(scene, "Body");

        var changed = CollisionShapes.SetAcross(layer, 0, 4, shape.Id, new ShapeBox(5, 6, 7, 8));

        Assert.Equal(2, changed);
        Assert.NotNull(CollisionShapes.At(layer.Cels[0].Frame!, shape.Id));
        Assert.NotNull(CollisionShapes.At(layer.Cels[2].Frame!, shape.Id));
    }

    [Fact]
    public void AHitboxIsActiveOnlyWhereItIsPlaced()
    {
        // What "absence is the off state" means in practice, and the reason there is
        // no Active flag: a sword connects on two frames of a six-frame swing, so the
        // rectangle exists on those two drawings and nowhere else. A flag would be a
        // second thing to keep in step with the animation.
        var scene = OnOnes(6);
        var sword = CollisionShapes.Declare(scene, "Sword", ShapeRole.Hitbox);
        CollisionShapes.SetAcross(scene.Layers[0], 2, 2, sword.Id, new ShapeBox(0, 0, 40, 10));

        for (var i = 0; i < 6; i++)
        {
            var active = CollisionShapes.ResolvedAt(scene, i).ContainsKey(sword.Id);
            Assert.Equal(i is 2 or 3, active);
        }
    }

    [Fact]
    public void ClearingAcrossARangeRemovesItAndTheKeyWithIt()
    {
        var scene = OnOnes(4);
        var shape = CollisionShapes.Declare(scene, "Body");
        CollisionShapes.SetAcross(scene.Layers[0], 0, 4, shape.Id, new ShapeBox(1, 2, 3, 4));

        var cleared = CollisionShapes.ClearAcross(scene.Layers[0], 1, 2, shape.Id);

        Assert.Equal(2, cleared);
        Assert.NotNull(CollisionShapes.At(scene.Layers[0].Cels[0].Frame!, shape.Id));
        Assert.Null(scene.Layers[0].Cels[1].Frame!.Shapes);
        Assert.NotNull(CollisionShapes.At(scene.Layers[0].Cels[3].Frame!, shape.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void ANonRangeIsANoOp(int count)
    {
        var scene = OnOnes(3);
        var shape = CollisionShapes.Declare(scene, "Body");
        Assert.Equal(0, CollisionShapes.SetAcross(
            scene.Layers[0], 0, count, shape.Id, new ShapeBox(1, 1, 1, 1)));
    }

    // ---- the property that made "on the frame" the right choice -----------------

    [Fact]
    public void AShapeTravelsWithItsDrawingWhenTheTimingChanges()
    {
        // The reason rectangles are on the frame rather than in a table keyed by
        // frame index. For a hitbox this is not cosmetic: a re-time that moved the
        // sword's active frames off the swing would be a gameplay bug with no
        // visible cause in the drawing.
        var scene = OnOnes(4);
        var shape = CollisionShapes.Declare(scene, "Sword", ShapeRole.Hitbox);
        var layer = scene.Layers[0];
        var third = layer.Cels[2].Frame!;
        CollisionShapes.SetAcross(layer, 2, 1, shape.Id, new ShapeBox(9, 9, 9, 9));

        Lightbox.Core.Timeline.ExposureSheet.ApplyTiming(
            layer, 0, 4, new Lightbox.Core.Timeline.TimingPreset("On 2s", [2]));

        var moved = layer.Cels.First(c => ReferenceEquals(c.Frame, third));
        Assert.Equal(new ShapeBox(9, 9, 9, 9), CollisionShapes.At(moved.Frame!, shape.Id));
    }

    [Fact]
    public void AShapeRoundTripsThroughTheFile()
    {
        var doc = new Doc { Scene = OnOnes(2) };
        var shape = CollisionShapes.Declare(doc.Scene, "Sword", ShapeRole.Hitbox);
        CollisionShapes.SetAcross(doc.Scene.Layers[0], 0, 2, shape.Id, new ShapeBox(12.5, 34.5, 40, 8.25));

        var back = DocJson.Clone(doc);

        var declared = Assert.Single(back.Scene.Shapes!);
        Assert.Equal("Sword", declared.Name);
        Assert.Equal(ShapeRole.Hitbox, declared.Role);
        Assert.Equal(
            new ShapeBox(12.5, 34.5, 40, 8.25),
            CollisionShapes.At(back.Scene.Layers[0].Cels[0].Frame!, declared.Id));
    }

    [Fact]
    public void ARoleDefaultsToHurtboxBecauseThatIsWhatAnArtistDrawsFirst()
    {
        Assert.Equal(ShapeRole.Hurtbox, CollisionShapes.Declare(new Scene(), "Body").Role);
        // And a blank name gets a usable one rather than an empty row.
        Assert.Equal("Body", CollisionShapes.Declare(new Scene(), "   ").Name);
    }

    [Fact]
    public void TheCentreIsWhereACollidersOffsetWillBeMeasuredFrom()
    {
        var box = new ShapeBox(10, 20, 30, 40);
        Assert.Equal(25, box.CentreX);
        Assert.Equal(40, box.CentreY);
    }

    // ---- resolving for the exporter --------------------------------------------

    [Fact]
    public void ResolvingAtAFrameReadsThroughHolds()
    {
        var scene = new Scene { FrameCount = 3 };
        var layer = new Layer();
        layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
        layer.Cels.Add(new Cel());
        scene.Layers.Add(layer);
        var shape = CollisionShapes.Declare(scene, "Body");
        CollisionShapes.SetAcross(layer, 0, 1, shape.Id, new ShapeBox(4, 5, 6, 7));

        // A hurtbox that vanished on every other frame would make the character
        // invulnerable half the time.
        Assert.Equal(new ShapeBox(4, 5, 6, 7), CollisionShapes.ResolvedAt(scene, 1)[shape.Id]);
    }

    [Fact]
    public void AnUpperLayerWinsWhenTwoPlaceTheSameShape()
    {
        var scene = OnOnes(1);
        var upper = new Layer { Name = "Upper" };
        upper.Cels.Add(new Cel { Frame = new PaintedFrame() });
        scene.Layers.Add(upper);
        var shape = CollisionShapes.Declare(scene, "Body");

        CollisionShapes.SetAcross(scene.Layers[0], 0, 1, shape.Id, new ShapeBox(1, 1, 1, 1));
        CollisionShapes.SetAcross(upper, 0, 1, shape.Id, new ShapeBox(2, 2, 2, 2));

        Assert.Equal(new ShapeBox(2, 2, 2, 2), CollisionShapes.ResolvedAt(scene, 0)[shape.Id]);
    }

    [Fact]
    public void NoDeclarationsMeansNothingToResolve()
    {
        Assert.Empty(CollisionShapes.ResolvedAt(OnOnes(2), 0));
    }

    [Fact]
    public void AnchorsAndShapesDoNotInterfereWithEachOther()
    {
        // They share the same two-place design and sit on the same frame, so a
        // clumsy implementation could clear one while clearing the other.
        var scene = OnOnes(2);
        var anchor = Anchors.Declare(scene, "Hand");
        var shape = CollisionShapes.Declare(scene, "Body");
        Anchors.SetAcross(scene.Layers[0], 0, 2, anchor.Id, new AnchorPoint(1, 2));
        CollisionShapes.SetAcross(scene.Layers[0], 0, 2, shape.Id, new ShapeBox(3, 4, 5, 6));

        CollisionShapes.Remove(scene, shape.Id);

        Assert.NotNull(Anchors.At(scene.Layers[0].Cels[0].Frame!, anchor.Id));
        Assert.NotNull(scene.Anchors);
    }
}
