# Q170 · Is preview-resolution ink acceptable for the newest dabs

Raised by: B322's seventh attempt, 2026-08-27, after Q169 ruled out rationing
the dabs.

What it blocks: whether the live tip can be made cheap enough to survive a fast
stroke at a large brush size, which is the whole of B322's remaining scope.

**Answered 2026-08-27 — the owner asked to see both before choosing.**

## The question

The tip is stamped into a document-sized buffer — 3840x2160 on the owner's
document — and then composited down to the 1440x810 surface being looked at.
Most of those pixels are discarded before anyone sees them.

Stamped at the compose scale instead, the same size-70 dab measures **24.7 us**
against **62.48**, and the median outstanding run of a fast stroke costs
**6.73 ms** instead of **17.13**. That is 2.5x rather than the 7x the area ratio
suggests, because a dab carries per-dab setup that does not shrink and a scaled
draw resamples — the figure is measured with both penalties in it
(`LiveTipDabCostTests`).

It is invariant 7's cheap side and not a breach of it: the **surface** carries
the scale as a canvas transform, dab coordinates are never multiplied, so
`Hash01` seeds every scatter, size, flow, roundness, rotation and colour jitter
from the same bits. It is the mechanism `ComposeScale` already uses for the
composite.

What it costs is a **seam**: the newest dabs land rasterised at preview
resolution beside a processed body rasterised at document resolution, until the
pass catches up and replaces them. And the saving is view-dependent — at
fit-to-window there is 2.5x in it, at 100% zoom there is none and a fast stroke
behaves exactly as it does today.

**Recommendation:** take it. Q169 leaves no other lever, the seam is transient
by construction, and the arithmetic says the typical fast-stroke publish becomes
affordable. The alternative is that B322 has nothing left to try.

**Decision: build both arms and show them.**

`LIGHTBOX_TIP_SCALE=preview` selects the cheaper arm, the default stays document
resolution, and the render report names which one ran so two captures that
differ only in this are not indistinguishable afterwards. Nothing is chosen
until the owner has drawn the same fast stroke each way.

This is the right call for a question about how a mark looks: no assertion in
the suite answers it, five measured improvements on this entry changed nothing
the artist could feel, and the ones that did were found in their captures.
