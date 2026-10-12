using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The shortcut bar's working switches are still where the artist left them the
/// next time the app is opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> "Snap to guides" carried a comment saying it survives
/// the session through settings, and nothing wrote it: it came back on at every
/// launch, so an artist who had turned the rulers off to draw freehand got them
/// back unasked. "View through the camera" was forgotten the same way. The other
/// switches on the bar already persist — show guides, lock guides and lock
/// references through the workspace layout, onion skin on the layer — which is
/// what made the two that did not read as the app being inconsistent.
/// </para>
/// <para>
/// A second view model stands in for the next launch: it is constructed after the
/// first one changed the switch, and reads the same settings file.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ShortcutBarTogglesRememberedTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    [AvaloniaFact]
    public void SnapToGuidesTurnedOffIsStillOffNextLaunch()
    {
        var first = new MainViewModel(null);
        Assert.True(first.SnapToGuides);            // the default, on a profile that never chose

        first.SnapToGuides = false;

        var next = new MainViewModel(null);
        output.WriteLine($"file says {AppSettings.Load().SnapToGuides}, next launch says {next.SnapToGuides}");
        Assert.False(AppSettings.Load().SnapToGuides);
        Assert.False(next.SnapToGuides);
    }

    [AvaloniaFact]
    public void SnapToGuidesTurnedBackOnIsRememberedToo()
    {
        var first = new MainViewModel(null);
        first.SnapToGuides = false;
        first.SnapToGuides = true;

        Assert.True(new MainViewModel(null).SnapToGuides);
    }

    /// <summary>
    /// The remembered switch is the one a stroke obeys — not only the one the
    /// button shows.
    /// </summary>
    [AvaloniaFact]
    public void ARememberedOffLeavesTheNextLaunchsStrokeAlone()
    {
        new MainViewModel(null).SnapToGuides = false;

        var next = new MainViewModel(null) { SmoothStrokes = false };
        next.AddGuide(GuideKind.Grid, 0, 0, spacing: 20);
        next.BeginStroke(23, 17, 1);
        next.EndStroke();

        var start = next.Doc.Scene.Layers
            .SelectMany(l => l.Cels).Select(c => c.Frame).OfType<Frame>()
            .SelectMany(f => f.Strokes).Last().Points[0];
        output.WriteLine($"stroke began at ({start.X:0.##}, {start.Y:0.##}); the grid would have put it at (20, 20)");
        Assert.Equal(23, start.X, 3);
        Assert.Equal(17, start.Y, 3);
    }

    [AvaloniaFact]
    public void ViewThroughTheCameraIsStillOnNextLaunch()
    {
        var first = new MainViewModel(null);
        Assert.False(first.ViewThroughCamera);      // the default: you draw in the world

        first.ViewThroughCamera = true;

        var next = new MainViewModel(null);
        output.WriteLine($"file says {AppSettings.Load().ViewThroughCamera}, next launch says {next.ViewThroughCamera}");
        Assert.True(AppSettings.Load().ViewThroughCamera);
        Assert.True(next.ViewThroughCamera);
    }

    /// <summary>
    /// A profile written before either switch was stored reads as the defaults,
    /// not as two falses.
    /// </summary>
    [Fact]
    public void ASettingsFileFromBeforeReadsAsTheDefaults()
    {
        var old = AppSettings.Deserialize("{}");

        Assert.True(old.SnapToGuides);
        Assert.False(old.ViewThroughCamera);
    }
}
