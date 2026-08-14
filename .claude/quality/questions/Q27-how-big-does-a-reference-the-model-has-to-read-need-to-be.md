# Q27 · How big does a reference the model has to read need to be? — **answered (d)**

**Answered 2026-08-07: (d), choose per view from what is in it.** Measure thin-
line density and pick the cap, rather than sending a face turnaround and a
walk-cycle sheet at the same size.

**The objection recorded against (d) still stands and has to be answered in the
build, not argued away:** a heuristic that decides what leaves the machine is
unpredictable in the way invariant 4 distrusts, one level up from pixels. It is
answerable, and cheaply — **make the choice visible and overridable**:

- The request shows what cap each view got, so the artist can see that the face
  sheet went at 1024 and the silhouette at 512.
- A view can be pinned to a size, which is (c) surviving inside (d) as the
  escape hatch rather than as the mechanism.
- The heuristic is a pure function of the view, tested on the same fixtures
  `RenderReferenceViewPng` already has, so it is inspectable rather than felt.

**That guard is inferred rather than given** — say so if the intent was the bare
heuristic. Without it this is a number nobody can predict changing what the
model sees, which is the one failure mode the objection named.

**Blocks:** nothing.


B31 capped reference views at **768 px on the long edge** on the way into a
request, and the number is doing real work: providers bill by area regardless of
file size, so 768 is 442 image tokens against 691 at 960, and 7 KB against
115 KB on a 1080p sheet. The cap is on the request, never on the view — the
artist's sheet stays whatever they drew.

**art-director's objection, with pictures.** Rendered through the real
`RenderReferenceViewPng(view, longEdge)` path and compared at authored, 768 and
512: a body silhouette at `Size 3–4` survives even 512, and this is the case the
cap was measured against. A **face close-up** and a **head drawn at natural
scale on a full-body sheet** do not — eyebrows go, the eyes reduce to grey
smudges, cheek hatching disappears. Mipmapped linear minification greys a thin
dark line toward the ground rather than keeping it crisp-and-small, which reads
to a model as *the line is not there* rather than *the line is thin*. The
failure it predicts is the quiet kind: an inbetween that goes subtly off-model
on the face, with nothing in the request or the response saying why.

**The caveat I owe the measurement, because it is this repo's recurring trap.**
The fixture's thin lines were drawn at pressure 0.25–0.5, and pressure drives
both size *and* flow here, so they are already hairline-faint at authored
resolution — visible, but at the edge of it. The cap made a marginal line
invisible; it did not make a solid line marginal. That is still a real cost and
it is a smaller one than the images alone suggest. *The number was real and the
attribution was partial* — same shape as the saturation trap, and worth stating
before anyone re-derives the conclusion from the pictures.

**ai-engineer's position** is that the token cost of a bigger cap is small in
context: 768→1024 is roughly 442→786 image tokens against the ~26k a stroke
payload already spends, so about 1.3%. It does not want the cap removed —
uncapped means a 4K sheet billed as a 4K sheet — and it will not spend the
budget on a number nobody has A/B'd against a real provider's output.

Four ways out:

**(a) Leave 768.** Cheapest, and defensible for pose, silhouette and
proportion — which is most of what a reference is asked for. Accepts the
facial-detail loss.

**(b) Raise it to 1024.** ~1.3% more tokens, and the two failing cases above
come back inside legibility. Still one number for every kind of view, which is
the thing art-director actually objected to.

**(c) Per-view opt-out, absent until asked for.** A view marked *detail* is sent
at its authored size. Fits the house rule exactly — the record grows a key only
for a view that used it — and puts the trade where the artist can see it, since
they know which sheet is the face turnaround. Costs a setting and a UI for it.

**(d) Choose per view from what is in it.** Measure thin-line density and pick
the cap. Best result on paper, and it makes what leaves the machine depend on a
heuristic nobody can predict — the *shape* invariant 4 is suspicious of, one
level up from pixels.

Not answerable by measurement in either direction: art-director can show that
768 loses facial construction and cannot show that 1024 is enough, and
ai-engineer can price a cap and cannot price a worse inbetween. **(c) is the one
I would open the argument with**, because it is the only option that does not
pretend a face turnaround and a walk-cycle sheet want the same number. (b) is
the cheap interim if a setting is too much for now.

---
