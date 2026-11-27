using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Cleanup;

namespace Lightbox.Ai;

/// <summary>
/// What a line review sends (Q296): the numbered listing, and — for a provider
/// that takes images — the picture the listing numbers.
/// </summary>
/// <param name="Listing">The sheet's listing (<see cref="ReviewSheet.Listing"/>): every line, its ends and what lies near them.</param>
/// <param name="Picture">A PNG, base64: the two keys and the inbetween side by side, every line labelled. Null for a text-only review.</param>
/// <param name="TextOnlyListing">
/// The listing with the keys' neighbour lines too, for a provider that takes
/// no picture; null to use <paramref name="Listing"/>. Two listings because
/// with a picture the keys' neighbours are two thirds of the tokens and say
/// what the picture shows (ai-engineer review).
/// </param>
public sealed record LineReviewRequest(string Listing, string? Picture, string? TextOnlyListing = null);

/// <summary>
/// The line review's prompt, schema and parser — one place for all five
/// providers, the way the inbetween's and the subject's are shared.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule is the keys (Q298).</b> The prompt says it, the schema has a
/// field for it, and the engine refuses a fix that leaves it empty: a feature
/// is a mistake only when neither key has it at the matching place, and
/// anything both keys share is the artist's style, however odd it looks.
/// </para>
/// <para>
/// <b>Three verbs, numbered lines, no coordinates.</b> A model judges which
/// line is wrong well and places a point badly (Q270); the answer has nowhere
/// to put a coordinate, so it cannot give one.
/// </para>
/// </remarks>
public static class LineReviewing
{
    public const string System =
        "You review one inbetween drawing of a hand-drawn animation for mistakes the inbetweener made, " +
        "so a deterministic engine can fix them. You receive the two key drawings the artist drew either " +
        "side of it and the inbetween itself, every line numbered: A1… in key A, B1… in key B, L1… in the " +
        "inbetween. You also receive a listing that gives, at each end of each line, which lines are near it, " +
        "how far, and how far they run alongside it.\n\n" +
        "THE KEYS DECIDE WHAT IS A MISTAKE. A feature of the inbetween is a mistake only when NEITHER key has " +
        "it at the matching place: two lines that run on past a joint side by side where both keys meet them " +
        "at one point; an end that stops short of a line where both keys join them; a short leftover mark " +
        "neither key has. Anything both keys share is the artist's style and is NOT a mistake, however odd it " +
        "looks — doubled sketch passes, overshooting corners, a broken line. The subject's other outer lines " +
        "(how its other joints meet) can support a judgement, never make it on their own. When in doubt, leave " +
        "it: an unnecessary fix is worse than a missed one.\n" +
        "If EITHER key has the feature, even in another amount or shape, it is style or motion: leave it. " +
        "Compare by which line it is — the thigh's back, the shin's front — not by where it sits, because the " +
        "pose has moved between the keys. A short mark that corresponds to any short mark in either key (a hook, " +
        "a tick, a crease) is never removed. When two lines run past each other at a joint, trim the one whose " +
        "end runs past where the keys put the joint, so the joint stays where the keys have it.\n" +
        "The listing ends with a JOINTS section for each drawing: where one line carries on from another, and " +
        "whether they meet, leave a gap, or overlap — and when they overlap, whether they lie on top of each " +
        "other (reading as one line, a common sketching habit) or side by side (reading as two lines). It is the " +
        "most direct evidence: find the inbetween joint that corresponds to a joint in the keys (the same pair of " +
        "lines of the body) and compare. First: if EITHER key has that joint side by side too (reading as two " +
        "lines), the inbetween's is style — leave it. Likewise an overlap that reads as one line is style when a key " +
        "does the same. Only an overlap that reads as two lines where BOTH keys' joint meets or reads as one line is " +
        "a mistake: trim the line whose end runs past the joint, back to the other line.\n\n" +
        "You may only ask for three things, by line number:\n" +
        "- trim: cut the given end of line L back to where it meets line `other`;\n" +
        "- join: carry the given end of line L and the given end of line `other` onto one point (ends at most 40 px apart);\n" +
        "- remove: take out a short mark L (no `other`).\n" +
        "For trim and remove, set `otherEnd` to \"end\"; it is only read for a join. " +
        "Every fix must cite in `cites` the key lines you compared it with, as \"A3\" or \"B5\" — a fix that cites " +
        "none is refused. Give a one-sentence `reason` an artist will read, in drawing terms and without line " +
        "numbers, naming the part of the body as you can see it in the keys' drawing — the joint you compared, not a " +
        "guess at another (\"the knee's front runs on past the shin; both keys meet them at one point\"). Return no fixes " +
        "when you find no mistake. Never invent coordinates, lines or numbers that are not on the sheet.";

