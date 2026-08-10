using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The cursor says what the tool will do, and whether it can.
/// </summary>
/// <remarks>
/// <para>
/// Driven with no window at all, which is the point of the mapping being pure:
/// a tool, five booleans, an answer. The alternative — branches inside the
/// canvas control — is testable only through synthetic input, which this
/// environment cannot do reliably, so it would have shipped unguarded.
/// </para>
/// <para>
/// <b>The refusal is the half that matters</b>, so most of this file is about
/// it. Painting on a hidden layer currently does nothing and says nothing, and
/// an artist cannot tell that apart from a broken application.
/// </para>
/// </remarks>
public class CanvasCursorTests(ITestOutputHelper output)
{
    // ---- the tool is legible before it is used ------------------------------------------

    [Fact]
    public void EveryToolHasACursorOfItsOwn()
    {
        var unarmed = Enum.GetValues<ToolId>()
            .Where(t => CanvasCursor.Armed(t) == CanvasCursorKind.Default)
            .ToList();

        output.WriteLine(
            unarmed.Count == 0
                ? "every tool is legible from the pointer"
                : $"still showing the default pointer: {string.Join(", ", unarmed)}");
        Assert.Empty(unarmed);
    }

    /// <summary>
    /// The tools that do different things show different pointers.
    /// </summary>
    /// <remarks>
    /// Not one cursor per tool — a brush and an eraser both paint and both draw
    /// their own size ring, so giving them separate pointers would be a
    /// distinction without a difference. What has to hold is that the *kinds* of
    /// action are told apart, which is what an artist is actually asking when
    /// they glance at the pointer.
    /// </remarks>
    [Fact]
    public void ToolsThatDoDifferentThingsLookDifferent()
    {
        Assert.NotEqual(CanvasCursor.Armed(ToolId.Brush), CanvasCursor.Armed(ToolId.Picker));
        Assert.NotEqual(CanvasCursor.Armed(ToolId.Brush), CanvasCursor.Armed(ToolId.Fill));
        Assert.NotEqual(CanvasCursor.Armed(ToolId.Brush), CanvasCursor.Armed(ToolId.Move));
        Assert.NotEqual(CanvasCursor.Armed(ToolId.Move), CanvasCursor.Armed(ToolId.Arrow));
        Assert.NotEqual(CanvasCursor.Armed(ToolId.Pen), CanvasCursor.Armed(ToolId.Arrow));
    }

    /// <summary>
    /// The zero value of <see cref="CanvasTarget"/> is an ordinary paintable
    /// place, and this is the test that keeps it that way.
    /// </summary>
    /// <remarks>
    /// It failed when written, which is why it is here rather than assumed. The
    /// fields were named for the permissive state — <c>LayerVisible = true</c>,
    /// <c>InsideSelection = true</c> — and a record struct's primary-constructor
    /// defaults do not run for <c>new CanvasTarget()</c>, so the zero value
    /// refused every tool everywhere while reading, in source, as though it
    /// allowed them. The fields are named for the obstacle now.
    /// </remarks>
    [Fact]
    public void NothingIsForbiddenWhenTheLayerWillTakeTheMark()
    {
        foreach (var tool in Enum.GetValues<ToolId>())
        {
            Assert.Null(CanvasCursor.Refusal(tool, new CanvasTarget()));
            Assert.NotEqual(CanvasCursorKind.Forbidden, CanvasCursor.For(tool, new CanvasTarget()));
        }
    }

    // ---- the refusal --------------------------------------------------------------------

    /// <summary>
    /// The four silent refusals, each one now visible before the artist commits
    /// to a stroke.
    /// </summary>
    /// <remarks>
    /// This is the roadmap item's own list, and B2's lesson written as a test:
    /// <em>even refusing would be better than nothing.</em> Each case asserts
    /// both halves — the pointer changes, and there is a sentence saying why.
    /// A cursor that forbids without explaining is a better bug, not a fix.
    /// </remarks>
    [Theory]
    [InlineData(true, false, false, false, false, "hidden")]
    [InlineData(false, true, false, false, false, "locked")]
    [InlineData(false, false, true, true, false, "Alpha lock")]
    [InlineData(false, false, false, false, true, "Outside the selection")]
    public void ADisallowedActionShowsWhyRatherThanDoingNothing(
        bool hidden, bool locked, bool alphaLocked, bool nothingUnder, bool outsideSelection,
        string expected)
    {
        var target = new CanvasTarget(hidden, locked, alphaLocked, nothingUnder, outsideSelection);

        var reason = CanvasCursor.Refusal(ToolId.Brush, target);
        output.WriteLine($"{target} -> {reason}");

        Assert.NotNull(reason);
        Assert.Contains(expected, reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CanvasCursorKind.Forbidden, CanvasCursor.For(ToolId.Brush, target));
    }

