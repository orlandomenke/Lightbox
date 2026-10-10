using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The performance lab's questions exist only on a lab instance (Q209): an
/// ordinary Lightbox, the one the MCP bridge talks to, answers lab_* exactly as it
/// answers any op it does not know.
/// </summary>
[Collection("BrushState")]
public sealed class LabSurfaceTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void AnOrdinaryInstanceDoesNotAnswerTheLabsQuestions()
    {
        var api = new IpcDocumentApi(new MainViewModel(null));
        foreach (var op in new[] { "lab_state", "lab_locate", "lab_show_panel", "lab_icons" })
        {
            var response = api.Handle(new IpcProtocol.Request { Op = op });
            Assert.False(response.Ok);
            Assert.Equal($"Unknown op \"{op}\".", response.Error);
        }
    }

    /// <summary>Not a lab instance in the test host: no throwaway profile, no perf log.</summary>
    [AvaloniaFact]
    public void TheTestHostIsNotALabInstance() => Assert.False(Lightbox.App.Views.MainWindow.LabInstance);
}
