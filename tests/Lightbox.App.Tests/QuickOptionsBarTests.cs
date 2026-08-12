using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The Quick options bar: the strip above the canvas, after Q70 renamed it and
/// settled what it is for.
/// </summary>
/// <remarks>
/// <para>
/// The owner's answer gave the bar three load-bearing properties, and each
/// test here guards one of them. Size and opacity are <em>pinned</em> — outside
/// the overflow, so they are the last thing the bar ever folds into its ▾ menu,
/// and present exactly once, because the old bar carried a per-tool copy for
/// the brush and another for the eraser and stage 2's drag-and-drop cannot
/// rearrange a control that exists three times. The transform session's
/// controls live on a page in the Tool options docker, not on the bar — and
/// because Ctrl+T can arrive with that docker closed, <c>BeginTransform</c>
/// must open it or the session has no Apply/Cancel anywhere on screen.
/// </para>
/// <para>
/// These read the XAML as text, the way <c>IconSetTests</c> does: what is
/// being asserted is where controls are declared, which no rendered-tree test
/// can distinguish once the overflow bar has re-parented its children.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class QuickOptionsBarTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string MainWindowXaml() => File.ReadAllText(
        Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views", "MainWindow.axaml"));

    /// <summary>The bar's fixed section, from its name to the overflow's open tag.</summary>
    private static string FixedSection(string xaml)
    {
        var start = xaml.IndexOf("x:Name=\"QuickFixedOptions\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"QuickToolOptions\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the fixed section is gone");
        Assert.True(end > start, "the fixed section is inside or after the overflow, not before it");
        return xaml[start..end];
    }

    /// <summary>The overflow half of the bar — everything that may fold into ▾.</summary>
    private static string OverflowSection(string xaml)
    {
        var start = xaml.IndexOf("x:Name=\"QuickToolOptions\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the overflow bar is gone");
        var end = xaml.IndexOf("</controls:OverflowBar>", start, StringComparison.Ordinal);
        Assert.True(end > start, "could not find the end of the overflow bar");
        return xaml[start..end];
    }

    // ---- size and opacity: pinned, and only there --------------------------------------

    [Fact]
    public void SizeAndOpacityArePinnedOutsideTheOverflow()
    {
        var fixedSection = FixedSection(MainWindowXaml());

        Assert.Contains("{Binding BrushSize}", fixedSection);
        Assert.Contains("{Binding BrushOpacity}", fixedSection);
    }

    /// <summary>
    /// No per-tool copy of the pinned pair survives in the overflow.
    /// </summary>
    /// <remarks>
    /// The old bar had one Size slider for the brush and another for the
    /// eraser, both bound to the same property. With the pair pinned, a copy
    /// in a tool section is a second place the same value edits — and a
    /// control stage 2's customisation would count twice.
    /// </remarks>
    [Fact]
    public void TheOverflowCarriesNoSecondCopyOfSizeOrOpacity()
    {
        var overflow = OverflowSection(MainWindowXaml());

        var copies = Regex.Matches(overflow, @"\{Binding (BrushSize|BrushOpacity)\}")
            .Select(m => m.Groups[1].Value)
            .ToList();

        output.WriteLine(copies.Count == 0
            ? "overflow has no copy of the pinned pair"
            : $"still duplicated: {string.Join(", ", copies)}");
        Assert.Empty(copies);
    }

    /// <summary>Pinned means always present: disabled without a mark to size, never hidden.</summary>
    [Fact]
    public void TheFixedSectionDisablesRatherThanHides()
    {
        var fixedSection = FixedSection(MainWindowXaml());

        Assert.Contains("IsEnabled=\"{Binding MakesSizedMarks}\"", fixedSection);
        Assert.DoesNotContain("IsVisible=", fixedSection);
    }

    [AvaloniaFact]
    public void TheShapeToolGetsThePinnedPairTooBecauseAShapeIsAStroke()
    {
        var vm = VmLayers.BareVm();

        var covered = new[] { ToolId.Brush, ToolId.Eraser, ToolId.Shape };
        foreach (var tool in Enum.GetValues<ToolId>())
        {
            vm.ActiveTool = tool;
            Assert.Equal(covered.Contains(tool), vm.MakesSizedMarks);
        }
    }

    // ---- the tool announces itself ------------------------------------------------------

    [AvaloniaFact]
    public void TheBarWearsTheActiveToolsIcon_ForEveryTool()
    {
        var vm = VmLayers.BareVm();
        var missing = new List<string>();

        foreach (var tool in Enum.GetValues<ToolId>())
        {
            vm.ActiveTool = tool;
            if (vm.ActiveToolIcon is null) missing.Add(tool.ToString());
        }

        output.WriteLine(missing.Count == 0
            ? "every tool resolves a bar icon"
            : $"no icon for: {string.Join(", ", missing)}");
        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void SwitchingToolsAnnouncesTheIconChange()
    {
        var vm = VmLayers.BareVm();
        var announced = new List<string?>();
        vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        vm.ActiveTool = ToolId.Fill;

        // Without this, the bar keeps drawing the previous tool's icon —
        // exactly the silent staleness that cost the shape options group.
        Assert.Contains(nameof(MainViewModel.ActiveToolIcon), announced);
    }

    // ---- the transform session lives in the docker --------------------------------------

    [Fact]
    public void TheTransformControlsAreOffTheBarAndOnADockerPage()
    {
        var xaml = MainWindowXaml();
        var overflow = OverflowSection(xaml);

        // Off the bar entirely — session controls are not quick options.
        Assert.DoesNotContain("OnTransformApply", overflow);
        Assert.DoesNotContain("TransformScopeChoices", overflow);

        // And on the docker page, whole: a session you cannot Apply, Cancel,
        // scope or mirror from where its controls now live is a regression
        // this test exists to catch handler by handler.
        var page = xaml.IndexOf("x:Name=\"TransformOptionsPage\"", StringComparison.Ordinal);
        var docker = xaml.IndexOf("x:Name=\"ToolOptionsDocker\"", StringComparison.Ordinal);
        Assert.True(docker > 0 && page > docker, "the transform page is not inside the tool options docker");

        var end = xaml.IndexOf("</controls:Docker>", page, StringComparison.Ordinal);
        var pageXaml = xaml[page..end];
        foreach (var needed in new[]
                 {
                     "{Binding TransformActive}", "TransformScopeChoices", "TransformSamplingChoices",
                     "OnTransformPerspectiveToggled", "OnTransformMirrorH", "OnTransformMirrorV",
                     "OnTransformReset", "OnTransformApply", "OnTransformCancel",
                 })
        {
            Assert.Contains(needed, pageXaml);
        }
    }

    [AvaloniaFact]
    public void BeginningATransformOpensTheToolOptionsDocker()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BeginStroke(200, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();

        // Ctrl+T with the docker closed: the session's Apply/Cancel live on
        // its page now, so the docker opening is part of the gesture.
        vm.Workspace.SetVisible(Docking.DockPanelId.ToolOptions, false);
        Assert.True(vm.BeginTransform());

        Assert.True(vm.Workspace.ToolOptionsDockerVisible);
        vm.CancelTransform();
    }
}
