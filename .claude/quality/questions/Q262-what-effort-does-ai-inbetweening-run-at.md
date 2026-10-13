# Q262 · What effort does AI inbetweening run at? — **answered 2026-10-13**

**Answered: low by default, with an Effort field on both Claude providers to raise it.** As recommended, after measuring first — which was the owner's answer to the question before this one.

Raised by the first run of real drawings through the inbetweener (2026-10-13): thinking was 60–90% of each request's output tokens and most of its time.

What it blocks: an inbetween on a line-art layer of 760 points took nine and a half minutes for one frame, which is not a tool an animator can use.

## What was measured

Four layers of the owner's test document, first key pair, one inbetween at the midpoint, through the Claude Code provider with the verifier on — once at the model's default effort, once at medium, once at low. Time is wall-clock for the whole run including any re-ask; cost is the list-price equivalent Claude Code reports.

| Layer | Default | Medium | Low |
| --- | --- | --- | --- |
| Shading (4 strokes, 2,578 points) | 1.6 min · $0.29 | 0.7 min · $0.17 | 0.8 min · $0.17 |
| Thin lines (4 strokes, 1,034 points) | 1.6 min · $0.30 | 1.5 min · $0.28 | 1.4 min · $0.27 |
| Arm colour (3 strokes, 3,561 points) | 3.5 min · $0.62 | 2.2 min · $0.43 | 1.8 min · $0.37 |
| Arm lines (7 strokes, 463 points) | 9.5 min · $1.50 | 2.6 min · $0.48 | 1.4 min · $0.31 |

All twelve runs were accepted (the arm colour on its second ask at every level). Rendered side by side with the artist's brushes restored (B468), the frames were indistinguishable across the three levels on three layers compared; the one bad result — the arm colour, drawn as a small scribble — was the same at every level, so it was not an effort problem.

## What was asked, and what was answered

**1. Measure a lower effort before changing anything?** *Measure first* (recommended). Against: lowering it unmeasured, or leaving the default.

**2. With the numbers in: which default?** *Low, adjustable* (recommended). Against: medium as a safety margin (1.1–3.7× faster than default on the same layers, a little dearer than low); or keeping the model default.

## What it costs

- **Four layers of one drawing is a small sample.** A harder subject may want more thinking, which is why this is a field and not a constant.
- **The API route was not measured.** The numbers are from the Claude Code route; the API route sends the same model the same request with the same effort parameter, and is assumed to behave alike.
- **A typo is forgiven silently.** A value that is not one of the five levels is read as low, so a request never fails halfway over a settings field — and a misspelt "hihg" runs at low without saying so.

## Where the reviewers and the decision met

The ai-engineer's must-fix was to send effort only when the artist set it, so a model that rejects the setting never gets a field nobody typed. That would have made the model's own default the default again, which is the opposite of what was decided here. Both concerns are kept: low is still sent by default, `model` is a level that sends nothing at all, and a rejection that mentions effort is reported as one field to change rather than as the service's raw error. The capability profile's subject now names the effort it was measured at.

Also not measured: reading a character from its sheets now runs at low as well. It is a short description rather than a drawing, and it was not part of the comparison.

The art-director's list of what should fail first at low effort, for the next measurement: forms revealed or hidden by an overlap, layers of forty or more strokes (count dropped labels and swapped strokes, not only "accepted"), limbs that could rotate the short way round, and gaps of three or more inbetweens — on a second character and a non-running action, flipped at 12 fps rather than paused.
