using Avalonia.Headless.XUnit;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A folder that keeps its layers inside a shape (Q215) — Krita's inherit
/// alpha, set once on the folder. The shape is the union of everything from
/// the folder's bottom up to the shape layer, and it carves every layer above
/// that inside the same folder.
/// </summary>
public class FolderShapeTests(ITestOutputHelper output)
{
    private static Layer LayerWith(string name, string? folder = null, int cels = 3)
    {
        var layer = new Layer { Name = name, GroupId = folder };
        for (var i = 0; i < cels; i++) layer.Cels.Add(new Cel { Frame = new Frame() });
        return layer;
    }

    /// <summary>Outside, then a folder holding skin, hair (the shape), shade and light.</summary>
    private static (Scene Scene, LayerGroup Folder) Character()
    {
        var folder = new LayerGroup { Id = "group-character", Name = "Character" };
        var scene = new Scene { Width = 64, Height = 48, FrameCount = 3 };
        scene.LayerGroups.Add(folder);
        scene.Layers.AddRange(
        [
            LayerWith("Background"),
            LayerWith("Skin", folder.Id),
            LayerWith("Hair", folder.Id),
            LayerWith("Shade", folder.Id),
            LayerWith("Light", folder.Id),
        ]);
        folder.ShapeLayerId = scene.Layers[2].Id;
        return (scene, folder);
    }

    [Fact]
    public void EveryLayerAboveTheShapeIsCarvedByTheUnionBeneathIt()
    {
        var (scene, folder) = Character();
        var skin = scene.Layers[1];
        var hair = scene.Layers[2];

        foreach (var carved in new[] { 3, 4 })
        {
            var shape = Assert.Single(LayerShapes.For(scene, carved, 1)!);
            Assert.False(shape.Inverted);
            // The shape layer first, then everything beneath it in the folder.
            Assert.Same(hair.Cels[1].Frame, shape.Frame);
            Assert.Same(skin.Cels[1].Frame, Assert.Single(shape.Or!).Frame);
            Assert.True(LayerShapes.Carves(scene, carved));
            Assert.Same(folder, LayerShapes.FolderShapingOf(scene, carved));
        }

        // The shape itself, and what is outside the folder, are not carved.
        foreach (var free in new[] { 0, 1, 2 })
        {
            Assert.Null(LayerShapes.For(scene, free, 1));
            Assert.False(LayerShapes.Carves(scene, free));
            Assert.Null(LayerShapes.FolderShapingOf(scene, free));
        }
    }

    [Fact]
    public void ALayerAboveTheFolderIsNotCarved()
    {
        var (scene, _) = Character();
        scene.Layers.Add(LayerWith("Sky"));
        Assert.Null(LayerShapes.For(scene, 5, 0));
    }

    [Fact]
    public void AHiddenOrEmptyShapeShowsNothingAbove()
    {
        var (scene, folder) = Character();
        scene.Layers[1].Visible = false;
        scene.Layers[2].Visible = false;
        Assert.Empty(LayerShapes.For(scene, 3, 0)!);
        Assert.False(LayerShapes.FolderShapeHasContent(scene, folder, 0));

        // One member back is enough: the union is whatever still shows.
        scene.Layers[1].Visible = true;
        var shape = Assert.Single(LayerShapes.For(scene, 3, 0)!);
        Assert.Same(scene.Layers[1].Cels[0].Frame, shape.Frame);
        Assert.Null(shape.Or);
        Assert.True(LayerShapes.FolderShapeHasContent(scene, folder, 0));
    }

    [Fact]
    public void AMaskedMemberCarvesItsOwnCoverage()
    {
        var (scene, _) = Character();
        var skin = scene.Layers[1];
        skin.Mask = new LayerMask { Inverted = true };

        var shape = Assert.Single(LayerShapes.For(scene, 3, 0)!);
        var member = Assert.Single(shape.Or!);
        Assert.Same(skin.Mask.Frame, member.Carve);
        Assert.True(member.CarveInverted);
    }

    [Fact]
    public void ANestedFolderInsideIsPartOfTheShapeAndCarvedAbove()
    {
        // Character > [Skin, Eyes folder > [Iris], Hair*] , Shade — the iris in a
        // subfolder beneath the shape counts towards it; a subfolder above it is carved.
        var (scene, folder) = Character();
        var eyes = new LayerGroup { Id = "group-eyes", ParentId = folder.Id };
        var glints = new LayerGroup { Id = "group-glints", ParentId = folder.Id };
        scene.LayerGroups.AddRange([eyes, glints]);
        var iris = LayerWith("Iris", eyes.Id);
        scene.Layers.Insert(2, iris); // between Skin and Hair
        var glint = LayerWith("Glint", glints.Id);
        scene.Layers.Add(glint);

        var shape = Assert.Single(LayerShapes.For(scene, scene.Layers.Count - 1, 0)!);
        Assert.Contains(shape.Or!, m => ReferenceEquals(m.Frame, iris.Cels[0].Frame));
        Assert.Null(LayerShapes.For(scene, 2, 0));
    }

