using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster.Tips;

namespace Lightbox.App.Tests;

/// <summary>
/// The brush tip library: where a tip lives, and what happens to a drawing
/// when the library changes underneath it.
/// </summary>
[Collection("BrushState")]
public class TipLibraryTests : BrushStateIsolated
{
    private readonly string _store =
        Path.Combine(Path.GetTempPath(), $"lightbox-tips-{Guid.NewGuid():N}.json");

    private static BrushTip Tip(string name) =>
        TipGenerator.Create(new TipRecipe { Shape = TipShape.HardCircle, Size = 32 }, name);

    public override void Dispose()
    {
        if (File.Exists(_store)) File.Delete(_store);
        base.Dispose();
    }

    [AvaloniaFact]
    public void ALibraryRoundTrips()
    {
        var tip = Tip("Round");
        TipStore.Save(new TipStore.State { Tips = [tip] }, _store);

        var back = TipStore.Load(_store);

        var only = Assert.Single(back.Tips);
        Assert.Equal("Round", only.Name);
        Assert.Equal(tip.Png, only.Png);
        Assert.Equal(TipShape.HardCircle, only.Recipe?.Shape);
    }

    [AvaloniaFact]
    public void ACorruptLibraryIsEmptyRatherThanFatal()
    {
        // Losing the library must never stop someone painting — the same rule
        // the brush preset store follows, and for the same reason.
        File.WriteAllText(_store, "{ not json");

        Assert.Empty(TipStore.Load(_store).Tips);
    }

    [AvaloniaFact]
    public void AProjectTipComesBeforeAUserTip()
    {
        // Project is the more specific scope, so it wins the top of the list —
        // and a tip that has been promoted must not then appear twice.
        var shared = Tip("Shared");
        var mine = Tip("Mine");
        var project = new Project(
            new ProjectManifest { Name = "P", Tips = [shared] }, "/tmp/p.lbproj");

        var all = TipStore.Available(project, new TipStore.State { Tips = [mine, shared] });

        Assert.Equal(["Shared", "Mine"], all.Select(t => t.Name));
    }

    [AvaloniaFact]
    public void WithNoProjectTheLibraryIsJustTheUsersOwn()
    {
        // Optional means absent. Someone who opens Lightbox to draw one picture
        // still has their brushes and never meets a project.
        var mine = Tip("Mine");

        var all = TipStore.Available(null, new TipStore.State { Tips = [mine] });

        Assert.Equal(["Mine"], all.Select(t => t.Name));
    }

    [AvaloniaFact]
    public void PaintingWithALibraryTipCopiesItIntoTheDrawing()
    {
        // The whole reason a tip carries pixels rather than a path. Once a
        // drawing has used one, the drawing is self-contained.
        var doc = DocumentFactory.CreateDoc();
        var tip = Tip("Round");

        TipStore.AdoptInto(doc, tip);

        Assert.Equal(tip.Png, doc.BrushTips[tip.Id]);
    }

    [AvaloniaFact]
    public void DeletingFromTheLibraryCannotChangeADrawing()
    {
        // The consequence worth pinning: the library is a place to choose
        // from, not what a picture renders out of. Emptying it must leave every
        // existing drawing exactly as it was.
        var doc = DocumentFactory.CreateDoc();
        var tip = Tip("Round");
        TipStore.AdoptInto(doc, tip);

        TipStore.Save(new TipStore.State(), _store);

        Assert.Empty(TipStore.Load(_store).Tips);
        Assert.Equal(tip.Png, doc.BrushTips[tip.Id]);
    }

