using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;

namespace Lightbox.Ai.Golden;

/// <summary>
/// The deterministic inbetweener dressed as an artist, so the golden set can be
/// run against a known quantity for nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists to grade the set, not to serve requests.</b> Q32 settled that a
/// failed AI request is refused rather than quietly answered by the free
/// engine, and this does not reopen that: nothing wires it into
/// <c>AiArtistFactory</c>, no provider names it, and an artist can never end up
/// talking to it. It is a subject the profiler can point at.
/// </para>
/// <para>
/// That is worth having twice over. It gives the set a baseline row that costs
/// no tokens and runs in CI — the profile of a model with no model in it. And
/// it inverts the usual direction of suspicion: when a pair fails here, the
/// pair is wrong until proven otherwise, because the engine's behaviour on
/// constructed geometry is not in question. A golden set nobody checks is a
/// published claim nobody checked.
/// </para>
/// <para>
/// Its <em>limits</em> are the interesting part and are the reason
/// <see cref="GoldenCategory.Organic"/> cannot be filled this way. Q32 and Q33
/// together: the free engine is weakest exactly on complex organic subjects, so
/// its answers there are not known-good and using them as references would
/// certify a model on the cases where the reference is already reliable.
/// </para>
/// </remarks>
public sealed class FreeEngineArtist : IAiArtist
{
    public Task<AiResult<List<InbetweenFrameResult>>> GenerateInbetweensAsync(
        InbetweenRequest request, CancellationToken ct)
    {
        var frames = request.Ts
            .Select(t => new InbetweenFrameResult(
                t, Inbetweener.Inbetween(request.KeyframeA, request.KeyframeB, t, request.Easing)))
            .ToList();
        return Task.FromResult(AiResult<List<InbetweenFrameResult>>.Success(frames));
    }

    /// <summary>
    /// Refused, and it has to be: reading a subject needs a model, and there is
    /// no deterministic stand-in that would not be a fabrication.
    /// </summary>
    public Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(SubjectRequest request, CancellationToken ct) =>
        Task.FromResult(AiResult<SubjectTaxonomy>.Refused(
            "The free engine measures geometry; reading a subject needs a model."));
}
