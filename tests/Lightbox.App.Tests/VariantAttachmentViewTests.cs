using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q143 through the surfaces: the editor that dresses a variant, the canvas
/// that shows the armor riding the rig, and the export that keeps the promise.
/// </summary>
/// <remarks>
/// The resolution maths is <c>VariantAttachmentTests</c> in Core. What these
/// guard is the wiring — the exact shape B58 taught: a feature can be fully
/// modelled, fully tested and invisible, because nothing on any surface calls
/// it.
/// </remarks>
[Collection("BrushState")]
public class VariantAttachmentViewTests(ITestOutputHelper output) : BrushStateIsolated
{
    private sealed class Scratch : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), $"lbatt-{Guid.NewGuid():N}");

        public string Root => Path.Combine(_parent, "Production.lbproj");

        public void Dispose()
        {
            if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }
    }

    /// <summary>A pauldron: one symbol frame with a fat opaque stroke at its origin.</summary>
    private static Symbol Pauldron()
    {
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#ff2020",
            Points = [new StrokePoint(0, 0, 1), new StrokePoint(1, 0, 1)],
            Brush = new BrushSettings { Size = 12, Opacity = 1 },
        });
        return new Symbol { Name = "Pauldron", Frames = [frame] };
    }

    /// <summary>
    /// A knight whose walk has a "shoulder" anchor at (40, 30), a Winter
    /// variant wearing the pauldron on it, and the walk open on the canvas.
    /// </summary>
    private static (MainViewModel Vm, Project Project, ProjectFolder Knight, SubjectVariant Winter)
        DressedKnight(Scratch scratch)
    {
        var vm = VmLayers.PaperVm();
        vm.NewProject(scratch.Root, "Production");
        var project = vm.ProjectDocker.Project!;

        var pauldron = Pauldron();
        project.Symbols[pauldron.Id] = pauldron;

        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        var doc = DocumentFactory.CreateDoc(80, 80, 12);
        var anchor = Anchors.Declare(doc.Scene, "shoulder");
        Anchors.SetAcross(doc.Scene.Layers[0], 0, 1, anchor.Id, new AnchorPoint(40, 30));
        var walk = ProjectIo.AddDocument(project, "Walk", doc, knight);

        var winter = ProjectIo.AddVariant(project, knight, "Winter");
        (winter.Attachments ??= []).Add(new VariantAttachment
        {
            Anchor = "shoulder",
            SymbolId = pauldron.Id,
        });

        vm.ProjectDocker.Adopt(project);
        vm.OpenProjectDocument(walk, project.Loaded[walk.Id]);
        return (vm, project, knight, winter);
    }

    /// <summary>
    /// The green channel at a canvas point. The composite sits on the
    /// document's white paper, so alpha is 255 everywhere and says nothing —
    /// the red pauldron is told from bare paper by green collapsing.
    /// </summary>
    private static byte GreenAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image!);
        return bmp.GetPixel(x, y).Green;
    }

    [AvaloniaFact]
    public void TheCanvasShowsTheArmorOnlyWhileTheVariantIsViewed()
    {
        using var scratch = new Scratch();
        var (vm, _, knight, winter) = DressedKnight(scratch);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, winter.Id));
        var worn = GreenAt(latest!, 40, 30);

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, null));
        var bare = GreenAt(latest!, 40, 30);

        output.WriteLine($"worn green {worn}, bare green {bare}");
        Assert.True(worn < 128, $"the pauldron is missing from the canvas: green {worn}");
        Assert.Equal(255, bare);   // bare paper again — the armor left with the variant
    }

    [AvaloniaFact]
    public void MovingTheAnchorMovesTheArmor()
    {
        // The anchor is the per-frame override channel — nudge it and the
        // armor goes with it, which is the whole "set anchors in the base
        // animation and the additions follow" promise.
        using var scratch = new Scratch();
        var (vm, _, knight, winter) = DressedKnight(scratch);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, winter.Id));
        vm.RigEditMode = true;
        var id = vm.Doc.Scene.Anchors![0].Id;
        vm.SelectedRigMarkId = id;

        vm.DragRig(id, RigCorner.None, 20, 25);

        var oldSpot = GreenAt(latest!, 40, 30);
        var newSpot = GreenAt(latest!, 60, 55);
        output.WriteLine($"old green {oldSpot}, new green {newSpot}");
        Assert.Equal(255, oldSpot);   // bare paper where it used to sit
        Assert.True(newSpot < 128, $"the pauldron did not follow the anchor: green {newSpot}");
    }

    [AvaloniaFact]
    public void TheExportKeepsThePromiseTheCanvasMade()
    {
        // An export that showed armor on screen and dropped it from the sheet
        // would be the canvas lying about the deliverable — the same contract
        // the variant's colours already keep.
        using var scratch = new Scratch();
        var (vm, project, knight, winter) = DressedKnight(scratch);
        var sheet = Path.Combine(Path.GetTempPath(), $"lbatt-{Guid.NewGuid():N}.png");
        try
        {
            vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, winter.Id));
            var result = SpriteSheetExporter.Export(
                vm.Doc, sheet, new SpriteSheetOptions { Trim = SpriteTrim.None });
            using var worn = SKBitmap.Decode(result.SheetPath);

            vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, null));
            var bareResult = SpriteSheetExporter.Export(
                vm.Doc, sheet, new SpriteSheetOptions { Trim = SpriteTrim.None });
            using var bare = SKBitmap.Decode(bareResult.SheetPath);

            output.WriteLine(
                $"worn {worn.GetPixel(40, 30).Alpha}, bare {bare.GetPixel(40, 30).Alpha}");
            Assert.True(worn.GetPixel(40, 30).Alpha > 32, "the export dropped the armor");
            Assert.Equal(0, bare.GetPixel(40, 30).Alpha);
        }
        finally
        {
            if (File.Exists(sheet)) File.Delete(sheet);
        }
    }

    [AvaloniaFact]
    public void AFrameThatWearsNothingResolvesOnceNotPerPublish()
    {
        // leak-hunter's finding, kept as a count (B151's precedent): the
        // null result used to be skipped rather than cached, so a drag
        // across a frame with no placed anchor re-ran the resolution on
        // every pointer event.
        using var scratch = new Scratch();
        var (vm, _, knight, winter) = DressedKnight(scratch);
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, winter.Id));

        var calls = 0;
        var inner = AttachmentOverlay.Resolver!;
        AttachmentOverlay.Resolver = (s, i) => { calls++; return inner(s, i); };
        // An index past the placed drawing: resolves to nothing, every time.
        for (var publish = 0; publish < 5; publish++) vm.WornOverlayFor(vm.Doc.Scene, 3);

        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public void TheEditorDressesAndUndressesAVariant()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, winter) = DressedKnight(scratch);
        winter.Attachments = null;   // start bare; the editor does the dressing
        var window = new ProjectWindowViewModel(project, () => { });
        window.SetSelection(window.Rows.Where(r => r.IsFolder));

        var row = Assert.Single(window.FolderVariantRows);
        row.NewSymbol = Assert.Single(row.Symbols);
        row.NewAnchor = " shoulder ";
        row.AddAttachmentCommand.Execute(null);

        var worn = Assert.Single(winter.Attachments!);
        Assert.Equal("shoulder", worn.Anchor);
        Assert.Contains("wears", window.Status);

        var freshRow = Assert.Single(window.FolderVariantRows);
        freshRow.RemoveAttachmentCommand.Execute(Assert.Single(freshRow.Attachments));
        // Null, not empty: a variant that stopped wearing things serializes
        // like one that never did.
        Assert.Null(winter.Attachments);
    }
}
