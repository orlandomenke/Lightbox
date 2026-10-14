# Q270 · Who draws an AI inbetween, the model or the engine — **answered 2026-10-14**

**Answered: the model matches, the engine draws.** The model looks at both keys
and says which line in key A is which line in key B: the direction, and where
one line is two in the other key. The deterministic inbetweener then draws the
frame from that answer, so every line keeps the artist's own shape and brush.
The branch that had the model draw (`feat/ai/key-pictures`) is parked rather
than merged; its picture builder and correspondence test carry over. Two
questions asked together with `AskUserQuestion`, both answered as recommended.

Raised by the owner on 2026-10-14, after a traced leg drawing came back with
every AI frame refused and the automatic inbetween scrambled:

> "But we cannot assume an artists draws each frame the same right? … One of
> the reasons we have the stroke record is due to AI inbetweening options to be
> better than pure pixels. So let's not nitpick about stroke direction. This
> should work"

What it blocks: AI inbetweening of keys whose strokes do not pair up, which
covers every traced drawing and most hand-drawn ones.

## What was tried first, and what it showed

`feat/ai/key-pictures` sent the model captioned pictures of both keys (the
layer alone, cropped, and the whole visible scene), dropped "keep stroke count"
from the prompt, and judged frames by shape when the keys' strokes do not pair
up. On the traced `LegAndFeetLines-Back` layer, frames 1 → 3, at low effort:

| | before | with pictures |
| --- | --- | --- |
| accepted | none, after 3 calls | every run, first call |
| cost per run | $1.31 | $0.30–0.36 |
| time per run | 325 s | 55–85 s |

But all four drawings failed in the same way: the back leg lost its knee and
shin, the shoes became blobs, and 21 strokes shrank to 12–16. A prompt line
saying "each part appears once, partway along; a hidden part keeps its shape"
made it worse (12 strokes). A high-effort run did not answer within the
10-minute limit. The owner's verdict: "Yeah the result is shit". The pictures
fixed which line is which; drawing exact coordinates for a complex pose is
where the model fails.

## What was asked, and what was answered

**1. Which way next.** *Model matches, engine draws* (recommended): **chosen**.
The answer is short, so it should be cheaper than the model drawing, and the
artist's correspondence pins (the third part of the 2026-10-14 plan) feed the
same path. Against: *shape matching first, with no AI* (free per run, but
harder on this pair, because the back leg's foot disappears behind the other
leg), and *keep tuning the drawing prompt* (the evidence said it tops out low).

**2. The key-pictures branch.** *Park it and reuse the pictures* (recommended):
**chosen**. Against: *ship it as an improvement*, which would mean frames the
owner called unusable, behind a shape check the art director found too loose
(it would let a double exposure or a copied key through).

## What it costs

- A new answer schema and a correspondence-driven path through the
  inbetweener: roughly one to two sessions.
- A part that goes out of sight (the back foot here) still needs an answer the
  engine can draw. That is the first thing the new route has to show.
