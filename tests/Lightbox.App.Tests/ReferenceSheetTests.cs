using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The reference-sheet document model, with no window in sight.
/// </summary>
/// <remarks>
/// Plain <see cref="FactAttribute"/> rather than <c>[AvaloniaFact]</c>, which is the
/// one thing in this file worth explaining. Nothing here touches Avalonia — it builds
/// a document, round-trips it through <see cref="DocJson"/> and reads the result — so
/// running it inside the headless session bought nothing and cost it a share of B93:
/// on 2026-08-06 it failed once at 1 ms under full-solution load, which is a body that
/// never ran, in a test whose assertions cannot vary between runs.
///
/// This is not a fix for B93. It takes one test out of the blast radius and leaves the
/// race exactly where it is for the ~1,100 that are genuinely UI tests.
/// </remarks>
public class ReferenceSheetModelTests
{
    [Fact]
    public void Sheets_RoundTripThroughJson_AndLegacyDocsLoadEmpty()
    {
        var doc = DocumentFactory.CreateDoc();
        var sheet = new ReferenceSheet { Name = "Hero" };
        sheet.Views.Add(ReferenceView.Create("side", 400, 300));
        ((Frame)sheet.Views[0].Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Points = [new(10, 10, 0.5), new(50, 50, 0.5)],
            Label = "back-line",
        });
        doc.ReferenceSheets.Add(sheet);

        var restored = DocJson.Deserialize(DocJson.Serialize(doc));
        var rSheet = Assert.Single(restored.ReferenceSheets);
        Assert.Equal("Hero", rSheet.Name);
        var view = Assert.Single(rSheet.Views);
        Assert.Equal("side", view.Name);
        Assert.Equal(400, view.Width);
        var frame = Assert.IsType<Frame>(view.Layers[0].Cels[0].Frame);
        Assert.Equal("back-line", Assert.Single(frame.Strokes).Label);

        // Documents saved before character sheets existed load with none.
        var legacy = DocJson.Serialize(DocumentFactory.CreateDoc())
            .Replace(",\"referenceSheets\":[]", "");
        Assert.Empty(DocJson.Deserialize(legacy).ReferenceSheets);
    }
}

