using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class ReferenceSheetModelTests
{
    [AvaloniaFact]
    public void Sheets_RoundTripThroughJson_AndLegacyDocsLoadEmpty()
    {
        var doc = DocumentFactory.CreateDoc();
        var sheet = new ReferenceSheet { Name = "Hero" };
        sheet.Views.Add(ReferenceView.Create("side", 400, 300));
        ((PaintedFrame)sheet.Views[0].Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
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
        var frame = Assert.IsType<PaintedFrame>(view.Layers[0].Cels[0].Frame);
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
        var vm = new MainViewModel(null) { SmoothStrokes = false };
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
        owner.IsDirty = false; // clear the add-sheet dirtiness for a sharp assert

        vm.BeginStroke(20, 20, 0.5);
        vm.MoveStroke(60, 60, 0.5);
        vm.EndStroke();

        // The stroke is in the OWNING document's reference view.
        var frame = (PaintedFrame)view.Layers[0].Cels[0].Frame!;
        Assert.Single(frame.Strokes);
        Assert.Single(((PaintedFrame)owner.Doc.ReferenceSheets[0].Views[0].Layers[0].Cels[0].Frame!).Strokes);
        Assert.True(owner.IsDirty);
        Assert.False(vm.ActiveTab!.IsDirty); // the reference tab itself stays clean

        // Undo inside the reference tab still routes to the owning document.
        vm.UndoCommand.Execute(null);
        Assert.Empty(((PaintedFrame)owner.Doc.ReferenceSheets[0].Views[0].Layers[0].Cels[0].Frame!).Strokes);
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
        vm.CloseTab(vm.Tabs[0]); // close the animation tab
        var remaining = Assert.Single(vm.Tabs);
        Assert.Equal(DocumentTabKind.Animation, remaining.Kind); // fresh untitled, orphan closed too
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
            var vm = new MainViewModel(null) { SmoothStrokes = false };
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
        var vm = new MainViewModel(fake) { SmoothStrokes = false };
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
        var vm = new MainViewModel(null);
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
