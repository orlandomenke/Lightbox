using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Lightbox.App;
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

    // ---- the three surfaces that arrived with the crop (Q128) ------------------

    private static MenuItem TopLevel(MainWindow window, string header) =>
        window.GetLogicalDescendants().OfType<Menu>().First()
            .Items.OfType<MenuItem>().First(m => m.Header as string == header);

    /// <summary>
    /// Undo and redo are the first two things in the Edit menu.
    /// </summary>
    /// <remarks>
    /// Position is the assertion, not merely presence. The menu is where
    /// somebody goes when the key did not do what they expected, so an undo
    /// entry buried under the brush library is an entry nobody finds in the
    /// moment they need it — and every application an artist arrives from puts
    /// these two at the top.
    /// </remarks>
    [AvaloniaFact]
    public void UndoAndRedoAreTheFirstTwoThingsInTheEditMenu()
    {
        var window = new MainWindow();
        var items = TopLevel(window, "_Edit").Items.OfType<MenuItem>().ToList();

        output.WriteLine(string.Join("\n", items.Take(3).Select(i => i.Header)));
        Assert.StartsWith("_Undo", (string)items[0].Header!);
        Assert.StartsWith("_Redo", (string)items[1].Header!);
        Assert.NotNull(items[0].Command);
        Assert.NotNull(items[1].Command);
        // Greyed on a document with no history rather than hidden: an entry
        // that vanishes reads as the feature being gone.
        Assert.False(items[0].IsEnabled);
        Assert.False(items[1].IsEnabled);
    }

    /// <summary>
    /// The Select menu exists, sits between Image and View, and every entry in
    /// it reaches a command.
    /// </summary>
    /// <remarks>
    /// <b>A null <c>Command</c> is the failure this is really for.</b> A
    /// mistyped binding path in XAML is not a build error and not a crash — the
    /// item simply renders, greys itself out, and does nothing, which reads as
    /// the feature being broken rather than as one typo.
    /// </remarks>
    [AvaloniaFact]
    public void TheSelectMenuIsBetweenImageAndViewAndEveryEntryIsWired()
    {
        var window = new MainWindow();
        var top = window.GetLogicalDescendants().OfType<Menu>().First()
            .Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();

        output.WriteLine(string.Join(" | ", top));
        Assert.Equal(top.IndexOf("_Image") + 1, top.IndexOf("_Select"));
        Assert.Equal(top.IndexOf("_Select") + 1, top.IndexOf("_View"));

        var items = TopLevel(window, "_Select").Items.OfType<MenuItem>().ToList();
        Assert.Equal(7, items.Count);
        foreach (var item in items)
        {
            output.WriteLine($"{item.Header}: command={item.Command is not null}, enabled={item.IsEnabled}");
            Assert.True(item.Command is not null, $"{item.Header} reaches no command");
        }

        // Deselect and Invert stay live with nothing selected, on purpose:
        // Deselect also clears the objects the arrows pick (B168), and
        // inverting nothing is how you select everything.
        Assert.True(items.Single(i => (string)i.Header! == "_Deselect").IsEnabled);
        Assert.True(items.Single(i => (string)i.Header! == "_Invert").IsEnabled);
        // Grow needs a region to grow.
        Assert.False(items.Single(i => (string)i.Header! == "_Grow").IsEnabled);
    }

    /// <summary>Both crops are on the Image menu, beneath the two resizes.</summary>
    [AvaloniaFact]
    public void TheImageMenuCarriesBothCrops()
    {
        var window = new MainWindow();
        var items = TopLevel(window, "_Image").Items.OfType<MenuItem>()
            .Select(m => m.Header as string).ToList();

        output.WriteLine(string.Join(" | ", items));
        Assert.Equal(
            ["Resize _canvas...", "Resize _image...", "Crop to _selection", "_Trim to drawing"],
            items);
    }

    /// <summary>
    /// The undo-history panel is called that everywhere it is named, and its
    /// rows are as dense as every other list in the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three places name it and all three have to agree: the docker's own
    /// title, the View menu entry that opens it, and
    /// <c>DockPanels</c> — which is not decoration, because
    /// <c>ShortcutDefinition.ScopeName</c> reads it to group panel-scoped
    /// bindings in the Configure window. A rename that missed the registry
    /// would leave the shortcut editor filing them under a panel nobody can
    /// find.
    /// </para>
    /// <para>
    /// The row height is asserted because it is a default rather than a
    /// decision: a <c>ListBoxItem</c> takes the theme's padding unless
    /// something says otherwise, and this list is scanned rather than read, so
    /// how many rows fit is the whole usefulness of the panel.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheUndoHistoryPanelIsNamedThatEverywhereAndItsRowsAreDense()
    {
        Assert.Equal("Undo history", Docking.DockPanels.TitleOf(Docking.DockPanelId.History));

        var window = new MainWindow();
        var docker = window.GetLogicalDescendants().OfType<Controls.Docker>()
            .First(d => d.PanelId == Docking.DockPanelId.History);
        output.WriteLine($"docker title: {docker.Title}");
        Assert.Equal("Undo history", docker.Title);

        var list = docker.GetLogicalDescendants().OfType<ListBox>().First();
        var height = list.Styles.OfType<Style>()
            .SelectMany(style => style.Setters.OfType<Setter>())
            .Where(setter => setter.Property == Layoutable.MinHeightProperty)
            .Select(setter => (double)setter.Value!)
            .Single();
        output.WriteLine($"row MinHeight: {height}");
        // A layer row is 22-ish with its padding; the theme's untouched
        // ListBoxItem is half again as tall as that.
        Assert.True(height <= 22, $"history rows are {height}px, which is taller than a layer row");
    }
}
