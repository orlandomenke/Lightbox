# Q270 · Who draws an AI inbetween, the model or the engine — **answered 2026-10-14, revised the same day**

**Answered, finally: the artist matches, the engine draws — no AI, for now.** The
first answer below (the model matches) was overtaken the same day by what the
experiments showed; both are kept, in order, with what moved the decision.

**First answer: the model matches, the engine draws.** The model looks at both keys
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

## What happened next, the same day

**The model's matching was not good enough.** Shown both keys with every stroke
numbered, it answered in 9–11 s for $0.05–0.07 — but called parts of the back
leg an arm and paired them wrongly. The traced keys made it harder than usual:
the tracer runs one stroke across several parts (waistband, front of the leg and
top of the shoe in one), so the strokes had to be cut at junctions and corners
(56 and 40 pieces) before any matching could be said at all.

**The engine, given a correct match, works.** A hand-written match produced a
believable inbetween in the artist's own lines — both legs whole, the back leg
folding between the poses — once parts of many small lines (cuffs, shoes, the
waistband) were moved as one thing and each line bent towards the other key's
nearest ink. `MatchedInbetween` is that engine.

**The owner then asked why matching was needed at all** — "I just want a frame
that is logical as a inbetween" — and two more routes were tried:

| route | result | cost |
| --- | --- | --- |
| Claude draws, then sees its own attempt over the keys and corrects it, 3 rounds | round 2 = round 1, round 3 worse (the back leg became a stick); the verifier accepted all three | $0.99 |
| Gemini image models (Nano Banana 2 / Pro), keys in, picture out, traced back to strokes | excellent on-model line art, but it does not interpolate: it overlays both keys, copies one, or copies halves of each. One attempt landed between, by luck; spelling out "each part midway", adding a red/blue guide, or handing it a rough to clean up all made it copy more. With only the rough and key A as a style reference it still returned key A | ~€1.45, 8 images |

The verdict on the Gemini route: whatever pose is in a reference picture is the
pose it draws. Its drawing is not the problem; its timing is, and no prompt
tried moved it.

**So the route is the one that already produced a usable frame without a
model: the artist says which line is which, and the engine draws.** The AI may
pre-fill a match later, when a model can be shown to do it reliably.

## What it costs, revised

- A matching tool: the artist marks which lines go together; the engine
  (`MatchedInbetween`, built) draws. Its design is still to be asked.
- Known engine gaps: a part hidden in one key fades by halfway instead of
  travelling and going out of view; small wobble on a shoe sole; a gap where a
  line's pieces meet.
