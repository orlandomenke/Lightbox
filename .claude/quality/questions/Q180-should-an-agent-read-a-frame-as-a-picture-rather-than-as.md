# Q180 · Should an agent read a frame as a picture rather than as geometry? — **open**

Raised by the owner, 2026-09-04, out of the bitmap discussion in [[Q179]]:
*"then it might be worth while to let the AI read the bitmaps and return
strokes?"* — which is a better question than the one it came from, and about a
different half of the surface.

What it blocks: whether `get_frame_strokes` is the normal way an agent reads a
drawing at all, and therefore how much the listing and the render cap matter.

## Why it is worth asking

**Nothing needs building to try it.** `render_frame` and `list_frame_strokes`
both exist as of Q177/Q178, so the whole shape is reachable today:

| What the agent reads | ≈ tokens |
| --- | --- |
| `get_frame_strokes` — the geometry | ~37,700 |
| `render_frame` — a picture of it | ~770 |
| `list_frame_strokes` — labels, colours, boxes, counts | ~2,600 |
| **picture + listing** | **~3,400 (≈11×)** |

**And it is how the job is actually done.** An inbetweener looks at two
drawings on a lightbox and draws the one that goes between; nobody reads a
coordinate list. The application is named after the device. A wire format that
insists the model read numbers where a person reads a picture is not obviously
the faithful one — which is an argument art-director should be allowed to make
at full strength before the engineering answer is settled.

**The listing is what makes it more than a picture.** A raster says where the
ink is and nothing about what the strokes *are*: no labels, no colours as
authored, no correspondence to key A and key B. That is exactly the gap
`list_frame_strokes` fills, and cheaply. So the pairing is not "picture instead
of data" but **picture for the shapes, listing for the identities** — and the
one thing neither supplies is the precise coordinates, which is the whole
question below.

## What this does *not* do

**It does not fix [[Q179]] or B359.** The ceiling is on what the model must
*emit*, and this changes only what it reads. Three inbetweens are still ~113,200
output tokens. Worth stating plainly because the two ideas arrived in the same
conversation and are easy to merge by accident.

## What has to be settled, and it is measurable

- **Can a model produce numerically accurate coordinates from a raster?** This
  is the crux and the likely failure. It must read a line off a 768 px image and
  emit scene-pixel coordinates on a canvas that may be 1920 or 3840 wide — a
  scale factor it has to apply itself. Models are weak at exactly this. If the
  strokes come back plausible-looking and a few percent off, the result is a
  drawing that reads as sloppy inbetweening rather than as a bug.
- **Does it beat geometry on the golden set, or only on cost?** Q34's scores are
  the instrument, and this is a fair fight to run: same pairs, geometry in
  against picture+listing in, scoring betweenness, label retention and the
  temporal coherence `InbetweenVerifier` already checks.
- **What does it do to the render cap ([[Q178]])?** Today 768 px is defended as
  *inspection* — a spot-check, with the authored size a call away. If a picture
  becomes the primary input, art-director's measurement stops being a caveat and
  becomes the main event: a face that loses its eyebrows at 768 would be a face
  the model never knew had any. The cap's answer may not survive this question
  being answered yes.
- **Which reading does the in-app AI path use?** `AiInbetweenAsync` sends stroke
  JSON plus reference views. If picture-first wins here it probably wins there
  too, and that is a much larger change than an MCP tool description.

## Recommendation

**Measure before deciding, and the measurement is cheap** — both tools exist, so
this is a golden-set run rather than a build. It goes to the pair by
construction: ai-engineer owns whether the coordinates come back accurate and
reproducible, art-director owns whether the inbetweens are better, and those two
answers could easily disagree. Nothing should change in the tool descriptions
until they do — steering agents toward a picture-first workflow on an untested
hunch would be the cheap-path mistake [[Q179]] is otherwise about.
