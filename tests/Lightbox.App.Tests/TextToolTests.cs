using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The text tool as an artist meets it: click, type, and the words become
/// drawing — undoably, retypably, and without the document ever holding a
/// half-finished caption.
/// </summary>
public class TextToolTests
{
    private static MainViewModel Typing()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        // No network from a test, ever — and this is also the switch an artist
        // in a studio with an air gap uses, so it is worth exercising.
        vm.Settings.Fonts.UseGoogleFonts = false;
        vm.SelectToolCommand.Execute(ToolId.Text);
        // The face this machine itself calls default, which is what the tool
        // picks when nobody has opened the font list. Deliberately not "the
        // first installed family": on the machine this was written on that is a
        // Type 1 face with no outlines, which set nothing at all.
        vm.SelectedFont = FontLibrary.Installed()
            .FirstOrDefault(f => f.Family == SKTypeface.Default.FamilyName
                && f is { Weight: 400, Italic: false })
            ?? FontLibrary.Installed().FirstOrDefault(f => f.Family == SKTypeface.Default.FamilyName);
        return vm;
    }

    private static List<Stroke> Glyphs(MainViewModel vm) =>
        [.. vm.PaintedCel().Strokes.Where(s => s.Tool == ToolKind.Text)];

    [AvaloniaFact]
    public void TypingPutsNothingInTheDocumentUntilItIsSet()
    {
        // The preview is on the scratch, not in the record. That is what makes
        // cancelling free and undo one step.
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("hello");

        Assert.True(vm.TextSessionActive);
        Assert.Empty(vm.PaintedCel().Strokes);
        Assert.Null(vm.PaintedCel().Strokes.FirstOrDefault());

        vm.CommitText();

        Assert.False(vm.TextSessionActive);
        Assert.NotEmpty(Glyphs(vm));
    }

    [AvaloniaFact]
    public void SetTypeIsOrdinaryStrokesThatNameTheirElement()
    {
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("ab");
        vm.CommitText();

        var glyphs = Glyphs(vm);
        Assert.Equal(2, glyphs.Count);
        var element = Assert.Single(vm.PanelEditor.Doc.Texts!).Value;
        Assert.Equal("ab", element.Text);
        Assert.All(glyphs, g => Assert.Equal(element.Id, g.TextId));
        Assert.All(glyphs, g => Assert.True(g.Points.Count >= 3));
    }

    [AvaloniaFact]
    public void TypingNothingLeavesNoTrace()
    {
        // A click that starts a caret and thinks better of it must not put an
        // empty element in the document.
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.CommitText();

        Assert.Empty(vm.PaintedCel().Strokes);
        Assert.Null(vm.PanelEditor.Doc.Texts);
    }

    [AvaloniaFact]
    public void EscapingWithWordsSetsThemAndUndoTakesThemBackInOneStep()
    {
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("undo me");
        vm.CommitText();
        Assert.NotEmpty(Glyphs(vm));

        vm.UndoCommand.Execute(null);

        Assert.Empty(Glyphs(vm));
        Assert.True(vm.PanelEditor.Doc.Texts is null or { Count: 0 });
    }

    [AvaloniaFact]
    public void CancellingThrowsTheWordsAwayAndTouchesNothing()
    {
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("never mind");
        vm.CancelText();

        Assert.False(vm.TextSessionActive);
        Assert.Empty(vm.PaintedCel().Strokes);
        Assert.Null(vm.PanelEditor.Doc.Texts);
    }

    [AvaloniaFact]
    public void BackspaceAndTheCaretEditTheWordRatherThanTheEnd()
    {
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("helo");
        vm.MoveTextCaret(-1);
        vm.TypeIntoText("l");

        Assert.Equal("hello", vm.LiveText!.Text);
        Assert.Equal(4, vm.TextCaret);

        vm.TextCaretToEdge(end: true);
        vm.TextBackspace();
        Assert.Equal("hell", vm.LiveText!.Text);

        vm.TextCaretToEdge(end: false);
        vm.TextDeleteForward();
        Assert.Equal("ell", vm.LiveText!.Text);
    }

    [AvaloniaFact]
    public void ClickingTypeAlreadySetPicksItUpToRetype()
    {
        var vm = Typing();
        vm.TextSize = 96;

        vm.BeginText(40, 120);
        vm.TypeIntoText("O");
        vm.CommitText();

        var glyph = Assert.Single(Glyphs(vm));
        // The middle of the letter's stem, which even-odd containment puts
        // inside the contour rather than in the counter. Kept as the aim even
        // though hit-testing no longer needs it: a click that lands on the ink
        // must still work, and it is the case that used to be the *only* one.
        var inside = Inside(glyph);

        vm.BeginText(inside.X, inside.Y);

        Assert.True(vm.TextSessionActive);
        Assert.Equal("O", vm.LiveText!.Text);

        // B347: the caret lands where the click did, not at the end of the
        // block. This test asserted 1 — the end — when the end was the only
        // answer the tool had; what it is named for is that the click picked
        // the type up, and that is unchanged. The rule it now pins is the
        // stronger one: which side of the letter you aimed at decides.
        var box = vm.BoxOf(Assert.Single(vm.PanelEditor.Doc.Texts!).Value)!.Value;
        var middleY = (box.Top + box.Bottom) / 2;
        vm.BeginText(box.Left + 1, middleY);
        Assert.Equal(0, vm.TextCaret);
        vm.BeginText(box.Right - 1, middleY);
        Assert.Equal(1, vm.TextCaret);
    }

    [AvaloniaFact]
    public void RetypingReplacesTheLettersRatherThanStackingNewOnesOver()
    {
        var vm = Typing();
        vm.TextSize = 96;

        vm.BeginText(40, 120);
        vm.TypeIntoText("O");
        vm.CommitText();
        var first = Assert.Single(Glyphs(vm));

        vm.BeginText(Inside(first).X, Inside(first).Y);
        // Said out loud since B347, because the caret now lands where the click
        // did: this test is about the letters being REPLACED rather than
        // stacked, and it should not also be a hostage to which half of an "O"
        // the hit-test helper happened to aim at.
        vm.TextCaretToEdge(end: true);
        vm.TypeIntoText("K");
        vm.CommitText();

        var after = Glyphs(vm);
        Assert.Equal(2, after.Count);
        Assert.Equal("OK", Assert.Single(vm.PanelEditor.Doc.Texts!).Value.Text);
        Assert.DoesNotContain(after, g => g.Id == first.Id);
    }

    [AvaloniaFact]
    public void ClearingTheWordsAndSettingItRemovesTheType()
    {
        // How type is deleted: retype it to nothing. The element goes with it,
        // so the document is not left naming a caption that is not there.
        var vm = Typing();
        vm.TextSize = 96;

        vm.BeginText(40, 120);
        vm.TypeIntoText("O");
        vm.CommitText();
        var glyph = Assert.Single(Glyphs(vm));

        vm.BeginText(Inside(glyph).X, Inside(glyph).Y);
        // B347 gave the tool a selection, and this is what it is for: clearing
        // a caption is Ctrl+A and Backspace rather than one Backspace per
        // letter from an end the caret had to be sent to first.
        vm.SelectAllText();
        vm.TextBackspace();
        vm.CommitText();

        Assert.Empty(Glyphs(vm));
        Assert.True(vm.PanelEditor.Doc.Texts is null or { Count: 0 });
    }

    [AvaloniaFact]
    public void UndoingARetypeBringsBackTheWordsThatWereThere()
    {
        // The revert has to restore the element the document had, not the one
        // that was being typed over it — otherwise undo puts the old letters
        // back beneath a caption that claims to say something else.
        var vm = Typing();
        vm.TextSize = 96;

        vm.BeginText(40, 120);
        vm.TypeIntoText("O");
        vm.CommitText();
        var first = Assert.Single(Glyphs(vm));

        vm.BeginText(Inside(first).X, Inside(first).Y);
        vm.TextCaretToEdge(end: true);   // B347: the caret follows the click now
        vm.TypeIntoText("K");
        vm.CommitText();
        Assert.Equal("OK", Assert.Single(vm.PanelEditor.Doc.Texts!).Value.Text);

        vm.UndoCommand.Execute(null);

        Assert.Equal("O", Assert.Single(vm.PanelEditor.Doc.Texts!).Value.Text);
        Assert.Single(Glyphs(vm));
    }

    [AvaloniaFact]
    public void PickingTypeUpAndSettingItAgainLeavesItsFontAlone()
    {
        // Picking a caption up to fix a letter must not silently re-font it to
        // whatever the tool happens to be set to — nor carry that font into the
        // document for words that are not set in it.
        var vm = Typing();
        vm.TextSize = 96;

        vm.BeginText(40, 120);
        vm.TypeIntoText("O");
        vm.CommitText();
        var was = Assert.Single(vm.PanelEditor.Doc.Texts!).Value.Font.Family;

        var other = FontLibrary.Installed().FirstOrDefault(f =>
            f.Family != was
            && Lightbox.Raster.Text.FontRegistry.System(new FontRef
            {
                Family = f.Family, Weight = f.Weight, Italic = f.Italic,
            }) is { } face
            && Lightbox.Raster.Text.TextBaker.CanSetType(face));
        Assert.SkipWhen(other is null, "this machine has only one usable font family");

        vm.SelectedFont = other;
        var glyph = Assert.Single(Glyphs(vm));
        vm.BeginText(Inside(glyph).X, Inside(glyph).Y);
        vm.CommitText();

        Assert.Equal(was, Assert.Single(vm.PanelEditor.Doc.Texts!).Value.Font.Family);
    }

    [AvaloniaFact]
    public void ReachingForAnotherToolSetsTheTypeRatherThanLosingIt()
    {
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("keep me");
        vm.SelectToolCommand.Execute(ToolId.Brush);

        Assert.False(vm.TextSessionActive);
        Assert.NotEmpty(Glyphs(vm));
    }

    [AvaloniaFact]
    public void ChangingTheSizeReshapesTheWordsUnderTheHand()
    {
        // Measured on the glyph outlines the session actually produced, not on
        // the number that was set: the point of the assertion is that the size
        // reached the shaper while the caret was still up.
        var vm = Typing();

        vm.BeginText(40, 200);
        vm.TypeIntoText("big");
        vm.TextSize = 120;
        vm.CommitText();
        var large = InkWidth(Glyphs(vm));

        var second = Typing();
        second.TextSize = 48;
        second.BeginText(40, 200);
        second.TypeIntoText("big");
        second.CommitText();
        var small = InkWidth(Glyphs(second));

        Assert.True(large > small * 1.5, $"120px type should be far wider than 48px: {large} vs {small}");
    }

    [AvaloniaFact]
    public void TheLivePreviewIsActuallyComposited()
    {
        // The shape tool shipped rendering a preview into the scratch that the
        // overlay never knew about, so it appeared only on release. This is that
        // trap, asserted.
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("seen");

        Assert.True(vm.LivePreviewIsVisible);
        Assert.NotNull(vm.LiveTextPaint);
    }

    [AvaloniaFact]
    public void TypeIsPaintedInTheColourThatWasChosenWhenItWasStarted()
    {
        // Invariant 4 from the type side: what the artist saw while typing is
        // what gets recorded, even if the swatch moves on the way past.
        var vm = Typing();
        vm.ColorHex = "#ff0000";

        vm.BeginText(40, 80);
        vm.TypeIntoText("red");
        vm.ColorHex = "#0000ff";
        vm.CommitText();

        Assert.All(Glyphs(vm), g => Assert.Equal("#ff0000", g.Color));
    }

    [AvaloniaFact]
    public void ADocumentWithTypeInItStillSaysNothingAboutFontsItDidNotCarry()
    {
        var vm = Typing();

        vm.BeginText(40, 80);
        vm.TypeIntoText("installed");
        vm.CommitText();

        // An installed face is named, never copied — so no fonts block appears.
        Assert.Null(vm.PanelEditor.Doc.Fonts);
        Assert.Null(Assert.Single(vm.PanelEditor.Doc.Texts!).Value.Font.EmbeddedId);
    }

    [AvaloniaFact]
    public void AFontWithNoOutlinesIsRefusedWhenItIsPickedRatherThanAtTheCommit()
    {
        // The defect this test exists for: a system font manager will happily
        // name a Type 1 or bitmap-only family, it shapes to nothing, and the
        // only symptom used to be a typed title that vanished on Escape.
        var unusable = FontLibrary.Installed()
            .FirstOrDefault(f => Lightbox.Raster.Text.FontRegistry.System(
                new FontRef { Family = f.Family, Weight = f.Weight, Italic = f.Italic }) is { } face
                && !Lightbox.Raster.Text.TextBaker.CanSetType(face));
        Assert.SkipWhen(unusable is null, "this machine has no outline-less font to refuse");

        var vm = Typing();
        var good = vm.SelectedFont;

        vm.SelectedFont = unusable;

        Assert.Contains("no outlines", vm.AiStatus);

        // And the face actually in hand is still the one that works, so typing
        // now sets letters rather than silently nothing.
        vm.SelectedFont = good;
        vm.BeginText(40, 80);
        vm.TypeIntoText("still works");
        vm.CommitText();
        Assert.NotEmpty(Glyphs(vm));
    }

    [AvaloniaFact]
    public void TypeSetBeforeAnybodyOpensTheFontListStillNamesItsFont()
    {
        // Without this the element records an empty family, and the words can
        // never be picked up again on another machine.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.Settings.Fonts.UseGoogleFonts = false;
        vm.SelectToolCommand.Execute(ToolId.Text);

        vm.BeginText(40, 80);
        vm.TypeIntoText("unopened");
        vm.CommitText();

        var element = Assert.Single(vm.PanelEditor.Doc.Texts!).Value;
        Assert.NotEqual("", element.Font.Family);
    }

    [AvaloniaFact]
    public void TypingOnAHiddenLayerIsRefusedTheWayPaintingIs()
    {
        var vm = Typing();
        vm.LayerRows[0].Visible = false;

        vm.BeginText(40, 80);

        Assert.False(vm.TextSessionActive);
        Assert.Contains("hidden", vm.AiStatus);
    }

    /// <summary>A point that really is on the glyph's ink.</summary>
    /// <remarks>
    /// <b>Found rather than guessed, and the guess is instructive.</b> The first
    /// version took a point just inside the left of <c>Points</c>, assuming that
    /// contour was the glyph's outline — but a glyph's contours arrive in
    /// whatever order the font stores them, and for the "O" of the machine this
    /// was written on <c>Points</c> is the counter and <c>Holes[0]</c> is the
    /// outer ring. Even-odd does not care, which is exactly why the tool works
    /// and the test did not. So: sweep the full contour set for a point the same
    /// rule the tool uses calls inside.
    /// </remarks>
    private static (double X, double Y) Inside(Stroke glyph)
    {
        var contours = new List<IReadOnlyList<StrokePoint>> { glyph.Points };
        if (glyph.Holes is not null) contours.AddRange(glyph.Holes);

        var all = contours.SelectMany(c => c).ToList();
        var minX = all.Min(p => p.X);
        var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y);
        var maxY = all.Max(p => p.Y);

        for (var fy = 0.1; fy < 1.0; fy += 0.1)
        {
            for (var fx = 0.02; fx < 1.0; fx += 0.02)
            {
                var x = minX + (maxX - minX) * fx;
                var y = minY + (maxY - minY) * fy;
                if (GeometryOps.ContainsEvenOdd(contours, x, y)) return (x, y);
            }
        }
        throw new InvalidOperationException("no point of this glyph reads as inside it");
    }

    /// <summary>How wide the letters actually came out, edge to edge.</summary>
    private static double InkWidth(IReadOnlyList<Stroke> glyphs) =>
        glyphs.Count == 0
            ? 0
            : glyphs.Max(g => g.Points.Max(p => p.X)) - glyphs.Min(g => g.Points.Min(p => p.X));
}
