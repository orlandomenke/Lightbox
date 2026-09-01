using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// B329. The live post-process records where its last pass stood in two
/// fields with two units — <c>PostStampedPoints</c> for the scheduling guard,
/// <c>PostStampedDabs</c> for anything measuring the mark — and both paths that
/// write them agree on which is which.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaces: one field, two units, one afternoon lost.</b>
/// <c>PostStampedCount</c> was set from <c>Points.Count</c> on the worker path
/// and from <c>dabs.Count</c> on the cap-only path. Its readers compared it
/// against points or against zero, so nobody noticed — until B322's fourth
/// attempt subtracted it from a dab count to learn what the pass had not seen,
/// got points subtracted from dabs, and restamped the whole stroke on every
/// publish. Pen-to-screen went from 63 ms to 991.
/// </para>
/// <para>
/// <b>Dabs and points must differ for these tests to say anything.</b> Dabs
/// are interpolated along the stroke by spacing, so on a real mark the two
/// counts are far apart; each test asserts that first, because a test where
/// they happen to be equal would pass with the units crossed.
/// </para>
/// <para>
/// Read mid-stroke, before <c>EndStroke</c> resets the session, and with the
/// synchronous runner <c>SoftBrushLiveIsExactTests</c> uses, so a queued pass
/// has landed by the time the fields are read.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class PostStampedUnitsTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Ready(string preset)
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#101010",
            LivePostRunner = work =>
            {
                work();
                return Task.CompletedTask;
            },
        };
        vm.NewDocument(new NewDocumentSettings(preset, 1280, 720, 12, 72, "#ffffff", false));
        vm.ApplyPreset(BuiltInPresets.Create().First(p => p.Name == preset));
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>Most of a stroke, left open so the session's fields are still live.</summary>
    private static void DrawWithoutLifting(MainViewModel vm, int events = 120)
    {
        vm.BeginStroke(80, 360, 1);
        Dispatcher.UIThread.RunJobs();
        var x = 80.0;
        for (var i = 0; i < events; i++)
        {
            x += 9;
            if (x > 1200) x = 80;
            vm.MoveStroke(x, 360 + Math.Sin(x / 55) * 120, 0.9);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private (int Points, int Dabs, int LivePoints, int LiveDabs) Read(MainViewModel vm, string preset)
    {
        var (points, dabs) = vm.LivePostStampedForTest;
        var livePoints = vm.LivePointCountForTest;
        var liveDabs = vm.LiveDabCountForTest;
        output.WriteLine(
            $"{preset}: recorded points {points}, dabs {dabs}; live points {livePoints}, dabs {liveDabs}");
        Assert.True(
            livePoints != liveDabs,
            "dabs and points coincide on this stroke, so the units cannot be told apart — change the stroke");
        return (points, dabs, livePoints, liveDabs);
    }

    [AvaloniaTheory]
    [InlineData("Soft round")]
    [InlineData("Pencil")]
    public void PostStampedDabsMatchesTheDabList(string preset)
    {
        var vm = Ready(preset);
        DrawWithoutLifting(vm);
        try
        {
            var r = Read(vm, preset);
            Assert.True(r.Dabs > 0, "no pass landed, so there is nothing to compare");
            Assert.Equal(r.LiveDabs, r.Dabs);
        }
        finally
        {
            vm.EndStroke();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// The cap-only path (Soft round: ceiling in place, no deferred pass) and
    /// the worker path (Pencil: granulation, a real pass) each write points into
    /// the points field and dabs into the dabs field. This is the assertion the
    /// old single field could not satisfy on both paths at once.
    /// </summary>
    [AvaloniaFact]
    public void ACapOnlyPassAndAWorkerPassAgreeOnTheirOwnUnits()
    {
        foreach (var preset in new[] { "Soft round", "Pencil" })
        {
            var vm = Ready(preset);
            var expectCapOnly = preset == "Soft round";
            Assert.Equal(
                expectCapOnly,
                BrushEngine.NeedsFootprintCap(vm.CurrentToolSettingsForTest)
                && vm.CurrentToolSettingsForTest.Granulation == 0);

            DrawWithoutLifting(vm);
            try
            {
                var r = Read(vm, preset);
                Assert.Equal(r.LivePoints, r.Points);
                Assert.Equal(r.LiveDabs, r.Dabs);
            }
            finally
            {
                vm.EndStroke();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
