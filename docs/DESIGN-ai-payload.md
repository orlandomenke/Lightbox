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
Recorded as **Q18**, and **answered (c) on 2026-08-07**: flat arrays for points only, objects for everything else. Points are 99% of the volume; `tool`, `color` and `label` keep their names, so the field whose loss costs an inbetween keeps its key. The adherence claim is still unmeasured — label retention goes into the golden set's scores (Q34) so a regression is a number rather than a bad inbetween somebody notices weeks later.

**Sending fewer strokes is six times bigger than any encoding trick**, and it
is the thing actually worth building. A 120-stroke frame is ~79k tokens, and
in most inbetweens the great majority of those strokes barely move. The
deterministic inbetweener already handles a matched stroke correctly; the AI
is needed where straight interpolation fails — arcs, rotation, overlap. So the
request should carry the strokes that need judgement plus enough context to
place them, not the whole drawing. Halving the stroke count halves the cost
exactly, with no risk to the wire format at all.

## What is worth doing to the images

They are 87% of the bytes for 5% of the cost, and there was one measured defect
among them. **Both of the mechanical items below have landed** (B31); the
numbers are kept as recorded rather than rewritten, because what the fix is
worth is only legible against what it cost before.

**The encode used to happen on the UI thread before every call.**
`CollectReferenceImages` rendered, composed, PNG-encoded and base64'd up to two
views synchronously in `AiInbetweenAsync` before the request was built — about
52 ms each at 960×540, so **~100 ms of stall on every AI call**, repeated
identically each time because nothing changed between calls. It is now memoised
per view and thrown away by `MarkDocumentEdited`: measured **225 ms cold, 0.03 ms
warm**. `ReferenceImagePayloadTests` guards both halves.

The invalidation is the part that was worth testing hardest, and it is the one
place this document's own plan was wrong. It proposed hanging invalidation off
`OnDocumentChanged`; that method takes an early return for scoped edits, and a
stroke commit is exactly that — so a cache keyed there would have survived the
edit it exists to notice, and handed the model a picture of art the artist had
already changed. Nothing in the result would say so; it would read as a worse
inbetween. `MarkDocumentEdited` is the real per-edit funnel.

**The long edge is capped on the way out**, on the request rather than on the
view, so the artist's sheet stays whatever size they drew it. Views were sent at
their authored size, and a view is 960×540 by default but nothing stops it being
4K. Billing is by area regardless of file size, so the cap is both a token and a
byte win: 768 px long edge is 442 tokens against 691, and 244 KB against 333 KB.
Measured on a 1920×1080 view it sends **7 KB instead of 115 KB**. Line art
survives the downscale — asserted rather than assumed, by counting dark pixels
in the downscaled reference against an empty sheet of the same size.

A cap is a ceiling and not a target: a 400×300 sheet is sent at 400×300.
Upscaling would spend tokens on pixels the artist never drew.

**768 is settled — Q27, answered (d) on 2026-08-07: choose the cap per view from what is in it.** One number never fitted both a face turnaround and a walk-cycle sheet. The objection to a heuristic stands and is answered in the build rather than argued away: the chosen cap is shown per view, a view can be pinned, and the heuristic is a pure function tested on the fixtures `RenderReferenceViewPng` already has.
art-director rendered a face close-up and a naturally-small head through the
real path at authored, 768 and 512: a body silhouette survives even 512, but
eyebrows vanish and eyes turn to grey smudges on the faces, because mipmapped
minification greys a thin dark line toward the ground instead of keeping it
crisp-and-small. The caveat belongs with it — those lines were drawn at pressure
0.25–0.5, and pressure drives flow as well as size here, so they were hairline
already; the cap made a marginal line invisible rather than a solid line
marginal. Both halves are in `QUESTIONS.md` along with the four ways out, and
the token arithmetic that makes 1024 cost about 1.3%.

