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

        // The catalogue is always there and always last: an artist's own work
        // must not be pushed down the list by shapes that shipped with the app.
        Assert.Equal(["Shared", "Mine"], all.Take(2).Select(t => t.Name));
        Assert.Equal(TipCatalogue.All.Count + 2, all.Count);
        Assert.All(all.Skip(2), t => Assert.True(TipCatalogue.IsBuiltIn(t.Id)));
    }

    [AvaloniaFact]
    public void WithNoProjectTheLibraryIsJustTheUsersOwn()
    {
        // Optional means absent. Someone who opens Lightbox to draw one picture
        // still has their brushes and never meets a project.
        var mine = Tip("Mine");

        var all = TipStore.Available(null, new TipStore.State { Tips = [mine] });

        Assert.Equal(["Mine"], all.Where(t => !TipCatalogue.IsBuiltIn(t.Id)).Select(t => t.Name));
        Assert.Equal(TipCatalogue.All.Count + 1, all.Count);

        // And the catalogue can be asked for without: the tip picker wants it,
        // a "what have I made" list does not.
        Assert.Equal(["Mine"],
            TipStore.Available(null, new TipStore.State { Tips = [mine] }, includeBuiltIn: false)
                .Select(t => t.Name));
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

        // The fade rows: everywhere except the circles (Hardness already is
        // that dial there) and the halo (which is its own gradient) — and the
        // falloff curve wherever any band exists, the soft circle included.
        window.ShapeBox.SelectedIndex = (int)TipShape.HardCircle;
        Assert.False(window.FadeRow.IsVisible);
        Assert.False(window.FalloffRow.IsVisible);

        window.ShapeBox.SelectedIndex = (int)TipShape.SoftCircle;
        Assert.False(window.FadeRow.IsVisible);
        Assert.True(window.FalloffRow.IsVisible);

        window.ShapeBox.SelectedIndex = (int)TipShape.Halo;
        Assert.False(window.FadeRow.IsVisible);

        window.ShapeBox.SelectedIndex = (int)TipShape.Drop;
        Assert.True(window.FadeRow.IsVisible);
        Assert.True(window.FalloffRow.IsVisible);
        Assert.True(window.SharpnessRow.IsVisible);
        Assert.Equal("Point", window.SharpnessLabel.Text);
        Assert.False(window.CountRow.IsVisible);

        window.ShapeBox.SelectedIndex = (int)TipShape.Crescent;
        Assert.Equal("Bite", window.SharpnessLabel.Text);

        window.ShapeBox.SelectedIndex = (int)TipShape.Blot;
        Assert.True(window.CountRow.IsVisible);
        Assert.Equal("Lobes", window.CountLabel.Text);
        Assert.Equal("Irregularity", window.SharpnessLabel.Text);
    }

    [AvaloniaFact]
    public void AFadedRecipeSurvivesTheRoundTripThroughTheWindow()
    {
        // The generate page writes Fade and its curve into the recipe, and
        // reopening the tip puts them back on the controls — the same promise
        // every other recipe field already keeps.
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);
        window.CategoryList.SelectedIndex = 1;
        window.ShapeBox.SelectedIndex = (int)TipShape.Blot;
        window.FadeSlider.Value = 0.6;
        window.FalloffBox.SelectedIndex = (int)TipFalloff.Airbrush;
        window.GenerateName.Text = "Soft blot";

        window.OnGenerateForTest();

        var saved = Assert.Single(TipStore.Load(_store).Tips);
        Assert.Equal(0.6, saved.Recipe!.Fade, 3);
        Assert.Equal(TipFalloff.Airbrush, saved.Recipe.FadeProfile);

        // And back onto the controls.
        window.FadeSlider.Value = 0;
        window.FalloffBox.SelectedIndex = 0;
        window.CategoryList.SelectedIndex = 0;
        window.TipList.SelectedItem = window.TipList.ItemsSource!.Cast<TipRow>().Single(r => r.Name == "Soft blot");
        window.EditCopyForTest();

        Assert.Equal(0.6, window.FadeSlider.Value, 3);
        Assert.Equal((int)TipFalloff.Airbrush, window.FalloffBox.SelectedIndex);
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
        var rows = window.TipList.ItemsSource!.Cast<TipRow>().ToList();

        Assert.Equal(["One", "Two"], rows.Where(r => r.Scope == TipScope.User).Select(r => r.Name));
        Assert.Equal(TipCatalogue.All.Count, rows.Count(r => r.Scope == TipScope.BuiltIn));
    }

    [AvaloniaFact]
    public void ABuiltInCannotBeDeletedOrRenamed()
    {
        // Not because it is precious: its id is recorded in every document that
        // ever painted with it, so removing one would leave an old file
        // referring to something that no longer means what it meant.
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);
        var rows = window.TipList.ItemsSource!.Cast<TipRow>().ToList();

        window.TipList.SelectedItem = rows.First(r => r.Scope == TipScope.BuiltIn);
        Assert.False(window.DeleteButton.IsEnabled);
        Assert.False(window.NameBox.IsEnabled);

        // …and it can still be used as a starting point, which is the point of
        // keeping the recipe on the tip at all.
        Assert.True(window.EditCopyButton.IsEnabled);
    }

    [AvaloniaFact]
    public void EditingACopyLoadsTheRecipeWithoutTouchingTheOriginal()
    {
        var window = new BrushTipsWindow(new ViewModels.MainViewModel(null), _store);
        var rows = window.TipList.ItemsSource!.Cast<TipRow>().ToList();
        var cutNib = rows.Single(r => r.Name == "Cut nib");
        var before = cutNib.Tip.Png;

        window.TipList.SelectedItem = cutNib;
        window.EditCopyForTest();

        Assert.Equal((int)TipShape.Polygon, window.ShapeBox.SelectedIndex);
        Assert.Equal(6, (int)Math.Round(window.CountSlider.Value));
        Assert.Equal("Cut nib copy", window.GenerateName.Text);
        Assert.Equal(before, cutNib.Tip.Png);
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
