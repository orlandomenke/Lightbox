using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The real publish path holds what it hands over.
/// </summary>
/// <remarks>
/// <para>
/// <b>The end-to-end half of B125 stage 3's lifetime.</b>
/// <c>PublishedPassLifetimeTests</c> proves the mechanism — a snapshot releases
/// what it borrowed, exactly once. These prove <em>it is wired</em>: that
/// <c>PublishSnapshot</c> actually pins, and that the count comes back to zero.
/// </para>
/// <para>
/// Worth separating, because stage 1 shipped a complete, tested pin protocol
/// that **nothing called**. A tested mechanism nobody invokes is indistinguishable
/// from a working one right up until the crash it was built to prevent.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class PublishPinsItsPassesTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Painted()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(160, 160, 1);
        vm.EndStroke();
        return vm;
    }

    private static RenderSnapshot Publish(MainViewModel vm)
    {
        RenderSnapshot? seen = null;
        void Capture(RenderSnapshot s) => seen = s;
        vm.SnapshotChanged += Capture;
        try { vm.PublishSnapshot(); }
        finally { vm.SnapshotChanged -= Capture; }
        return seen ?? throw new InvalidOperationException("nothing was published");
    }

    /// <summary>
    /// A live snapshot holds its passes' bitmaps, and gives every one of them
    /// back when it is freed.
    /// </summary>
    [AvaloniaFact]
    public void APublishedSnapshotHoldsItsPassesUntilItIsDisposed()
    {
        var vm = Painted();

        var snapshot = Publish(vm);

        output.WriteLine($"pinned while live: {vm.PinnedPassBitmaps}, passes: {snapshot.Passes?.Count}");
        Assert.NotNull(snapshot.Passes);
        Assert.NotEmpty(snapshot.Passes);
        Assert.True(vm.PinnedPassBitmaps > 0,
            "the publish path handed over a pass list without holding the bitmaps in it — "
            + "an eviction between publish and render frees pixels Skia is about to read");

        snapshot.Dispose();

        Assert.Equal(0, vm.PinnedPassBitmaps);
    }

    /// <summary>
    /// <b>Publishing repeatedly must not accumulate holds.</b> A publish runs per
    /// pointer event while drawing, so a release that fired on some paths and
    /// not others would pin the entire frame cache open within a single stroke —
    /// which reads as the cache budget being ignored rather than as a leak.
    /// </summary>
    [AvaloniaFact]
    public void RepeatedPublishesDoNotAccumulateHolds()
    {
        var vm = Painted();

        for (var i = 0; i < 20; i++) Publish(vm).Dispose();

        output.WriteLine($"pinned after 20 publish/dispose cycles: {vm.PinnedPassBitmaps}");
        Assert.Equal(0, vm.PinnedPassBitmaps);
    }

    /// <summary>
    /// A publish with no listener attached — headless, or IPC-only — pins
    /// nothing, because nobody was handed a pass list to hold.
    /// </summary>
    [AvaloniaFact]
    public void APublishNobodyIsListeningToHoldsNothing()
    {
        var vm = Painted();

        vm.PublishSnapshot();

        Assert.Equal(0, vm.PinnedPassBitmaps);
    }
}
