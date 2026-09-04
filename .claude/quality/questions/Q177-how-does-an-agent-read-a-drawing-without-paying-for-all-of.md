# Q177 · How does an agent read a drawing without paying for all of it — **answered (a) 2026-09-03: a listing tool plus a filter, full read stays the default**

Raised by: measuring the MCP surface after the observation that
`docs/DESIGN-ai-payload.md` costs a *request* and an MCP reply is not one.

What it blocks: the shape of `get_frame_strokes`, and every read tool added
after it — the ~38 document-level commands the roadmap still owes an agent.

## The finding that made this a question

An MCP reply is spent out of the agent's **context**, not out of a request. The
difference is not a detail: a request is built, sent and paid once, while a tool
result stays in the conversation and is re-read on every turn after it. So the
existing design note's two halves — bytes against tokens — do not describe this
surface, and nothing in the suite measured it.

Measured on a 120-stroke, 90-point frame through the real path:

| | Reply | ≈ tokens |
| --- | --- | --- |
| `get_frame_strokes` | 147.4 KB | ~37,700 |
| a listing of the same drawing | 10.1 KB | ~2,594 |
| three strokes named out of it | 3.7 KB | ~950 |

In most tasks an agent read the 37,700 only to learn **which strokes exist**.

## The options

- **(a) A listing tool plus a filter; the unfiltered read stays the default.**
  `list_frame_strokes` gives index, label, colour, point count and a box per
  stroke; `get_frame_strokes` grows `labels` and `indices`.
- **(b) The same, but the listing becomes what `get_frame_strokes` returns by
  default**, with geometry opt-in.
- **(c) The filter only, no listing.**

**Recommendation: (a)** — and what the alternatives cost. (b) saves strictly
more, because it makes the cheap path the one an agent falls into rather than
one it has to know about; that is a real advantage and it is why the option was
put up. It was declined because it changes what every existing agent gets back
from a call it already makes, and a silent halving of a reply is the kind of
change that is discovered as a wrong drawing rather than as an error. (c) is
unusable on first contact: an agent cannot ask for labels it has not been told
exist, so the filter without the listing is a feature only a second session
could use.

**Answered (a), 2026-09-03.** The cost of (a) over (b) is named rather than
waved away: the expensive path is still the default one, so this saves nothing
from an agent that does not know the listing is there. Two things carry that
weight instead of a default — `get_frame_strokes`'s own description says a dense
frame runs to tens of thousands of tokens and to prefer naming what it needs,
and `list_frame_strokes` says it is about a twelfth the size. If measurement
later shows agents ignoring both, (b) is the escalation and this entry is the
record of why it was not taken first.

## What the answer had to carry

- **One list, so an index means one thing.** Both stroke ops number the same
  effective record through one helper; two walks with different filtering would
  make the round trip silently fetch the wrong line.
- **A refusal, never an empty array.** A label that is not there is refused by
  name with the present labels listed — `import_character`'s rule, for
  `import_character`'s reason: an agent cannot see the drawing, and an empty
  reply from a misspelled label reads as "that stroke is gone".
- **The box is what makes it usable.** Labels alone say what is in a drawing; a
  label and a box say which strokes a change would touch, which is the question
  actually in front of an agent. It comes from `TransformOps.Bounds(Stroke)` so
  it cannot disagree with the transform gizmo about the same stroke.

Related: [[Q18]] — flat arrays for points, objects for everything else — is
applied here to the box and to nothing else.
