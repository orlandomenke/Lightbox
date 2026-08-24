# AI assistance


## Inbetweens

Set the number of inbetweens and an easing, then **＋ Inbetween** fills the run
the playhead is in. Because a frame is a stroke record, the inbetweener matches
*strokes*, not pixels.

**A run is extreme to extreme, and a breakdown is a stop along the way rather
than the end of it.** Mark a cel as a breakdown and **＋ Inbetween** fills every
gap of the run in one go — the drawings either side of the breakdown, in one
action and one undo — and the easing runs once across the whole span instead of
restarting at each drawing. That is the difference between one slow-out and
slow-in across the movement and two of them with a hitch in the middle. Your
breakdown is never moved or redrawn: it keeps its frame and its pose, and the
inbetweens are spaced around it. If it does not sit where the easing would have
put it, the graph editor's spacing curve is what shows you that — the app will
not quietly re-space a drawing you placed.

A sequence with no breakdowns behaves exactly as before, because every drawing
is an extreme until you say otherwise.

**A timing chart on the extreme wins over both controls.** Right-click the key's
cel on the X-sheet and choose **Timing chart…** to pencil the classic ladder
onto it: each rung is one inbetween, placed at its fraction of the travel
across the run. Drag a rung to re-space it, click the rail to add one,
right-click a rung to remove it; the preset buttons write the standard shapes
(even, ease in, ease out, ease in-out) to start from. With a chart on the
extreme, **＋ Inbetween** draws exactly one inbetween per rung, exactly where
the rung says — favour the next pose by bunching rungs toward the right. The
chart is part of the document: it saves, undoes, and travels with the drawing
through re-times and holds. The graph editor's **Spacing (intended)** curve
reads it too, so the dashed line shows *your* chart where one exists and the
bar's easing everywhere else — a chart whose rung count no longer matches the
run's inbetweens is ignored rather than misread, and the curve falls back to
the easing until the counts agree again.

The same ladder steers **✦ AI Inbetween**: the model is asked for one frame
per rung, at the rung's position, so accepting the AI's frames or the
deterministic ones lands the same timing. One difference to know about, for now:
**✦ AI Inbetween** still works one gap at a time, so on a run with a breakdown
it fills the gap you are in rather than the whole run. Sending the model a third
drawing costs materially more per request, and that trade has not been taken
yet.

## AI inbetweens

**✦ AI Inbetween** asks the model for the frames between two keys. It needs a
provider; until one is chosen the AI controls are disabled and say where to
choose it.

Anything the AI produces arrives as ordinary strokes — undoable, editable, and
subject to every rule your own strokes are, including the layer ones. A hidden
or locked layer refuses the AI exactly as it refuses a brush.

**There is no way to ask for a drawing from nothing.** Everything the AI does
starts from something you drew: here, the two keys it works between. Lightbox
has no prompt box, and that is a decision rather than a gap — the AI is here to
take the tedious parts off an artist, not to make the drawing.

**What gets sent.** Along with the frames, the **first two views on your character
sheets that have a visible layer** go out as pictures, so the model can see who it
is drawing. In a project those are the sheets filed above the document — the
knight's animations ride with the knight's sheet, wherever it was drawn. They are sent at up to 768 pixels on the long edge — your sheet keeps
whatever size you drew it at, and only the copy in the request is smaller. Hide
every layer in a view and it stops being sent.

**What you erased is not sent.** The AI is given the drawing, not the drawing's
history: erasures and the lines they rubbed out are both left out of the
request, whether you erased with the Eraser or cleared a selection. This is
worth knowing for two reasons — you are not billed for artwork you deliberately
removed, and the model never inbetweens a line you took off the page. The record
still keeps all of it so the drawing rebuilds exactly and undo still works; it
just is not part of what the request describes.

