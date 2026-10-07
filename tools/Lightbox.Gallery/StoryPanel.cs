using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Lightbox.Gallery.Stories;

namespace Lightbox.Gallery;

/// <summary>
/// One story laid out: its title and note, then every state on the stage with
/// its name under it. The window and the snapshot run both draw this, so a
/// snapshot is exactly what the gallery shows.
/// </summary>
public static class StoryPanel
{
    public static Control Build(Story story)
    {
        var grid = new UniformGrid { Columns = Math.Min(story.Columns, story.States.Count) };
        foreach (var state in story.States)
        {
            Control built;
            try
            {
                built = state.Build();
            }
            catch (Exception e)
            {
                // A story that cannot build says so where it would have been,
                // rather than taking the whole gallery down with it.
                built = new TextBlock { Text = $"could not build: {e.GetType().Name}: {e.Message}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "storyNote" } };
            }
            built.HorizontalAlignment = HorizontalAlignment.Center;
            built.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(new StackPanel
            {
                Margin = new Thickness(12),
                Spacing = 8,
                Children =
                {
                    new Border { MinHeight = 32, Child = built },
                    new TextBlock { Text = state.Name, Classes = { "stateLabel" }, HorizontalAlignment = HorizontalAlignment.Center },
                },
            });
        }

        return new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = story.Name, Classes = { "storyTitle" } },
                        new TextBlock { Text = story.Note, Classes = { "storyNote" }, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left },
                    },
                },
                new Border { Classes = { "stage" }, Child = grid },
            },
        };
    }
}
