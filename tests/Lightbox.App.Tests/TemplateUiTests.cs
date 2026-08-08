using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Q12 as the app sees it: marking a template, starting from one, and pulling a
/// changed one forward.
/// </summary>
[Collection("BrushState")]
public sealed class TemplateUiTests : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-tmpl-{Guid.NewGuid():N}.lbproj");

    public new void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        return vm;
    }

    // ---- absence: no project, no template UI -----------------------------------

    [AvaloniaFact]
    public void WithNoProjectThereIsNothingToOfferAndNothingToPull()
    {
        // Without a project a template is a file you Open and then Save as,
        // which has always worked. The feature is project-scoped because a
        // project can list its templates and a loose folder cannot.
        var vm = Vm();

        Assert.False(vm.HasProject);
        Assert.Empty(vm.TemplateChoices);
        Assert.False(vm.CanUpdateFromTemplate);
        Assert.False(vm.IsActiveDocumentTemplate);
    }

    [AvaloniaFact]
    public void ADocumentThatIsNotATemplateWritesNoTemplateKeys()
    {
        var vm = Vm();
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(80, 80, 1);
        vm.EndStroke();

        var json = vm.SerializeDocument();

        Assert.DoesNotContain("\"isTemplate\"", json);
        Assert.DoesNotContain("\"templateId\"", json);
    }

    // ---- marking one -----------------------------------------------------------

    [AvaloniaFact]
    public void MarkingADocumentAsATemplateIsJustAFlag()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");

        vm.IsActiveDocumentTemplate = true;

        Assert.True(vm.IsActiveDocumentTemplate);
        Assert.Contains("\"isTemplate\"", vm.SerializeDocument());
        Assert.Contains("template", vm.TemplateLabel);
        // It is a change to the document, so it has to be saved like one.
        Assert.True(vm.ActiveTab!.IsDirty);

        vm.IsActiveDocumentTemplate = false;
        Assert.DoesNotContain("\"isTemplate\"", vm.SerializeDocument());
    }

    [AvaloniaFact]
    public void AMarkedDocumentAppearsInTheList()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        Assert.Empty(vm.TemplateChoices);

        vm.IsActiveDocumentTemplate = true;

        var offered = Assert.Single(vm.TemplateChoices);
        Assert.Equal(vm.ActiveTab!.Source!.Id, offered.Id);
    }

    // ---- starting from one -----------------------------------------------------

    [AvaloniaFact]
    public void NewFromTemplateOpensACopyInItsOwnTab()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.IsActiveDocumentTemplate = true;
        var template = vm.TemplateChoices[0];
        var tabs = vm.Tabs.Count;

        vm.NewFromTemplate(template);

        Assert.Equal(tabs + 1, vm.Tabs.Count);
        Assert.NotEqual(template.Id, vm.ActiveTab!.Source!.Id);
        // The copy is not itself a template, or every animation started from one
        // would show up in the list.
        Assert.False(vm.ActiveTab.Doc.IsTemplateDocument);
        Assert.Equal(template.Id, vm.ActiveTab.Doc.TemplateId);
        Assert.Single(vm.TemplateChoices);
    }

    [AvaloniaFact]
    public void EditingTheTemplateAfterwardsLeavesTheCopyAlone()
    {
        // The test the whole design rests on.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.IsActiveDocumentTemplate = true;
        var templateTab = vm.ActiveTab!;
        var template = vm.TemplateChoices[0];

        vm.NewFromTemplate(template);
        var copy = vm.ActiveTab!;

        templateTab.Doc.Scene.Layers[^1].Name = "Renamed in the template";
        templateTab.Doc.Scene.Fps = 6;

        Assert.NotEqual("Renamed in the template", copy.Doc.Scene.Layers[^1].Name);
        Assert.NotEqual(6, copy.Doc.Scene.Fps);
    }

    // ---- pulling forward -------------------------------------------------------

    /// <summary>A project with a template and a copy open, the copy active.</summary>
    private MainViewModel WithTemplateAndCopy(out DocumentTab templateTab)
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.IsActiveDocumentTemplate = true;
        templateTab = vm.ActiveTab!;
        vm.NewFromTemplate(vm.TemplateChoices[0]);
        return vm;
    }

    [AvaloniaFact]
    public void SwitchingTabsChangesTheAnswer()
    {
        // Template state is per document, and the File menu reads it through
        // bindings. Reading the properties here would prove nothing — their
        // getters are always fresh — so this asserts the *notification*, which
        // is the thing that was missing and the only thing a binding sees.
        var vm = Vm();
        vm.NewProject(_root, "Knight");
        vm.IsActiveDocumentTemplate = true;
        var templateTab = vm.ActiveTab!;
        vm.NewFromTemplate(vm.TemplateChoices[0]);

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        vm.ActiveTab = templateTab;

        Assert.Contains(nameof(vm.IsActiveDocumentTemplate), raised);
        Assert.Contains(nameof(vm.TemplateLabel), raised);
        Assert.Contains(nameof(vm.CanUpdateFromTemplate), raised);
        // And the values that reach the menu are the new tab's.
        Assert.True(vm.IsActiveDocumentTemplate);
        Assert.False(vm.CanUpdateFromTemplate);
    }

    [AvaloniaFact]
    public void ADocumentThatDidNotComeFromATemplateCannotBeAsked()
    {
        var vm = Vm();
        vm.NewProject(_root, "Knight");

        Assert.False(vm.CanUpdateFromTemplate);
        Assert.Null(vm.PreviewTemplatePull());
    }

    [AvaloniaFact]
    public void ACopyCanBeAskedAndReportsNothingWhenItMatches()
    {
        var vm = WithTemplateAndCopy(out _);

        Assert.True(vm.CanUpdateFromTemplate);
        var preview = vm.PreviewTemplatePull();
        Assert.NotNull(preview);
        Assert.False(preview!.Any);
    }

    [AvaloniaFact]
    public void ANewLayerInTheTemplateIsOfferedAndArrives()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        templateTab.Doc.Scene.Layers.Add(new Layer { Name = "Clean" });

        var preview = vm.PreviewTemplatePull()!;
        Assert.Single(preview.NewLayers);

        var changed = vm.UpdateFromTemplate(new Templates.PullOptions());

        Assert.Equal(1, changed);
        Assert.Contains("Clean", vm.ActiveTab!.Doc.Scene.Layers.Select(l => l.Name));
    }

    [AvaloniaFact]
    public void APullIsOneUndoStep()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        templateTab.Doc.Scene.Layers.Add(new Layer { Name = "Clean" });
        templateTab.Doc.Scene.Layers.Add(new Layer { Name = "Colour" });
        templateTab.Doc.Scene.Fps = 8;
        var before = vm.ActiveTab!.Doc.Scene.Layers.Count;

        vm.UpdateFromTemplate(new Templates.PullOptions());
        Assert.Equal(before + 2, vm.ActiveTab.Doc.Scene.Layers.Count);

        vm.UndoCommand.Execute(null);

        Assert.Equal(before, vm.ActiveTab.Doc.Scene.Layers.Count);
        Assert.NotEqual(8, vm.ActiveTab.Doc.Scene.Fps);
    }

    [AvaloniaFact]
    public void APullThatChangesNothingLeavesNoUndoStepToPressThrough()
    {
        var vm = WithTemplateAndCopy(out _);
        var couldUndo = vm.UndoCommand.CanExecute(null);

        Assert.Equal(0, vm.UpdateFromTemplate(new Templates.PullOptions()));

        Assert.Equal(couldUndo, vm.UndoCommand.CanExecute(null));
        Assert.Contains("Nothing to pull", vm.AiStatus);
    }

    [AvaloniaFact]
    public void APullNeverTouchesTheArtistsDrawings()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        vm.BeginStroke(30, 30, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();
        var mine = vm.PaintStrokes().Select(s => s.Id).ToList();
        Assert.NotEmpty(mine);

        templateTab.Doc.Scene.Layers.Add(new Layer { Name = "Clean" });
        vm.UpdateFromTemplate(new Templates.PullOptions());

        Assert.Equal(mine, vm.PaintStrokes().Select(s => s.Id));
    }

    [AvaloniaFact]
    public void ALayerTheArtistDrewOnIsReportedAsSuchAndSkipped()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        vm.BeginStroke(30, 30, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();
        var id = vm.ActiveTab!.Doc.Scene.Layers[^1].Id;
        templateTab.Doc.Scene.Layers[^1].Name = "Roughs";

        var preview = vm.PreviewTemplatePull()!;
        Assert.Contains(preview.LayerChanges, c => c.LayerId == id && c.DrawnOnSince);

        vm.UpdateFromTemplate(new Templates.PullOptions());
        Assert.NotEqual("Roughs", vm.ActiveTab.Doc.Scene.Layers[^1].Name);

        vm.UpdateFromTemplate(new Templates.PullOptions(
            DrawnOnLayerIds: new HashSet<string> { id }));
        Assert.Equal("Roughs", vm.ActiveTab.Doc.Scene.Layers[^1].Name);
    }

    // ---- the dialog ------------------------------------------------------------

    [AvaloniaFact]
    public void TheDialogTurnsThePreviewIntoTicksAndBack()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        templateTab.Doc.Scene.Layers.Add(new Layer { Name = "Clean" });
        templateTab.Doc.Scene.Fps = 8;
        var preview = vm.PreviewTemplatePull()!;

        var dialog = new UpdateFromTemplateWindow();
        dialog.Show(preview);
        dialog.ApplyForTests();

        var options = dialog.Result;
        Assert.NotNull(options);
        Assert.True(options!.NewLayers);
        Assert.True(options.Fps);
        // Nothing was drawn on, so there is no per-layer list to tick.
        Assert.Empty(options.DrawnOnLayerIds!);
    }

    [AvaloniaFact]
    public void TheDialogOffersOnlyWhatActuallyDiffers()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        templateTab.Doc.Scene.Fps = 8;

        var dialog = new UpdateFromTemplateWindow();
        dialog.Show(vm.PreviewTemplatePull()!);
        dialog.ApplyForTests();

        // Only the frame rate changed, so a pull must not claim to bring layers,
        // guides or a camera the template has nothing to say about.
        Assert.True(dialog.Result!.Fps);
        Assert.False(dialog.Result.NewLayers);
        Assert.False(dialog.Result.Guides);
        Assert.False(dialog.Result.Camera);
    }

    [AvaloniaFact]
    public void TheDialogListsADrawnOnLayerAndDefaultsItOff()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        vm.BeginStroke(30, 30, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();
        templateTab.Doc.Scene.Layers[^1].Name = "Roughs";

        var dialog = new UpdateFromTemplateWindow();
        dialog.Show(vm.PreviewTemplatePull()!);

        var row = Assert.Single(dialog.DrawnOnForTests);
        Assert.False(row.Include);
        Assert.Contains("Roughs", row.Label);

        dialog.ApplyForTests();
        Assert.Empty(dialog.Result!.DrawnOnLayerIds!);
    }

    [AvaloniaFact]
    public void CancellingReturnsNothing()
    {
        var vm = WithTemplateAndCopy(out var templateTab);
        templateTab.Doc.Scene.Fps = 8;

        var dialog = new UpdateFromTemplateWindow();
        dialog.Show(vm.PreviewTemplatePull()!);
        dialog.CancelForTests();

        Assert.Null(dialog.Result);
    }
}
