using System.ComponentModel;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The resize dialog's outline preview, as arithmetic: the old paper and the
/// new one, placed by the anchor, before anything is applied.
/// </summary>
/// <remarks>
/// <para>
/// The owner's report was that the anchor "does not work", and half of that
/// was that nothing in the dialog showed which way the paper would grow until
/// after the button. These pin the picture to the edit: every placement here
/// is read back in pixels and compared with what <c>CanvasResize.Apply</c>
/// does to a real scene, so a preview that drew the spare pixel on the other
/// side would be a red test rather than a one-pixel lie.
/// </para>
/// <para>
/// The rectangles are normalised so the larger side of their union is 1, and
/// <see cref="ResizeOutline.Scale"/> converts back — which is what lets these
/// assert in pixels while the control only ever sees unit space.
/// </para>
/// </remarks>
public class ResizeOutlineTests(ITestOutputHelper output)
{
    private static ResizeDialogViewModel Vm(int w = 400, int h = 300) =>
        new ResizeDialogViewModel(DocumentFactory.CreateDoc(w, h, 1).Scene, ResizeMode.Canvas).WithDefaults();

    /// <summary>A rectangle back in pixels, relative to the old paper's top-left.</summary>
    private static (double X, double Y, double W, double H) Px(ResizeOutline o, Avalonia.Rect r) =>
        ((r.X - o.Old.X) / o.Scale, (r.Y - o.Old.Y) / o.Scale, r.Width / o.Scale, r.Height / o.Scale);

