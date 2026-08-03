# What an AI request actually costs

Status: **measured, mostly not acted on.** The question was whether
compressing images, optimising the JSON, or moving to GraphQL would make AI
assistance faster. The answer is only useful with numbers, so here they are
first and the conclusions second.

Measured with `AiPayloadBudgetTests` (stroke JSON) and a throwaway Skia harness
(images), on synthetic line art that is *denser* than a real character sheet —
so the image figures are a pessimistic bound, not a typical one.

## The two halves, and why they pull in opposite directions

**Stroke JSON**, per inbetween request, at `MaxWirePoints = 32`:

| Frame pair | User payload | ≈ tokens | gzipped | as flat arrays |
| --- | --- | --- | --- | --- |
| 12 strokes | 30.8 KB | ~7.9k | 4.1 KB (13%) | 13.3 KB (43%) |
| 40 strokes | 102.1 KB | ~26k | 19.0 KB (19%) | 44.1 KB (43%) |
| 120 strokes | 309.5 KB | ~79k | 55.5 KB (18%) | 136.0 KB (44%) |

The token column is characters ÷ 4, which is **a floor**: numeric JSON
tokenizes worse than prose because tokenizers split digit runs. Treat these as
"at least this many".

**Reference images**, one 960×540 view (the default) as base64 PNG:

| Size | PNG + base64 | Encode | Image tokens |
| --- | --- | --- | --- |
| 1920×1080 | 829 KB | — | ~2764 |
| 960×540 | 333 KB | 52 ms | ~691 |
| 768×432 | 244 KB | 38 ms | ~442 |
| 512×288 | 135 KB | — | ~196 |

Put a typical request together — 40 strokes, two reference views:

|  | Bytes | Tokens |
| --- | --- | --- |
| Stroke JSON | 102 KB (13%) | ~26k (95%) |
| Two images | 666 KB (87%) | ~1.4k (5%) |

**That inversion is the whole answer.** The images are almost all of the
upload and almost none of the cost. The strokes are almost none of the upload
and almost all of the cost. Any optimisation that helps one is aimed at the
wrong half unless you say which you meant.

The fixed overhead — system prompt 1336 B, schema 1151 B — is 2% of a
40-stroke request. There is nothing to win there and it should be left alone.

## The three things that were asked about

### Compressing the request — no

Gzip takes 82% off the bytes, and that is genuinely a lot of bytes. It buys
almost nothing:

- **It does not touch tokens.** The model tokenizes the decoded text. Cost and
  generation time are unchanged; only the upload shrinks.
- **The upload is not the wait.** 768 KB on a 20 Mbit link is about 0.3 s
  against 30–120 s of generation. Removing 82% of 0.3 s is a rounding error on
  a bar the artist watches for a minute.
- **Neither Anthropic nor OpenAI documents gzipped request bodies.** Response
  compression is standard; request compression is not, and it would have to be
  disabled per provider the first time one rejected it.

A real optimisation that is invisible next to the thing it sits beside is not
an optimisation. Compression is that.

### GraphQL — no, and it is a category error

GraphQL is a query language for an API you control, so a client can ask for
exactly the fields it wants. The provider endpoints are fixed REST contracts
someone else defines; there is no schema of theirs to query and no server of
ours in the path. Adding it would put a layer between Lightbox and Lightbox.

The one place the *idea* applies is the opposite direction — an agent asking
Lightbox for parts of a document rather than all of it. That is what
`Lightbox.Mcp` already does, with tools instead of a query language, and it is
the right shape for it.

### Optimising the JSON — yes, but not the way it looks

Two candidates, and only one of them is the big one.

**Flat point arrays** save 57%: `[123.4,567.8,0.55]` against
`{"x":123.4,"y":567.8,"pressure":0.55}` repeats three keys per point, and a
40-stroke pair carries 2560 points. That is the largest single encoding win
available.

It is **not obviously worth taking**. `StrokePayload.cs` says the wire shape
mirrors the document format because that "measurably improves schema
adherence" — a claim that is undated, unmeasured in this repo, and plausible:
a model that has seen a million `{"x": …}` objects has seen very few
`[[123.4,567.8,0.55], …]`. Trading 57% of the tokens for a model that
occasionally drops a label or transposes a coordinate is a bad trade, and
whether it does can only be settled by running both against a real provider.
Recorded as **Q18** rather than guessed.

**Sending fewer strokes is six times bigger than any encoding trick**, and it
is the thing actually worth building. A 120-stroke frame is ~79k tokens, and
in most inbetweens the great majority of those strokes barely move. The
deterministic inbetweener already handles a matched stroke correctly; the AI
is needed where straight interpolation fails — arcs, rotation, overlap. So the
request should carry the strokes that need judgement plus enough context to
place them, not the whole drawing. Halving the stroke count halves the cost
exactly, with no risk to the wire format at all.

## What is worth doing to the images

They are 87% of the bytes for 5% of the cost, and there is one measured defect
among them.

**The encode happens on the UI thread before every call.**
`CollectReferenceImages` renders, composes, PNG-encodes and base64s up to two
views synchronously in `AiInbetweenAsync` before the request is built — about
52 ms each at 960×540, so **~100 ms of stall on every AI call**, repeated
identically each time because nothing changed between calls. A content-keyed
cache removes all of it. This is the only thing in this document that is a
defect rather than a preference (**B31**).

**Cap the long edge.** Views are sent at their authored size, and a view is
960×540 by default but nothing stops it being 4K. Billing is by area
regardless of file size, so a cap is both a token and a byte win: 768 px long
edge is 442 tokens against 691, and 244 KB against 333 KB. Line art survives
the downscale; it is the shape the model needs, not the pixels.

**WebP halves the bytes and doubles the encode** — 196 KB against 333 KB, but
106 ms against 52 ms. On the UI thread that is a bad trade. Once the cache
above exists the encode happens once, and it becomes free to reconsider.

**Not JPEG.** Ringing around black lines on white is exactly the artefact that
makes a drawing harder to read, and the model is being asked to read it
precisely. Every provider here accepts PNG.

**Prompt caching** (Anthropic `cache_control`) is the cheap one. The system
prompt, the schema and the reference images are byte-identical across every
call in a session, so after the first they can be cached — about 90% off ~1.4k
image tokens per call. Small, but it costs one field.

## Order

1. **B31** — cache the encoded reference views. A measured 100 ms of UI stall
   before every AI call, and the only defect here.
2. **Cap the reference long edge**, with the cap on the request rather than on
   the view: the artist's sheet stays whatever size they drew it.
3. **Send the strokes that need judgement**, not the frame. The 6× win, and
   the one that needs design rather than a constant.
4. **Prompt caching** on the providers that offer it.
5. **Q18** — the flat-array encoding, once somebody has A/B'd adherence
   against a real provider.

Numbers 1, 2 and 4 are mechanical. Number 3 is the interesting one and it is
the same question `DESIGN-subject-reading.md` is circling from the other side:
knowing *which strokes matter* is knowing what you are looking at.

## The rule this leaves behind

**Say which half you are optimising.** Bytes and tokens differ by more than an
order of magnitude in opposite directions here, so "make the payload smaller"
is not a goal — it is two goals that recommend opposite changes. The same
mistake as the saturation trap in `CLAUDE.md`: the number was real and the
attribution was not.