    [AvaloniaFact]
    public void AProjectThatNeverMadeATipWritesNoTipsKey()
    {
        // Optional means absent, not present-and-empty — the camera's rule, and
        // the same test shape the project brush key already has.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-proj-{Guid.NewGuid():N}.lbproj");
        try
        {
            var project = new Project(new ProjectManifest { Name = "Plain" }, root);
            ProjectIo.Save(project);

            var json = File.ReadAllText(Path.Combine(root, "project.json"));

            Assert.DoesNotContain("\"tips\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void AProjectTipSurvivesSaveAndReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-proj-{Guid.NewGuid():N}.lbproj");
        try
        {
            var tip = Tip("Shared");
            var project = new Project(
                new ProjectManifest { Name = "Shared", Tips = [tip] }, root);
            ProjectIo.Save(project);

            var back = ProjectIo.Load(root);

            var only = Assert.Single(back.Manifest.Tips!);
            Assert.Equal("Shared", only.Name);
            Assert.Equal(tip.Png, only.Png);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

/// <summary>The tip workshop window itself.</summary>
[Collection("BrushState")]
public class BrushTipsWindowTests : BrushStateIsolated
{
    private readonly string _store =
        Path.Combine(Path.GetTempPath(), $"lightbox-tips-{Guid.NewGuid():N}.json");

    public override void Dispose()
    {
        if (File.Exists(_store)) File.Delete(_store);
        base.Dispose();
    }

    [AvaloniaFact]
    public void GeneratingATipPutsItInTheLibraryAsPixels()
    {
        // The decision the whole feature rests on: a tip leaves this window
        // baked. Nothing it produces is re-derived when a stroke is drawn.
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);
        window.CategoryList.SelectedIndex = 1;
        window.ShapeBox.SelectedIndex = (int)TipShape.Ring;
        window.GenerateName.Text = "My ring";

        window.OnGenerateForTest();

        var saved = Assert.Single(TipStore.Load(_store).Tips);
        Assert.Equal("My ring", saved.Name);
        Assert.NotEmpty(saved.Png);
        Assert.Equal(TipShape.Ring, saved.Recipe?.Shape);
    }

    [AvaloniaFact]
    public void OnlyTheControlsTheShapeActuallyReadsAreShown()
    {
        // A slider that does nothing is worse than an absent one — charter O7,
        // and the same argument that put the cost badge on the brush picker.
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);

        window.ShapeBox.SelectedIndex = (int)TipShape.SoftCircle;
        Assert.True(window.HardnessRow.IsVisible);
        Assert.False(window.HatchRows.IsVisible);

        window.ShapeBox.SelectedIndex = (int)TipShape.Hatch;
        Assert.False(window.HardnessRow.IsVisible);
        Assert.True(window.HatchRows.IsVisible);

        window.ShapeBox.SelectedIndex = (int)TipShape.Ring;
        Assert.True(window.InnerRow.IsVisible);
        Assert.False(window.RoundnessRow.IsVisible);
    }

    [AvaloniaFact]
    public void AnEmptyLibrarySaysSoRatherThanShowingNothing()
    {
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);

        Assert.True(window.EmptyLibrary.IsVisible);

        window.CategoryList.SelectedIndex = 1;
        window.OnGenerateForTest();

        Assert.False(window.EmptyLibrary.IsVisible);
    }

    [AvaloniaFact]
    public void TheLibraryListsWhatIsInTheStore()
    {
        TipStore.Save(
            new TipStore.State
            {
                Tips =
                [
                    TipGenerator.Create(new TipRecipe { Size = 32 }, "One"),
                    TipGenerator.Create(new TipRecipe { Size = 32 }, "Two"),
                ],
            },
            _store);

        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);

        Assert.Equal(2, window.TipList.ItemCount);
    }

    [AvaloniaFact]
    public void ThePreviewBakesSmallHoweverBigTheOutputIs()
    {
        // Dragging a slider must not start baking 1024² per frame. The preview
        // is a fixed small bake; the chosen size only applies when the tip is
        // actually added.
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);
        window.SizeBox.SelectedIndex = 3; // 1024

        window.HardnessSlider.Value = 0.9;

        var preview = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(window.GeneratePreview.Source);
        Assert.True(preview.PixelSize.Width <= 256,
            $"the preview baked at {preview.PixelSize.Width}px — that is the output size, not a preview");
    }
}
