using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Two rules of the design system, measured on the dialogs that were written
/// before anything measured them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A dialog's two buttons are the same size, and dismissing is the quiet
/// one.</b> Left to their labels, "Cancel" and "Export…" are different widths
/// in the most looked-at row of the window.
/// </para>
/// <para>
/// <b>A row that is hidden leaves no gap behind.</b> A grid with row spacing
/// keeps the spacing of a row whose contents are hidden, so the gap where
/// Quality would be on a PNG is twice every other gap in the dialog. Both were
/// found by the UI review of <c>ImageSequenceWindow</c> and fixed there; these
/// are the three older windows with the same two faults.
/// </para>
/// <para>
/// Measured rather than asserted on the XAML, because both faults are in what
/// the layout does with markup that reads as correct.
/// </para>
/// </remarks>
[Collection("ExportPresetFile")]
public class DialogGeometryTests(Xunit.ITestOutputHelper output)
{
    private static T Laid<T>(T window) where T : Window
    {
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static double Top(Control control, Window window) =>
        control.TranslatePoint(new Point(0, 0), window)!.Value.Y;

    private static double Bottom(Control control, Window window) =>
        Top(control, window) + control.Bounds.Height;

    private void AssertAPair(Window window, string dismiss, string act)
    {
        var quiet = window.FindControl<Button>(dismiss)!;
        var primary = window.FindControl<Button>(act)!;

        output.WriteLine(
            $"{window.GetType().Name}: {dismiss} {quiet.Bounds.Width} wide, {act} {primary.Bounds.Width} wide");
        Assert.True(quiet.Bounds.Width > 0);
        Assert.Equal(quiet.Bounds.Width, primary.Bounds.Width);
        Assert.Contains("tertiary", quiet.Classes);
        Assert.Contains("primary", primary.Classes);
    }

    // ---- the two buttons -------------------------------------------------------

    [AvaloniaFact]
    public void SaveAsImagesTwoButtonsAreAPair() =>
        AssertAPair(Laid(new SaveImageDialog(new Scene { Width = 200, Height = 100 })), "CancelButton", "SaveButton");

    [AvaloniaFact]
    public void TheEngineExportsTwoButtonsAreAPair() =>
        AssertAPair(Laid(new ExportWindow()), "CancelButton", "ExportButton");

    [AvaloniaFact]
    public void TheVideoExportsTwoButtonsAreAPair() =>
        AssertAPair(Laid(new VideoExportWindow()), "CloseButton", "ExportButton");

    // ---- a hidden row ----------------------------------------------------------

    [AvaloniaFact]
    public void SaveAsImageLeavesNoGapWhereQualityWouldBe()
    {
        var dialog = Laid(new SaveImageDialog(new Scene { Width = 200, Height = 100 }));
        var format = dialog.FindControl<ComboBox>("FormatBox")!;
        var quality = dialog.FindControl<NumericUpDown>("QualityBox")!;
        var size = dialog.FindControl<NumericUpDown>("SizeBox")!;

        // JPEG shows all three rows: this is what one gap looks like.
        dialog.Choice.Format = ImageSaveFormat.Jpeg;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var ordinary = Top(quality, dialog) - Bottom(format, dialog);

        // PNG hides Quality, in the middle.
        dialog.Choice.Format = ImageSaveFormat.Png;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var acrossHidden = Top(size, dialog) - Bottom(format, dialog);

        output.WriteLine($"one gap is {ordinary}; across the hidden Quality row it is {acrossHidden}");
        Assert.True(ordinary > 0);
        Assert.Equal(ordinary, acrossHidden, 1);
    }

    [AvaloniaFact]
    public void TheEngineExportLeavesNoGapWhereAStripsLayoutRowsWouldBe()
    {
        // A GameMaker strip decides packing and padding for itself, so those
        // two rows go away — from the middle, between Trim and Background.
        var window = new ExportWindow();
        window.ShowForTests(new ExportPreset { Name = "sheet", Target = ExportTarget.SpriteSheet });
        Laid(window);
        var target = window.FindControl<ComboBox>("TargetBox")!;
        var trim = window.FindControl<ComboBox>("TrimBox")!;
        var background = window.FindControl<ComboBox>("BackgroundBox")!;
        var ordinary = Top(trim, window) - Bottom(target, window);

        window.ShowForTests(new ExportPreset { Name = "strip", Target = ExportTarget.GameMaker });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<ComboBox>("PackBox")!.IsEffectivelyVisible);
        var acrossHidden = Top(background, window) - Bottom(trim, window);

        output.WriteLine($"one gap is {ordinary}; across the two hidden rows it is {acrossHidden}");
        Assert.True(ordinary > 0);
        Assert.Equal(ordinary, acrossHidden, 1);
    }

    [AvaloniaFact]
    public void TheEngineExportLeavesNoTailWhereAllTheSheetRowsWouldBe()
    {
        // A PNG sequence hides every row under Format. Four hidden rows must
        // not leave four gaps under the one that is left.
        var window = new ExportWindow();
        window.ShowForTests(new ExportPreset { Name = "sheet", Target = ExportTarget.SpriteSheet });
        Laid(window);
        var target = window.FindControl<ComboBox>("TargetBox")!;
        var rows = window.FindControl<Control>("SheetRows")!;

        window.ShowForTests(new ExportPreset { Name = "seq", Target = ExportTarget.PngSequence });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tail = Bottom(rows, window) - Bottom(target, window);
        output.WriteLine($"space left under Format inside its group: {tail}");
        Assert.Equal(0, tail, 1);
    }
}
