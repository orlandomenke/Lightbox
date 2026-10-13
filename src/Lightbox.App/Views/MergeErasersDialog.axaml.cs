using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.Raster;

namespace Lightbox.App.Views;

/// <summary>
/// Asks what a merge should do with drawings that have erasing in them: keep
/// their strokes, or flatten them (Q264).
/// </summary>
/// <remarks>
/// Closes with a <see cref="MergeErasersAnswer"/>, or null when cancelled —
/// which cancels the merge, since there is no answer to merge with.
/// </remarks>
public partial class MergeErasersDialog : Window
{
    public MergeErasersDialog()
    {
        AvaloniaXamlLoader.Load(this);
        Describe("the layer below");
    }

    /// <summary>Name the layer the merge lands on.</summary>
    public MergeErasersDialog Describe(string lowerName)
    {
        this.FindControl<TextBlock>("Explanation")!.Text =
            $"Some drawings on this layer have erasing in them, and merged into “{lowerName}” as they are "
            + "their erasers would rub out its marks too.";
        return this;
    }

    private void OnMerge(object? sender, RoutedEventArgs e) =>
        Close(new MergeErasersAnswer(
            this.FindControl<RadioButton>("FlattenOption")!.IsChecked == true
                ? EraserMerge.Flatten
                : EraserMerge.KeepStrokes,
            this.FindControl<CheckBox>("Remember")!.IsChecked == true));

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}

/// <summary>What the artist chose, and whether to stop asking.</summary>
public sealed record MergeErasersAnswer(EraserMerge Erasers, bool Remember);
