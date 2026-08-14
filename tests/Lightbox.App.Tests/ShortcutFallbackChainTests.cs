using Avalonia.Input;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The chain a key press walks: the panel it is over, then the canvas, then the
/// general binding (Q82).
/// </summary>
/// <remarks>
/// <b>The canvas rung was a decision, and it went against the recommendation.</b>
/// It used to stop at the docker's edge, so <c>Delete</c> over the Colour docker
/// did nothing at all. The argument for stopping was that a destructive key
/// firing on the canvas from a place that gives no hint the canvas is the target
/// is a bad surprise; the argument that won is that an artist's model is "the
/// docker overrides, everything else still works", and a key that goes dead over
/// eleven of twelve dockers contradicts it. <c>QUESTIONS.md</c> → Q82 has both,
/// including the cost that was accepted.
/// </remarks>
public class ShortcutFallbackChainTests(ITestOutputHelper output)
{
    private static KeyEventArgs Key(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Key = key, KeyModifiers = modifiers };

    /// <summary>All three rungs on one gesture, which is what makes it a chain.</summary>
    [Fact]
    public void ThePanelWinsThenTheCanvasThenTheGeneralBinding()
    {
        var map = new ShortcutMap();
        var delete = Key(Avalonia.Input.Key.Delete);

        // Rung 1: the panel that claims the key.
        Assert.Equal("docker.deleteLayer", map.IdFor(delete, ShortcutScope.In(DockPanelId.Layers)));
        // Rung 2: any other panel falls through to the canvas.
        Assert.Equal("select.clear", map.IdFor(delete, ShortcutScope.In(DockPanelId.Color)));
        // And the canvas itself, unchanged.
        Assert.Equal("select.clear", map.IdFor(delete, ShortcutScope.Canvas));

        // Rung 3, on a key no panel and no canvas binding claims.
        var brush = Key(Avalonia.Input.Key.B);
        Assert.Equal("tool.brush", map.IdFor(brush, ShortcutScope.In(DockPanelId.Color)));
        Assert.Equal("tool.brush", map.IdFor(brush, ShortcutScope.Canvas));
    }

    /// <summary>
    /// A panel binding still stays in its own panel. This is the half that makes
    /// per-docker bindings worth having, and the half a wider fallback could
    /// quietly eat.
    /// </summary>
    [Theory]
    [InlineData(DockPanelId.Color)]
    [InlineData(DockPanelId.Palette)]
    [InlineData(DockPanelId.Timeline)]
    public void APanelBindingNeverLeaksIntoAnotherPanel(DockPanelId elsewhere)
    {
        var map = new ShortcutMap();

        // docker.deleteLayer is the Layers docker's and nobody else's.
        Assert.NotEqual("docker.deleteLayer", map.IdFor(Key(Avalonia.Input.Key.Delete), ShortcutScope.In(elsewhere)));
        // timeline.insertKey likewise, over everything that is not the timeline.
        if (elsewhere != DockPanelId.Timeline)
        {
            Assert.Equal("canvas.pickColor", map.IdFor(Key(Avalonia.Input.Key.I), ShortcutScope.In(elsewhere)));
        }
    }

    /// <summary>
    /// <b>Only a docker gets the canvas rung.</b> <see cref="ShortcutScope.General"/>
    /// is "nowhere in particular" — deliberately the narrowest scope there is —
    /// and letting a marquee-clearing Delete answer there would make the canvas
    /// commands reachable from a scope that exists to reach almost nothing.
    /// </summary>
    [Fact]
    public void TheGeneralScopeDoesNotPickUpTheCanvasBindings()
    {
        var map = new ShortcutMap();

        Assert.Null(map.IdFor(Key(Avalonia.Input.Key.Delete), ShortcutScope.General));
        Assert.Null(map.IdFor(Key(Avalonia.Input.Key.Left), ShortcutScope.General));
        // A general binding still answers there, or the scope would be useless.
        Assert.Equal("tool.brush", map.IdFor(Key(Avalonia.Input.Key.B), ShortcutScope.General));
    }

    /// <summary>
    /// The timeline claims the arrows for scrubbing, so the canvas nudge must
    /// not reach it — the panel rung comes first, and this is the case where
    /// both rungs have an answer and the order is the whole behaviour.
    /// </summary>
    [Fact]
    public void ThePanelRungBeatsTheCanvasRungWhenBothHaveAnAnswer()
    {
        var map = new ShortcutMap();
        var right = Key(Avalonia.Input.Key.Right);

        Assert.Equal("timeline.nextFrame", map.IdFor(right, ShortcutScope.In(DockPanelId.Timeline)));
        Assert.Equal("canvas.nudgeRight", map.IdFor(right, ShortcutScope.Canvas));
        // And over a docker that claims neither, the canvas answer.
        Assert.Equal("canvas.nudgeRight", map.IdFor(right, ShortcutScope.In(DockPanelId.Color)));
    }

    /// <summary>
    /// Every binding names the place it applies in words an artist would use —
    /// the heading the Configure window groups by. Derived from the scope, so a
    /// binding cannot be filed under a heading it does not belong to.
    /// </summary>
    [Fact]
    public void EveryBindingSaysWhereItApplies()
    {
        var map = new ShortcutMap();

        Assert.Equal("Everywhere", map.Find("tool.brush")!.ScopeName);
        Assert.Equal("Canvas", map.Find("select.clear")!.ScopeName);
        Assert.Equal("Timeline", map.Find("timeline.insertKey")!.ScopeName);
        Assert.Equal("Layers", map.Find("docker.deleteLayer")!.ScopeName);

        foreach (var d in map.Definitions)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.ScopeName), $"{d.Id} has no scope name");
            output.WriteLine($"{d.Id}: {d.ScopeName}");
        }
    }

    /// <summary>
    /// The scope name is what the editor groups by, so two scopes sharing one
    /// name would silently merge their groups — a docker's bindings filed under
    /// "Canvas", reading as if they applied there. No docker is named either of
    /// these words today; this is what makes naming one a decision rather than
    /// an accident nobody sees.
    /// </summary>
    [Fact]
    public void NoDockerIsNamedAfterOneOfTheWiderScopes()
    {
        foreach (var panel in Enum.GetValues<DockPanelId>())
        {
            var title = DockPanels.TitleOf(panel);
            Assert.False(
                title is "Canvas" or "Everywhere",
                $"the {panel} docker is titled \"{title}\", which is already a scope heading — "
                + "its bindings would be grouped as if they applied everywhere or on the canvas");
        }
    }
}
