using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Typing, through the real window.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file exists because the tool shipped unusable.</b> Every other text
/// test drives the view model directly — <c>BeginText</c>, <c>TypeIntoText</c>,
/// <c>CommitText</c> — and all of them passed while an artist clicking on the
/// canvas got a caret that would not take a letter. A test that calls the method
/// the keyboard is supposed to reach proves the method works and says nothing
/// about whether the keyboard reaches it.
/// </para>
/// <para>
/// So nothing here calls <c>TypeIntoText</c>. Input goes in as key presses on
/// the window, through the same dispatch an artist's keyboard uses, and the
/// assertion is on what the session ended up holding.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TextTypingTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Typing()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var vm = (MainViewModel)window.DataContext!;
        vm.SelectToolCommand.Execute(ToolId.Text);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Where the canvas would have put it. The press path itself is covered
        // by CanvasControl's own tests; what is under test here is what happens
        // after the caret exists.
        vm.BeginText(200, 200);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    [AvaloniaFact]
    public void AClickWithTheTextToolOpensASession()
    {
        var (_, vm) = Typing();

        Assert.True(vm.TextSessionActive, "clicking with the text tool must open a typing session");
        Assert.True(vm.HasTextFace, "a session with no typeface can never shape a letter");
    }

    [AvaloniaFact]
    public void TypingALetterPutsItInTheText()
    {
        var (window, vm) = Typing();

        window.KeyTextInput("A");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"after 'A': text='{vm.LiveText?.Text}' caret={vm.TextCaret}");
        Assert.Equal("A", vm.LiveText?.Text);
    }

    [AvaloniaFact]
    public void TypingAWordPutsTheWholeWordIn()
    {
        var (window, vm) = Typing();

        foreach (var c in "Title")
        {
            window.KeyTextInput(c.ToString());
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"after 'Title': text='{vm.LiveText?.Text}'");
        Assert.Equal("Title", vm.LiveText?.Text);
    }

    [AvaloniaFact]
    public void ALetterKeyIsALetterRatherThanATool()
    {
        // B is the brush's shortcut. While a caret is up it must be a letter,
        // or every word an artist types changes tools halfway through.
        var (window, vm) = Typing();

        window.KeyTextInput("B");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("B", vm.LiveText?.Text);
        Assert.Equal(ToolId.Text, vm.ActiveTool);
    }

    [AvaloniaFact]
    public void ALetterKeyIsLeftUnhandledSoItCanBecomeACharacter()
    {
        // The typing bug itself, at the only level a headless test can reach.
        //
        // What broke was the session marking every plain key handled, to keep B
        // from being the brush — and a handled KeyDown never becomes a
        // TextInput, because that is the step a handled event cancels. So the
        // assertion is on Handled rather than on the letter arriving: this
        // harness cannot turn a key into a character at all (KeyPressQwerty
        // leaves even a focused TextBox empty, measured), which is exactly why
        // the original defect shipped green.
        var (window, vm) = Typing();

        var press = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.A,
            KeyModifiers = KeyModifiers.None,
        };
        window.RaiseEvent(press);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"letter key: handled={press.Handled} tool={vm.ActiveTool}");
        Assert.False(
            press.Handled,
            "a letter key must be left unhandled, or the framework never turns it into a character");
        // And still kept away from the shortcuts, which is the other half.
        Assert.Equal(ToolId.Text, vm.ActiveTool);
        Assert.True(vm.TextSessionActive);
    }

    [AvaloniaFact]
    public void AnEditingKeyIsHandledSoNothingElseSeesIt()
    {
        // The other side of the same split: keys the session acts on must be
        // marked handled, or they reach the shortcut dispatch as well.
        var (window, vm) = Typing();
        window.KeyTextInput("A");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var press = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Back,
            KeyModifiers = KeyModifiers.None,
        };
        window.RaiseEvent(press);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(press.Handled, "backspace is the session's and nothing else should see it");
        Assert.Equal("", vm.LiveText?.Text);
    }

    [AvaloniaFact]
    public void BackspaceTakesTheLastLetterBack()
    {
        var (window, vm) = Typing();

        window.KeyTextInput("A");
        window.KeyTextInput("B");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("A", vm.LiveText?.Text);
    }

    [AvaloniaFact]
    public void EscapeSetsTheTypeAndLeavesStrokesBehind()
    {
        var (window, vm) = Typing();

        window.KeyTextInput("H");
        window.KeyTextInput("i");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.TextSessionActive, "Escape must close the session");
        var glyphs = vm.PaintedCel().Strokes
            .Where(s => s.Tool == Lightbox.Core.Documents.ToolKind.Text)
            .ToList();
        output.WriteLine($"{glyphs.Count} glyph stroke(s) after setting 'Hi'");
        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, s => Assert.NotNull(s.TextId));
    }
}
