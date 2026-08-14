# Q18 · Do flat point arrays cost schema adherence? — **answered (c)**

**Answered 2026-08-07: (c), flat arrays for points only, objects for everything
else.** Points are 99% of the volume and the only part that repeats; `tool`,
`color` and `label` keep their names, so the field whose loss actually costs an
inbetween keeps its key.

**Adopt it with the measurement rather than instead of it.** The adherence
claim in `StrokePayload.cs` was undated and unmeasured, and this answer does not
make it true — it makes the risk small enough to take. The golden set (Q34) is
the natural place to watch it: **label retention** belongs in the scores, so a
regression shows up as a number rather than as a bad inbetween somebody notices
weeks later.

**Blocks:** nothing.


The measurement is settled and the trade is not. `docs/DESIGN-ai-payload.md`
has the numbers: writing a point as `[123.4,567.8,0.55]` instead of
`{"x":123.4,"y":567.8,"pressure":0.55}` takes **57%** off the payload, and at
2560 points in a 40-stroke frame pair that is the largest encoding win
available — 102 KB down to 44 KB, ~26k tokens down to ~11k.

Against it, `StrokePayload.cs` says the wire shape mirrors the document format
because it "measurably improves schema adherence". That claim is undated,
unmeasured anywhere in this repo, and entirely plausible: a model has seen a
great many `{"x": …}` objects and very few positional triples, and positional
encodings invite exactly the failure that matters most here — a transposed
coordinate, or a dropped `label`.

**A lost label is a lost correspondence**, which is a worse inbetween. So this
is not "57% cheaper, ship it"; it is 57% cheaper against a quality risk nobody
has quantified.

What would settle it: the same twenty frame pairs through both encodings on at
least two providers, scoring label retention, point-count fidelity and whether
the inbetween lands between its keys — the check `AiConnectionTester` already
implements. Real API calls, so it is a deliberate spend rather than something
to slip into an unrelated commit.

Three ways it could land:

**(a) Keep objects.** Adherence is worth more than tokens, and the bigger win
is sending fewer strokes anyway — six times bigger, per the same document, and
with no format risk at all.

**(b) Flat arrays everywhere.** If adherence holds across providers, 57% is not
a rounding error and refusing it out of caution is superstition.

**(c) Flat arrays for points only, objects for everything else.** Points are
99% of the volume and the only part that repeats; `tool`, `color` and `label`
stay named, so the field most at risk keeps its key. Probably the answer, and
it is still a guess until somebody runs it.

This is the standing disagreement between **ai-engineer** and **art-director**,
and it is written here rather than settled by whichever of them ran last.
