using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Layer links from the docker's side: the gestures, and what they write.
/// </summary>
/// <remarks>
/// The gesture map is a pure function on purpose — synthetic input through
/// Xvfb is unreliable here, so a mapping living inside a
/// <c>PointerPressed</c> handler would ship unguarded and a dropped click
/// would look exactly like a wrong binding.
/// </remarks>
public class LayerLinkToolTests
{
    private static MainViewModel Stack()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        // Three layers to link: colour at the bottom, then lines, then FX.
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddPaintedLayerCommand.Execute(null);
        return vm;
    }

    private static Layer At(MainViewModel vm, int sceneIndex) => vm.Doc.Scene.Layers[sceneIndex];

    // ---- the gesture map ----------------------------------------------------------

    [Fact]
    public void TheModifiersMapToTheGesturesTheOwnerAskedFor()
    {
        Assert.Equal(
            LayerLinkGesture.LinkAbove,
            LayerLinkGestures.From(KeyModifiers.Control | KeyModifiers.Shift, rightButton: true));
        Assert.Equal(
            LayerLinkGesture.LinkBelow,
            LayerLinkGestures.From(KeyModifiers.Control | KeyModifiers.Alt, rightButton: true));
        Assert.Equal(LayerLinkGesture.Unlink, LayerLinkGestures.From(KeyModifiers.Shift, rightButton: true));
        // A BARE right-click is not ours: the docker's context menu is already
        // there with rename, reorder, merge and folders on it, and opening a
        // second menu over it would shadow all of that. Linking lives inside
        // that menu as a flyout instead.
        Assert.Null(LayerLinkGestures.From(KeyModifiers.None, rightButton: true));
    }

    [Fact]
    public void CtrlShiftIsReadBeforeShiftAlone()
    {
        // Most specific first. An if-chain in the wrong order reads "link to
        // the layer above" as "remove the link" — which a reader does not see
        // and this does.
        Assert.NotEqual(
            LayerLinkGestures.From(KeyModifiers.Shift, rightButton: true),
            LayerLinkGestures.From(KeyModifiers.Control | KeyModifiers.Shift, rightButton: true));
    }

    [Fact]
    public void TheWholeLeftButtonStaysTheDockersSelection()
    {
        // This is the correction that produced the current mapping. Ctrl+click
        // toggles the multi-layer selection and Shift+click takes a range;
        // moving the link menu off Ctrl only helps if it clears Shift too, or
        // one collision has just been traded for another.
        foreach (var modifiers in new[]
                 {
                     KeyModifiers.None, KeyModifiers.Control, KeyModifiers.Shift, KeyModifiers.Alt,
                     KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Control | KeyModifiers.Alt,
                 })
            Assert.Null(LayerLinkGestures.From(modifiers, rightButton: false));
    }

    // ---- what the gestures write --------------------------------------------------

    [AvaloniaFact]
    public void LinkingToTheLayerAboveMakesALinkThatCarriesTheRig()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 1;

        vm.LinkLayerAboveCommand.Execute(null);

        var link = Assert.Single(vm.Doc.Scene.LayerLinks!);
        Assert.Equal(At(vm, 1).LinkId, At(vm, 2).LinkId);
        Assert.Equal(link.Id, At(vm, 1).LinkId);
        // A new link carries bones and nothing else — rigging is why the
        // gesture exists, and arriving with everything switched on is the
        // general-mechanism trap Q87 paid to avoid.
        Assert.True(link.CarriesBones);
        Assert.False(link.CarriesAlpha);
        Assert.False(link.CarriesVisibility);
    }

    [AvaloniaFact]
    public void JoiningAnExistingLinkMergesRatherThanMakingASecond()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);          // 1 + 2

        vm.ActiveLayerIndex = 0;
        vm.LinkLayerAboveCommand.Execute(null);          // 0 joins them

        // Building a character is a gesture per layer and ends with ONE link,
        // not a chain of pairs and a layer that quietly left the first.
        Assert.Single(vm.Doc.Scene.LayerLinks!);
        Assert.Equal(3, vm.Doc.Scene.LinkedWith(At(vm, 0)).Count());
    }

    [AvaloniaFact]
    public void LinkingBelowIsTheSameGestureTheOtherWay()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 2;

        vm.LinkLayerBelowCommand.Execute(null);

        Assert.Equal(At(vm, 2).LinkId, At(vm, 1).LinkId);
        Assert.NotNull(At(vm, 2).LinkId);
    }

    [AvaloniaFact]
    public void AtTheEndOfTheStackTheGestureDoesNothingRatherThanWrapping()
    {
        var vm = Stack();
        var topIndex = vm.Doc.Scene.Layers.Count - 1;
        var top = At(vm, topIndex);
        vm.ActiveLayerIndex = topIndex;

        vm.LinkLayerAboveCommand.Execute(null);

        // Wrapping round to the bottom would link two layers that are not
        // neighbours at all, which is worse than doing nothing.
        Assert.Null(vm.Doc.Scene.LayerLinks);
        Assert.Null(top.LinkId);
    }

    [AvaloniaFact]
    public void UnlinkingTheSecondToLastMemberDropsTheLinkEntirely()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);

        vm.UnlinkLayerCommand.Execute(null);

        // A one-member link is not a link, and leaving one would have the
        // docker draw a bracket round a single row. Absent, not empty.
        Assert.Null(vm.Doc.Scene.LayerLinks);
        Assert.Null(At(vm, 1).LinkId);
        Assert.Null(At(vm, 2).LinkId);
    }

    [AvaloniaFact]
    public void TheRigTravelsTheLinkAndTheRowSaysSo()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);

        vm.ActiveLayerIndex = 1;
        vm.SetLayerBone("arm");

        Assert.Equal("arm", vm.Doc.Scene.RiggedBoneOf(At(vm, 2)));
        // And the docker says it, on both rows, or the artist cannot tell the
        // link did anything.
        var rows = vm.LayerRows.Where(r => r.Layer.LinkId is not null).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsLinked));
        Assert.All(rows, r => Assert.Equal("arm", r.RigBadge));
    }

    [AvaloniaFact]
    public void AnUnriggedLayerShowsNoBadgeAtAll()
    {
        var vm = Stack();
        // Nothing rigged anywhere: the docker gains not one word.
        Assert.All(vm.LayerRows, r => Assert.False(r.HasRigBadge));
        Assert.All(vm.LayerRows, r => Assert.False(r.IsLinked));
    }

    [AvaloniaFact]
    public void SwitchingAPropertyOffTakesItBackToAbsent()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);

        vm.LinkCarriesVisibility = true;
        Assert.True(vm.Doc.Scene.LayerLinks![0].Visibility);

        vm.LinkCarriesVisibility = false;
        Assert.Null(vm.Doc.Scene.LayerLinks![0].Visibility);
        // And bones, switched off, must not leave a stored false either.
        vm.LinkCarriesBones = false;
        Assert.Null(vm.Doc.Scene.LayerLinks![0].Bones);
        Assert.Null(vm.Doc.Scene.RiggedBoneOf(At(vm, 2)));
    }

    [AvaloniaFact]
    public void TheBracketReadsAsOneRunDownTheDocker()
    {
        var vm = Stack();
        vm.AddPaintedLayerCommand.Execute(null);       // four layers
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);        // 1 + 2
        vm.ActiveLayerIndex = 2;
        vm.LinkLayerAboveCommand.Execute(null);        // + 3

        // Docker order is topmost first, so scene index 3 is the top of the
        // run and 1 is the bottom of it.
        Assert.Equal(LayerLinkMark.Top, vm.LinkMarkOf(At(vm, 3)));
        Assert.Equal(LayerLinkMark.Middle, vm.LinkMarkOf(At(vm, 2)));
        Assert.Equal(LayerLinkMark.Bottom, vm.LinkMarkOf(At(vm, 1)));
        Assert.Equal(LayerLinkMark.None, vm.LinkMarkOf(At(vm, 0)));

        // Exactly one of the four marks is drawn per row, or the bracket
        // would double up or vanish.
        foreach (var row in vm.LayerRows.Where(r => r.IsLinked))
            Assert.Equal(
                1,
                new[] { row.IsLinkTop, row.IsLinkMiddle, row.IsLinkBottom, row.IsLinkDetached }.Count(b => b));
    }

    [AvaloniaFact]
    public void AMemberWhoseNeighboursAreNotInTheLinkSaysSoRatherThanDrawingALineToNothing()
    {
        var vm = Stack();
        vm.AddPaintedLayerCommand.Execute(null);       // four layers
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);        // 1 + 2
        // Move a third layer into the link across a gap: 0 joins, but layer 1
        // is between them only if we link 0 to 1 — so instead put layer 3 in
        // the same link directly, leaving 2 and 3 adjacent and 0 alone.
        At(vm, 0).LinkId = At(vm, 1).LinkId;
        vm.NotifyLinkSurface();

        // Layer 0's neighbours in the docker: layer 1 is in the link, so it is
        // the bottom of a run — and the run above it is unbroken.
        Assert.Equal(LayerLinkMark.Bottom, vm.LinkMarkOf(At(vm, 0)));
        Assert.Equal(LayerLinkMark.Middle, vm.LinkMarkOf(At(vm, 1)));

        // Now break the run: take the middle layer out, and the two ends are
        // members with no adjacent member at all.
        At(vm, 1).LinkId = null;
        vm.NotifyLinkSurface();
        Assert.Equal(LayerLinkMark.Detached, vm.LinkMarkOf(At(vm, 0)));
        Assert.Equal(LayerLinkMark.Detached, vm.LinkMarkOf(At(vm, 2)));
    }

    [AvaloniaFact]
    public void LinkingIsUndoableAndTheDockerFollows()
    {
        var vm = Stack();
        vm.ActiveLayerIndex = 1;
        vm.LinkLayerAboveCommand.Execute(null);
        Assert.True(vm.HasActiveLayerLink);

        vm.UndoCommand.Execute(null);

        Assert.Null(vm.Doc.Scene.LayerLinks);
        Assert.False(vm.HasActiveLayerLink);
        Assert.All(vm.LayerRows, r => Assert.False(r.IsLinked));
    }
}
