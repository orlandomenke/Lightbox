# AI assistance


## Inbetweens

Set the number of inbetweens and an easing, then **＋ Inbetween** interpolates
between this key and the next. Because a frame is a stroke record, the
inbetweener matches *strokes*, not pixels.

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
is drawing. They are sent at up to 768 pixels on the long edge — your sheet keeps
whatever size you drew it at, and only the copy in the request is smaller. Hide
every layer in a view and it stops being sent.

**Frames the AI cannot defend are refused.** Every frame that comes back is
checked against your two keys before it can touch the document: it has to lie
between them, carry every stroke both keys draw, keep a closed shape's volume,
and sit smoothly against the frames beside it. A frame that fails is not
inserted — its slot on the timeline simply stays a hold — and the status bar
says which frame was refused and why, so a three-of-four result is a decision
you can read rather than a puzzle. Lightbox never quietly substitutes its own
deterministic inbetween for a refused frame; that engine is **＋ Inbetween**,
one click away, and asking for the AI means getting the AI or nothing.

The check is not "did the model invent something" — invention is the point.
Revealed lines behind something that moved away, follow-through trailing a
motion, and small departures near the drawing all pass. What gets refused is
ink nothing explains, a stroke that went missing, or a frame that jitters
against its neighbours — the noise that reads as boiling when played at speed.

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
the scene, add strokes and frames, request inbetweens. Everything it does goes
through the same stroke record as everything else, so its work is undoable and
indistinguishable in kind from yours.

---
