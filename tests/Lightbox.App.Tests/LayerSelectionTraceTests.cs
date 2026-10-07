using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The layer-selection trace: written while an input trace is armed, silent
/// otherwise, and carrying positions rather than names.
/// </summary>
/// <remarks>
/// It exists because Ctrl+click and Shift+click in the Layers docker pick one
/// layer at a time on the owner's machine while every headless route works on
/// the same commit. These tests prove the instrument, not a fix.
/// </remarks>
[Collection("BrushState")]
public sealed class LayerSelectionTraceTests : BrushStateIsolated, IDisposable
{
    private readonly string? _previous = DiagnosticLog.DirectoryOverride;
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "lb-seltrace-" + Guid.NewGuid().ToString("N"));

    public LayerSelectionTraceTests()
    {
        DiagnosticLog.DirectoryOverride = _scratch;
        InputTrace.ResetForTests();
    }

    public new void Dispose()
    {
        InputTrace.ResetForTests();
        DiagnosticLog.DirectoryOverride = _previous;
        try { Directory.Delete(_scratch, recursive: true); } catch { }
        base.Dispose();
    }

    private string Log() =>
        File.Exists(Path.Combine(_scratch, "diagnostics.log"))
            ? File.ReadAllText(Path.Combine(_scratch, "diagnostics.log"))
            : "";

    private static MainViewModel ThreeLayers()
    {
        var vm = VmLayers.PaperVm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        vm.Doc.Scene.Layers[1].Name = "secret-name";
        return vm;
    }

    [AvaloniaFact]
    public void ArmedItRecordsEverySelectionChangeAndWhoAskedForIt()
    {
        var vm = ThreeLayers();
        InputTrace.Arm();

        vm.SelectLayer(vm.LayerRows.First(r => r.SceneIndex == 1), toggle: true, range: false);
        vm.ActiveLayerIndex = 2; // a reset to one layer, not through the docker

        var log = Log();
        Assert.Contains("[layer-selection]", log);
        Assert.Contains("selection now [1,", log);
        Assert.Contains("SyncLayerSelectionToActive", log);   // the reset names its path
        Assert.DoesNotContain("secret-name", log);
    }

    [AvaloniaFact]
    public void DisarmedItWritesNothing()
    {
        var vm = ThreeLayers();
        vm.SelectLayer(vm.LayerRows.First(r => r.SceneIndex == 1), toggle: true, range: false);
        Assert.DoesNotContain("[layer-selection]", Log());
    }

    /// <summary>
    /// B395: the trace names its callers whether or not the JIT inlined the
    /// method between them and the trace.
    /// </summary>
    /// <remarks>
    /// The first version skipped a fixed two frames, assuming its immediate
    /// caller was a frame of its own. Inlined, the second skip was the method
    /// it meant to name, and ArmedItRecordsEverySelectionChangeAndWhoAskedForIt
    /// failed on whichever run had warmed the JIT enough. Here the inlining is
    /// forced rather than waited for: the caller is optimised from its first
    /// call and the helper between asks to be inlined.
    /// </remarks>
    [Fact]
    public void ACallerIsNamedEvenWhenTheMethodBetweenIsInlined()
    {
        var via = NamedCaller();
        Assert.Contains(nameof(NamedCaller), via);
        Assert.DoesNotContain("LayerSelectionTrace.Callers", via);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
        | System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static string NamedCaller() => InlinedBetween();

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static string InlinedBetween() => Services.LayerSelectionTrace.Callers();
}
