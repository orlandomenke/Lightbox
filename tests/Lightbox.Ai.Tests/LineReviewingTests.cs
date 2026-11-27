using System.Text.Json;
using Lightbox.Ai;
using Lightbox.Core.Cleanup;

namespace Lightbox.Ai.Tests;

/// <summary>The line review's answer, read the same way for every provider (Q296–Q298).</summary>
public class LineReviewingTests
{
    private static AiResult<List<ReviewFix>> Parse(object answer) =>
        LineReviewing.Parse(AiResult<string>.Success(JsonSerializer.Serialize(answer)), "The model");

    [Fact]
    public void AFixIsReadWithItsLinesItsEndsAndItsCitations()
    {
        var result = Parse(new
        {
            fixes = new[]
            {
                new { kind = "trim", line = 7, end = "end", other = (int?)8, otherEnd = "start", cites = new[] { "A4", "B2" }, reason = "Runs past the knee." },
            },
        });

        var fix = Assert.Single(result.Value!);
        Assert.Equal(ReviewFixKind.Trim, fix.Kind);
        Assert.Equal(7, fix.Line);
        Assert.False(fix.AtStart);
        Assert.Equal(8, fix.Other);
        Assert.True(fix.OtherAtStart);
        Assert.Equal(["A4", "B2"], fix.Cites);
    }

    [Fact]
    public void AnEndIsReadInAnyCase()
    {
        var result = Parse(new { fixes = new[] { new { kind = "Trim", line = 2, end = "Start", other = (int?)1, otherEnd = "END", cites = new[] { "A1" }, reason = "" } } });

        var fix = Assert.Single(result.Value!);
        Assert.Equal(ReviewFixKind.Trim, fix.Kind);
        Assert.True(fix.AtStart);
    }

    [Theory]
    [InlineData("{\"fixes\":null}")]
    [InlineData("{\"fixes\":[null]}")]
    public void AnEmptyOrNullAnswerIsReadWithoutThrowing(string json)
    {
        var result = LineReviewing.Parse(AiResult<string>.Success(json), "The model");

        Assert.Equal(AiOutcome.Success, result.Outcome);
        Assert.All(result.Value!, f => Assert.StartsWith("unreadable", f.Reason));
    }

    [Fact]
    public void AnUnknownVerbOrALineNumberBelowOneIsReportedNotDropped()
    {
        var result = Parse(new
        {
            fixes = new object[]
            {
                new { kind = "redraw", line = 1, end = "end", other = (int?)null, otherEnd = "end", cites = new[] { "A1" }, reason = "" },
                new { kind = "remove", line = 0, end = "end", other = (int?)null, otherEnd = "end", cites = new[] { "A1" }, reason = "" },
                new { kind = "remove", line = 3, end = "end", other = (int?)null, otherEnd = "end", cites = new[] { "A1" }, reason = "" },
            },
        });

        Assert.Equal(3, result.Value!.Count);
        Assert.Equal(2, result.Value.Count(f => f.Reason.StartsWith("unreadable", StringComparison.Ordinal)));
        Assert.Contains(result.Value, f => f.Line == 3);
    }

    [Fact]
    public void AnEndThatIsNeitherStartNorEndIsNotGuessed()
    {
        var result = Parse(new { fixes = new[] { new { kind = "trim", line = 2, end = "top", other = (int?)1, otherEnd = "end", cites = new[] { "A1" }, reason = "" } } });

        Assert.Contains("the end \"top\"", Assert.Single(result.Value!).Reason);
    }

    [Fact]
    public void AnAnswerIsCappedAndItsTextClipped()
    {
        var many = Enumerable.Range(1, LineReviewing.MaxFixes + 10).Select(i =>
            new { kind = "remove", line = i, end = "end", other = (int?)null, otherEnd = "end", cites = new[] { "A1-and-a-great-deal-more" }, reason = new string('x', 1000) }).ToArray();

        var result = Parse(new { fixes = many });

        Assert.Equal(LineReviewing.MaxFixes + 1, result.Value!.Count);
        Assert.Contains("past the limit", result.Value[^1].Reason);
        Assert.All(result.Value.Take(LineReviewing.MaxFixes), f => Assert.True(f.Reason.Length <= 240 && f.Cites.All(c => c.Length <= 8)));
    }

    [Fact]
    public void ARefusalIsForwardedNotTurnedIntoNoMistakes()
    {
        var result = LineReviewing.Parse(AiResult<string>.Refused("declined"), "The model");

        Assert.Equal(AiOutcome.Refused, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public void SomethingThatIsNotAReviewIsAnError()
    {
        var result = LineReviewing.Parse(AiResult<string>.Success("not json"), "The model");

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("not a line review", result.Message);
    }

    [Fact]
    public void ThePromptSaysTheKeysDecideAndTheSchemaHasNoRoomForACoordinate()
    {
        Assert.Contains("THE KEYS DECIDE", LineReviewing.System);
        Assert.DoesNotContain("\"x\"", LineReviewing.Schema);
        Assert.DoesNotContain("\"y\"", LineReviewing.Schema);
        Assert.Contains("\"cites\"", LineReviewing.Schema);
    }

    [Fact]
    public void AProviderThatCannotReviewSaysSo()
    {
        IAiArtist free = new Lightbox.Ai.Golden.FreeEngineArtist();

        var result = free.ReviewLinesAsync(new LineReviewRequest("L1: …", null), CancellationToken.None).Result;

        Assert.Equal(AiOutcome.Error, result.Outcome);
        Assert.Contains("cannot review lines", result.Message);
    }
}
