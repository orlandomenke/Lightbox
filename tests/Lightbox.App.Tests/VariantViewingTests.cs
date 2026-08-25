using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Viewing a variant, end to end through the docker: the picker, the palette
/// stand-in that repaints the shared drawings, the override rows, and the two
/// gestures that create and dissolve an override.
/// </summary>
/// <remarks>
/// The model half — inheritance, palette copies, serialization — is
/// <c>CharacterVariantTests</c>. These exist because that half shipped long
/// before any surface could reach it: <c>ActiveVariant</c> was written by
/// nothing, <c>OverrideDocument</c> had no caller, and a variant could be
/// created in the project window and then never looked at. Q143.
/// </remarks>
[Collection("BrushState")]
public class VariantViewingTests(ITestOutputHelper output) : BrushStateIsolated
{
    private sealed class Scratch : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), $"lbvar-{Guid.NewGuid():N}");

        public string Root => Path.Combine(_parent, "Production.lbproj");

        public void Dispose()
        {
            if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }
    }

    private static Doc Drawing(string paletteId, string swatchId)
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#8090a0",
            SwatchId = swatchId,
            PaletteId = paletteId,
            Points = [new StrokePoint(10, 10, 1), new StrokePoint(50, 50, 1)],
            Brush = new BrushSettings { Size = 8, Opacity = 1 },
        });
        return doc;
    }

    /// <summary>
    /// A knight with a scoped palette, one armour swatch, one walk cycle — and
    /// the walk open on the canvas, so the registry is resolving for it.
    /// </summary>
    private static (MainViewModel Vm, Project Project, ProjectFolder Knight,
        Palette BasePalette, Swatch Armour, DocumentRef Walk) Knight(Scratch scratch)
    {
        var vm = VmLayers.PaperVm();
        vm.NewProject(scratch.Root, "Production");
        var project = vm.ProjectDocker.Project!;

        var armour = new Swatch { Color = "#8090a0", Name = "Armour" };
        var palette = new Palette { Name = "Knight", Swatches = [armour] };
        project.Palettes.Add(palette);

        var knight = ProjectFolders.Add(project.Manifest, "Knight");
        ResourceScopes.Declare(project.Manifest, knight, PaletteScopes.Kind, palette.Id);
        var walk = ProjectIo.AddDocument(project, "Walk", Drawing(palette.Id, armour.Id), knight);

        vm.ProjectDocker.Adopt(project);
        vm.OpenProjectDocument(walk, project.Loaded[walk.Id]);
        return (vm, project, knight, palette, armour, walk);
    }

    private static SubjectVariant Winter(Project project, ProjectFolder knight, string recolour)
    {
        var variant = ProjectIo.AddVariant(project, knight, "Winter Armour");
        project.Palettes.First(p => p.Id == variant.PaletteId).Swatches[0].Color = recolour;
        return variant;
    }

    private static ProjectRow Row(MainViewModel vm, Func<ProjectRow, bool> which) =>
        vm.ProjectDocker.Rows.First(which);

    [AvaloniaFact]
    public void SwitchingTheVariantRepaintsTheSharedDrawing()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, basePalette, armour, _) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");

        // The whole trick: strokes name the base palette, so the variant's
        // copy has to answer for the base palette's id — not merely exist.
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));
        var viewed = PaletteRegistry.ResolveSwatch(basePalette.Id!, armour.Id)!.Color;

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, null));
        var back = PaletteRegistry.ResolveSwatch(basePalette.Id!, armour.Id)!.Color;

        output.WriteLine($"viewed {viewed}, base {back}");
        Assert.Equal("#f0f8ff", viewed);
        Assert.Equal("#8090a0", back);
    }

    [AvaloniaFact]
    public void TheFolderRowWearsTheVariantsName()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, _, _, _) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));
        Assert.Equal("Winter Armour", Row(vm, r => r.Folder == knight && r.IsFolder).VariantBadge);

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, null));
        Assert.Equal("", Row(vm, r => r.Folder == knight && r.IsFolder).VariantBadge);
    }

    [AvaloniaFact]
    public void ViewingAVariantIsNotAnEdit()
    {
        // Which variant is on screen is view state, like the playhead: nothing
        // serialized moved, so nothing may be marked unsaved by looking.
        using var scratch = new Scratch();
        var (vm, project, knight, _, _, _) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");
        // Compared before and after rather than asserted clean, because
        // opening the project already dirtied the tab — the claim under test
        // is that the switch adds nothing to either ledger of unsaved work.
        var dirtyBefore = vm.ProjectDocker.Dirty.Count;
        var tabBefore = vm.ActiveTab!.IsDirty;
        var revisionBefore = vm.ActiveTab!.Editor.Revision;

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));

        Assert.Equal(dirtyBefore, vm.ProjectDocker.Dirty.Count);
        Assert.Equal(tabBefore, vm.ActiveTab!.IsDirty);
        Assert.Equal(revisionBefore, vm.ActiveTab!.Editor.Revision);
    }

    [AvaloniaFact]
    public void TheVariantsOwnDrawingStandsInOnItsOwnRow()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, basePalette, armour, walk) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");
        var own = ProjectIo.OverrideDocument(
            project, knight, variant, walk, Drawing(basePalette.Id!, armour.Id));

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation == own);
        Assert.DoesNotContain(vm.ProjectDocker.Rows, r => r.Animation == walk);

        // And the base never shows the variant's own art — one walk cycle per
        // view, never two.
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, null));
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation == walk);
        Assert.DoesNotContain(vm.ProjectDocker.Rows, r => r.Animation == own);
    }

    [AvaloniaFact]
    public void GivingTheVariantItsOwnVersionIsADuplicateThatStandsIn()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, _, _, walk) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));

        vm.ProjectDocker.Selected = Row(vm, r => r.Animation == walk);
        vm.ProjectDocker.GiveVariantOwnVersionCommand.Execute(null);

        var over = Assert.Single(variant.Overrides);
        Assert.Equal(walk.Id, over.Key);
        var ownId = over.Value;
        var own = project.Manifest.Documents.First(d => d.Id == ownId);
        Assert.Equal("Walk (Winter Armour)", own.Name);
        // Selected so the artist lands on the thing they just made, like
        // Duplicate — and marked unsaved, because this one is an edit.
        Assert.Equal(ownId, vm.ProjectDocker.Selected?.Animation?.Id);
        Assert.Contains(ownId, vm.ProjectDocker.Dirty);
        output.WriteLine(vm.ProjectDocker.Status);
        Assert.Contains("still shares the original", vm.ProjectDocker.Status);
    }

    [AvaloniaFact]
    public void TheGestureIsRefusedWhereItMakesNoSense()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, _, _, walk) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");

        // No variant on screen — the menu item is hidden, and the command
        // behind it refuses too, so a stale menu cannot make an override.
        vm.ProjectDocker.Selected = Row(vm, r => r.Animation == walk);
        Assert.Equal("", vm.ProjectDocker.Selected!.GiveVariantHeader);
        vm.ProjectDocker.GiveVariantOwnVersionCommand.Execute(null);
        Assert.Empty(variant.Overrides);

        // Already its own — offering it again would make a copy of the copy.
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));
        vm.ProjectDocker.Selected = Row(vm, r => r.Animation == walk);
        vm.ProjectDocker.GiveVariantOwnVersionCommand.Execute(null);
        var ownRow = vm.ProjectDocker.Selected!;
        Assert.Equal("", ownRow.GiveVariantHeader);
        Assert.True(ownRow.IsVariantOverride);
        vm.ProjectDocker.GiveVariantOwnVersionCommand.Execute(null);
        Assert.Single(variant.Overrides);
    }

    [AvaloniaFact]
    public void ReturningToSharedKeepsTheDrawingItMade()
    {
        // Un-arranging must not be the fastest way to delete the art made for
        // the arrangement — RemoveVariant's rule, one override at a time.
        using var scratch = new Scratch();
        var (vm, project, knight, basePalette, armour, walk) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");
        var own = ProjectIo.OverrideDocument(
            project, knight, variant, walk, Drawing(basePalette.Id!, armour.Id));
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));

        vm.ProjectDocker.Selected = Row(vm, r => r.Animation == own);
        vm.ProjectDocker.ReturnToSharedCommand.Execute(null);

        Assert.Empty(variant.Overrides);
        Assert.Contains(own, project.Manifest.Documents);
        // Both visible now: the shared walk plays for the variant again, and
        // the freed drawing stays reachable as an ordinary document.
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation == walk);
        Assert.Contains(vm.ProjectDocker.Rows, r => r.Animation == own);
    }

    [AvaloniaFact]
    public void ThePickerListsBaseAndEveryVariantAndMarksTheViewedOne()
    {
        using var scratch = new Scratch();
        var (vm, project, knight, _, _, _) = Knight(scratch);
        var winter = Winter(project, knight, "#f0f8ff");
        ProjectIo.AddVariant(project, knight, "Damaged");
        vm.ProjectDocker.Refresh();

        var folderRow = Row(vm, r => r.Folder == knight && r.IsFolder);
        Assert.True(folderRow.HasVariants);
        Assert.Equal(["● Base", "Winter Armour", "Damaged"],
            folderRow.VariantMenu.Select(e => e.Label));

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, winter.Id));
        Assert.Equal(["Base", "● Winter Armour", "Damaged"],
            Row(vm, r => r.Folder == knight && r.IsFolder).VariantMenu.Select(e => e.Label));
    }

    [AvaloniaFact]
    public void ASwatchOnlyTheVariantHasFreezesToItsInkOutsideTheVariant()
    {
        // Deliberate, not an accident: a stroke painted from a variant-only
        // swatch records the BASE palette's id (the shared slot every variant
        // answers for), so it follows the swatch while that variant is viewed
        // — and everywhere else the named palette owns no such swatch, which
        // is Q30's literal-ink fallback doing exactly what it is for. The
        // alternative recordings are all worse: the variant's own id would
        // resolve nowhere at all, and a bare id would bleed across palettes.
        using var scratch = new Scratch();
        var (vm, project, knight, basePalette, _, _) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");
        var rust = new Swatch { Color = "#a05030", Name = "Rust" };
        project.Palettes.First(p => p.Id == variant.PaletteId).Swatches.Add(rust);

        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));
        var viewed = PaletteRegistry.ResolveSwatch(basePalette.Id!, rust.Id);
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, null));
        var outside = PaletteRegistry.ResolveSwatch(basePalette.Id!, rust.Id);

        output.WriteLine($"viewed {viewed?.Color ?? "null"}, outside {outside?.Color ?? "null"}");
        Assert.Equal("#a05030", viewed!.Color);
        Assert.Null(outside);   // null tells the engine: use the stroke's own ink
    }

    [AvaloniaFact]
    public void RecolouringTheVariantThroughThePanelLeavesTheBaseAlone()
    {
        // The base and the copy share swatch ids on purpose — that sharing is
        // what makes the stand-in repaint the same strokes — so the undo step
        // has to aim at one palette, or recolouring Winter Armour quietly
        // recolours the knight when the edit run commits.
        using var scratch = new Scratch();
        var (vm, project, knight, basePalette, armour, _) = Knight(scratch);
        var variant = Winter(project, knight, "#f0f8ff");
        vm.ProjectDocker.SwitchVariantCommand.Execute(new VariantChoice(knight, variant.Id));

        vm.PaletteDocker.Load(vm.Doc);
        vm.PaletteDocker.SelectedPalette =
            vm.PaletteDocker.Palettes.First(p => p.Id == variant.PaletteId);
        var row = vm.PaletteDocker.Swatches.Single(s => s.Id == armour.Id);
        row.Color = "#102030";
        vm.CommitSwatchEdit();

        var copy = project.Palettes.First(p => p.Id == variant.PaletteId);
        output.WriteLine($"variant {copy.Swatches[0].Color}, base {armour.Color}");
        Assert.Equal("#102030", copy.Swatches[0].Color);
        Assert.Equal("#8090a0", armour.Color);

        // And the undo goes back to the copy, not to every palette with the id.
        vm.UndoCommand.Execute(null);
        Assert.Equal("#f0f8ff", copy.Swatches[0].Color);
        Assert.Equal("#8090a0", armour.Color);
    }
}
