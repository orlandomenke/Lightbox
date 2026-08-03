using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Update from template…</c> — shows what a pull would change, then
/// applies what the artist ticked.
/// </summary>
/// <remarks>
/// <para>
/// The dialog exists because the pull is the one part of the template design
/// that reaches into a document somebody has been working in. Everything about
/// it is therefore explicit: the artist opens it, sees the list, and chooses.
/// Nothing here happens on its own, and nothing ever travels from the document
/// back to the template.
/// </para>
/// <para>
/// It reports the counts rather than only the categories — "2 new layers" tells
/// you whether to look closer, where "New layers" does not.
/// </para>
/// </remarks>
/// <summary>One layer with work on it, and whether the artist wants it anyway.</summary>
/// <remarks>
/// Top-level rather than nested inside the window: a nested type cannot be named
/// as a template's <c>x:DataType</c>, and without that the checkbox bindings
/// resolve against nothing and fail at build time.
/// </remarks>
public sealed partial class TemplatePullRow : ObservableObject
{
    public TemplatePullRow(string layerId, string label)
    {
        LayerId = layerId;
        Label = label;
    }

    public string LayerId { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _include;
}

public partial class UpdateFromTemplateWindow : Window
{
    private readonly List<TemplatePullRow> _drawnOn = [];

    public UpdateFromTemplateWindow()
    {
        InitializeComponent();
    }

    /// <summary>The options the artist ticked, or null if they cancelled.</summary>
    public Templates.PullOptions? Result { get; private set; }

    public void Show(Templates.PullPreview preview)
    {
        if (!preview.Any)
        {
            NothingPanel.IsVisible = true;
            ChangesPanel.IsVisible = false;
            ApplyButton.IsEnabled = false;
            return;
        }

        NewLayersBox.Content = preview.NewLayers.Count == 1
            ? $"1 new layer — {preview.NewLayers[0].Layer.Name}"
            : $"{preview.NewLayers.Count} new layers";
        NewLayersBox.IsEnabled = preview.NewLayers.Count > 0;
        NewLayersBox.IsChecked = preview.NewLayers.Count > 0;

        var pullable = preview.LayerChanges.Count(c => !c.DrawnOnSince);
        LayerPropertiesBox.Content = pullable == 1
            ? "1 layer's name, blend, opacity or lock"
            : $"{pullable} layers' names, blends, opacities or locks";
        LayerPropertiesBox.IsEnabled = preview.LayerChanges.Count > 0;
        LayerPropertiesBox.IsChecked = preview.LayerChanges.Count > 0;

        GuidesBox.Content = "Guides and grids (replaced)";
        GuidesBox.IsEnabled = preview.GuidesDiffer;
        GuidesBox.IsChecked = preview.GuidesDiffer;

        // Size is deliberately not on this list: changing a canvas under
        // finished drawings is a different operation with its own questions.
        FpsBox.Content = $"Frame rate — {preview.TemplateFps} fps (canvas size is not pulled)";
        FpsBox.IsEnabled = preview.FpsDiffers;
        FpsBox.IsChecked = preview.FpsDiffers;

        CameraBox.Content = "Camera (added; an existing one is never overwritten)";
        CameraBox.IsEnabled = preview.CameraAvailable;
        CameraBox.IsChecked = preview.CameraAvailable;

        foreach (var change in preview.LayerChanges.Where(c => c.DrawnOnSince))
        {
            var what = string.Join(", ", Differences(change));
            _drawnOn.Add(new TemplatePullRow(change.LayerId, $"{change.From} → {change.To}  ({what})"));
        }
        if (_drawnOn.Count > 0)
        {
            DrawnOnPanel.IsVisible = true;
            DrawnOnList.ItemsSource = _drawnOn;
        }
    }

    private static IEnumerable<string> Differences(Templates.LayerChange change)
    {
        if (change.NameDiffers) yield return "name";
        if (change.BlendDiffers) yield return "blend";
        if (change.OpacityDiffers) yield return "opacity";
        if (change.LockDiffers) yield return "lock";
    }

    /// <summary>The drawn-on rows the dialog is showing. Test seam.</summary>
    internal IReadOnlyList<TemplatePullRow> DrawnOnForTests => _drawnOn;

    /// <summary>Press Update without a window. Test seam.</summary>
    /// <remarks>
    /// The mapping from ticks to <see cref="Templates.PullOptions"/> is the part
    /// of this window with a decision in it, and it is worth a test that does not
    /// need a real dialog to run. <see cref="Window.Close"/> on an unopened
    /// window is a no-op, so these go through the same handlers the buttons do
    /// rather than duplicating the logic.
    /// </remarks>
    internal void ApplyForTests() => OnApply(null, new RoutedEventArgs());

    /// <summary>Press Cancel without a window. Test seam.</summary>
    internal void CancelForTests() => OnCancel(null, new RoutedEventArgs());

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        Result = new Templates.PullOptions(
            NewLayers: NewLayersBox.IsChecked == true,
            LayerProperties: LayerPropertiesBox.IsChecked == true,
            Guides: GuidesBox.IsChecked == true,
            Fps: FpsBox.IsChecked == true,
            Camera: CameraBox.IsChecked == true,
            DrawnOnLayerIds: _drawnOn.Where(r => r.Include).Select(r => r.LayerId)
                .ToHashSet(StringComparer.Ordinal));
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
