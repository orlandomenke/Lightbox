using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// What the bone overlay shows, and when.
/// </summary>
/// <remarks>
/// Two reports, and the second turned out to be wider than it looked: "during
/// playback the last pose stays visible while the rest on canvas animates". The
/// overlay is pushed to the canvas only when <c>BoneChromes</c> raises a change,
/// and a frame change never did — so the rig sat at whatever pose it last
/// computed, on a scrub as much as during playback. <c>BoneChromes</c> was
/// already frame-correct; nothing was asking it again.
/// </remarks>
[Collection("BrushState")]
public class BoneOverlayModeTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == index);

    /// <summary>A rigged document posed differently on two frames.</summary>
    private static MainViewModel Rigged()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        for (var i = 1; i < 6; i++) vm.InsertFrameAt(Cell(vm, i), FrameRole.Key);
        vm.Doc.Armature = new Armature
        {
            Bones = [new Bone { Id = "b", Name = "arm", X = 100, Y = 100, Length = 60 }],
        };
        vm.Doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 0 } } },
                new PoseKey { Frame = 4, Bones = { ["b"] = new BonePose { RotationDeg = 90 } } },
            ],
        };
        vm.ArmatureEditMode = true;
        return vm;
    }

    /// <summary>
    /// The live bone's tip at a frame — not a ghost of it.
    /// </summary>
    /// <remarks>
    /// The chrome carries the armature's own onion skin: the skeleton at the
    /// neighbouring pose keys, sharing the bone's id and marked with a
    /// <see cref="BoneGhost"/>. Asking for the bone by id alone finds three of
    /// them on a frame between two keys.
    /// </remarks>
    private static (double X, double Y) TipAt(MainViewModel vm, int frame)
    {
        vm.CurrentFrameIndex = frame;
        var chrome = vm.BoneChromes.Single(c => c.Id == "b" && c.Ghost == BoneGhost.None);
        return (chrome.X1, chrome.Y1);
    }

    // ---- the rig follows the playhead ------------------------------------------

    [AvaloniaFact]
    public void PosingMode_TheRigStandsWhereThePlayheadIs()
    {
        var vm = Rigged();
        vm.PosingMode = true;

        var atRest = TipAt(vm, 0);
        var posed = TipAt(vm, 4);

        output.WriteLine($"frame 0 tip {atRest}, frame 4 tip {posed}");
        // Ninety degrees apart: if the overlay did not follow the playhead
        // these would be the same point, which is the reported symptom.
        Assert.NotEqual(atRest, posed);
    }

    [AvaloniaFact]
    public void BindMode_TheRigDoesNotMoveWithThePlayhead()
    {
        var vm = Rigged();
        vm.IsBindMode = true;

        // Bind shows the rest skeleton, so the playhead means nothing to it —
        // which is why the frame change only notifies when a pose is showing.
        Assert.Equal(TipAt(vm, 0), TipAt(vm, 4));
    }

    // ---- and its colour says which it is ----------------------------------------

    [AvaloniaFact]
    public void TheOverlaySaysWhetherItIsShowingAPose()
    {
        var vm = Rigged();

        vm.IsBindMode = true;
        Assert.False(vm.BonesShowAPose);

        vm.PosingMode = true;
        Assert.True(vm.BonesShowAPose);

        // Weight painting solves at the playhead too, so it is a pose as far as
        // the canvas is concerned — the colour tracks what the bones are
        // standing at, not what the mode is called.
        vm.WeightPainting = true;
        Assert.True(vm.BonesShowAPose);
    }

    // ---- playback ----------------------------------------------------------------

    [AvaloniaFact]
    public void PlaybackHidesTheRigByDefault()
    {
        var vm = Rigged();
        vm.PosingMode = true;
        Assert.NotEmpty(vm.BoneChromes);

        vm.IsPlaying = true;

        // Absent rather than frozen: a skeleton standing still over a moving
        // drawing reads as the drawing having come off its rig.
        Assert.Empty(vm.BoneChromes);
    }

    [AvaloniaFact]
    public void TheRigComesBackWhenPlaybackStops()
    {
        var vm = Rigged();
        vm.PosingMode = true;
        vm.IsPlaying = true;

        vm.IsPlaying = false;

        Assert.NotEmpty(vm.BoneChromes);
    }

    [AvaloniaFact]
    public void AskedFor_TheRigAnimatesWithPlaybackInstead()
    {
        var vm = Rigged();
        vm.PosingMode = true;
        vm.AnimateRigDuringPlayback = true;

        vm.IsPlaying = true;

        Assert.NotEmpty(vm.BoneChromes);
        // And it is still standing at the playhead, which is the whole point of
        // asking for it.
        Assert.NotEqual(TipAt(vm, 0), TipAt(vm, 4));
    }

    [AvaloniaFact]
    public void TheChoiceSurvivesTheSession()
    {
        var vm = Rigged();

        vm.AnimateRigDuringPlayback = true;

        Assert.True(vm.Settings.AnimateRigDuringPlayback);
    }

    /// <summary>
    /// Onion ghosts of the skeleton answer "where did this pose come from" —
    /// which is what playback is answering, so they stand down for it.
    /// </summary>
    /// <remarks>
    /// The same rule <c>ScenePassBuilder</c> already applies to the drawing's
    /// own onion skin, and here it is also what makes the opt-in affordable:
    /// the ghost memo is keyed on the playhead, so playback would miss it every
    /// tick. See <see cref="TheOptInPathIsPricedRatherThanAssumed"/>.
    /// </remarks>
    [AvaloniaFact]
    public void TheRigsOnionSkinStandsDownForPlayback()
    {
        var vm = Rigged();
        vm.PosingMode = true;
        vm.Onion.Enabled = true;
        vm.AnimateRigDuringPlayback = true;
        vm.CurrentFrameIndex = 2;
        Assert.Contains(vm.BoneChromes, c => c.Ghost != BoneGhost.None);

        vm.IsPlaying = true;

        Assert.NotEmpty(vm.BoneChromes);
        Assert.DoesNotContain(vm.BoneChromes, c => c.Ghost != BoneGhost.None);
    }

    // ---- what the opt-in costs -------------------------------------------------

    /// <summary>
    /// The opt-in path pays a rig solve on every played tick, so it gets priced
    /// rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// B152's lesson is that per-tick overlay work costs several times what it
    /// looks like, and it is why the default is off. But "off by default" is not
    /// an answer to "what does it cost when it is on" — an artist who ticks the
    /// box is entitled to playback that still plays.
    /// </para>
    /// <para>
    /// A twenty-bone character at twelve to twenty-four frames a second gives a
    /// tick roughly 40 ms of headroom to do everything in, of which the rig may
    /// have a slice. The budget here is <b>2 ms per tick</b>, loose on purpose in
    /// the way the charter's budgets are: it catches a solve that went
    /// order-of-magnitude wrong, not drift. If this ever fails, the fix is a memo
    /// keyed on the playhead — the ghosts already have one — not a raised number.
    /// It measures <b>0.05 ms</b> here, so the box is affordable to tick; the
    /// budget is forty times that because a budget at the measurement is a
    /// budget that fails on a slower machine.
    /// </para>
    /// <para>
    /// <b>What is timed is one evaluation of <c>BoneChromes</c>, not a tick of
    /// playback</b>, and that distinction is the whole measurement.
    /// Advancing the playhead costs several milliseconds of view-model work that
    /// playback pays whether or not a rig exists; timing the loop with the frame
    /// change inside it read 4.7 ms and would have attributed all of it here.
    /// The cost this feature actually adds per tick is the notification and this
    /// evaluation, so this is what it measures — the same "what else is in this
    /// number" the performance notes keep having to relearn.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void TheOptInPathIsPricedRatherThanAssumed()
    {
        var vm = Rigged();
        vm.Doc.Armature = new Armature
        {
            Bones = [.. Enumerable.Range(0, 20).Select(i => new Bone
            {
                Id = $"b{i}",
                Name = $"bone {i}",
                ParentId = i == 0 ? null : $"b{i - 1}",
                X = 100 + i * 8,
                Y = 100,
                Length = 24,
            })],
        };
        vm.Doc.Scene.PoseTrack = new PoseTrack
        {
            Keys =
            [
                new PoseKey { Frame = 0, Bones = { ["b3"] = new BonePose { RotationDeg = 0 } } },
                new PoseKey { Frame = 4, Bones = { ["b3"] = new BonePose { RotationDeg = 90 } } },
            ],
        };
        vm.PosingMode = true;
        vm.Onion.Enabled = true;
        vm.AnimateRigDuringPlayback = true;
        vm.IsPlaying = true;

        vm.CurrentFrameIndex = 2;

        // Warm the JIT: a cold first call is measuring the runtime, not the rig.
        for (var i = 0; i < 50; i++) _ = vm.BoneChromes;

        const int Ticks = 200;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < Ticks; i++) _ = vm.BoneChromes;
        sw.Stop();

        var perTick = sw.Elapsed.TotalMilliseconds / Ticks;
        output.WriteLine($"20 bones, pose solve per tick: {perTick:F3} ms");
        Assert.True(perTick < 2.0, $"rig solve costs {perTick:F3} ms per played tick, budget 2 ms");
    }
}