**Frames the AI cannot defend are refused.** Every frame that comes back is
checked against your two keys before it can touch the document: it has to lie
between them, carry every stroke both keys draw, keep a closed shape's volume,
and sit smoothly against the frames beside it. A frame that fails is not
inserted — its slot on the timeline simply stays a hold — and the status bar
says which frame was refused and why, so a three-of-four result is a decision
you can read rather than a puzzle. Lightbox never quietly substitutes its own
deterministic inbetween for a refused frame; that engine is **＋ Inbetween**,
one click away, and asking for the AI means getting the AI or nothing.

**A refused frame gets a second and third go before it is given up on.** When a
frame fails a check, Lightbox does not simply try again and hope — it asks the
model once more, telling it exactly what was wrong and handing back the drawing
it just rejected, so the model can correct that frame rather than start over.
Up to two of these re-asks happen per frame, and only the frames that failed are
re-asked; the ones that already passed are never redrawn. The status bar says
which attempt it is on while it works, and **Cancel** stops it between attempts.

Two things worth knowing about what this costs and what it protects:

- **It can take a while.** A frame that never comes good occupies three requests
  before it is refused, and each one is a full round-trip to the model. When
  nothing survives, the status says so and says how many attempts it took —
  *"Nothing was inserted after 3 attempts"* — so a long wait for an empty result
  is at least legible.
- **A re-ask can never lose you a frame you already had.** If correcting one
  frame would upset a frame that already passed — usually by making the two
  read as a jitter next to each other — Lightbox keeps what it had and leaves
  the other frame refused. It will not trade a good frame for a different one.

Frames that needed more than one attempt remember it, so the record of what the
AI drew includes what it cost. Nothing changes about the drawing itself: a
repaired frame is checked against exactly the same rules as one the model got
right first time, and it is inserted only if it passes them.

**When the status says a frame "matched what ＋ Inbetween would have drawn",
read it.** It means the model returned the same answer the free deterministic
inbetweener would have given for nothing. That is not a failure and the frame is
perfectly usable — but it is worth knowing, because it is the safest thing a
model can do when it is being pushed to correct itself, and if you are seeing it
often you are paying a model to do what **＋ Inbetween** does instantly.

The check is not "did the model invent something" — invention is the point.
Revealed lines behind something that moved away, follow-through trailing a
motion — a tail or a cape drawn as a chain of strokes counts, however long,
because each link may hang off the one before — and departures near the
drawing all pass. What gets refused is ink nothing explains, a stroke that
went missing, or a frame that jitters against its neighbours — the noise that
reads as boiling when played at speed.

**AI frames remember where they came from.** A frame the AI drew carries a
small provenance note in the saved file — which provider, and the model name
when there is one. Frames you drew carry nothing, and a document that never
used the AI is byte-for-byte what it would have been before the feature
existed. Provenance is a record, never behaviour: it changes nothing about how
the frame renders, and deleting it changes no pixel.

## Reading a folder

Right-click a folder in the **Project** panel and choose **Read this folder…**.
The model looks at the sheets you have drawn and writes down what it is — a
biped, say, with a head, a torso and two arms, and which arm is normally in
front. That reading then rides along with every inbetween of a drawing in that
folder, so the model knows the arm passes in front of the body instead of
guessing.

**Any folder can be read**, and reading it is what makes it a character as far
as Lightbox is concerned — you do not declare one first. Put the work in a
folder, read it, and give it a glyph if you want it to look like one.

**Once per folder, not once per frame.** The answer is kept on the folder in the
project, so a twenty-four frame cycle pays for it once and the next animation in
the same folder pays nothing at all. A drawing in a sub-folder inherits the
reading above it, and a sub-folder with its own reading overrides it — nearest
wins, the same rule palettes follow.

**It is yours to correct.** Once you have edited a reading, Lightbox will not
overwrite it — asking again says so rather than throwing your corrections away.
A reading is a starting point, and where you have said what something is, that
is what it is.

Two things it deliberately does not do. It never describes a *pose* — where an
arm is in frame 12 changes every frame and is not worth keeping. And it never
reaches a pixel: delete every reading in a project and your drawings render
exactly the same, because the reading tells the model what it is looking at and
nothing else.

A folder with no sheets has nothing to read; draw one first, or make a layer on
it visible.

