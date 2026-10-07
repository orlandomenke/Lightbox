using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Lightbox.App.Controls;
using Lightbox.App.Services;
using Lightbox.Gallery.Stories;

namespace Lightbox.Gallery;

/// <summary>
/// The gallery: the stories down the left, the chosen one on the right, and the
/// look's three switches — theme, corners, effects — plus the interface scale,
/// across the top.
/// </summary>
/// <remarks>
/// Changing a look rebuilds the whole window rather than restyling it in place.
/// The app's colours are StaticResources, resolved when a control loads, so a
/// control that outlived the reload would keep the old look; building fresh is
/// what makes the gallery honest about what the app would draw.
/// </remarks>
public sealed class GalleryWindow : Window
{
    private int _story;

    public GalleryWindow()
    {
        Title = "Lightbox gallery";
        Width = 1280;
        Height = 860;
        Rebuild();
        WatchLooks();
    }

    /// <summary>
    /// Re-apply the look whenever a file in Looks/ is saved. Debounced, because
    /// an editor's save is several file events, and a half-written file read
    /// on the first one would flash an error for nothing.
    /// </summary>
    private void WatchLooks()
    {
        if (Looks.SourceDir is not { } dir) return;
        var reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        reload.Tick += (_, _) =>
        {
            reload.Stop();
            Use(App.Look);
        };
        void Changed() => Dispatcher.UIThread.Post(() => { reload.Stop(); reload.Start(); });
        var watcher = new FileSystemWatcher(dir, "*.axaml") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        watcher.Changed += (_, _) => Changed();
        watcher.Created += (_, _) => Changed();
        watcher.Renamed += (_, _) => Changed();
        watcher.EnableRaisingEvents = true;
        Closed += (_, _) => watcher.Dispose();
    }

    private GalleryApp App => (GalleryApp)Application.Current!;

    private void Rebuild()
    {
        var look = App.Look;
        Background = (Avalonia.Media.IBrush?)Application.Current!.FindResource("BackgroundSecondaryBrush");

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(12, 8) };
        bar.Children.Add(Choice("Theme", Enum.GetValues<LookTheme>(), look.Theme, v => Use(look with { Theme = v })));
        bar.Children.Add(Choice("Corners", Enum.GetValues<LookCorners>(), look.Corners, v => Use(look with { Corners = v })));
        var fx = new CheckBox { Content = "Light effects", IsChecked = look.Effects, VerticalAlignment = VerticalAlignment.Center };
        fx.IsCheckedChanged += (_, _) => Use(look with { Effects = fx.IsChecked == true });
        bar.Children.Add(fx);
        var scale = new Slider { Minimum = 75, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true, Width = 140, Value = UiScale.Current * 100, VerticalAlignment = VerticalAlignment.Center };
        scale.ValueChanged += (_, e) => UiScale.Current = e.NewValue / 100;
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "UI scale", VerticalAlignment = VerticalAlignment.Center }, scale } });

        var list = new ListBox { ItemsSource = Catalog.All.Select(s => s.Name).ToList(), SelectedIndex = _story, Width = 200 };
        var stage = new ScrollViewer
        {
            Padding = new Thickness(24),
            // The stage follows the interface scale, so a look can be judged
            // at the size somebody will actually use it.
            Content = new ScaledChrome { Child = StoryPanel.Build(Catalog.All[_story]), HorizontalAlignment = HorizontalAlignment.Left },
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            _story = list.SelectedIndex;
            stage.Content = new ScaledChrome { Child = StoryPanel.Build(Catalog.All[_story]), HorizontalAlignment = HorizontalAlignment.Left };
        };

        var status = new TextBlock
        {
            Margin = new Thickness(12, 0, 12, 6),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Classes = { "storyNote" },
            Text = Looks.Errors.Count > 0
                ? "Could not load: " + string.Join("  ·  ", Looks.Errors)
                : Looks.SourceDir is { } live ? $"Live: saving a file in {live} updates this window." : "Built looks (not live).",
        };
        if (Looks.Errors.Count > 0) status.Foreground = Avalonia.Media.Brushes.OrangeRed;
        DockPanel.SetDock(status, Dock.Top);

        var body = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(list, Dock.Left);
        body.Children.Add(bar);
        body.Children.Add(status);
        body.Children.Add(list);
        body.Children.Add(stage);
        Content = body;
    }

    private void Use(Look look)
    {
        App.Use(look);
        Rebuild();
    }

    private static Control Choice<T>(string label, T[] values, T current, Action<T> picked) where T : struct, Enum
    {
        var combo = new ComboBox { ItemsSource = values, SelectedItem = current, Width = 130 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is T v && !v.Equals(current)) picked(v);
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, combo },
        };
    }
}