    /// <summary>
    /// A hidden layer that is also locked reports being hidden.
    /// </summary>
    /// <remarks>
    /// The order is the message. Both are true, and only one is the thing to fix
    /// first — unlocking a hidden layer still leaves nothing to see, so naming
    /// the lock would send the artist to the wrong switch.
    /// </remarks>
    [Fact]
    public void TheReasonGivenIsTheOneToFixFirst()
    {
        var reason = CanvasCursor.Refusal(
            ToolId.Brush, new CanvasTarget(LayerHidden: true, LayerLocked: true));

        Assert.Contains("hidden", reason!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Alpha lock only refuses where there is nothing to paint on.
    /// </summary>
    /// <remarks>
    /// The one refusal that varies pixel by pixel rather than per layer, and so
    /// the one that would be most wrong to state as "this layer is alpha
    /// locked": over existing paint the brush works perfectly.
    /// </remarks>
    [Fact]
    public void AlphaLockRefusesOnlyWhereThereIsNothingToPaintOn()
    {
        var overPaint = new CanvasTarget(AlphaLocked: true, NothingUnderPointer: false);
        var overNothing = new CanvasTarget(AlphaLocked: true, NothingUnderPointer: true);

        Assert.Null(CanvasCursor.Refusal(ToolId.Brush, overPaint));
        Assert.NotNull(CanvasCursor.Refusal(ToolId.Brush, overNothing));
    }

    /// <summary>
    /// A tool that does not paint is never refused by the layer's state.
    /// </summary>
    /// <remarks>
    /// The picker reads pixels rather than writing them, so a locked layer does
    /// not stop it — and forbidding it would be a lie the artist could disprove
    /// in one click. The arrows and the select tools act on records and regions
    /// rather than on the layer's pixels, and are excluded for the same reason.
    /// </remarks>
    [Fact]
    public void AToolThatDoesNotPaintIsNeverStoppedByTheLayer()
    {
        var hostile = new CanvasTarget(
            LayerHidden: true, LayerLocked: true, AlphaLocked: true,
            NothingUnderPointer: true, OutsideSelection: true);

        foreach (var tool in Enum.GetValues<ToolId>().Where(t => !CanvasCursor.Paints(t)))
        {
            output.WriteLine($"{tool}: {CanvasCursor.For(tool, hostile)}");
            Assert.Null(CanvasCursor.Refusal(tool, hostile));
            Assert.NotEqual(CanvasCursorKind.Forbidden, CanvasCursor.For(tool, hostile));
        }
    }

    /// <summary>Every painting tool is refused by every refusal.</summary>
    /// <remarks>
    /// The eraser is the one worth naming: it removes paint rather than adding
    /// it, and a locked layer has to stop it just as hard — an eraser that
    /// quietly did nothing on a locked layer is the same bug wearing the
    /// opposite sign.
    /// </remarks>
    [Fact]
    public void EveryPaintingToolIsStoppedByAHiddenOrLockedLayer()
    {
        foreach (var tool in Enum.GetValues<ToolId>().Where(CanvasCursor.Paints))
        {
            Assert.NotNull(CanvasCursor.Refusal(tool, new CanvasTarget(LayerHidden: true)));
            Assert.NotNull(CanvasCursor.Refusal(tool, new CanvasTarget(LayerLocked: true)));
        }
    }

    // ---- the wiring, so the mapping is not decoration ------------------------------------

    /// <summary>
    /// The view model actually asks the mapping, and the answer changes with the
    /// layer.
    /// </summary>
    /// <remarks>
    /// Everything above tests a pure function that nothing has to call. This is
    /// the test that fails if <c>PointerIntent</c> stops being bound, stops being
    /// notified, or starts reading a different layer's flags — which is the
    /// failure where the whole feature works perfectly and no artist ever sees
    /// it.
    /// </remarks>
    [AvaloniaFact]
    public void TheViewModelReportsTheIntentForTheActiveLayer()
    {
        var vm = VmLayers.PaperVm();
        vm.SelectToolCommand.Execute(ToolId.Brush);

        Assert.Equal(CanvasCursorKind.Paint, vm.PointerIntent);
        Assert.Null(vm.PointerRefusal);

        vm.PaintLayer().Locked = true;
        vm.RefreshPointerIntent();

        output.WriteLine($"{vm.PointerIntent}: {vm.PointerRefusal}");
        Assert.Equal(CanvasCursorKind.Forbidden, vm.PointerIntent);
        Assert.Contains("locked", vm.PointerRefusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Switching tool changes what the pointer promises.</summary>
    [AvaloniaFact]
    public void ChangingToolChangesTheIntent()
    {
        var vm = VmLayers.PaperVm();

        vm.SelectToolCommand.Execute(ToolId.Brush);
        var painting = vm.PointerIntent;
        vm.SelectToolCommand.Execute(ToolId.Picker);

        Assert.NotEqual(painting, vm.PointerIntent);
        Assert.Equal(CanvasCursorKind.Pick, vm.PointerIntent);
    }

    /// <summary>Every refusal is a sentence, not a code.</summary>
    /// <remarks>
    /// It goes in the status line beside the pointer, so it has to read as
    /// something said to a person. Cheap to assert and it is the part that
    /// decays first.
    /// </remarks>
    [Fact]
    public void EveryReasonReadsAsASentence()
    {
        CanvasTarget[] refusals =
        [
            new(LayerHidden: true),
            new(LayerLocked: true),
            new(OutsideSelection: true),
            new(AlphaLocked: true, NothingUnderPointer: true),
        ];

        foreach (var target in refusals)
        {
            var reason = CanvasCursor.Refusal(ToolId.Brush, target);
            Assert.NotNull(reason);
            output.WriteLine(reason!);
            Assert.EndsWith(".", reason!, StringComparison.Ordinal);
            Assert.True(char.IsUpper(reason![0]), $"not a sentence: {reason}");
        }
    }
}