**WebP halves the bytes and doubles the encode** — 196 KB against 333 KB, but
106 ms against 52 ms. On the UI thread that was a bad trade. The cache now
exists, so the encode is paid once per edit rather than once per call, and the
trade is open again — but the cap took most of the bytes already, and bytes are
the 5% half. Reconsider it when the upload is what somebody is waiting on.

**Not JPEG.** Ringing around black lines on white is exactly the artefact that
makes a drawing harder to read, and the model is being asked to read it
precisely. Every provider here accepts PNG.

**Prompt caching** (Anthropic `cache_control`) is the cheap one. The system
prompt, the schema and the reference images are byte-identical across every
call in a session, so after the first they can be cached — about 90% off ~1.4k
image tokens per call. Small, but it costs one field.

## What a repair re-ask costs

Added when the repair loop landed (Phase 3 of `DESIGN-ai-correctness.md`, Q85),
because it is the first thing to make a *request count* variable rather than
one.

**Say which half, and this document has to obey its own rule here.** A repair
changes the *number of calls*, which touches both halves and not in the same
way.

### The stroke half — tokens

| | 40 strokes × 60 points |
| --- | --- |
| First ask | 102.1 KB |
| Ordinary repair, carrying one rejected frame | 153.2 KB — **1.50×** |
| Jitter repair, which also ships the accepted neighbours | 255.1 KB — **2.50×** |
| Worst case: two re-asks, everything refused | **4.00×** across three calls |

It reads as expensive and is the cheaper of the shapes available. A repair
re-sends both keys and adds the rejected frame's strokes, which is why it is
half again rather than a third: the keys *are* the payload, and there is no way
to name a fault in a drawing without the drawing. The alternative — sending the
fault sentence alone — costs a rounding error and is a blind retry with a hint,
which spends a whole call for a much worse chance.

The 2.50× row is why the neighbours are conditional. Only one refusal —
`InbetweenFault.Incoherent`, *"it jitters against the frames beside it"* — is
defined against frames the re-ask would not otherwise carry, so only that one
pays for them. Every other fault names a stroke and a distance the model can
act on with the keys and its own drawing.

### The image half — bytes, and it is the one that surprises

**A repair re-sends the reference images in full, and prompt caching is not
built yet** (Order item 4, below — no artist sets `cache_control`). So a run
with two 960×540 views attached — 666 KB and ~1.4k tokens, from the table above
— uploads roughly **2 MB across a full-refusal run** for essentially no extra
token cost.

That is the shape this document warns about, pointed at itself: the stroke table
says 4× and the byte cost is also 3×, and neither number is the other one. In
practice the image half is latency rather than money — 0.3 s of upload against
30–120 s of generation, tripled, is still invisible — which is the same reason
compression was declined above. It is recorded so that **prompt caching moved up
the list when repair landed**: it now saves on up to three calls per request
instead of one, and the images are byte-identical and the same string instance
across all of them.

The lever is otherwise the same one as everywhere else here, and it is number 3
below: **a repair inherits whatever the first ask sent.** Halving the strokes in
a request halves the cost of every attempt at it, not just the first.
`ARepairReAskCostsAboutHalfAgain_NotAWholeSecondRequest` holds the ordinary
ratio under 2× and `AJitterRepairCostsMoreBecauseItShipsTheNeighbours` holds the
expensive one under 3×, on the grounds that a repair costing a whole second
request has stopped being a correction.

## The other surface: MCP, where the arithmetic is different

Everything above costs a **request** — built, sent, paid once, gone. The MCP
surface is not that, and the difference was missed for as long as this document
existed because "payload" reads like one thing.

**A tool result lands in the agent's context and is re-read on every turn after
it.** That makes a fat reply a standing charge rather than a one-off, and it
gives this surface three cost classes where a request has two:

| Class | What it is | Measured, 2026-09-03 |
| --- | --- | --- |
| **Resident** | Tool schemas, re-sent every turn all session | ~1,564 tokens of description text over 13 tools |
| **Accumulating** | A reply, paid once to fetch and again every turn after | `get_frame_strokes` on a 120-stroke frame: 147.4 KB, **~37,700 tokens** |
| **Output** | What the agent must *emit* to write a frame back | three inbetweens: 442.2 KB, **~113,200 tokens** |