## Turning AI off

**Edit ▸ Configure ▸ AI ▸ Use AI assistance.** On by default. Off removes the
AI bar rather than greying it out — a row that can never do anything is worse
than no row.

Everything below the switch keeps working while it is off, so a provider can be
set up and tested before AI is turned on. That is the useful order, and
refusing to test until the switch is on would invert it.

## Choosing a provider

**Edit ▸ Configure ▸ AI.** Pick a service from the dropdown and the fields
below it change to what that service needs — a key and a model for a hosted
one, a URL for a local one, a command line for an agent of your own.

| Provider | What it needs | Notes |
| --- | --- | --- |
| **Claude (Anthropic)** | API key, model | What Lightbox is tuned against, and the strongest inbetweener here. |
| **GPT (OpenAI)** | API key, model | Strict JSON schema, so replies parse by construction. |
| **OpenRouter** | API key, model | One key for many vendors' models. |
| **Ollama** | Model | Local, no key, no network. Weaker inbetweens — good for working offline. |
| **Custom (OpenAI-compatible)** | Endpoint, model | LM Studio, vLLM, llama.cpp's server, your own gateway. The key is optional. |
| **Custom agent (MCP)** | Command, tool | An MCP server you supply that owns the model. |

## Testing it

**Test connection** asks for real work rather than pinging, because most of the
ways this fails are not reachability. Both depths ask for an inbetween — that
is the only thing Lightbox asks a model for, so testing anything else could
pass on a provider that cannot do the job. There are two depths:

| | What it does | Cost |
| --- | --- | --- |
| **Quick test** | One inbetween of a two-point line on a small canvas | Seconds; a few hundred tokens |
| **Test with a drawing** | The quick test, then a real inbetween between two keyframes | Minutes on a local model |

Both check that the *output* is usable, not just that it parsed: strokes with
fewer than two points, or every point in the same place, are reported as a
problem rather than counted as a pass. The thorough test adds the one check
that separates a working connection from a working inbetweener — the frame it
returns has to land **between** the two keys. A small model that answers in
perfect JSON and copies a keyframe fails there, and nowhere else.

The verdict comes in three colours, because "unreachable" and "reachable but
drawing nonsense" need different fixes:

- **Green** — connected, and what came back is usable.
- **Amber** — connected, but the output is not usable. The connection is fine;
  the model may be the wrong one.
- **Red** — nothing answered, or the key, endpoint or tool name is wrong.

A test shows a progress bar and an elapsed clock while it runs, and says which
stage it is on. Past two minutes it says so explicitly rather than sitting
silent, and **Cancel** stops it — a thorough test against a local model
genuinely takes that long, and silence for that long is indistinguishable from
a hang.

