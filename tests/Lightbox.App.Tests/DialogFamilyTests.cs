using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lightbox.App.Services;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The rules every dialog shares, asked of every dialog (B411, and the
/// owner's three answers of 2026-10-08).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DialogGeometryTests"/> measured three windows; the review that
/// followed found the same faults in six more and found that the dialog cited
/// as the pattern passed only because its labels happened to be short. So the
/// rules are asked of the whole family by one theory, and a dialog added later
/// joins it by being added to <see cref="Dialogs"/>.
/// </para>
/// <list type="bullet">
/// <item>The buttons at the foot are one width — the widest label's.</item>
/// <item>Dismissing is on the left and quiet; the action is on the right and
/// is the only primary. <b>Cancel left, action right</b>, decided 2026-10-08:
/// it is what most of the dialogs already did.</item>
/// <item>A form's fields start at one distance from the edge in every dialog,
/// which is the label token, now 120.</item>
/// <item>A notice is tinted by what it is — amber for a warning, the info
/// colour for information — and is not the dialog's own surface, on which a
/// "card" is invisible.</item>
/// </list>
/// </remarks>
[Collection("ExportPresetFile")]
public class DialogFamilyTests(Xunit.ITestOutputHelper output)
{
    /// <summary>Every dialog with a row of buttons at its foot, by name.</summary>
    public static TheoryData<string> Dialogs =>
    [
        nameof(NewDocumentDialog), nameof(NewProjectDialog), nameof(PlacementChoiceDialog),
        nameof(RecoveryDialog), nameof(SaveFirstDialog), nameof(ResizeDialog),
        nameof(UpdateFromTemplateWindow), nameof(VersionHistoryWindow),
        nameof(SaveImageDialog), nameof(ImageSequenceWindow), nameof(ExportWindow), nameof(VideoExportWindow),
    ];

    private static Window Make(string name) => name switch
    {
        nameof(NewDocumentDialog) => new NewDocumentDialog(),
        nameof(NewProjectDialog) => new NewProjectDialog(),
        nameof(PlacementChoiceDialog) => new PlacementChoiceDialog(),
        nameof(RecoveryDialog) => new RecoveryDialog(),
        nameof(SaveFirstDialog) => new SaveFirstDialog(),
        nameof(ResizeDialog) => new ResizeDialog(),
        nameof(UpdateFromTemplateWindow) => new UpdateFromTemplateWindow(),
        nameof(VersionHistoryWindow) => new VersionHistoryWindow(),
        nameof(SaveImageDialog) => new SaveImageDialog(),
        nameof(ImageSequenceWindow) => new ImageSequenceWindow(),
        nameof(ExportWindow) => new ExportWindow(),
        nameof(VideoExportWindow) => new VideoExportWindow(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not a dialog this test knows"),
    };

    private static Window Laid(Window window)
    {
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static double Left(Control control, Window window) =>
        control.TranslatePoint(new Point(0, 0), window)!.Value.X;

    /// <summary>The buttons at the foot of the dialog, left to right.</summary>
    private static List<Button> Foot(Window window)
    {
        var row = window.FindControl<UniformGrid>("ButtonRow");
        Assert.True(row is not null, $"{window.GetType().Name} has no ButtonRow");
        return [.. row!.Children.OfType<Button>().Where(b => b.IsVisible).OrderBy(b => Left(b, window))];
    }

    // ---- the buttons -----------------------------------------------------------

    [AvaloniaTheory]
    [MemberData(nameof(Dialogs))]
    public void TheButtonsAtTheFootAreOneWidth(string dialog)
    {
        var window = Laid(Make(dialog));

        var buttons = Foot(window);

        output.WriteLine($"{dialog}: " + string.Join(", ", buttons.Select(b => $"{b.Content} {b.Bounds.Width}")));
        Assert.True(buttons.Count >= 2, "a row of one button is not a pair");
        Assert.All(buttons, b => Assert.True(b.Bounds.Width > 0));
        Assert.Single(buttons.Select(b => b.Bounds.Width).Distinct());
    }

    [AvaloniaTheory]
    [MemberData(nameof(Dialogs))]
    public void DismissingIsOnTheLeftAndTheActionOnTheRight(string dialog)
    {
        var window = Laid(Make(dialog));

        var buttons = Foot(window);

        output.WriteLine($"{dialog}: " + string.Join(" | ", buttons.Select(
            b => $"{b.Content} [{string.Join(' ', b.Classes.Where(c => !c.StartsWith(':')))}]")));
        // One primary at most, and when there is one it is the last.
        var primaries = buttons.Where(b => b.Classes.Contains("primary")).ToList();
        Assert.True(primaries.Count <= 1, "two primaries is no primary");
        if (primaries.Count == 1) Assert.Same(buttons[^1], primaries[0]);
        // Everything that is not the action is quiet.
        Assert.All(buttons.Where(b => !b.Classes.Contains("primary")), b => Assert.Contains("tertiary", b.Classes));
        // Escape dismisses, and the button it presses is not the action.
        var cancel = Assert.Single(buttons, b => b.IsCancel);
        Assert.DoesNotContain("primary", cancel.Classes);
    }

    [AvaloniaFact]
    public void TheRecoveryDialogsSecondRowIsAPairToo()
    {
        var window = (RecoveryDialog)Laid(new RecoveryDialog());
        var row = window.FindControl<UniformGrid>("ConfirmRow")!;
        // Shown only while confirming a delete; measured as it would be then.
        row.IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var widths = row.Children.OfType<Button>().Select(b => b.Bounds.Width).ToList();

        output.WriteLine(string.Join(", ", widths));
        Assert.Equal(2, widths.Count);
        Assert.True(widths[0] > 0);
        Assert.Equal(widths[0], widths[1]);
    }

    // ---- the label column ------------------------------------------------------

    [AvaloniaFact]
    public void AFormsFieldsStartAtTheSameDistanceInEveryDialog()
    {
        Application.Current!.TryGetResource("SizeDialogLabel", null, out var token);
        var label = Assert.IsType<double>(token);
        Assert.Equal(120, label);

        var starts = new Dictionary<string, double>();
        foreach (var (name, field) in new[]
                 {
                     (nameof(SaveImageDialog), "FormatBox"), (nameof(ImageSequenceWindow), "FormatBox"),
                     (nameof(ExportWindow), "TargetBox"), (nameof(VideoExportWindow), "FormatBox"),
                 })
        {
            var window = Laid(Make(name));
            starts[name] = Left(window.FindControl<Control>(field)!, window);
        }

        output.WriteLine(string.Join(", ", starts.Select(s => $"{s.Key} {s.Value}")));
        Assert.Single(starts.Values.Distinct());
        // Dialog padding, the label, and the gap after it.
        Assert.Equal(16 + label + 8, starts.Values.First());
    }

    [AvaloniaTheory]
    [InlineData(nameof(NewDocumentDialog))]
    [InlineData(nameof(NewProjectDialog))]
    public void TheWiderLabelColumnPushesNothingOffTheEdge(string dialog)
    {
        // These two were on a label of 110; ten more pixels of label must not
        // cost a field its right-hand end.
        var window = Laid(Make(dialog));

        var fields = window.GetVisualDescendants().OfType<Control>()
            .Where(c => c is TextBox or ComboBox or NumericUpDown && c.IsEffectivelyVisible && c.Bounds.Width > 0)
            .ToList();
        var furthest = fields.Max(f => Left(f, window) + f.Bounds.Width);

        output.WriteLine($"{dialog}: window {window.Bounds.Width} wide, furthest field edge {furthest}");
        Assert.NotEmpty(fields);
        Assert.True(furthest <= window.Bounds.Width - 15, $"{furthest} of {window.Bounds.Width}");
    }

    // ---- notices ---------------------------------------------------------------

    private static Color Ground(Border border) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;

    [AvaloniaFact]
    public void AWarningIsTintedAmberAndIsNotTheDialogsOwnSurface()
    {
        var dialog = (SaveImageDialog)Laid(new SaveImageDialog(new Scene { TransparentBackground = true }));
        dialog.Choice.Format = ImageSaveFormat.Jpeg;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var warning = dialog.FindControl<Border>("TransparencyWarning")!;

        Assert.True(warning.IsVisible);
        Assert.Contains("notice", warning.Classes);
        Assert.Contains("warning", warning.Classes);
        dialog.TryFindResource("StatusWarningSurfaceBrush", dialog.ActualThemeVariant, out var amber);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(amber).Color, Ground(warning));
        Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color, Ground(warning));
        Assert.True(warning.BorderThickness.Left > 0, "a tinted notice has a state border");
    }

    [AvaloniaFact]
    public void AnSvgNoticeIsInformationAndLooksDifferentFromAWarning()
    {
        var report = new SvgReport([new SvgLayerReport("Shading", SvgLayerForm.Pixels)], null);
        var dialog = (SaveImageDialog)Laid(new SaveImageDialog(new Scene(), () => report));
        dialog.Choice.Format = ImageSaveFormat.Svg;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var notice = dialog.FindControl<Border>("SvgNoticeCard")!;

        Assert.True(notice.IsVisible);
        Assert.Contains("notice", notice.Classes);
        Assert.Contains("info", notice.Classes);
        dialog.TryFindResource("StatusInfoSurfaceBrush", dialog.ActualThemeVariant, out var info);
        dialog.TryFindResource("StatusWarningSurfaceBrush", dialog.ActualThemeVariant, out var amber);
        Assert.NotNull(info);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(info).Color, Ground(notice));
        Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(amber).Color, Ground(notice));
    }

    [AvaloniaFact]
    public void TheSequenceExportsWarningIsTheSameWarning()
    {
        var window = (ImageSequenceWindow)Laid(new ImageSequenceWindow(new Scene { TransparentBackground = true }));
        window.Choice.Format = ImageSaveFormat.Jpeg;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var warning = window.FindControl<Border>("TransparencyWarning")!;

        Assert.True(warning.IsVisible);
        Assert.Contains("warning", warning.Classes);
    }

    [Fact]
    public void ASurveyThatFailsSaysSoWithoutQuotingTheException()
    {
        // The exception's own text is for a log, not for an artist: it can be
        // a path, a type name or a stack frame.
        var vm = new ViewModels.SaveImageDialogViewModel(
            new Scene(), () => throw new InvalidOperationException("C:\\Users\\someone\\secret.lightbox.json"))
        {
            Format = ImageSaveFormat.Svg,
        };

        Assert.Contains("could not be checked", vm.SvgNotice);
        Assert.DoesNotContain("secret", vm.SvgNotice);
    }
}
