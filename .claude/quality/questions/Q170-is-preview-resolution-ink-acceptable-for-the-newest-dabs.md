# Q170 · Is preview-resolution ink acceptable for the newest dabs — **answered 2026-09-02: yes, and it is the default**

Raised by: B322's seventh attempt, 2026-08-27, after Q169 ruled out rationing
the dabs.

What it blocks: whether the live tip can be made cheap enough to survive a fast
stroke at a large brush size, which is the whole of B322's remaining scope.

**Answered 2026-08-27 — the owner asked to see both before choosing.**

## The question

The tip is stamped into a document-sized buffer — 3840x2160 on the owner's
document — and then composited down to the 1440x810 surface being looked at.
Most of those pixels are discarded before anyone sees them.

Stamped at the compose scale instead, the same size-70 dab measures about
**11 us** against about **45**, and the median outstanding run of a fast stroke
costs about **3.0 ms** instead of **12.5** — which is inside the 3 ms budget
that already exists. That is **4.2x** rather than the 7x the area ratio
suggests, because a dab carries per-dab setup that does not shrink and a scaled
draw resamples; the figure is measured with both penalties in it
(`LiveTipDabCostTests`).

It is invariant 7's cheap side and not a breach of it: the **surface** carries
the scale as a canvas transform, dab coordinates are never multiplied, so
`Hash01` seeds every scatter, size, flow, roundness, rotation and colour jitter
from the same bits. It is the mechanism `ComposeScale` already uses for the
composite.

What it costs is a **seam**: the newest dabs land rasterised at preview
resolution beside a processed body rasterised at document resolution, until the
pass catches up and replaces them. And the saving is view-dependent — at
fit-to-window there is 4.2x in it, at 100% zoom there is none and a fast stroke
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

## Decided 2026-09-02: preview is the default

Both arms were drawn on the owner's machine on 2026-08-27 (B322's captures of
22:52–22:59): refusals roughly halved, the stamp 3.4x cheaper, and the owner's
verdict was *"I wasn't able to feel or see any discernible difference between
A & B."* Asked again on 2026-09-02, with the standard stated as *nothing less
than Photoshop, Krita and Clip Studio responsiveness*: **make it the default.**

So the variable inverts rather than disappears — `LIGHTBOX_TIP_SCALE=document`
(or `full`, or `0`) pins the old arm, `preview` still names the new one so an
old capture recipe keeps meaning what it meant, and unset is preview. The render
report names which ran either way. At 100% zoom the two arms are the same
pixels, so the decision costs nothing there and buys headroom everywhere else —
headroom rather than a fix, which B322's entry is careful to say.
