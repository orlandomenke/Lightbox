using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The shape of the menu bar: submenus that are built on open can actually
/// open, and Configure sits where an artist arriving from any other
/// application will look for it.
/// </summary>
/// <remarks>
/// The first half guards a quiet failure mode: Open recent, New from template
/// and the project-type converter are all populated in code when their
/// submenu opens — but an Avalonia <c>MenuItem</c> with no items never opens a
/// submenu at all, so <c>SubmenuOpened</c> never fired and the flyouts simply
/// did not exist. The fix is a placeholder child in the XAML, and this is the
/// test that stops it being tidied away as dead markup.
/// </remarks>
public class MainMenuShapeTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void TheDeferredSubmenusHaveAChildSoTheyCanOpen()
    {
        var window = new MainWindow();

        foreach (var name in new[] { "RecentMenu", "ConvertProjectMenu", "TemplatesMenu" })
        {
            var item = window.GetLogicalDescendants().OfType<MenuItem>()
                .FirstOrDefault(m => m.Name == name);
            Assert.NotNull(item);
            output.WriteLine($"{name}: {item!.ItemCount} item(s)");
            // Zero items means the submenu never opens and the builder wired
            // to SubmenuOpened never runs — the flyout silently vanishes.
            Assert.True(item.ItemCount > 0, $"{name} has no children, so its submenu can never open");
        }
    }

    [AvaloniaFact]
    public void ConfigureIsTheLastThingInTheEditMenu()
    {
        var window = new MainWindow();
        var menu = window.GetLogicalDescendants().OfType<Menu>().First();
        var edit = menu.Items.OfType<MenuItem>().First(m => m.Header as string == "_Edit");

        var items = edit.Items.OfType<MenuItem>().ToList();
        Assert.NotEmpty(items);
        output.WriteLine(string.Join("\n", items.Select(i => i.Header)));
        Assert.Equal("_Configure…", items[^1].Header as string);
    }
}
