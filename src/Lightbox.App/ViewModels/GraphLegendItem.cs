using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One row of the graph editor's legend: the series' name in its colour, and
/// whether it is drawn. Stable identity per name — the projection rebuilds
/// its lists constantly, and a toggle must not be swapped out mid-click.
/// </summary>
public sealed partial class GraphLegendItem(
    MainViewModel owner, string name, IBrush colour, bool shown) : ObservableObject
{
    public string Name { get; } = name;

    public IBrush Colour { get; } = colour;

    private bool _shown = shown;

    public bool IsShown
    {
        get => _shown;
        set
        {
            if (_shown == value) return;
            _shown = value;
            OnPropertyChanged();
            owner.SetGraphSeriesShown(Name, value);
        }
    }
}
