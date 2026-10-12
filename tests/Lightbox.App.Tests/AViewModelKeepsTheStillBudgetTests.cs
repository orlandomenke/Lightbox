using Avalonia.Headless.XUnit;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Opening a view model applies the picture limit only when it changes, so it
/// does not reset a still budget somebody set.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>MainViewModel</c> applies the settings' picture limit as it is
/// built, and that wrote <see cref="FrameBitmapCache.ByteBudget"/> back to the
/// brokered share every time, even when the limit was the one already in force.
/// In the app there is one view model and it never mattered. In the suite, a test
/// that set a small still budget lost it the moment a test in another collection
/// built a view model: <c>NeighbourStillWarmTests.OnlyTheNeighboursThereIsRoomForAreRendered</c>
/// failed that way in two full runs (2026-10-10 and 2026-10-12) and passed every
/// time alone.
/// </para>
/// </remarks>
[Collection("FrameCacheBudget")]
public sealed class AViewModelKeepsTheStillBudgetTests
{
    [AvaloniaFact]
    public void BuildingAnotherViewModelLeavesTheStillBudgetAsSet()
    {
        _ = VmLayers.PaperVm(); // the limit in force, as any earlier view model applied it
        var before = FrameBitmapCache.ByteBudget;
        try
        {
            FrameBitmapCache.ByteBudget = 12345678;

            _ = VmLayers.PaperVm();

            Assert.Equal(12345678, FrameBitmapCache.ByteBudget);
        }
        finally
        {
            FrameBitmapCache.ByteBudget = before;
        }
    }

    [AvaloniaFact]
    public void AChangedLimitStillReachesTheStillCache()
    {
        var before = (PictureMemory.Limit, FrameBitmapCache.ByteBudget);
        try
        {
            Lightbox.App.ViewModels.MainViewModel.ApplyPictureLimit(MemoryBudget.PicturesFloorBytes);
            Assert.Equal(PictureMemory.Brokered, FrameBitmapCache.ByteBudget);
            Lightbox.App.ViewModels.MainViewModel.ApplyPictureLimit(MemoryBudget.PicturesFloorBytes * 2);
            Assert.Equal(PictureMemory.Brokered, FrameBitmapCache.ByteBudget);
        }
        finally
        {
            PictureMemory.Limit = before.Limit;
            FrameBitmapCache.ByteBudget = before.ByteBudget;
        }
    }
}
