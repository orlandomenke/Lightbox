# Q180 · Should an agent read a frame as a picture rather than as geometry? — **measured 2026-09-04: the committed set cannot separate the arms, and finding that out found B360**

Raised by the owner, 2026-09-04, out of the bitmap discussion in [[Q179]]:
*"then it might be worth while to let the AI read the bitmaps and return
strokes?"* — a better question than the one it came from, and about a different
half of the surface.

> **Note for whoever merges this.** The question was filed on
> `feat/ai/mcp-cheap-reads` and answered on `feat/ai/Q180-golden-pair-harness`.
> Same id, same path, and this copy is a superset of that one — take this side.

What it blocks: whether `get_frame_strokes` is the normal way an agent reads a
drawing at all, and therefore how much the listing and the render cap matter.

## Why it was worth asking

**Nothing needed building to try it.** `render_frame` and `list_frame_strokes`
both exist as of Q177/Q178, so the shape was reachable immediately:

| What the agent reads | ≈ tokens (120-stroke frame) |
| --- | --- |
| `get_frame_strokes` — the geometry | ~37,700 |
| `render_frame` — a picture of it | ~770 |
| `list_frame_strokes` — labels, colours, boxes, counts | ~2,600 |
| **picture + listing** | **~3,400 (≈11×)** |

**And it is how the job is actually done.** An inbetweener looks at two drawings
on a lightbox and draws the one between; nobody reads a coordinate list. The
application is named after the device.

## How it was measured

`GoldenPairArms` dumps every pair in `GoldenSet.Short()` (7 pairs) as three
mutually blind arms and scores the answers back through the real
`StrokeParsing` and `CapabilityProfiler`:

| Arm | Sees |
| --- | --- |
| `geometry` | exactly `Prompts.InbetweenUser` — what the application sends today |
| `picture` | the two keys rendered as PNGs, plus the listing |
| `listing` | **the listing only, no images** — the control |

**The control is the part that makes this an experiment rather than a demo.**
Every stroke in the committed set is a straight two-point line, so a bounding box
very nearly determines it; without the third arm a picture arm could have scored
perfectly without ever looking at a picture, and the result would have read as
"rasters work" having measured nothing.

Each arm was answered by a separate agent that had not seen this conversation and
was told not to open the other arms' files. That blinding is imperfect — they are
the same model family, and an agent that ignored the instruction would not be
detectable — so the result below is a signal, not a certificate.

## The result: all three arms scored identically

Schema adherence 100%, label retention 100%, every pair accepted, ladder cleared
to 24 strokes — in all three arms. The only difference anywhere in the matrix was
`swing`, where the listing arm bowed the path 5.0 px and the other two did not.

**So the committed golden set cannot tell the three reading modes apart, and this
question is not answered by it.** That is a finding about the set rather than
about the idea, and it is the same gap `GoldenCategory.Organic` already documents
by shipping empty: constructed straight lines exercise schema adherence,
correspondence and the ladder honestly, and they cannot exercise *reading a
drawing*, because there is barely a drawing to read.

## What the arms disagreed about anyway, and why it matters more than the scores

The scores were identical; **the answers were not.** On `arc` — a rod pivoting at
`(128,128)`, reaching up-left in key A and up-right in key B:

| Arm | Answer at t=0.5 | |
| --- | --- | --- |
| `geometry` | `(128,128)-(128,16.8)` | rod straight up — correct |
| `picture` | `(128,128)-(128,16.8)` | rod straight up — correct |
| `listing` | `(128,60)-(128,171.2)` | **rod straight down, pivot displaced** |

**A bounding box cannot tell `/` from `\`.** The boxes-only arm had to guess which
diagonal each key ran along, picked the wrong corner as the pivot, and swung the
pendulum to the side neither key occupies. The picture arm got it right *because
it could see it* — which is direct evidence that a raster carries information the
listing cannot, on exactly the axis this question is about.

So the honest reading is: **the cost case is strong, the accuracy case is
unproven, and the one visible divergence favours the picture.** The picture arm
also reported reading `ladder-24`'s 9-px tooth spacing off the listing rather
than off the 256 px image, which is the coordinate-accuracy worry showing up
exactly where it was predicted.

## The bug this turned up — B360, and it is the real yield

All three arms scored `Arc: clean (1/1)` **including the one that drew the
pendulum backwards.** Put directly to `InbetweenVerifier.Verify`, the mirrored
answer is accepted with no fault and no note, and its departure from the free
engine is reported as **21.6 px — identical to the correct answer's**, for
drawings whose tips are 154 px apart.

That is worse than the set being weak. A verifier whose stated job is to reject
"not between the keys at all" accepted ink 43 px below a swing where neither key
has any ink below the pivot. Filed as **B360**.

## What still has to be settled

- **The set needs a pair a box cannot answer** before this question can be
  retried: a curve, or a stroke with real interior points. That is a change to a
  *published* claim (Q34), so it is its own decision and not a tweak.
- **Coordinate accuracy at scale is still unmeasured.** These canvases are
  256 px and the keys are unmissable. The worry was a line read off a 768 px
  render of a 1920 or 3840 px canvas, and nothing here touches that.
- **If this is ever answered yes, [[Q178]]'s render cap probably does not
  survive it** — a face that loses its eyebrows at 768 is one the model never
  knew had any.
- The in-app path (`AiInbetweenAsync`) sends stroke JSON plus reference views; if
  picture-first wins, it wins there too, and that is a much larger change than a
  tool description.

## Recommendation

**Do not change any tool description or prompt on this result.** It is one model
family, one run, seven degenerate pairs, and the headline is that the instrument
could not measure the thing. Fix B360 first — a verifier that certifies a
mirrored arc will certify anything this experiment produces — then add a pair
with genuine curvature and run the three arms again. The harness is committed and
takes one environment variable, so the retry is cheap.