A field left empty is not necessarily unset. Its placeholder says what it
resolves to: a default, or an environment variable that is already supplying it
(`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `OPENROUTER_API_KEY`,
`LIGHTBOX_OLLAMA_URL`). What you type wins over the environment, which wins
over the default — and only what you type is saved, so a rotated key is not
shadowed by a stale copy.

Changing provider takes effect immediately; there is no restart and no Save
button.

## Grading a model

A connection test says the model answers. **Grade this model** says what it can
*do*. It asks a committed set of keyframe pairs — a swing, an arc, a rotation,
an occlusion, and a ladder of drawings with more and more strokes in them — and
scores every answer with the same checks that judge the model's real work.

The result is numbers rather than a pass mark, deliberately. There is no
overall verdict because there is no single answer: a model weak on arcs is
still worth using where straight interpolation is weak, and one that gives up
past twelve strokes is fine if you send it ten. What you get is a reading:

```
Schema adherence: 100 %
Label retention: 60 %
Degrades past 8 strokes — send it fewer than that.
Swing: clean (1/1)
Arc: clean (1/1) — interpolated along the chord — no arc
Rotation: clean (1/1)
Occlusion: clean (1/1)
StrokeLadder: 2/6 — ladder-16: out of context
Organic: not measured — ships with no pairs
```

**The line worth reading is the stroke one.** It is the number nobody usually
measures, and it is the one that decides whether a model is usable on a real
drawing: a frame with forty strokes is ordinary, and a model that copes with
eight will quietly produce nonsense on it.

Two run sizes, and the page tells you what each will send **before** you spend
it — a full run is roughly five times the short one, almost all of it the long
stroke ladder. The short run grades every category and places the stroke limit
roughly; the full run places it precisely. Start short.

A run shows its progress and an elapsed clock, and **Cancel** stops it. A
cancelled run records nothing on purpose: half a ladder would report a limit
that is really just where you clicked.

The reading is kept, so it is still there next time you open the window. It is
stored against the model it was taken on — point the connection at a different
model and the page says so in amber rather than letting an old reading pass as
a new one. The old reading is not thrown away; it is still true about the old
model, and re-running costs money.

**One row always says "not measured".** *Organic* is for complex organic
subjects — a quadruped's gait, a figure turning — and it ships empty, because
those need answers drawn by hand rather than computed. It appears anyway so
that a question nobody asked can never read as one the model passed.

## An agent of your own, over MCP

The **Custom agent (MCP)** provider launches a server you name and calls one
tool on it:

```
tools/call { name: <tool>, arguments: {
  system: string,   // the role and the rules
  prompt: string,   // the task, with the keyframes as JSON
  schema: object    // JSON Schema the reply must match
}}
→ { content: [{ type: "text", text: "<json matching schema>" }] }
```

Anything behind that contract works — your own agent, your own retrieval, a
model with no public API. If the tool is named something else, Test connection
lists the tools the server actually offers.

This is the opposite direction from Lightbox's *own* MCP server, and the two
are independent. Here Lightbox calls out to a model. There, an agent you
already run calls in and works the document directly — no provider needed on
this page at all.

---

## Working with an agent (MCP)

Lightbox runs an MCP server, so an agent can work the document directly: read
the scene, add strokes and frames, request inbetweens. It can also *see* — a
timeline frame or a character-sheet view comes back to it as a rendered image,
which is how it checks a drawing before inbetweening it and its own results
afterwards. Everything it does goes through the same stroke record as
everything else, so its work is undoable and indistinguishable in kind from
yours.

### Timing, not just drawing

An agent can author the exposure sheet as well as read it. Four tools cover it,
and all of them are one undo step on your side:

| Tool | What it does |
| --- | --- |
| `set_key` | Makes a frame a key — a new empty drawing where there was a hold, or a changed role (key, breakdown, inbetween) on a drawing already there. A frame past the end of the timeline extends it. |
| `extend_exposure` | Holds one drawing a frame longer, on that layer only — the rest of the layer shifts right and other layers stay put, the way an X-sheet works. |
| `reduce_exposure` | Shortens a hold by one frame. |
| `set_exposure_step` | Re-times a range so every drawing in it is held for the same number of frames — step 2 is animating on 2s. |

**None of them can lose a drawing.** `reduce_exposure` removes a hold and never
a drawing, so on a frame that is not held it reports an error rather than
quietly doing nothing; `set_exposure_step` absorbs the holds already in the
range instead of multiplying them, so asking for 2s twice leaves you on 2s
rather than 4s. Thinning a range by discarding drawings is a destructive edit
and is deliberately not offered over MCP.

**A key the agent created says so; a frame it merely re-labelled does not.** If
an agent makes a new drawing, that frame carries its provenance, the same as an
AI inbetween. If it only changes the role of a drawing you made, the frame stays
yours and unmarked — the timing changed, not the art.

A locked or hidden layer refuses all four, and says which.

**If an agent reports a bug you know was fixed, check which build it is talking
to.** The server is a separate published program that your MCP client launches,
so it goes on running an old copy until you rebuild it *and* fully quit and
reopen the client — reloading is not enough. Ask the agent for `get_scene`: it
returns `appBuild` for the running Lightbox and `mcpBuild` for the server, and
they should name the same commit. Different means only one half was
republished. `mcpBuild` missing altogether means the server is older than this
feature, which settles it.

---
