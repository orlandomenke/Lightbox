using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Whether the document is on disk yet, which is the question export and status share.
/// </summary>
/// <remarks>
/// Pure, so the interesting cases are stated rather than staged: "the file was moved" and
/// "there are edits the file does not have" both need a filesystem to demonstrate and
/// neither needs one to decide.
/// </remarks>
public class SaveRequirementTests
{
    private static Func<string, bool> Exists(params string[] present) =>
        path => present.Contains(path);

    private static readonly Func<string, bool> NothingExists = _ => false;

    [Fact]
    public void ADocumentThatWasNeverSavedHasToBeAskedAbout()
    {
        Assert.Equal(SaveGate.AskWhereItGoes, SaveRequirement.For(null, false, NothingExists));
        Assert.Equal(SaveGate.AskWhereItGoes, SaveRequirement.For("", false, NothingExists));
        Assert.Equal(SaveGate.AskWhereItGoes, SaveRequirement.For("   ", true, NothingExists));
    }

    [Fact]
    public void ASavedAndUnchangedDocumentIsReadyToGo()
    {
        Assert.Equal(
            SaveGate.Ready,
            SaveRequirement.For("/w/hero.lightbox.json", false, Exists("/w/hero.lightbox.json")));
    }

    [Fact]
    public void EditsTheFileDoesNotHaveAreWrittenWithoutAsking()
    {
        // The distinction that keeps this from being a nuisance: the artist already said
        // where the file goes, so writing to it needs no permission. A dialog here would be
        // a click between "mark Ready" and it being Ready.
        var gate = SaveRequirement.For("/w/hero.lightbox.json", true, Exists("/w/hero.lightbox.json"));

        Assert.Equal(SaveGate.SaveInPlaceFirst, gate);
        Assert.False(SaveRequirement.NeedsTheArtist(gate));
    }

    [Fact]
    public void AFileThatIsNoLongerThereIsNotTheSameAsNeverSaved()
    {
        // Kept apart on purpose. The artist deleted or moved it, so writing back to the
        // remembered path would put the file somewhere they deliberately emptied — and
        // "never saved" would be the wrong thing to tell them about a drawing they saved.
        var gate = SaveRequirement.For("/w/gone.lightbox.json", false, NothingExists);

        Assert.Equal(SaveGate.FileHasVanished, gate);
        Assert.NotEqual(SaveGate.AskWhereItGoes, gate);
        Assert.True(SaveRequirement.NeedsTheArtist(gate));
    }

    [Fact]
    public void OnlyTheTwoGatesThatNeedAnAnswerBlock()
    {
        Assert.True(SaveRequirement.NeedsTheArtist(SaveGate.AskWhereItGoes));
        Assert.True(SaveRequirement.NeedsTheArtist(SaveGate.FileHasVanished));
        Assert.False(SaveRequirement.NeedsTheArtist(SaveGate.Ready));
        Assert.False(SaveRequirement.NeedsTheArtist(SaveGate.SaveInPlaceFirst));
    }

    [Theory]
    [InlineData(SaveGate.AskWhereItGoes)]
    [InlineData(SaveGate.FileHasVanished)]
    public void AnExplanationSaysWhatWasBeingAttempted(SaveGate gate)
    {
        // The message has to name the action, because the artist clicked something and the
        // dialog appearing over it is otherwise a non-sequitur.
        var why = SaveRequirement.Explain(gate, "marking this Ready");

        Assert.Contains("marking this Ready", why);
        Assert.NotEqual("", why);
    }

    [Fact]
    public void AReadyDocumentHasNothingToExplain()
    {
        Assert.Equal("", SaveRequirement.Explain(SaveGate.Ready, "exporting"));
    }

    [Fact]
    public void TheTwoBlockingGatesGiveDifferentReasons()
    {
        // Because they are different problems and the fix differs: one is "save it", the
        // other is "save it again, somewhere that exists".
        var never = SaveRequirement.Explain(SaveGate.AskWhereItGoes, "exporting");
        var gone = SaveRequirement.Explain(SaveGate.FileHasVanished, "exporting");

        Assert.NotEqual(never, gone);
        Assert.Contains("not been saved", never);
        Assert.Contains("no longer there", gone);
    }
}
