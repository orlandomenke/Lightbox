using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// The one guide-persistence path the suite does not cover: a character height
/// scale through a real save and a real reopen, on an ordinary document tab.
/// </summary>
/// <remarks>
/// Everything else is already pinned — an in-memory round trip, a
/// save-and-reopen of a standalone document, and the canvas being told on the
/// way back in — all with <see cref="GuideKind.Line"/>. The reported loss names
/// the height guide specifically, and it is the only kind whose defining
/// numbers live in fields another kind never sets (<c>Divisions</c> for the
/// head count, <c>Spacing</c> for one head's height), so it is the only kind
/// that can round-trip as a shape and come back meaningless.
/// </remarks>
[Collection("BrushState")]
public class HeightGuidePersistenceTests : BrushStateIsolated
{
    private static MainViewModel Vm() => VmLayers.PaperVm();

    [AvaloniaFact]
    public void AHeightScaleSurvivesSaveAndReopenWithItsProportions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-height-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            var vm = Vm();
            vm.ActiveTab!.FilePath = path;
            vm.AddGuide(GuideKind.HeightScale, 250, 900, spacing: 45, divisions: 7);
            vm.Save();

            var reopened = Vm();
            reopened.OpenDocumentTab(DocJson.Load(path), path);

            Assert.True(reopened.HasGuides);
            var back = reopened.Guides[0];
            Assert.Equal(GuideKind.HeightScale, back.Kind);
            Assert.Equal(250, back.X);
            Assert.Equal(900, back.Y);
            // The two that make it a height chart rather than a bare mark.
            Assert.Equal(7, back.Divisions);
            Assert.Equal(45, back.Spacing);
            // And the surface the options bar reads it through.
            Assert.Single(reopened.HeightScaleGuides);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AHeightScaleResizedByItsTopSurvivesTheSame()
    {
        // The gesture the report describes doing before the loss: dragging the
        // top rung to size the character. It writes Spacing rather than Y, and
        // only EndHeightScaleResize records it — so a save taken after a drag
        // that never closed would write the old proportions.
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-height-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            var vm = Vm();
            vm.ActiveTab!.FilePath = path;
            var guide = vm.AddGuide(GuideKind.HeightScale, 250, 900, spacing: 45, divisions: 7);

            for (var i = 0; i < 10; i++) vm.DragHeightScaleTop(guide, 1);
            vm.EndHeightScaleResize(guide);
            var expected = guide.Spacing;
            Assert.NotEqual(45, expected);

            vm.Save();

            var reopened = Vm();
            reopened.OpenDocumentTab(DocJson.Load(path), path);

            Assert.True(reopened.HasGuides);
            Assert.Equal(expected, reopened.Guides[0].Spacing, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