**The first row went up to buy the second, and that is the trade rather than a
regression.** It was ~1,243 tokens over 12 tools before this work; the listing
tool and the warnings G12 required — that a box says where a stroke is and never
what it does, that 768 px loses eyebrows — cost ~320 tokens resident, once per
turn, to save ~35,000 accumulating, once per look. It is a good trade at any
plausible number of looks and it is still a trade: **a description is the only
thing here billed whether or not the tool is used.**

`McpReadBudgetTests` holds these the way `AiPayloadBudgetTests` holds the
request figures.

**The output row is not a cost, it is a ceiling.** 113k output tokens does not
fit in one response at any provider here, so an agent cannot inbetween a dense
drawing over MCP however much anybody is willing to spend. Every other number in
this document is something you pay; that one is something you cannot do. B359,
and its fix is blocked on Q179 rather than on effort — a delta encoding removes
the ceiling and risks making the cheap path "translate these strokes", which is
the deterministic answer wearing the AI's clothes.

**The accumulating row has the same shape as the lever in the section above, and
it was cheaper to build here.** Order item 3 wants the request to carry the
strokes that need judgement; on this surface the equivalent is letting the agent
*ask* for them, which needs no judgement at all — only a way to see what is
there. `list_frame_strokes` answers a 120-stroke drawing in 10.1 KB against
147.4, a **14× reduction**, because index, label, colour, point count and a box
are all an agent needs to decide which strokes it cares about. Naming three of
them costs 2.5% of the frame.

That is worth stating plainly, because it cuts against the instinct this
document otherwise encourages: **the read side of MCP got its 14× without
anybody solving stroke triage.** Triage is hard because the *sender* must decide
what matters; here the receiver decides, and the receiver is the one who knows.

**Images on this surface cap too, and the cap needed a condition attached.**
`render_frame` sends at most 768 px on the long edge — ~442 image tokens against
~2,764 at 1080p — while `render_reference_view` stays uncapped, because an agent
asking for a view should get the view (B31). The constants are separate for that
reason and must stay separate. What the reference-side reasoning did *not*
transfer is the legibility question: measured through the real path, 768 keeps a
frame's pose and staging and removes **84% of the fine dark pixels from a 1080p
face**, with eyebrows and eyes gone outright at 4K — worse in a scene than on a
sheet, because a face is a smaller share of a full frame. So a reduced render
now reports the scale it used, and a full-size one reports nothing. Q178 carries
the numbers and the alternative that is still live.

**The resident row is small today and is the one that scales worst.** Tool
schemas are the only cost here paid on every turn regardless of what the agent
does, and the roadmap still owes an agent "the rest of the ~38 document-level
commands" — layers, camera, effects, export, selection. At today's ~120 tokens
per tool that is roughly **5k tokens resident, forever, before any work**. The
implication for anyone adding to this surface: a tool description is not
documentation and is not free. Say what the tool does and what it costs, and
stop.

### The rule the halves become here

The document's own rule — *say which half you are optimising* — still holds and
needs a third term. On MCP: **resident scales with the size of the surface,
accumulating with how much an agent looks at, and output with how much it
writes.** They have different fixes and only one of them is a wire format.

## Order

1. ~~**B31** — cache the encoded reference views.~~ **Done.** 225 ms cold,
   0.03 ms warm.
2. ~~**Cap the reference long edge**, on the request rather than on the view.~~
   **Done**, in the same commit: 768 px, 7 KB instead of 115 KB on a 1080p view.
3. **Send the strokes that need judgement**, not the frame. The 6× win, and
   the one that needs design rather than a constant.
4. **Prompt caching** on the providers that offer it. **Now cheaper than it
   was** — the reference images are byte-identical across a session *and* the
   same string instance, so there is nothing left to compute before caching them —
   **and worth more than it was**, because the repair loop turned one call per
   request into up to three, each re-uploading the same images and the same two
   keys. This moved up the list when Phase 3 landed.
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
