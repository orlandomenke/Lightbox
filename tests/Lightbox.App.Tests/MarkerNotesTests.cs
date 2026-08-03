using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// One record, three uses: a chip on the ruler, an engine event, and prose.
/// </summary>
/// <remarks>
/// Pillar 6 listed <em>frame tagging</em>, <em>animation tagging</em>,
/// <em>timeline bookmarks</em> and <em>animation notes</em> as four items. Three
/// of them are one thing seen three ways — a named point on a frame — and the
/// fourth is a range, which P5d built as <see cref="AnimationTag"/>. What was
/// genuinely missing was prose and a way to *reach* a marker, and these are the
/// tests for both.
/// </remarks>
public class MarkerNotesTests
{
    private static MainViewModel Vm(int frames)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.Doc.Scene.FrameCount = frames;
        return vm;
    }

    // ---- prose -----------------------------------------------------------------

    [AvaloniaFact]
    public void AMarkerWithNoNoteWritesNoNoteKey()
    {
        var vm = Vm(4);
        vm.SetMarkerAt(1, "contact", "#e0a030");

        var json = vm.SerializeDocument();

        Assert.DoesNotContain("\"note\"", json);
        Assert.DoesNotContain("\"hasNote\"", json);
        Assert.False(vm.MarkerAt(1)!.HasNote);
    }

    [AvaloniaFact]
    public void WritingANoteOnAnUnmarkedFrameMakesTheMarker()
    {
        // A frame worth writing about is a frame worth marking, so this creates
        // rather than refusing.
        var vm = Vm(4);

        vm.SetMarkerNoteAt(2, "the hand pops here, fix on 2s");

        var marker = vm.MarkerAt(2);
        Assert.NotNull(marker);
        Assert.True(marker!.HasNote);
        Assert.Equal("the hand pops here, fix on 2s", marker.Note);
        Assert.Single(vm.Notes);
    }

    [AvaloniaFact]
    public void ANoteSurvivesRenamingTheMarker()
    {
        // SetMarkerAt replaces the marker, so without carrying these across a
        // rename would silently throw away the prose — a deletion disguised as an
        // edit, and the kind only noticed later.
        var vm = Vm(4);
        vm.SetMarkerNoteAt(1, "check the arc");
        vm.SetMarkerIsEventAt(1, true);

        vm.SetMarkerAt(1, "renamed", "#ff0000");

        var marker = vm.MarkerAt(1)!;
        Assert.Equal("renamed", marker.Label);
        Assert.Equal("check the arc", marker.Note);
        Assert.True(marker.ExportsAsEvent);
    }

    [AvaloniaFact]
    public void ClearingTheTextRemovesTheNoteAndKeepsTheMarker()
    {
        // The marker may be doing its own job as a chip on the ruler.
        var vm = Vm(4);
        vm.SetMarkerAt(1, "contact", "#e0a030");
        vm.SetMarkerNoteAt(1, "something");

        vm.SetMarkerNoteAt(1, "   ");

        Assert.NotNull(vm.MarkerAt(1));
        Assert.False(vm.MarkerAt(1)!.HasNote);
        Assert.Empty(vm.Notes);
    }

    [AvaloniaFact]
    public void ANoteIsNotAnEvent()
    {
        // Prose must never reach an engine as a callback. "check the hand" is not
        // something a game handles.
        var vm = Vm(4);
        vm.SetMarkerNoteAt(1, "check the hand");

        Assert.False(vm.MarkerAt(1)!.ExportsAsEvent);
    }

    [AvaloniaFact]
    public void TheEventFlagGoesBackToAbsentRatherThanFalse()
    {
        var vm = Vm(4);
        vm.SetMarkerAt(1, "OnFootstep", "#e0a030");
        vm.SetMarkerIsEventAt(1, true);
        Assert.Contains("\"isEvent\"", vm.SerializeDocument());

        vm.SetMarkerIsEventAt(1, false);

        Assert.DoesNotContain("\"isEvent\"", vm.SerializeDocument());
    }

    [AvaloniaFact]
    public void NotesAreListedInFrameOrder()
    {
        var vm = Vm(10);
        vm.SetMarkerNoteAt(7, "late");
        vm.SetMarkerNoteAt(2, "early");
        vm.SetMarkerAt(4, "no note", "#e0a030");

        Assert.Equal([2, 7], vm.Notes.Select(n => n.Frame));
    }

    [AvaloniaFact]
    public void ANoteIsOneUndoStep()
    {
        var vm = Vm(4);
        vm.SetMarkerNoteAt(1, "written");

        vm.UndoCommand.Execute(null);

        Assert.Null(vm.MarkerAt(1));
    }

    // ---- what "bookmarks" actually wanted --------------------------------------

    [AvaloniaFact]
    public void TheMarkersCanBeWalkedForwardsAndBackwards()
    {
        // Markers have existed since M9c with no way to reach one, so on a long
        // sheet they were labels you hunted for by eye.
        var vm = Vm(20);
        vm.SetMarkerAt(3, "a", "#e0a030");
        vm.SetMarkerAt(9, "b", "#e0a030");
        vm.SetMarkerAt(15, "c", "#e0a030");
        vm.CurrentFrameIndex = 0;

        Assert.True(vm.GoToNextMarker());
        Assert.Equal(3, vm.CurrentFrameIndex);
        Assert.True(vm.GoToNextMarker());
        Assert.Equal(9, vm.CurrentFrameIndex);

        Assert.True(vm.GoToPreviousMarker());
        Assert.Equal(3, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void WalkingPastTheLastMarkerStaysPutRatherThanWrapping()
    {
        // Wrapping would move the playhead somewhere the artist did not ask for,
        // and on a long sheet they would not see where it went.
        var vm = Vm(20);
        vm.SetMarkerAt(5, "only", "#e0a030");
        vm.CurrentFrameIndex = 5;

        Assert.False(vm.GoToNextMarker());
        Assert.Equal(5, vm.CurrentFrameIndex);
        Assert.False(vm.GoToPreviousMarker());
        Assert.Equal(5, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void WithNoMarkersNavigationDoesNothing()
    {
        var vm = Vm(10);
        vm.CurrentFrameIndex = 4;

        Assert.False(vm.GoToNextMarker());
        Assert.False(vm.GoToPreviousMarker());
        Assert.Equal(4, vm.CurrentFrameIndex);
    }

    // ---- the range half --------------------------------------------------------

    [AvaloniaFact]
    public void ATagCarriesProseToo()
    {
        // "The whole run cycle reads too heavy" is a note about a stretch of
        // frames, and a marker is a point.
        var vm = Vm(8);
        vm.Doc.Scene.Tags = [new AnimationTag { Name = "run", Start = 0, End = 7, Note = "reads too heavy" }];

        var back = Lightbox.Core.Serialization.DocJson.Deserialize(vm.SerializeDocument());

        var tag = Assert.Single(back.Scene.Tags!);
        Assert.True(tag.HasNote);
        Assert.Equal("reads too heavy", tag.Note);
    }

    [AvaloniaFact]
    public void ATagWithNoNoteWritesNoNoteKey()
    {
        var vm = Vm(8);
        vm.Doc.Scene.Tags = [new AnimationTag { Name = "run", Start = 0, End = 7 }];

        var json = vm.SerializeDocument();

        Assert.DoesNotContain("\"note\"", json);
        Assert.DoesNotContain("\"hasNote\"", json);
    }
}
