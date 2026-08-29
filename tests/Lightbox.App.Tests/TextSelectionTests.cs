using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Entering type through its box, and selecting inside it.
/// </summary>
/// <remarks>
/// <para>
/// Reported as "ours is currently limited", and it was, in three ways that
/// compounded. <c>TypeAt</c> hit-tested <b>glyph outlines</b>, so clicking the
/// gap between two letters — or the middle of an "o" — missed every contour and
/// started a <em>second</em> block on top of the first. Picking type up put the
/// caret at the end whatever you had aimed at. And there was no selection at
/// all, so correcting a word meant arrowing to it and pressing Backspace once
/// per character.
/// </para>
/// <para>
/// The box is now what answers, which is also what makes it honest to draw one:
/// the shape an artist can see is the shape that responds.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TextSelectionTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A view model with one block of type already set.</summary>
    private static MainViewModel Typed(string words = "hello world")
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ActiveTool = ToolId.Text;
        vm.TextSize = 48;
        vm.EnsureFontsLoaded();
        vm.BeginText(100, 200);
        vm.TypeIntoText(words);
        vm.CommitText();
        return vm;
    }

    private static TextElement TheType(MainViewModel vm) => vm.Doc.Texts!.Values.Single();

    // ---- the box is the target ----------------------------------------------

    [AvaloniaFact]
    public void TypeSetsAndCanBeFoundAgain()
    {
        var vm = Typed();
        Assert.False(vm.TextSessionActive);
        Assert.Equal("hello world", TheType(vm).Text);
        Assert.NotNull(vm.BoxOf(TheType(vm)));
    }

    /// <summary>
    /// Clicking the gap between two letters picks the block up rather than
    /// starting another one.
    /// </summary>
    /// <remarks>
    /// <b>The reported fault, at its centre.</b> The space in "hello world" has
    /// no outline, so the old hit test found nothing there and began a second
    /// block — two elements stacked, and an artist who typed a correction into
    /// the wrong one.
    /// </remarks>
    [AvaloniaFact]
    public void ClickingTheSpaceBetweenWordsPicksTheBlockUp()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        var middle = (box.Left + box.Right) / 2;
        var baseline = (box.Top + box.Bottom) / 2;

        vm.BeginText(middle, baseline);

        Assert.True(vm.TextSessionActive);
        Assert.Equal("hello world", vm.LiveText!.Text);
        vm.CommitText();
        Assert.Single(vm.Doc.Texts!);
    }

    /// <summary>Clicking well outside the box still starts a new block.</summary>
    /// <remarks>
    /// The box widened the target; it must not have widened it to the whole
    /// canvas. Without this, "click here to start typing" would have stopped
    /// working anywhere near existing type.
    /// </remarks>
    [AvaloniaFact]
    public void ClickingWellClearOfTheBoxStartsANewBlock()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;

        vm.BeginText(box.Right + 200, box.Bottom + 200);
        vm.TypeIntoText("second");
        vm.CommitText();

        Assert.Equal(2, vm.Doc.Texts!.Count);
    }

    /// <summary>The caret lands where the click was, not at the end.</summary>
    [AvaloniaFact]
    public void PickingTypeUpPutsTheCaretWhereYouClicked()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        var baseline = (box.Top + box.Bottom) / 2;

        vm.BeginText(box.Left + 2, baseline);
        output.WriteLine($"caret {vm.TextCaret} of {vm.LiveText!.Text.Length}");
        Assert.True(
            vm.TextCaret < vm.LiveText!.Text.Length,
            "the caret went to the end of the block rather than to the click");
        Assert.Equal(0, vm.TextCaret);
    }

    // ---- selecting -----------------------------------------------------------

    [AvaloniaFact]
    public void ADragAcrossTheTypeSelectsWhatItCrossed()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        var baseline = (box.Top + box.Bottom) / 2;

        vm.BeginText(box.Left + 2, baseline);
        Assert.False(vm.HasTextSelection);
        vm.DragTextSelectionTo(box.Right - 2, baseline);

        Assert.True(vm.HasTextSelection);
        var (start, end) = vm.TextSelection;
        output.WriteLine($"selected [{start},{end}) of {vm.LiveText!.Text.Length}");
        Assert.Equal(0, start);
        Assert.True(end > 0);
    }

    /// <summary>Double-click takes the word, not the whole line.</summary>
    [AvaloniaFact]
    public void ADoubleClickTakesTheWordUnderIt()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        var baseline = (box.Top + box.Bottom) / 2;

        vm.BeginText(box.Left + 2, baseline);
        vm.SelectTextWordAt(box.Left + 4, baseline);

        var (start, end) = vm.TextSelection;
        output.WriteLine($"word [{start},{end}) = '{vm.LiveText!.Text[start..end]}'");
        Assert.Equal("hello", vm.LiveText!.Text[start..end]);
    }

    [AvaloniaFact]
    public void CtrlATakesTheWholeBlock()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        vm.BeginText(box.Left + 2, (box.Top + box.Bottom) / 2);

        vm.SelectAllText();

        Assert.Equal((0, "hello world".Length), vm.TextSelection);
    }

    /// <summary>Shift+arrow extends from the caret; a bare arrow does not.</summary>
    [AvaloniaFact]
    public void ShiftExtendsAndABareArrowCollapses()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        vm.BeginText(box.Left + 2, (box.Top + box.Bottom) / 2);

        vm.MoveTextCaret(1, extend: true);
        vm.MoveTextCaret(1, extend: true);
        Assert.Equal((0, 2), vm.TextSelection);

        // Without Shift the selection collapses to its far edge rather than
        // stepping one on from whichever end the caret was.
        vm.MoveTextCaret(1);
        Assert.False(vm.HasTextSelection);
        Assert.Equal(2, vm.TextCaret);
    }

    // ---- and the selection is what the next keystroke replaces --------------

    [AvaloniaFact]
    public void TypingOverASelectionReplacesIt()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        var baseline = (box.Top + box.Bottom) / 2;
        vm.BeginText(box.Left + 2, baseline);
        vm.SelectTextWordAt(box.Left + 4, baseline);

        vm.TypeIntoText("goodbye");

        output.WriteLine(vm.LiveText!.Text);
        Assert.Equal("goodbye world", vm.LiveText!.Text);
        Assert.False(vm.HasTextSelection);
    }

    [AvaloniaFact]
    public void BackspaceOverASelectionTakesAllOfIt()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        vm.BeginText(box.Left + 2, (box.Top + box.Bottom) / 2);
        vm.SelectAllText();

        vm.TextBackspace();

        Assert.Equal("", vm.LiveText!.Text);
        Assert.False(vm.HasTextSelection);
    }

    /// <summary>
    /// A selection replaced and then committed leaves one block, not two.
    /// </summary>
    /// <remarks>
    /// The end-to-end shape of the whole feature: enter through the box, select
    /// a word, type over it, set the type. If any step had started a second
    /// element this is where it would show.
    /// </remarks>
    [AvaloniaFact]
    public void CorrectingAWordLeavesOneBlockSayingTheNewThing()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        var baseline = (box.Top + box.Bottom) / 2;

        vm.BeginText(box.Left + 2, baseline);
        vm.SelectTextWordAt(box.Left + 4, baseline);
        vm.TypeIntoText("goodbye");
        vm.CommitText();

        Assert.Single(vm.Doc.Texts!);
        Assert.Equal("goodbye world", TheType(vm).Text);
    }

    // ---- the other way in ----------------------------------------------------

    /// <summary>The Arrow's double-click opens the type and takes the tool.</summary>
    [AvaloniaFact]
    public void ADoubleClickWithTheArrowEntersTheType()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        vm.ActiveTool = ToolId.Arrow;

        var entered = vm.EnterTypeAt((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);

        Assert.True(entered);
        Assert.True(vm.TextSessionActive);
        // The tool follows, so what is in hand matches what the next key does.
        Assert.Equal(ToolId.Text, vm.ActiveTool);
    }

    [AvaloniaFact]
    public void ADoubleClickOnEmptyCanvasEntersNothing()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;
        vm.ActiveTool = ToolId.Arrow;

        Assert.False(vm.EnterTypeAt(box.Right + 300, box.Bottom + 300));
        Assert.False(vm.TextSessionActive);
        Assert.Equal(ToolId.Arrow, vm.ActiveTool);
    }

    /// <summary>What the canvas outlines while the text tool hovers.</summary>
    [AvaloniaFact]
    public void TheBoxUnderThePointerIsTheOneThatWouldBeEntered()
    {
        var vm = Typed();
        var box = vm.BoxOf(TheType(vm))!.Value;

        var under = vm.TypeBoxUnder((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        Assert.NotNull(under);
        Assert.Equal(box.Left, under!.Value.Left, 2);

        Assert.Null(vm.TypeBoxUnder(box.Right + 300, box.Bottom + 300));
    }
}