    private void Print(ResizeOutline o)
    {
        output.WriteLine($"old {Px(o, o.Old)}  new {Px(o, o.New)}  kept {Px(o, o.Kept)}  extent {o.Extent}  scale {o.Scale}");
    }

    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 6);

    [Fact]
    public void AnUnchangedSizeShowsOnePaper()
    {
        var o = Vm().Outline;
        Print(o);
        Assert.Equal(o.Old, o.New);
        Assert.Equal(o.Old, o.Kept);
        Assert.False(o.Crops);
        // Fitted: the long side of the union fills the unit box.
        Near(1, Math.Max(o.Extent.Width, o.Extent.Height));
    }

    [Fact]
    public void TopLeftGrowsRightAndDown()
    {
        var vm = Vm();
        vm.Anchor = ResizeAnchor.TopLeft;
        vm.Width = 600;
        vm.Height = 400;
        var o = vm.Outline;
        Print(o);

        Assert.Equal((0, 0, 600, 400), Round(Px(o, o.New)));
        Assert.Equal((0, 0, 400, 300), Round(Px(o, o.Old)));
        Assert.False(o.Crops);
    }

    [Fact]
    public void BottomRightGrowsLeftAndUp()
    {
        var vm = Vm();
        vm.Anchor = ResizeAnchor.BottomRight;
        vm.Width = 600;
        vm.Height = 400;
        var o = vm.Outline;
        Print(o);

        Assert.Equal((-200, -100, 600, 400), Round(Px(o, o.New)));
        // The old paper's right and bottom edges are the new paper's.
        Near(o.New.Right, o.Old.Right);
        Near(o.New.Bottom, o.Old.Bottom);
    }

    [Fact]
    public void CenterSplitsTheGrowthWithTheSparePixelRightAndBottom()
    {
        var vm = Vm();
        vm.Width = 403;  // 3 more: 1 left, 2 right
        vm.Height = 305; // 5 more: 2 top, 3 bottom
        var o = vm.Outline;
        Print(o);

        var n = Px(o, o.New);
        Near(-1, n.X);
        Near(-2, n.Y);
        Near(2, (o.New.Right - o.Old.Right) / o.Scale);
        Near(3, (o.New.Bottom - o.Old.Bottom) / o.Scale);
    }

    [Theory]
    [InlineData(ResizeAnchor.TopLeft, 0, 0)]
    [InlineData(ResizeAnchor.Center, 100, 75)]
    [InlineData(ResizeAnchor.BottomRight, 200, 150)]
    public void ACropShowsThePartThatIsKept(ResizeAnchor anchor, int keptX, int keptY)
    {
        var vm = Vm();
        vm.Anchor = anchor;
        vm.Width = 200;
        vm.Height = 150;
        var o = vm.Outline;
        Print(o);

        Assert.True(o.Crops);
        Assert.Equal((keptX, keptY, 200, 150), Round(Px(o, o.Kept)));
        Assert.Equal(o.New, o.Kept);
        // The old paper is the whole union when it is cropped on both axes.
        Assert.Equal((0, 0, 400, 300), Round(Px(o, o.Old)));
    }

    [Fact]
    public void WiderAndShorterCropsOneAxisAndGrowsTheOther()
    {
        var vm = Vm();
        vm.Anchor = ResizeAnchor.Center;
        vm.Width = 500;
        vm.Height = 200;
        var o = vm.Outline;
        Print(o);

        Assert.True(o.Crops);
        Assert.Equal((0, 50, 400, 200), Round(Px(o, o.Kept)));
        Assert.Equal((-50, 50, 500, 200), Round(Px(o, o.New)));
    }

    /// <summary>
    /// The point of the whole thing: the picture and the edit cannot disagree,
    /// on every anchor, growing, cropping and odd.
    /// </summary>
    [Theory]
    [InlineData(603, 401)]
    [InlineData(197, 151)]
    [InlineData(500, 201)]
    public void ThePreviewIsWhereApplyPutsThePaper(int w, int h)
    {
        foreach (var anchor in Enum.GetValues<ResizeAnchor>())
        {
            var doc = DocumentFactory.CreateDoc(400, 300, 1);
            var vm = new ResizeDialogViewModel(doc.Scene, ResizeMode.Canvas).WithDefaults();
            vm.Anchor = anchor;
            vm.Width = w;
            vm.Height = h;
            var o = vm.Outline;
            var n = Px(o, o.New);

            Assert.True(CanvasResize.Apply(doc.Scene, w, h, anchor));
            output.WriteLine($"{anchor}: preview at ({n.X}, {n.Y}), applied at ({doc.Scene.Left}, {doc.Scene.Top})");
            Near(doc.Scene.Left, n.X);
            Near(doc.Scene.Top, n.Y);
            Near(doc.Scene.Width, n.W);
            Near(doc.Scene.Height, n.H);
        }
    }

    [Theory]
    [InlineData(nameof(ResizeDialogViewModel.Width))]
    [InlineData(nameof(ResizeDialogViewModel.Height))]
    [InlineData(nameof(ResizeDialogViewModel.Anchor))]
    public void TheOutlineFollowsEveryInput(string input)
    {
        var vm = Vm();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        switch (input)
        {
            case nameof(ResizeDialogViewModel.Width): vm.Width = 500; break;
            case nameof(ResizeDialogViewModel.Height): vm.Height = 500; break;
            default: vm.Anchor = ResizeAnchor.TopLeft; break;
        }

        Assert.Contains(nameof(ResizeDialogViewModel.Outline), raised);
    }

    /// <summary>
    /// The routes that set the fields without the artist typing in them: the
    /// swap button, Reset and the linked field. Each one has to move the
    /// picture too, or the preview shows a size the dialog no longer holds.
    /// </summary>
    [Theory]
    [InlineData("swap")]
    [InlineData("reset")]
    [InlineData("linked")]
    public void TheOutlineFollowsTheRoutesThatSetTheFieldsForYou(string route)
    {
        var vm = Vm();
        if (route == "reset") vm.Width = 600;
        if (route == "linked") vm.Linked = true;
        var before = vm.Outline;
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        switch (route)
        {
            case "swap": vm.Swap(); break;
            case "reset": vm.Reset(); break;
            default: vm.Width = 800; break;
        }

        var after = vm.Outline;
        Print(after);
        Assert.Contains(nameof(ResizeDialogViewModel.Outline), raised);
        Assert.NotEqual(before, after);
        Assert.Equal(((double)vm.Width, (double)vm.Height), (Px(after, after.New).W, Px(after, after.New).H));
    }

    [Theory]
    [InlineData(400, 0)]
    [InlineData(-5, 300)]
    [InlineData(-1, -1)]
    public void ANonSizeOnEitherAxisDrawsTheOldPaperAlone(int w, int h)
    {
        var vm = Vm();
        vm.Width = w;
        vm.Height = h;
        var o = vm.Outline;
        Assert.Equal(o.Old, o.New);
        Assert.False(o.Crops);
        Assert.True(double.IsFinite(o.Scale) && o.Scale > 0);
    }

    [Fact]
    public void AHugeSizeStillFitsTheBox()
    {
        // The field's own maximum, against a small document: the old paper is
        // a speck, but it is placed and it is inside the unit box.
        var vm = Vm(10, 10);
        vm.Anchor = ResizeAnchor.BottomRight;
        vm.Width = 30000;
        vm.Height = 30000;
        var o = vm.Outline;
        Print(o);
        Near(1, o.Extent.Width);
        Near(1, o.Extent.Height);
        Near(1, o.Old.Right);
        Near(1, o.Old.Bottom);
        Assert.True(o.Old.Width > 0);
        Assert.Equal((-29990, -29990, 30000, 30000), Round(Px(o, o.New)));
    }

    [Fact]
    public void ASizeBelowOnePixelDrawsTheOldPaperAlone()
    {
        // A field mid-edit can hold 0; the picture must not divide by it.
        var vm = Vm();
        vm.Width = 0;
        var o = vm.Outline;
        Print(o);
        Assert.Equal(o.Old, o.New);
        Assert.False(o.Crops);
    }

    private static (int, int, int, int) Round((double X, double Y, double W, double H) r) =>
        ((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.W), (int)Math.Round(r.H));
}
