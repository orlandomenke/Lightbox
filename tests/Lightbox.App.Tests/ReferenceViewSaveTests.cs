using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Saving while a reference view is the tab in front (B249).
/// </summary>
/// <remarks>
/// Two halves of one report — *"every save a save-as prompt"*. A view onto a
/// project sheet had nowhere for Save to go, so it fell through to the picker;
/// and the name the picker was handed was the tab's title, <c>Hero / front</c>,
/// which is a path rather than a filename on every platform we ship to. Answer
/// the dialog and the next Ctrl+S asked again.
/// </remarks>
[Collection("BrushState")]
public class ReferenceViewSaveTests : BrushStateIsolated
{
    // ---- the name the picker is handed ------------------------------------------

    [Theory]
    [InlineData("Hero / front", "Hero-front")]
    [InlineData("Knight / view 2", "Knight-view 2")]
    [InlineData("a\\b:c", "a-b-c")]
    [InlineData("   ", "untitled")]
    [InlineData("///", "untitled")]
    public void ATabTitleBecomesANameAFilesystemAccepts(string title, string expected)
    {
        // The separator becomes a hyphen rather than vanishing, so the file
        // still says which view it is — the owner's ask, in their words:
        // "saved as name-view#".
        Assert.Equal(expected, MainWindow.FileStem(title));
    }

    [Fact]
    public void TheStemNeverCarriesSomethingAPickerWouldRefuse()
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            var stem = MainWindow.FileStem($"Hero{invalid}front");

            Assert.DoesNotContain(invalid, stem);
            Assert.NotEmpty(stem);
        }
    }

    // ---- and the prompt that should not appear at all -----------------------------

    private static (MainViewModel Vm, string Root) ProjectWithASheetView()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-b249-{Guid.NewGuid():N}.lbproj");
        Directory.CreateDirectory(root);
        var vm = new MainViewModel(null);
        var project = new Project(new ProjectManifest { Name = "Knight" }, root);
        ProjectIo.Save(project);
        vm.ProjectDocker.Adopt(project);

        var sheet = ProjectSheets.Add(project, "Hero", null);
        sheet.Views.Add(ReferenceView.Create("front", 200, 200));
        vm.OpenReferenceView(sheet.Views[0]);
        return (vm, root);
    }

    [AvaloniaFact]
    public void SavingASheetViewWritesTheProjectInsteadOfAskingWhereToPutAFile()
    {
        var (vm, root) = ProjectWithASheetView();
        try
        {
            Assert.True(vm.IsProjectSheetView);

            // The whole of the report: this was false, so Save fell through to
            // the picker every single time.
            Assert.True(vm.CanSaveInPlace);

            vm.Save();

            Assert.False(vm.ActiveTab!.IsDirty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ADocumentsOwnSheetStillSavesThroughTheDocument()
    {
        // The other kind of sheet view, unchanged: it belongs to a document, so
        // Save means saving that document and an untitled one still has to be
        // asked about.
        var vm = VmLayers.PaperVm();
        var sheet = vm.AddReferenceSheet("Hero")!;
        vm.AddReferenceView(sheet);
        vm.OpenReferenceView(sheet.Views[0]);

        Assert.False(vm.IsProjectSheetView);
        Assert.Equal(DocumentTabKind.Reference, vm.ActiveTab!.Kind);
        Assert.NotNull(vm.SaveTargetTab);
    }
}
