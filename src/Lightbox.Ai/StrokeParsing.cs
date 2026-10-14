using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.Ai;

/// <summary>
/// Turning a model's JSON text into strokes, once.
/// </summary>
/// <remarks>
/// Every artist differs only in how it gets the text — auth, endpoint,
/// framing. What it does with the text is identical, and was copied per
/// artist until there were four of them. The <paramref name="noun"/> and
/// <paramref name="hint"/> parameters keep the one thing that legitimately
/// varied: a local model that returns nothing wants "try a larger model",
/// and a hosted one does not.
/// </remarks>
internal static class StrokeParsing
{
    public static AiResult<List<InbetweenFrameResult>> Inbetweens(
        AiResult<string> call, SceneInfo scene, string noun, string hint = "")
    {
        if (call.Outcome != AiOutcome.Success) return Forward<List<InbetweenFrameResult>>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.InbetweenResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            var frames = dto.Inbetweens
                .Where(f => f.T is > 0 and < 1)
                .OrderBy(f => f.T)
                .Select(f => new InbetweenFrameResult(f.T, StrokeWire.FromWire(f.Strokes, scene)))
                .Where(f => f.Strokes.Count > 0)
                .ToList();
            if (frames.Count == 0)
                return AiResult<List<InbetweenFrameResult>>.Error(
                    $"{noun} returned no usable inbetween frames.{hint}", retryable: true);
            return AiResult<List<InbetweenFrameResult>>.Success(frames);
        }
        catch (JsonException e)
        {
            return AiResult<List<InbetweenFrameResult>>.Error(
                $"Could not parse the response: {e.Message}", retryable: true);
        }
    }

    /// <summary>
    /// Turn a taxonomy reply into the stored record, dropping what cannot be
    /// used and refusing what would be worse than nothing.
    /// </summary>
    /// <remarks>
    /// The validation here is not defensive tidying — each rule is a failure a
    /// model actually produces. An unnamed part cannot be referred to by
    /// anything later; a part naming a parent that is not in the list is a
    /// dangling reference the inbetweener would print into a prompt as fact;
    /// and a part that is its own ancestor makes any walk of the tree hang.
    /// The reading is an artist's starting point, so a small correct answer
    /// beats a long one with a lie in it.
    /// </remarks>
    /// <summary>
    /// A pose reading (Q274), kept to what can be trusted: a joint without two
    /// finite numbers in both keys is dropped, a part left with fewer than two
    /// joints is no chain and goes, and nothing usable is a retryable error.
    /// </summary>
    public static AiResult<PoseReading> Pose(AiResult<string> call, string noun)
    {
        if (call.Outcome != AiOutcome.Success) return Forward<PoseReading>(call);
        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.PoseResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");
            static bool Point(List<double>? p) => p is { Count: >= 2 } && double.IsFinite(p[0]) && double.IsFinite(p[1]);
            var parts = new List<PosePart>();
            foreach (var part in dto.Parts)
            {
                var joints = part.Joints
                    .Where(j => Point(j.A) && Point(j.B))
                    .Select(j => new PoseJoint
                    {
                        Name = j.Name.Trim(),
                        A = new PosePoint(j.A![0], j.A[1]),
                        B = new PosePoint(j.B![0], j.B[1]),
                    })
                    .ToList();
                if (joints.Count >= 2) parts.Add(new PosePart { Name = part.Name.Trim(), Joints = joints });
            }
            if (parts.Count == 0)
                return AiResult<PoseReading>.Error($"{noun} found no part of the subject it could place.", retryable: true);
            return AiResult<PoseReading>.Success(new PoseReading(new PoseGuide { Parts = parts }, dto.Notes.Trim()));
        }
        catch (JsonException e)
        {
            return AiResult<PoseReading>.Error($"{noun} answered with a pose that could not be read: {e.Message}", retryable: true);
        }
    }

    public static AiResult<SubjectTaxonomy> Subject(
        AiResult<string> call, string noun, string hint = "")
    {
        if (call.Outcome != AiOutcome.Success) return Forward<SubjectTaxonomy>(call);

        try
        {
            var dto = JsonSerializer.Deserialize<StrokeWire.SubjectResultDto>(call.Value!)
                      ?? throw new JsonException("null payload");

            var named = dto.Parts
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name.Trim().ToLowerInvariant())
                .Select(g => g.First())
                .ToList();
            var names = named.Select(p => p.Name.Trim().ToLowerInvariant()).ToHashSet();

            var parts = named.Select(p => new SubjectPart
            {
                Name = p.Name.Trim().ToLowerInvariant(),
                Parent = Parent(p),
                Depth = p.Depth,
            }).ToList();

            if (parts.Count == 0)
                return AiResult<SubjectTaxonomy>.Error(
                    $"{noun} named no parts of the subject.{hint}", retryable: true);

            BreakCycles(parts);

            return AiResult<SubjectTaxonomy>.Success(new SubjectTaxonomy
            {
                Kind = dto.Kind.Trim().ToLowerInvariant(),
                Parts = parts,
            });

            string? Parent(StrokeWire.SubjectPartDto p)
            {
                var parent = p.Parent?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(parent)) return null;
                // A parent nobody declared, or a part parented to itself.
                if (!names.Contains(parent) || parent == p.Name.Trim().ToLowerInvariant()) return null;
                return parent;
            }
        }
        catch (JsonException e)
        {
            return AiResult<SubjectTaxonomy>.Error(
                $"Could not parse the response: {e.Message}", retryable: true);
        }
    }

    /// <summary>
    /// Orphan any part that can reach itself through its parents. A cycle is
    /// rare and cheap to break; a cycle that survives into the record makes
    /// every later walk of the tree a hang rather than a wrong answer.
    /// </summary>
    private static void BreakCycles(List<SubjectPart> parts)
    {
        var byName = parts.ToDictionary(p => p.Name);
        foreach (var part in parts)
        {
            var seen = new HashSet<string> { part.Name };
            var walker = part;
            while (walker.Parent is { } parent && byName.TryGetValue(parent, out var next))
            {
                if (!seen.Add(parent)) { part.Parent = null; break; }
                walker = next;
            }
        }
    }

    public static AiResult<T> Forward<T>(AiResult<string> call) => call.Outcome switch
    {
        AiOutcome.Refused => AiResult<T>.Refused(call.Message ?? "Refused."),
        AiOutcome.Truncated => AiResult<T>.Truncated(call.Message ?? "Truncated."),
        _ => AiResult<T>.Error(call.Message ?? "Unknown error.", call.Retryable),
    };
}
