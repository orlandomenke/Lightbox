using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

public class ZZZAudioSilentDropTests
{
    private readonly ITestOutputHelper _output;
    public ZZZAudioSilentDropTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void AnAudioTrackThatFailsToResolveIsSilentlyDropped()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        doc.Scene.FrameCount = 6;
        // A real (unmuted) track exists on the document, but the file it
        // points to does not exist on disk any more (moved/deleted) and it
        // carries no embedded Data — exactly what ResolvedAudioPathForExport
        // returns null for.
        doc.Scene.Audio = new AudioTrack { Path = "/no/such/file/take.wav", Muted = false };

        // resolveAudio mimics MainViewModel.ResolvedAudioPathForExport for this
        // exact situation: track present + unmuted, but resolves to null.
        var window = new VideoExportWindow(doc, () => null, "take");

        var audioBox = window.FindControl<CheckBox>("AudioBox")!;
        _output.WriteLine("AudioBox.IsEnabled = " + audioBox.IsEnabled);
        _output.WriteLine("AudioBox.IsChecked = " + audioBox.IsChecked);

        // The claim under test: the window offers sound (checked, enabled)
        // even though it cannot actually be muxed in, and nothing on screen
        // says so.
        Assert.True(audioBox.IsEnabled);
        Assert.True(audioBox.IsChecked);
    }
}
