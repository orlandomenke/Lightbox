using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The brush parameter pages size to what is on them (B16).
/// </summary>
/// <remarks>
/// <para>
/// The bug was filed against a flyout whose grid declared <c>Height="430"</c>
/// for five pages of different lengths, so the short ones carried dead space and
/// the long ones scrolled. That flyout no longer exists: the owner moved the
/// parameters into the Tool options docker — "a tool options docker instead of
/// keeping it in the rail" — and a docker takes the height the artist drags it
/// to, which is a better answer than the <c>MaxHeight</c> the entry proposed.
/// </para>
/// <para>
/// What survives the redesign is the rule underneath it, and that is what these
/// guard: <b>no page and nothing hosting one is pinned to a constant.</b> A
/// fixed height reintroduced anywhere on this path brings the reported symptom
/// back whatever the container is, which is why this is asserted on the pages
/// rather than on the flyout that used to hold them.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class ToolOptionsHeightTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static readonly string[] PageNames =
    [
        "BrushPageGeneral", "BrushPageEffects", "BrushPageMedium",
        "BrushPagePressure", "BrushPagePresets",
    ];

    private static (MainWindow Window, MainViewModel Vm) OpenWithToolOptions()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        vm.ActiveTool = ToolId.Brush;
        vm.OpenToolOptionsCommand.Execute(null);
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 6; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Control Page(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);

    [AvaloniaFact]
    public void NoBrushParameterPageIsPinnedToAHeight()
    {
        var (window, _) = OpenWithToolOptions();

        foreach (var name in PageNames)
        {
            var page = Page(window, name);
            Assert.True(double.IsNaN(page.Height),
                $"{name} declares Height={page.Height} — the five pages are different "
                + "lengths, so a constant gives the short ones dead space and makes the "
                + "long ones scroll, which is B16");
            Assert.True(double.IsNaN(page.MinHeight) || page.MinHeight == 0,
                $"{name} declares MinHeight={page.MinHeight}, which pins it the same way");
        }
    }

    [AvaloniaFact]
    public void NothingHostingThePagesIsPinnedEither()
    {
        // A page free to grow inside a host pinned to 430 is still pinned. Walk
        // from the page up to the docker and check every step.
        var (window, _) = OpenWithToolOptions();
        var docker = window.GetVisualDescendants().OfType<Lightbox.App.Controls.Docker>()
            .First(d => d.PanelId == Docking.DockPanelId.ToolOptions);

        for (Visual? v = Page(window, "BrushPageGeneral");
             v is not null && !ReferenceEquals(v, docker);
             v = v.GetVisualParent())
        {
            if (v is Control c && !double.IsNaN(c.Height))
            {
                Assert.Fail($"{c.GetType().Name} '{c.Name}' between the page and the "
                    + $"docker declares Height={c.Height} — that is the shape of B16");
            }
        }
    }

    /// <summary>
    /// And the positive half: the pages really are different lengths, so "not
    /// pinned" is a claim about something rather than a check that passes because
    /// every page happens to be empty.
    /// </summary>
    [AvaloniaFact]
    public void ThePagesAreGenuinelyDifferentLengths()
    {
        var (window, _) = OpenWithToolOptions();
        var list = window.GetVisualDescendants().OfType<ListBox>()
            .First(l => l.Name == "BrushCategoryList");

        var heights = new List<(string Name, double H)>();
        for (var i = 0; i < PageNames.Length; i++)
        {
            list.SelectedIndex = i;
            Pump();
            var page = Page(window, PageNames[i]);
            page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            heights.Add((PageNames[i], page.DesiredSize.Height));
        }

        foreach (var (name, h) in heights) output.WriteLine($"{name}: {h:F0}");

        Assert.All(heights, p => Assert.True(p.H > 0, $"{p.Name} measured as empty"));
        Assert.True(heights.Select(p => Math.Round(p.H)).Distinct().Count() > 1,
            "every page measured the same height, so this is not testing what it claims");
    }
}