    public static string User(LineReviewRequest request) =>
        (request.Picture is null
            ? "There is no picture: judge from the listing alone.\n\n"
            : "The picture shows key A on the left, the inbetween in the middle and key B on the right, every line labelled; a dot marks each line's start.\n\n")
        + (request.Picture is null ? request.TextOnlyListing ?? request.Listing : request.Listing);

    public static readonly string Schema = """
        {
          "type": "object",
          "properties": {
            "fixes": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "kind": { "type": "string", "enum": ["trim", "join", "remove"] },
                  "line": { "type": "integer" },
                  "end": { "type": "string", "enum": ["start", "end"] },
                  "other": { "type": ["integer", "null"] },
                  "otherEnd": { "type": "string", "enum": ["start", "end"] },
                  "cites": { "type": "array", "items": { "type": "string" } },
                  "reason": { "type": "string" }
                },
                "required": ["kind", "line", "end", "other", "otherEnd", "cites", "reason"],
                "additionalProperties": false
              }
            }
          },
          "required": ["fixes"],
          "additionalProperties": false
        }
        """;

    /// <summary>The most fixes one answer may carry; a drawing with more mistakes than this wants redrawing, not fixing.</summary>
    public const int MaxFixes = 40;

    /// <summary>The fixes in a model's answer, or the failure it forwards.</summary>
    public static AiResult<List<ReviewFix>> Parse(AiResult<string> call, string noun)
    {
        if (call.Outcome != AiOutcome.Success)
        {
            return call.Outcome switch
            {
                AiOutcome.Refused => AiResult<List<ReviewFix>>.Refused(call.Message ?? "The model declined to review the drawing."),
                AiOutcome.Truncated => AiResult<List<ReviewFix>>.Truncated(call.Message ?? "The review was cut off."),
                _ => AiResult<List<ReviewFix>>.Error(call.Message ?? "The review failed.", call.Retryable),
            };
        }
        try
        {
            var dto = JsonSerializer.Deserialize<AnswerDto>(call.Value!) ?? throw new JsonException("null payload");
            var fixes = new List<ReviewFix>();
            var given = (dto.Fixes ?? []).ToList();
            foreach (var f in given.Take(MaxFixes))
            {
                // A fix that cannot be read is reported, not dropped: dropping
                // every fix would tell the artist "no mistakes" (ai-engineer).
                if (f is null) { fixes.Add(Unreadable("an empty fix")); continue; }
                var kind = f.Kind?.Trim().ToLowerInvariant() switch
                {
                    "trim" => ReviewFixKind.Trim,
                    "join" => ReviewFixKind.Join,
                    "remove" => ReviewFixKind.Remove,
                    _ => (ReviewFixKind?)null,
                };
                if (kind is null) { fixes.Add(Unreadable($"the verb \"{Clip(f.Kind ?? "", 12)}\"")); continue; }
                if (f.Line < 1) { fixes.Add(Unreadable($"line {f.Line}")); continue; }
                if (End(f.End) is not { } atStart) { fixes.Add(Unreadable($"the end \"{Clip(f.End ?? "", 12)}\" of L{f.Line}")); continue; }
                var otherAtStart = End(f.OtherEnd);
                if (kind == ReviewFixKind.Join && otherAtStart is null) { fixes.Add(Unreadable($"the other end \"{Clip(f.OtherEnd ?? "", 12)}\"")); continue; }
                fixes.Add(new ReviewFix(
                    kind.Value, f.Line, atStart, f.Other, otherAtStart ?? false,
                    [.. (f.Cites ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => Clip(c.Trim(), 8))],
                    Clip(f.Reason?.Trim() ?? "", 240)));
            }
            if (given.Count > MaxFixes) fixes.Add(Unreadable($"{given.Count - MaxFixes} fixes past the limit of {MaxFixes}"));
            return AiResult<List<ReviewFix>>.Success(fixes);
        }
        catch (JsonException e)
        {
            return AiResult<List<ReviewFix>>.Error($"{noun} answered with something that is not a line review ({e.Message}).", retryable: true);
        }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>"start" or "end", in any case; anything else is not an end.</summary>
    private static bool? End(string? end) => end?.Trim().ToLowerInvariant() switch
    {
        "start" => true,
        "end" => false,
        _ => null,
    };

    /// <summary>A placeholder the engine refuses with its own words, so the reader learns what was not understood.</summary>
    private static ReviewFix Unreadable(string what) =>
        new(ReviewFixKind.Remove, 0, false, null, false, [], $"unreadable: {what} — use trim, join or remove, a line number from the sheet, and \"start\" or \"end\".");

    private sealed class AnswerDto
    {
        [JsonPropertyName("fixes")] public List<FixDto?>? Fixes { get; set; } = [];
    }

    private sealed class FixDto
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("line")] public int Line { get; set; }
        [JsonPropertyName("end")] public string? End { get; set; }
        [JsonPropertyName("other")] public int? Other { get; set; }
        [JsonPropertyName("otherEnd")] public string? OtherEnd { get; set; }
        [JsonPropertyName("cites")] public List<string>? Cites { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }
}