public class ReferenceTabTests
{
    private static MainViewModel VmWithSheet(out ReferenceView view)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.AddReferenceSheet();
        var sheet = vm.ReferenceSheetsView[0];
        vm.AddReferenceView(sheet); // also opens the tab
        view = sheet.Views[0];
        return vm;
    }

    [AvaloniaFact]
    public void AddView_OpensReferenceTab_TimelineHidden()
    {
        var vm = VmWithSheet(out var view);
        Assert.Equal(2, vm.Tabs.Count);
        var tab = vm.ActiveTab!;
        Assert.Equal(DocumentTabKind.Reference, tab.Kind);
        Assert.Same(view, tab.View);
        Assert.Same(vm.Tabs[0], tab.Owner);
        Assert.True(vm.TimelineVisible);
        Assert.False(vm.ShowTimeline); // hidden on reference tabs

        vm.ActiveTab = vm.Tabs[0];
        Assert.True(vm.ShowTimeline);
    }

    [AvaloniaFact]
    public void PaintingInReferenceTab_LandsInOwningDocument_AndDirtiesOwner()
    {
        var vm = VmWithSheet(out var view);
        var owner = vm.Tabs[0];
        owner.MarkSaved(); // clear the add-sheet dirtiness for a sharp assert

        vm.BeginStroke(20, 20, 0.5);
        vm.MoveStroke(60, 60, 0.5);
        vm.EndStroke();

        // The stroke is in the OWNING document's reference view.
        var frame = (Frame)view.Layers[0].Cels[0].Frame!;
        Assert.Single(frame.Strokes);
        Assert.Single(((Frame)owner.Doc.ReferenceSheets[0].Views[0].Layers[0].Cels[0].Frame!).Strokes);
        Assert.True(owner.IsDirty);
        // B95. Both tabs show it, and this assertion used to say the opposite.
        // The reporter found the badge on the parent only: a sheet tab is a view
        // onto the owner's document, so an artist looking at the sheet should not
        // have to go and find another tab to learn there is unsaved work.
        Assert.True(vm.ActiveTab!.IsDirty);

        // Undo inside the reference tab still routes to the owning document.
        vm.UndoCommand.Execute(null);
        Assert.Empty(((Frame)owner.Doc.ReferenceSheets[0].Views[0].Layers[0].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void SaveFromReferenceTab_SerializesTheOwningDocument()
    {
        var vm = VmWithSheet(out _);
        vm.BeginStroke(20, 20, 0.5);
        vm.EndStroke();

        var json = vm.SerializeDocument(); // active tab is the reference tab
        var doc = DocJson.Deserialize(json);
        Assert.Single(doc.ReferenceSheets); // the OWNER document, not the wrapper
        Assert.Equal(960, doc.Scene.Width);

        vm.NotifySaved("/tmp/hero.lightbox.json");
        Assert.False(vm.Tabs[0].IsDirty); // owner cleared, not the reference tab
    }

    [AvaloniaFact]
    public void ClosingOwnerTab_ClosesItsReferenceTabs()
    {
        var vm = VmWithSheet(out _);
        Assert.Equal(2, vm.Tabs.Count);

        var announced = 0;
        vm.LastDocumentClosed += () => announced++;

        vm.CloseTab(vm.Tabs[0]); // close the animation tab

        // Both go: a sheet tab is a view onto its owner's document, so an owner
        // that is gone leaves nothing for it to show. This used to end with one
        // fresh untitled tab, because closing the last document conjured a
        // replacement; the application can now simply be empty, and the sheet
        // going with its owner is what makes that the *right* number rather
        // than an accident of the auto-create.
        Assert.Empty(vm.Tabs);
        Assert.False(vm.HasDocument);
        Assert.Equal(1, announced);
    }

    [AvaloniaFact]
    public void OpeningSameView_FocusesExistingTab()
    {
        var vm = VmWithSheet(out var view);
        vm.ActiveTab = vm.Tabs[0];
        vm.OpenReferenceView(view);
        Assert.Equal(2, vm.Tabs.Count); // no duplicate tab
        Assert.Same(view, vm.ActiveTab!.View);
    }
}

public class ReferenceAiTests
{
    [AvaloniaFact]
    public void RenderReferenceView_ProducesDecodablePng()
    {
        var vm = VmWith(out var view);
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();

        var png = vm.RenderReferenceViewPng(view);
        using var bmp = SKBitmap.Decode(Convert.FromBase64String(png));
        Assert.Equal(view.Width, bmp.Width);
        Assert.NotEqual(SKColors.White, bmp.GetPixel(50, 50)); // stroke crosses here

        static MainViewModel VmWith(out ReferenceView v)
        {
            var vm = VmLayers.PaperVm();
            vm.SmoothStrokes = false;
            vm.AddReferenceSheet();
            vm.AddReferenceView(vm.ReferenceSheetsView[0]); // opens tab, active
            v = vm.ReferenceSheetsView[0].Views[0];
            return vm;
        }
    }

    [AvaloniaFact]
    public void AiInbetween_CarriesReferenceImages()
    {
        var fake = new FakeArtist();
        var vm = VmLayers.PaperVm(fake);
        vm.SmoothStrokes = false;
        vm.AddReferenceSheet();
        vm.AddReferenceView(vm.ReferenceSheetsView[0]);
        vm.BeginStroke(10, 10, 1);
        vm.EndStroke(); // give the view content so it renders

        vm.ActiveTab = vm.Tabs[0]; // back to the animation
        vm.BeginStroke(0, 0, 0.5);
        vm.MoveStroke(50, 0, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(0, 100, 0.5);
        vm.MoveStroke(50, 100, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        vm.AiInbetweenCommand.Execute(null);

        Assert.NotNull(fake.LastInbetweenRequest);
        var images = fake.LastInbetweenRequest!.ReferenceImages;
        Assert.NotNull(images);
        Assert.Single(images!);
        using var bmp = SKBitmap.Decode(Convert.FromBase64String(images![0]));
        Assert.NotNull(bmp);
    }

    [AvaloniaFact]
    public void IpcListAndRender_ExposeReferenceViews()
    {
        var vm = VmLayers.PaperVm();
        vm.AddReferenceSheet();
        vm.AddReferenceView(vm.ReferenceSheetsView[0]);
        vm.ActiveTab = vm.Tabs[0];

        var api = new Lightbox.App.Services.IpcDocumentApi(vm);
        var list = api.Handle(new Lightbox.App.Services.IpcProtocol.Request { Op = "list_reference_views" });
        Assert.True(list.Ok);
        var viewId = list.Payload!.Value
            .GetProperty("sheets")[0].GetProperty("views")[0].GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(viewId));

        var render = api.Handle(new Lightbox.App.Services.IpcProtocol.Request
        {
            Op = "render_reference_view",
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { viewId }, Lightbox.App.Services.IpcProtocol.Json),
        });
        Assert.True(render.Ok);
    }
}

/// <summary>
/// B66: a character sheet is part of a document (Q25 answered (a)), so the
/// thing that has to exist on disk is the document it lives in.
/// </summary>
public class CharacterSheetFileTests
{

    // ---- B66: a sheet has somewhere to live ---------------------------------

    /// <summary>
    /// The reported defect, as the condition the UI acts on: a sheet created on
    /// an untitled standalone document has no file behind it.
    /// </summary>
    [AvaloniaFact]
    public void ACharacterSheetOutsideAProjectPromptsToSave()
    {
        var vm = VmLayers.PaperVm();
        var asked = 0;
        vm.ReferenceSheetNeedsAFile += () => asked++;

        Assert.True(vm.AReferenceSheetWouldBeUnsaved, "a fresh document already has a file somehow");
        vm.AddReferenceSheet("Knight");

        Assert.Equal(1, asked);
        // The sheet exists regardless — the save is offered after it is made, so
        // cancelling keeps the work rather than discarding it.
        Assert.Contains(vm.ReferenceSheetsView, s => s.Name == "Knight");
    }

    /// <summary>
    /// The other half of the report: in a project a sheet is added directly,
    /// because the project saves the document it lives in.
    /// </summary>
    [AvaloniaFact]
    public void ACharacterSheetInAProjectIsWrittenOnCreation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-b66-{Guid.NewGuid():N}.lbproj");
        try
        {
            var vm = VmLayers.PaperVm();
            vm.NewProject(root, "Knight");
            vm.ProjectDocker.AddDocumentCommand.Execute(null);

            var asked = 0;
            vm.ReferenceSheetNeedsAFile += () => asked++;

            Assert.False(
                vm.AReferenceSheetWouldBeUnsaved,
                "a document inside a project was reported as having nowhere to live");
            vm.AddReferenceSheet("Knight sheet");

            Assert.Equal(0, asked);
            Assert.Contains(vm.ReferenceSheetsView, s => s.Name == "Knight sheet");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// B65's ordering rule on this surface: the name is supplied before anything
    /// is written, rather than being a number to correct afterwards.
    /// </summary>
    [AvaloniaFact]
    public void ACharacterSheetAsksForItsNameBeforeItsLocation()
    {
        var vm = VmLayers.PaperVm();

        var named = Assert.IsType<ReferenceSheet>(vm.AddReferenceSheet("Rusty knight"));
        Assert.Equal("Rusty knight", named.Name);

        // Whitespace is not a name, and an empty prompt must not produce a sheet
        // called "   ". It falls back to the numbered default rather than
        // refusing, because the sheet is already wanted by this point.
        var blank = Assert.IsType<ReferenceSheet>(vm.AddReferenceSheet("   "));
        Assert.StartsWith("Character ", blank.Name);

        // The old parameterless call still means what it did, so nothing that
        // already added a sheet changed behaviour.
        var legacy = Assert.IsType<ReferenceSheet>(vm.AddReferenceSheet());
        Assert.StartsWith("Character ", legacy.Name);
    }
}
