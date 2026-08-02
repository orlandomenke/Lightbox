using System.Runtime.CompilerServices;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

internal static class TestSetup
{
    /// <summary>Keep brush persistence away from the user's real settings during tests.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        MainViewModel.BrushStorePath = Path.Combine(
            Path.GetTempPath(), $"lightbox-test-brushes-{Guid.NewGuid():N}.json");
    }
}