    [Fact]
    public void AFolderLoopInACraftedFileCarvesOnce()
    {
        // Sensitivity S1: A inside B inside A. Each folder's shape carves once;
        // the loop is not walked to the depth cap stacking the same carve.
        var (scene, folder) = Character();
        var outer = new LayerGroup { Id = "group-outer", ParentId = folder.Id };
        folder.ParentId = outer.Id;
        scene.LayerGroups.Add(outer);

        var shapes = LayerShapes.For(scene, 3, 0)!;
        Assert.Single(shapes);
        Assert.Same(folder, LayerShapes.FolderShapingOf(scene, 3));
    }

    [Fact]
    public void AShapeIdThatLeftTheFolderCarvesNothing()
    {
        var (scene, folder) = Character();
        scene.Layers[2].GroupId = null; // dragged out of the folder
        Assert.Null(LayerShapes.For(scene, 3, 0));
        Assert.Null(LayerShapes.FolderShapingOf(scene, 3));

        FolderTree.Settle(scene, null);
        Assert.Null(folder.ShapeLayerId);
    }

    [Fact]
    public void AFolderWithoutAShapeWritesNoKey()
    {
        var (scene, folder) = Character();
        folder.ShapeLayerId = null;
        var doc = new Doc { Scene = scene };
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"shapeLayerId\"", json);

        folder.ShapeLayerId = scene.Layers[2].Id;
        var back = DocJson.Deserialize(DocJson.Serialize(doc))!;
        Assert.Equal(scene.Layers[2].Id, back.Scene.LayerGroups[0].ShapeLayerId);
    }

    // ---- the compositor: the union carves --------------------------------------

    private static SKBitmap Rect(SKColor color, SKRect where, int size = 12)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(where, paint);
        canvas.Flush();
        return bmp;
    }

    [AvaloniaFact]
    public void TheShadeShowsInsideEitherFlatAndNowhereElse()
    {
        // Two flats side by side with a gap; a shade layer covering everything.
        using var left = Rect(SKColors.Red, SKRect.Create(0, 0, 4, 12));
        using var right = Rect(SKColors.Green, SKRect.Create(8, 0, 4, 12));
        using var shade = Rect(SKColors.Blue, SKRect.Create(0, 0, 12, 12));
        using var image = SceneRenderer.Compose(12, 12,
        [
            new RenderPass(left, null, 1),
            new RenderPass(right, null, 1),
            new RenderPass(shade, null, 1,
                Shapes: [new PassShape(right, Or: [new PassShape(left)])]),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);

        var inLeft = bmp.GetPixel(1, 6);
        var inGap = bmp.GetPixel(6, 6);
        var inRight = bmp.GetPixel(10, 6);
        output.WriteLine($"left {inLeft}, gap {inGap}, right {inRight}");
        Assert.Equal(SKColors.Blue, inLeft);
        Assert.Equal(SKColors.White, inGap);
        Assert.Equal(SKColors.Blue, inRight);
    }

    [AvaloniaFact]
    public void AMembersOwnMaskTrimsItsShareOfTheUnion()
    {
        using var left = Rect(SKColors.Red, SKRect.Create(0, 0, 4, 12));
        using var right = Rect(SKColors.Green, SKRect.Create(8, 0, 4, 12));
        // The left flat's mask hides its top half.
        using var bottomHalf = Rect(SKColors.White, SKRect.Create(0, 6, 12, 6));
        using var shade = Rect(SKColors.Blue, SKRect.Create(0, 0, 12, 12));
        using var image = SceneRenderer.Compose(12, 12,
        [
            new RenderPass(right, null, 1),
            new RenderPass(shade, null, 1,
                Shapes: [new PassShape(right, Or: [new PassShape(left, Carve: bottomHalf)])]),
        ], SKColors.White);
        using var bmp = SKBitmap.FromImage(image);

        Assert.Equal(SKColors.White, bmp.GetPixel(1, 2)); // masked off
        Assert.Equal(SKColors.Blue, bmp.GetPixel(1, 9));  // inside the mask
        Assert.Equal(SKColors.Blue, bmp.GetPixel(10, 2)); // the other member, untouched
    }
}
