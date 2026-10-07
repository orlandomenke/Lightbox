using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Cage mode in the transform tool (Q199), through the real view-model and
/// gizmo paths: begin, drag a handle, preview, commit.
/// </summary>
/// <remarks>
/// The geometry is <c>CageWarpTests</c> in the Core suite. What is tested here
/// is the half that only exists once the tool is wired — that a commit reaches
/// the stroke record with the points a bend needs, that a raster baseline
/// follows the same mesh, that the gizmo's modes exclude one another, and that
/// a posed drawing goes through the pose-space mover.
/// </remarks>
[Collection("BrushState")]
public class TransformCageTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 400;
    private const int H = 400;

    /// <summary>A figure: one long horizontal stroke from x=50 to x=350 at y=200.</summary>
    private static MainViewModel VmWithAFigure()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("cage", W, H, 12, 180, "#808080", false));
        var layer = vm.Doc.Scene.Layers[^1];
        var frame = layer.Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#101010",
            Brush = new BrushSettings { Size = 8, Hardness = 0.8, Flow = 1, Opacity = 1 },
            // Two points, 300 px apart: the coarse sampling that makes insertion matter.
            Points = [new StrokePoint(50, 200, 1), new StrokePoint(350, 200, 1)],
        });
        return vm;
    }

    private static Stroke TheStroke(MainViewModel vm) =>
        vm.Doc.Scene.Layers[^1].Cels[0].Frame!.Strokes[0];

    /// <summary>The transform box the session opened on, as the lattice's identity over it.</summary>
    private static CageWarp.Lattice LatticeOver(MainViewModel vm, int grid = 2)
    {
        // The box is the visible drawing: the stroke plus its brush reach.
        var pts = TheStroke(vm).Points;
        var reach = Lightbox.Raster.BrushEngine.ReachOf(TheStroke(vm).Brush);
        return CageWarp.Lattice.Identity(
            pts.Min(p => p.X) - reach, pts.Min(p => p.Y) - reach,
            pts.Max(p => p.X) + reach, pts.Max(p => p.Y) + reach, grid, grid);
    }

    /// <summary>
    /// <b>The whole promise, end to end: the ends stay, the middle bends.</b>
    /// A 2×2 lattice's centre handle dragged down 60 px bows the line through
    /// the drag, and the points it needs to bow were inserted on the way.
    /// </summary>
    [AvaloniaFact]
    public void ACageCommitBendsTheLineThroughTheDraggedHandle()
    {
        var vm = VmWithAFigure();
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        var lattice = LatticeOver(vm);
        var centre = lattice.IndexOf(1, 1);
        var (cx, cy) = lattice.Points[centre];
        lattice = CageWarp.Drag(lattice, centre, cx, cy + 60);

        vm.CommitTransformCage(lattice);

        var pts = TheStroke(vm).Points;
        output.WriteLine($"{pts.Count} points; y spans {pts.Min(p => p.Y):0.0}..{pts.Max(p => p.Y):0.0}");
        Assert.False(vm.TransformActive);
        Assert.True(pts.Count > 10, "no points were inserted along the bend");
        // The middle went exactly where the handle went. The ends sit inside
        // the box too — half a cell from the handle on a 2×2 lattice — so the
        // bicubic pulls them a little; what matters is that they barely moved
        // while the middle moved the whole way.
        Assert.InRange(pts.Max(p => p.Y), 259.999, 260.001);
        Assert.InRange(pts[0].Y, 200, 206);
        Assert.InRange(pts[^1].Y, 200, 206);
        // Brush size is untouched: a bent arm keeps its weight.
        Assert.Equal(8, TheStroke(vm).Brush.Size, 9);
    }

    /// <summary>An untouched lattice applied is nothing: no edit, no undo step.</summary>
    [AvaloniaFact]
    public void ApplyingAnUntouchedCageChangesNothing()
    {
        var vm = VmWithAFigure();
        var before = TheStroke(vm).Points.Select(p => (p.X, p.Y)).ToList();
        Assert.True(vm.BeginTransform());

        vm.CommitTransformCage(LatticeOver(vm));

        Assert.Equal(before, TheStroke(vm).Points.Select(p => (p.X, p.Y)).ToList());
        Assert.False(vm.TransformActive);
    }

    /// <summary>
    /// A raster baseline follows the same mesh the strokes do: the pixels the
    /// cage pulled down are found where the handle went, and the pixels outside
    /// the box are where they were.
    /// </summary>
    [AvaloniaFact]
    public void ImportedPixelsAreResampledThroughTheCage()
    {
        var vm = VmWithAFigure();
        var frame = vm.Doc.Scene.Layers[^1].Cels[0].Frame!;
        // A baseline: a black bar across y=195..205 and a red dot outside the box at (20, 20).
        using (var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul))
        {
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.Transparent);
            using var black = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(60, 195, 280, 10, black);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(15, 15, 10, 10, red);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            frame.PngBase64 = Convert.ToBase64String(data.ToArray());
        }
        vm.MarkDocumentEditedForTests();
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        var lattice = LatticeOver(vm);
        var centre = lattice.IndexOf(1, 1);
        var (cx, cy) = lattice.Points[centre];
        lattice = CageWarp.Drag(lattice, centre, cx, cy + 60);

        vm.CommitTransformCage(lattice);

        using var after = SKBitmap.Decode(Convert.FromBase64String(frame.PngBase64!));
        Assert.NotNull(after);
        // The bar's middle moved down with the handle…
        Assert.True(after!.GetPixel(200, 255).Alpha > 200, "the bar did not follow the cage downward");
        Assert.True(after.GetPixel(200, 200).Alpha < 50, "the bar's old pixels were left behind in the box");
        // …and the dot outside the box did not move.
        var dot = after.GetPixel(20, 20);
        Assert.True(dot.Red > 200 && dot.Green < 50, $"the pixels outside the box changed ({dot})");
    }

    /// <summary>
    /// Mode exclusion on the gizmo: cage, bands and perspective are one choice.
    /// </summary>
    [AvaloniaFact]
    public void TheGizmoModesExcludeOneAnother()
    {
        var canvas = new CanvasControl();
        canvas.BeginTransformGizmo(50, 150, 350, 250);
        canvas.TransformCage = true;
        Assert.True(canvas.TransformCage);
        Assert.False(canvas.TransformBands);
        Assert.False(canvas.TransformPerspective);
        Assert.True(canvas.TransformIsIdentity);
        Assert.Null(canvas.TransformCageMesh);              // untouched: nothing to preview

        canvas.TransformBands = true;
        Assert.False(canvas.TransformCage);
        canvas.TransformCage = true;
        canvas.TransformPerspective = true;
        Assert.False(canvas.TransformCage);
    }

    /// <summary>
    /// Dragging a handle through the gizmo's own press/move/release path
    /// changes the lattice, builds a mesh with the expected shape, and makes
    /// the session non-identity; the grid control reseeds the lattice.
    /// </summary>
    [AvaloniaFact]
    public void DraggingAHandleBuildsTheMeshAndChangingTheGridReseeds()
    {
        var canvas = new CanvasControl();
        canvas.BeginTransformGizmo(50, 150, 350, 250);
        canvas.TransformCage = true;
        var changes = 0;
        canvas.TransformGizmoChanged += () => changes++;

        // Handle (1, 1) of a 3×3 lattice over that 300×100 box stands at (150, 183.3).
        Assert.Equal((int?)canvas.TransformCageResult.IndexOf(1, 1), canvas.TxCageHitTest(151, 184));
        Assert.Null(canvas.TxCageHitTest(200, 200));          // between handles
        Assert.True(canvas.TxCagePress(151, 184));
        canvas.TxCageDragTo(180, 230);
        Assert.False(canvas.TransformIsIdentity);
        Assert.True(changes >= 1);
        var mesh = canvas.TransformCageMesh;
        Assert.NotNull(mesh);
        Assert.Equal(13 * 13, mesh!.Positions.Length);
        Assert.Equal(12 * 12 * 6, mesh.Indices.Length);
        // The mesh lands where the lattice says: the vertex at the centre handle moved with it.
        var at = canvas.TransformCageResult.Points[canvas.TransformCageResult.IndexOf(1, 1)];
        Assert.Equal((180, 230), at);
        Assert.Same(mesh, canvas.TransformCageMesh);        // cached until the next move

        canvas.TransformCageGrid = 4;
        Assert.Equal(4, canvas.TransformCageGrid);
        Assert.True(canvas.TransformIsIdentity);            // reseeded
        Assert.Equal(25, canvas.TransformCageResult.Points.Count);
        canvas.TransformCageGrid = 99;
        Assert.Equal(CageWarp.MaxGrid, canvas.TransformCageGrid);
    }

    /// <summary>
    /// A press inside the box that is not on a handle is still cage business,
    /// so the pinned box's own handles beneath cannot resize it; a press well
    /// outside is not.
    /// </summary>
    [AvaloniaFact]
    public void CageModeOwnsPressesInsideTheBox()
    {
        var canvas = new CanvasControl();
        canvas.BeginTransformGizmo(50, 150, 350, 250);
        canvas.TransformCage = true;
        Assert.True(canvas.TxCagePress(120, 170));         // inside, between handles
        Assert.False(canvas.TxCagePress(10, 10));           // outside
    }

    /// <summary>
    /// The preview entry point marks the box and the mesh's reach dirty, and
    /// clears without a session.
    /// </summary>
    [AvaloniaFact]
    public void ThePreviewRidesTheSessionAndClearsWithIt()
    {
        // Drawn through the pen rather than added to the record: this test
        // reads pixels, and the frame cache only knows a stroke that was
        // committed through it. The other tests here read the record.
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false, ColorHex = "#101010", BrushSize = 8, BrushHardness = 0.8,
            BrushOpacity = 1, BrushFlow = 1, BrushWetEdge = 0, BrushGranulation = 0, BrushScatter = 0,
        };
        vm.NewDocument(new NewDocumentSettings("cage", W, H, 12, 180, "#808080", false));
        vm.BeginStroke(50, 200, 1);
        vm.MoveStroke(200, 200, 1);
        vm.MoveStroke(350, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        Assert.True(vm.BeginTransform());
        var lattice = LatticeOver(vm);
        var centre = lattice.IndexOf(1, 1);
        var (cx, cy) = lattice.Points[centre];
        lattice = CageWarp.Drag(lattice, centre, cx, cy + 60);

        vm.PreviewTransformCage(CanvasControl.ToPassMesh(CageWarp.MeshOf(lattice)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        using (var bmp = SKBitmap.FromImage(latest!.Image))
        {
            // The line's middle is previewed where the cage takes it, and the
            // paper shows where it was.
            Assert.True(bmp.GetPixel(200, 258).Red < 120, $"the preview did not bend the line (red {bmp.GetPixel(200, 258).Red})");
            Assert.True(bmp.GetPixel(200, 200).Red > 100, $"the preview left the line where it was (red {bmp.GetPixel(200, 200).Red})");
        }

        vm.CancelTransform();
        Assert.False(vm.TransformActive);
        vm.PreviewTransformCage(null);                      // nothing to clear; no throw
    }

    /// <summary>
    /// A posed drawing is caged on the picture the artist sees and written back
    /// to rest (B381's mover): the posed line bows through the drag.
    /// </summary>
    [AvaloniaFact]
    public void APosedDrawingIsCagedWhereItStands()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("cage", W, H, 12, 180, "#ffffff", false));
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        vm.SetLayerBone(bone);
        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);
        var frame = ExposureSheet.ExposedFrame(layer, vm.CurrentFrameIndex)!;
        var stroke = new Stroke
        {
            Brush = new BrushSettings { Size = 6 },
            Points = [new StrokePoint(110, 150, 1), new StrokePoint(130, 150, 1), new StrokePoint(150, 150, 1)],
        };
        frame.Strokes.Add(stroke);
        vm.PosingMode = true;
        vm.PoseBoneTo(bone, 100, 250);                      // posed: x=100, y 160..200
        vm.PosingMode = false;
        vm.ArmatureEditMode = false;
        vm.MarkDocumentEditedForTests();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        // A lattice over the posed line, its centre pulled 30 px right.
        var lattice = CageWarp.Lattice.Identity(80, 140, 120, 220, 2, 2);
        var centre = lattice.IndexOf(1, 1);
        lattice = CageWarp.Drag(lattice, centre, 130, 180);
        vm.CommitTransformCage(lattice);

        var posed = Skinning.PoseFrameForRender(vm.Doc, frame, vm.CurrentFrameIndex, RigIndex.For(vm.Doc));
        var shown = posed.Strokes.Single(s => s.Id == stroke.Id).Points;
        output.WriteLine($"posed x: {string.Join(", ", shown.Select(p => p.X.ToString("0.0")))}");
        // The posed middle point stood on the handle, so it went exactly where
        // the handle went; the ends, half a cell away, were pulled less. (The
        // render densifies the posed line, so the middle is found by its x.)
        Assert.InRange(shown.Max(p => p.X), 129.999, 130.001);
        Assert.InRange(shown[0].X, 100.5, 129.5);
        Assert.InRange(shown[^1].X, 100.5, 129.5);
        // No points were inserted into the weighted record (B381): still three.
        Assert.Equal(3, stroke.Points.Count);
    }
}
