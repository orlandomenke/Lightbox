# Q178 · Is `render_frame` capped when `render_reference_view` is not — **answered (a) 2026-09-03: yes, capped at 768 with `longEdge: 0` as the way out**

Raised by: the same measurement as [[Q177]], and by B31, which recorded capping
an MCP reply as the *bug* rather than the fix.

What it blocks: whether two sibling image tools may have different defaults, and
what a future agent-facing render inherits.

## Why this needed asking rather than doing

B31 has an explicit, tested answer already, and it points the other way. Putting
the 768 px request cap on the parameterless `RenderReferenceViewPng` also shrank
what `render_reference_view` answered with, and that was caught by
`RenderReferenceView_ProducesDecodablePng` — a test older than the work, which
had asserted the authored width since the feature landed. The entry's sentence
is the one to argue against: **an agent asking for a picture of a view should
get the view.** The cap was moved to `EncodedReferenceView`, the one call site
where "a provider bills this by area" is true.

So capping `render_frame` is either a straightforward extension of a good rule
or a repeat of a mistake, depending on whether the two tools are the same kind
of thing.

## The options

- **(a) Default-cap `render_frame` at 768; `longEdge: 0` returns the authored
  canvas.**
- **(b) Add `longEdge`, keep authored size as the default** — consistent with
  the sibling.
- **(c) Cap both.**

**Recommendation: (a).** The two tools are not the same kind of thing.
`render_reference_view` answers *with a view* — the artist's reference art is
the deliverable, and shrinking it is answering a different question than the one
asked. `render_frame`'s own description says it exists so an agent can **see** a
drawing and check its own results: that is inspection, and inspection does not
need the pixels. At 1080p it costs ~2,764 image tokens against ~442 capped, on
every look, and an agent looks often.

(c) was declined because it reverses a decision that was correct and is guarded
by a test written before the feature it now protects. (b) is honest and saves
nothing — the whole finding is that an agent will not opt in to a cheaper path
it has to know about, which is the same reason [[Q177]] needed its listing tool
advertised in the description rather than merely present.

**Answered (a), 2026-09-03.** Two defaults with two reasons is the outcome, and
that is a cost: a surface where sibling tools behave differently reads as
inconsistency unless the difference is written down where somebody meets it. It
is written down in three places — the tool descriptions, `RenderedFrameLongEdge`
and `RenderFramePng`'s remarks — and the constants are deliberately **not**
shared. They are both 768 today and they cap for different reasons, and [[Q27]]'s
per-view heuristic will make the reference cap a function of what is in the
view; a shared constant would drag `render_frame` along with a decision that was
never about it, which is B31's mistake pointed the other way.

`RenderFrame_IsCappedOnTheLongEdge` replaced an assertion reading 960, and the
replacement is deliberate rather than a concession — that is the behaviour
change this question authorises. Three tests hold the edges: the opt-out returns
the authored canvas, a 400×300 canvas is not upscaled to meet the cap (a ceiling
is not a target), and `RenderFramePng` still answers every in-app caller at the
authored size.

## Amended the same day: the answer was right and the reasoning behind it was not

**Gate G12's art-director refuted the sentence this decision rested on**, by
rendering the case rather than arguing about it. "Inspection does not need the
pixels" was tested against B31's reference sheets and never against a character
inside a full scene, which is what `render_frame` actually shows:

| Canvas | At the 768 cap |
| --- | --- |
| 1920×1080, ~110 px head, brows at pressure 0.25–0.3 | brows faint; dark pixels in the face region **8,398 → 1,330 (−84%)** |
| 3840×2160, same face at the same absolute size | **brows and eyes gone entirely** — a bare head outline |

**It is worse in a scene than on a sheet, not milder**, because a face is a
smaller share of a full frame than of a face-forward reference view. So the
guess embedded in the recommendation — that a scene would be more forgiving —
was backwards. And the tool is used "to check your own results after inserting
frames": an agent inbetweening a face would see a browless head whether it had
drawn the brows correctly, incorrectly, or not at all.

**The cap stands; its silence does not.** Put back to the owner with the
measurements, 2026-09-03, and answered: keep 768, and **report the scale
applied**. A reduced render now returns a line saying what fraction of the
canvas it is and that fine marks are gone at this size, and a full-size render
returns no such line — absent unless it says something, because a note on every
render is one nobody reads by the time it counts. That is exactly the guard
[[Q27]] recorded as a *condition* of choosing a cap at all — "the request shows
what cap each view got" — arriving here on the same reasoning.

What was declined, and why it is still the live alternative: a scale-aware
default (never reduce past ~0.5×) answers the structural complaint that a flat
cap fails harder the larger the canvas, which is [[Q27]]'s argument again. It
unbounds cost exactly where cost is largest, so it needs its own measurement to
pick a floor, and it is the escalation if agents turn out not to act on the
note. `ACappedRenderSaysHowMuchOfTheCanvasYouAreSeeing` guards both halves.

**The lesson worth keeping is about the evidence, not the number.** An answer
reached from the adjacent case looked identical to one reached from the case in
hand, and only rendering it told them apart — which is why the pair renders.
