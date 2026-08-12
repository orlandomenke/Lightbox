using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Space starts playback and Space stops it — from wherever the keyboard
/// happens to be.
/// </summary>
/// <remarks>
/// <para>
/// <b>B159 was reported as "Space starts the playback, but pressing space again
/// does not stop it", and these tests are the half of the answer that is
/// certain: the wiring is sound.</b> The gesture resolves to
/// <c>timeline.playPause</c> from any scope because it is a Global binding, the
/// handler runs <c>TogglePlaybackCommand</c>, and the command is a real toggle.
/// All three were verified end to end through the real window, both with focus
/// on nothing in particular and with focus on the transport button — which is
/// where a click leaves it, and the state most likely to have swallowed the key.
/// </para>
/// <para>
/// <b>The trap that made the first probe lie.</b> Raising only
/// <c>KeyDown</c> makes the focused-button case look completely dead: Avalonia's
/// <c>Button</c> takes Space on key-down to show itself pressed, marks the event
/// handled — so the window's handler never sees it — and fires the click on key
/// <em>up</em>. A test that presses without releasing therefore reports a bug
/// that does not exist. Both events, or nothing.
/// </para>
/// <para>
/// <b>What these cannot show is the reported fault</b>, and it is worth being
/// plain about that rather than letting a green test stand in for a fix. The
/// suspicion is that the second press is not lost but *delayed*: during playback
/// the tick costs ~35 ms at 1080p and arrives late in every capture on record
/// (B158), and key input is dispatched in the same band the playback clock now
/// runs in. A saturated UI thread makes a keypress land whenever there is room
/// for it, which from a chair is indistinguishable from a key that did nothing.
/// That is the stutter, not a shortcut fault, and it is why this file guards the
/// wiring rather than claiming to close the report.
/// </para>
/// </remarks>
public class SpaceTogglesPlaybackTests(ITestOutputHelper output)
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    /// <summary>A whole press: down and up. See the remarks — down alone lies.</summary>
    private static void Space(Interactive from)
    {
        from.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        from.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SpaceStartsPlaybackAndSpaceStopsIt()
    {
        var (window, vm) = Open();
        Assert.False(vm.IsPlaying);

        Space(window);
        Assert.True(vm.IsPlaying, "the first Space did not start playback");

        Space(window);
        Assert.False(vm.IsPlaying,
            "the second Space did not stop playback — Space is a toggle, and stopping "
            + "is the half that was reported broken");
    }

    /// <summary>
    /// The state a click leaves behind, and the one that could plausibly eat the
    /// key: focus sitting on the transport button itself.
    /// </summary>
    [AvaloniaFact]
    public void SpaceStillTogglesWhenTheTransportButtonHasFocus()
    {
        var (window, vm) = Open();
        var play = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Command == vm.TogglePlaybackCommand);
        Assert.NotNull(play);

        play!.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        output.WriteLine($"focused={play.IsFocused}");

        Space(play);
        Assert.True(vm.IsPlaying, "Space did nothing with the play button focused");

        Space(play);
        Assert.False(vm.IsPlaying,
            "Space did not stop playback with the play button focused — if this is the "
            + "one that fails, the button is consuming the key and the window handler "
            + "never runs");
    }

    /// <summary>
    /// It is a Global binding, so it means the same thing over a docker as over
    /// the canvas. A panel-scoped Space would be a silent regression: playback
    /// would keep working in most places and stop working in one.
    /// </summary>
    [AvaloniaFact]
    public void ThePlayPauseBindingIsGlobalRatherThanScopedToTheTimeline()
    {
        var map = new Lightbox.App.Services.ShortcutMap();
        var def = map.Find("timeline.playPause");

        Assert.NotNull(def);
        Assert.Equal(Lightbox.App.Services.ShortcutContext.Global, def!.Context);
    }
}
