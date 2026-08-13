# Decomposing the two big files: which pieces come out, and with which tool

Status: **done, 2026-08-13.** Both files are decomposed. `MainViewModel.cs` went
13,110 → **655** lines and `MainWindow.axaml.cs` 5,544 → **429**, in seven steps
recorded below. This revision was written when the first one had gone stale before it
was merged, and because it answered "how do we split this" with one tool when the two
files needed different ones — that turned out to be the load-bearing correction.

Two files are in scope, and the whole point of this revision is that they are
not the same problem:

| File | Lines | Shape | Tool | State |
| --- | --- | --- | --- | --- |
| `ViewModels/MainViewModel.cs` | 13,628 → **692** | large, shallow, one hub | collaborators for the hub, then partials for the rest | **done** |
| `Views/MainWindow.axaml.cs` | 5,706 → **455** | 37 near-independent sections over one field | partials, and that is the whole job | **done** |

`MainViewModel.cs` is at once the document API, the tool state machine, the
render scheduler and the binding surface, with no interface between the four.
`MainWindow.axaml.cs` is none of those things — it is event handlers — and it was
missing from the first draft entirely.

## Read the numbers from the script, not from here

`scripts/monolith.py` derives every count and anchor below:

    python3 scripts/monolith.py report     # sections ranked by extractability
    python3 scripts/monolith.py anchors    # the leaf table, as markdown
    python3 scripts/monolith.py hot        # the fields that are the hub
    python3 scripts/monolith.py wide       # sections whose markers lie

**This is the correction that matters most.** The first draft hand-wrote its
table, measured the file at 10,098 lines, and was merged when the file was
already 12,001 — so every anchor in it was off by 1,200 to 2,000 lines and the
map pointed at the wrong code. Frame markers were quoted at `:7991` and are at
`:9756`; the fill tool at `:3410` and is at `:4597`; the move tool at `:6589` and
is at `:8269`. Anchors printed below are correct at the revision date and will
not stay correct. Regenerate before using one.

## The analysis reproduced, which is why the diagnosis stands

The first draft's measurement was re-run against the current file, 3,012 lines
larger:

| | First draft (10,098 ln) | Now (13,110 ln) |
| --- | --- | --- |
| Fields touched by exactly one section | 54% | **53%** |
| Fields crossing five or more sections | 9 | **9** |
| Widest section (the shape tool) | 32 fields | **33** |

One field changed places in the hot nine — `_lastStrokeEnd` out,
`_selectionContours` in. Otherwise it is the same file, 30% longer.

**So the file is not getting more tangled as it grows; it is getting longer at
constant shallowness.** The coupling runs section → hub, not section → section,
and 53% of state belongs to exactly one place. That is what makes an incremental
extraction viable, and it is now measured twice rather than once.

The hub, whatever it ends up being called:

    28  _editor          7  _autosave         6  _strokeBuilder
    16  _cache           7  _composeRing      5  _applyingPreset
    11  _dirtyThumbIds   6  _selectionContours 5  _liveScratch

## Which tool, and why not one tool

The first draft rejected more partial files outright: they buy navigability with
"zero decoupling", because "every section keeps its licence to touch every
field". The argument is sound in the language and false in this codebase. Nine
partials exist. The licence is barely used:

| Partial | Lines | Fields used | Declared locally |
| --- | --- | --- | --- |
| `StrokeSelection` | 181 | **0** | 0 |
| `PathHover` | 82 | 1 | 1 |
| `Momentary` | 203 | 4 | 4 |
| `Pen` | 293 | 4 | 1 |
| `StrokeActions` | 335 | 3 | 0 |
| `PathEditing` | 438 | 3 | 2 |
| `Rig` | 287 | 3 | 2 |
| `Audio` | 608 | 13 | 10 |
| `Symbols` | 1,100 | 8 | 4 |

3,527 lines left the file this way and stayed loose. The 2,000-line partial
reaching into eight others, which the draft was right to fear, is a partial
nobody has written — and the reason is worth naming, because it is the thing that
makes this route work: **giving a section its own file creates the pressure to
declare its state there.** The licence survives; the habit does not.

The honest caveat, recorded because it decides where the line falls: these are
the *easy* sections. They were split precisely because they were already loose,
so nothing here proves the shape tool would behave the same way. It would not —
it reaches 30 foreign fields.

**So the tool is chosen per section by what the fields say** (Q76):

| If a section… | then |
| --- | --- |
| owns its state and touches ≤5 hub fields | **a partial.** Cheap, no behaviour change, no XAML moves |
| is a hub, or shares mutable state across sections | **an extracted collaborator** that owns the state, in the manner of `SelectionManager` |
| owns nothing at all | **leave it** until the hub it reads is named — there is nothing to move |

`ViewModels/SelectionManager.cs` remains the model for the second row: 346 lines,
owns its sets, exposes read-only views, raises `SelectionChanged`, handed to the
canvas through `CanvasControl.SetSelectionManager`.

What this rules out, and the draft was right about, is a partial for a *hub*.
A 2,000-line partial holding the render core would look solved and decouple
nothing. The rule is not "partials are fine"; it is "partials are fine for a
section whose state comes with it".

## The ratchet comes first, because the arithmetic does not work without it

Since the first draft was merged: **94 commits to `MainViewModel.cs`,
+2,793/−965 lines.** Zero leaves extracted. The file gained more lines while the
plan sat unstarted than the plan proposed to remove.

An extraction that costs a branch, a full suite run and a `leak-hunter` pass per
leaf cannot outrun feature work landing in the same file. So the growth is capped
from the other side first: `MonolithRatchetTests` holds a line budget for the
four oversized files, seeded at their current lengths. They may shrink and may
not grow; a budget comes down when an extraction lands, in the same commit; there
is no environment-variable escape hatch, because raising the number in a diff is
the visible version of the same decision.

This is deliberately not architecture. It is the cheapest mechanism that makes
the rest of this document arithmetically possible.

## `MainWindow.axaml.cs` — the easy one, and it is easy for a reason

Run the same analysis on the view and it comes back inverted:

- **79%** of its fields are touched by exactly one section.
- **One** field crosses five or more sections: `_vm`, used in 35 of 37.
- The widest section touches 6 fields; most touch one to three.

There is no hub to name, no shared mutable state, no Tier 0. It is 37
near-independent groups of event handlers over a single view-model reference,
already marked with `// ---- section ----` comments that mostly tell the truth.

**So the view needed splitting, not decomposing** — and that is **done**. It is
now 429 lines holding the usings, the class identity, three shared fields and the
constructor, with fifteen partials beside it:

| Partial | Lines | Sections it took |
| --- | --- | --- |
| `MainWindow.Workspace.cs` | 747 | the workspace, workspace commands, dragging a panel, floating panels |
| `MainWindow.ProjectFiling.cs` | 616 | re-filing a document by dragging it |
| `MainWindow.Projects.cs` | 488 | projects, converting a project, the start screen, recents, templates |
| `MainWindow.Palette.cs` | 467 | palette, drag a colour onto the canvas, the hierarchy, dragging a swatch |
| `MainWindow.BrushPicker.cs` | 459 | brush presets, the picker, pressure curves, the tip picker |
| `MainWindow.CanvasViewTools.cs` | 412 | canvas view tools |
| `MainWindow.Timeline.cs` | 385 | cell context menu, cel clipboard, range selection, cel drag, markers |
| `MainWindow.Guides.cs` | 381 | guides, rulers |
| `MainWindow.CanvasBars.cs` | 334 | the bars on the canvas, the gradient ramp editor |
| `MainWindow.Chrome.cs` | 304 | the chrome is ours |
| `MainWindow.Layers.cs` | 208 | layer rename, folder rename/collapse, docker context menus |
| `MainWindow.Toolbar.cs` | 202 | toolbar |
| `MainWindow.Symbols.cs` | 190 | symbols, drag a symbol onto the canvas |
| `MainWindow.Transform.cs` | 186 | transform session (window side) |
| `MainWindow.CharacterSheets.cs` | 127 | character sheets |

**The partition was derived, not chosen.** Three fields decided it, and they are
the only reason the grouping is not simply "one file per marker":

- `_panels` and `_floating` are declared in *the workspace* and used by *dragging
  a panel* and *floating panels*, so those three sections share a file.
- `_celDrag` and `_celDragPress` are declared in *drag a cel along its row* and
  used by *multi-cel range selection*, so those two share a file.
- `_shortcuts` is declared in *canvas view tools* and used by *projects* — and by
  the constructor. Same for `_hoveredElement`. Both are genuinely shared, so both
  declarations moved to the root file rather than forcing two unrelated concerns
  together.

Everything else was already field-closed, which is what made this cheap. Verified
three ways before it was believed: the ranges cover all 5,544 lines with no gaps
and no overlaps; all 37 markers sit at class level (brace depth 1) so no range
cuts a member in half; and the class body is **identical as a multiset of lines**,
4,994 non-blank lines before and after. The only edits were the per-file wrapper
and moving those two declarations.

**A warning worth keeping**, because it is the one thing the split broke: three
tests assert on the *source text* of `MainWindow.axaml.cs` and went red, not
because behaviour changed but because the members they grep for moved. They now
read every `MainWindow*.cs`. A source-text test that names one file of a partial
class silently stops guarding anything the moment its target moves, so it should
name the class instead.

The one thing to watch: **`_vm` being everywhere is not coupling to fix here.**
A view holding one reference to its view model is a view. Splitting the file must
not turn into pushing logic across that line.

Worth saying, since it is where the risk actually is: `HOTSPOTS.md` puts
`MainWindow.axaml` — 4,188 lines of **XAML**, 53 commits, **no test file** — at
the top of its risk table, above both C# files. `MainViewModel.cs` ranks fourth
*because* 71 tests construct it. The XAML is out of scope here and is the largest
unguarded surface in the repository; it is in the ratchet so that it at least
stops growing.

## Tier 0 — the hub. Not leaves, and first

Two clusters must be named before anything large moves. Neither is a leaf,
neither should be split internally, and both want collaborators rather than
partials.

**Both are now named** (Q77), which was step 3 and is **done**. Naming was a pure
code-motion change: no line of code was altered, verified by showing the file
identical as a multiset of lines. The extraction is the next branch.

**The live-paint state machine** — `painting`, which owned 19 `_live*` fields,
together with `live post-processing`, which read all of them. Those two were one
mechanism, and they are now `ViewModels/LivePaintSession.cs`: 22 fields and four
lifecycle methods, owned by one object the view model holds for its lifetime.

| | Before | After |
| --- | --- | --- |
| `MainViewModel.cs` | 13,141 ln | **12,919** |
| private fields | 143 | **122** |
| fields touched by one section | 53% | **63%** |
| `live post-processing` foreign fields | 19 | **6** |
| `painting` fields owned | 19 | **3** |

**The state moved and the engine did not, and that was the call.** `MoveStroke`,
`FlushLivePreview`, `StampLiveDabs`, `StampLiveSmudge`, `RenderLivePostProcess` and
`EndStroke` stay on the view model, because they also need the editor, the frame
cache, the brush settings and the stroke builder — moving them would have dragged
all four into the session and produced a second view model.

**What it honestly did not buy:** `_live` now crosses seven sections, so the
coupling did not vanish — it became **one typed reference instead of 22 raw
fields**, which is the whole of the trade. The session is not an encapsulation
boundary either; the engine mutates its properties directly, and a version that hid
them would have to expose the dab walk to be useful. The value is that
`ClearEffectState` is now impossible to get half-right (B39's failure mode), and
that a reader sees the whole of the live-stroke state on one screen.

**It was not readable as one before the re-mark.** 580 lines of the engine —
`MoveStroke`, `FlushLivePreview`, `StampLiveDabs`, `StampLiveSmudge`,
`ClearLiveEffectState`, `EndStroke` — sat under a heading reading **"the shape
tool"**, 800 lines from the state they mutate. That is the whole reason the shape
tool measured 30 foreign field touches, the widest in the file: it was not a tool
tangled into the paint path, it *was* the paint path with a tool on top. Moving
those members next to their state cost nothing and bought this:

| Section | Before | After |
| --- | --- | --- |
| the shape tool | 804 ln, **30** hub fields | **184 ln, 5** — an ordinary Tier 1 leaf |
| painting | 195 ln, owns 19 | **605 ln** — the engine is here now |
| live post-processing | 196 ln | **321 ln** — its own methods returned to it |
| gradient tool | 157 ln | **197 ln** — likewise |

**The render and publish core** (`:11777`) — 785 lines that had **no name at
all**, sitting under a heading reading *video clip bars (Q57)*, which is why every
map of this file pointed at the wrong place. `monolith.py wide` is what surfaced
it. It is obstacle 1 in `DESIGN-cloud-readiness.md`, the same work arrived at from
the other direction.

**This document said it wanted an orchestrator holding those six collaborators. That
was wrong, and the correction is the useful part.** It was written from a partial
reading; reading `PublishSnapshot` end to end says otherwise on two counts.

*First, the state was not all owned.* The claim was that `_composeRing`, `_cache`,
`_tileFlats`, `_stackBake`, `_prewarm` and `_tileFallbacks` already own everything.
True of the **caches**, false of the **bookkeeping**: `_pendingDirty`,
`_dirtyIsWholeCanvas`, `_pendingViewport`, `_publishSeq`, `_lastPublished`,
`LastPublishClip` and `FramesReused` were seven raw fields belonging to nothing.

*Second, an orchestrator is the wrong shape here.* `PublishSnapshot` reads about
fifteen pieces of view-model state — scene, playhead, compose scale, camera
transform, playing, light table, onion, active layer, playback range, and the whole
live-edit tuple. An orchestrator must be handed that per call or hold a reference
back to the view model. The second is a second view model with circular coupling.
The first allocates a request per publish — and **the code next door already refuses
that trade**: the transform-split delegate is cached in a field rather than written
as a lambda, precisely because "a lambda capturing `this` allocates a closure and a
delegate on every publish, and a publish happens per pointer event while drawing".
A path that avoids one closure allocation should not gain a record allocation and a
layer of indirection.

**So what came out was the bookkeeping**, as `ViewModels/PublishState.cs` — seven
fields plus `MarkDirty`, `InvalidateWholeCanvas` and `TakeDirty`. The sequencing
stays in `PublishSnapshot`, reading the view model directly and allocating nothing.

`TakeDirty` is the whole reason this is a class and not a struct of fields. Reading
the dirty region and clearing it is three statements that must happen together, and
both ways of getting it wrong are silent: clear without reading and the next publish
repaints nothing that changed; read without clearing and the dirty rect grows
forever, so painting stops being bounded work. **Invariant 6 rests on that one
method**, and `PublishStateTests` sabotages it both ways to prove the guard bites.

The lesson worth carrying to the remaining tiers: **"its state is already owned" is a
claim to check against the code, not to read off a collaborator list.** Six owned
caches hid seven unowned fields in the same cluster.

`RequestSnapshot` moved to the head of it, because it schedules a publish rather
than belonging to the paint path that calls it. That keeps B73 out of the
live-paint extraction entirely.

## Tier 1 — the leaves

Own their state, ≤5 hub touchpoints, extractable one at a time, one branch each.
Regenerate with `monolith.py anchors`; anchors below are correct at 2026-08-13.

| # | Leaf | At | Size | Owns | Needs from the hub |
| --- | --- | --- | --- | --- | --- |
| 1 | Frame markers | `:9723` | 150 ln, 3 cmd | `_markersView` | `_editor` |
| 2 | Guides | `:10696` | 533 ln, 4 cmd | 8 fields incl. `_snapToGuides`, `_lockedGuide`, `_snapTolerance` | `_editor` |
| 3 | Editing the grid by hand | `:11230` | 338 ln, 2 cmd | `_referenceGridEditMode`, `_selectedReferenceCell` | `_editor` |
| 4 | Playback transport | `:7616` | 166 ln, 8 cmd | `_playDirection`, `_playbackStartFrame`, `_playbackEndFrame` | `_clock`, `_strokeBuilder` |
| 5 | Camera | `:12600` | 280 ln, 4 cmd | `_viewThroughCamera` | `_autosave`, `_composeRing` |
| 6 | Character sheets | `:1671` | 590 ln, 0 cmd | `_referenceViewPngs`, `_refreshingLinkedStrips` | `_cache`, `_settingColorFromSwatch` |
| 7 | AI | `:9979` | 324 ln, 3 cmd | 7 fields incl. `_artist`, `_aiCts`, `_aiStatus` | `_editor` |
| 8 | Timing presets | `:7870` | 302 ln, 3 cmd | `_newTimingPresetName`, `_newTimingPresetPattern`, `_selectedTimingPreset` | `_allThumbsDirty`, `_dirtyThumbIds`, `_editor` |
| 9 | Fill tool | `:4593` | 336 ln, 2 cmd | 5 fields, `_fillTolerance` through `_smartFill` | `_cache`, `_committingScopedEdit`, `_dirtyThumbIds`, `_editor`, `_selectionContours` |
| 10 | The shape tool | `:7429` | 184 ln, 1 cmd | `_activeShape`, `_liveShape`, `_polygonSides` | `_committingScopedEdit`, `_dirtyThumbIds`, `_editor`, `_liveScratchCanvas`, `_liveScratchUsed` |

**10 is new to this table and it is the point of the re-mark.** It was Tier 0's
worst tangle at 804 lines and 30 touchpoints; naming the mechanism it was hiding
turned it into an ordinary leaf without changing a line of code. It is the
strongest argument in this document for re-marking before extracting: the tangle
was in the map, not in the program.

Four changes from the first draft's table, all from re-deriving rather than
re-reading:

- **Guides is the best leaf in the file, not the seventh.** The draft listed it
  as needing five hub fields; `_lockDecided`, `_lockedGuide` and `_snapTolerance`
  are **declared inside the guides section**. It owns eight fields and needs one.
  533 lines for one touchpoint is the largest clean win available.
- **Character sheets was missing** — 590 lines, two owned fields, two
  touchpoints. It is a better leaf than most of what was listed.
- **The move tool has moved tiers.** The draft had it at 438 lines owning six
  fields (`_moveAnchor`, `_moveDelta`, the four `*MoveDelta`s). It is now 504
  lines owning **none** — the state went elsewhere, so taken alone it is pure
  behaviour over hub state and belongs in Tier 3 or with selection in Tier 2.
- **AI needs one hub field, not four.** `_artist` is declared in the section.

Why this order: **1 is the proof of pattern** — one owned field, one touchpoint,
150 lines; if the first extraction is contentious the argument is about the
approach rather than the leaf. **2 is the largest win per unit of risk.** **7
must go through the pair** — charter gate G12 requires `ai-engineer` and
`art-director` on any diff touching an AI path in the view model, refactor or not.


### What a leaf actually yields, measured after the first one

**The line counts in this table are what a section *contains*, not what can leave
it**, and the difference is large enough to change the plan. Across the ten leaves'
3,203 lines there are **24 `[ObservableProperty]`, 30 `[RelayCommand]` and 119 public
members**. The generated commands and observable properties must stay on the object
the XAML binds to — `SnapToGuides`, `SnapTolerance` and `GridSpacing` are bound by
name in two windows — and the public members either stay or become delegating
one-liners, which *adds* lines here while moving a body out. **Only 55 private fields
can genuinely move.**

So the earlier estimate that all ten leaves would take the file to ~9,676 was
optimistic; the realistic floor is nearer 11,000–11,500. Leaves still buy what Q78
chose them for — 55 fields getting an owner, and mechanisms like "decide once, then
hold" becoming testable on their own — but they do not answer the size question. The
partial split (Q78) is what answers that, and it remains deferred rather than refused.

**Guides is also two features, which is why it measured 533 lines.** A
`// ---- imported references ----` marker sat 190 lines above its own content with
nothing under it, while reference strips, sheets and video lived inside `guides`.
Moving that one marker — no code with it — split the section into `guides` (190 ln)
and `imported references` (320 ln), with `editing the grid by hand` (338 ln) as the
third part of the same feature. That is the third lying marker this refactor has
found, after `the shape tool` and `video clip bars`. **Run `monolith.py wide` before
trusting any remaining row of this table.**

### And then measured across all of them: the table is mostly Tier 3

After `GuideSnap`, the same count was run on every remaining row — private fields
declared in the section that are **not** `[ObservableProperty]` backing fields, i.e.
state that can actually leave:

| Leaf | Lines | Bound (cannot move) | **Movable** |
| --- | --- | --- | --- |
| AI | 324 | 1 | **6** |
| the shape tool | 184 | 1 | 2 |
| character sheets | 590 | 0 | 2 |
| playback transport | 166 | 2 | 1 |
| editing the grid by hand | 338 | 1 | 1 |
| timing presets | 302 | 3 | **0** |
| imported references | 320 | 4 | **0** |
| frame markers | 150 | 1 | **0** |
| fill tool | 336 | 5 | **0** |
| camera | 280 | 1 | **0** |

**Twelve fields, across nine sections, and five of them have none at all.** Those
five are Tier 3 by this document's own definition — pure behaviour over document and
hub state — and they were filed as Tier 1 because the original table counted section
size instead of ownership. `character sheets` is the clearest case: 590 lines, 27
members, and its only private state is a re-entrancy guard and a PNG cache.

So **the leaf seam is close to exhausted**, and that is a finding rather than a
setback. What Tier 1 was for — giving unowned state an owner — has largely been done
by Tier 0, which took 22 + 7 fields. What is left in these sections is the binding
surface and the document API, and neither is extractable without moving every XAML
path that names it.

**Two exceptions, and both are gated.** The AI section has six genuinely movable
fields (`_aiBusy`, `_aiCts`, `_aiEnabled`, `_aiModelLabel`, `_aiProviderLabel`,
`_artist`) and is a coherent session object. And inside `character sheets` there is a
real collaborator that the row's "2 movable" undersells — the **reference-view PNG
cache**: the dictionary, `RenderReferenceViewPng`, `Downscaled`,
`EncodedReferenceView` and the 768 px `ReferenceLongEdge` cap, about 110 lines with
one job and a measured reason (B31: 52 ms per view on the UI thread before every AI
call, byte-identical each time). Both touch an AI path in the view model, so **charter
gate G12 applies to both** — `ai-engineer` and `art-director` on the diff, not
optional.

**The recommendation after this measurement is the partial split** (Q78), which is
what the remaining size problem actually responds to. The leaf route has given what
it has to give.

## The split, which is what actually answered the size question

The leaf pass gave what it had — three collaborators and a lot of measurement — and
left the file at 12,749 lines. The partial split took it to **655**, in two separately
verified steps, and the ordering is the reason it was cheap:

**Step A: 33 shared fields moved to the root.** Every field touched by more than one
section now lives in one marked block at the top of `MainViewModel.cs`. That is the
whole rule — *a section's own state travels with it; shared state does not move* — and
it is what makes each partial's dependencies legible: a field it uses is either
declared in it, or it is shared and named in one place.

**Step B: 61 sections became 19 files.** With the shared state hoisted, union-find over
the remaining fields returns **61 independent groups** — every section closed over its
own state. So the grouping into files was free, and chosen by concern rather than
forced by coupling.

| | Lines |
| --- | --- |
| `MainViewModel.cs` (usings, class identity, 54 shared fields, constructor) | **655** |
| largest partial (`Painting`) | 1,310 |
| `Timeline`, `Documents`, `Brushes` | ~1,050 each |
| smallest (`Guides`) | 208 |

**Why the threshold is "touched by more than one section" and not "three or more".**
The first attempt used three, and union-find chained 16 sections into one 4,500-line
group — a field shared by exactly two sections links them, and the links form chains.
Raising the root's share from 37 fields to 54 broke every chain. **That is the trade the
split makes explicit rather than removes:** 54 of 114 fields are read from two or more
places, and they are all in one visible block instead of scattered through 12,000 lines.

Verified the way the view split was, three ways: the section ranges cover the region
with no gaps and no overlaps; every marker sits at brace depth 1 so no member was cut
in half; and the class body is **identical as a multiset of lines** against `HEAD`,
11,454 non-blank before and after, the only additions being the ten comment lines
introducing the shared-state block.

## Tier 2 — clusters, extracted whole or not at all

Genuinely shared state. These are the collaborator cases.

**Brush preset state** — *brush tool state* (`:2863`, owns `_brushWork`,
`_eraserWork`, `_applyingPreset`, `_userPresets`, `_selectedBrushPreset` and
needs **nothing**), with *tags* (`:3829`), *editing the preset you are on*
(`:3671`) and *whose brush is it (Q9)* (`:805`), which own nothing and read that
set. One owner, three readers — the cleanest collaborator boundary in the file.

**Selection and transform** — *selection* (`:4991`), *move tool* (`:8269`) and
*live transform preview* (`:8774`), sharing `_selectionContours`,
`_selectionManager`, `_transformFrames`, `_transformFilter`, `_transformPreview`.
`SelectionManager` already exists as the seed; this finishes it rather than
starting it.

The move tool appears here and in the note above. Taken alone it is 504 lines of
pure behaviour; taken with selection it is part of a better boundary. Either is
defensible — extracting it twice is not.

## Tier 3 — nothing to own

Sections that declare **zero** fields: *templates* (`:1189`), *what you had open
last* (`:1371`), *active layer compositing* (`:9383`, 316 ln), *layer folders*
(`:9035`, 347 ln and 16 commands), *tags* (`:3829`), *whose brush is it* (`:805`),
*medium* (`:3234`), *editing the preset you are on* (`:3671`), *move tool*
(`:8269`). Pure behaviour over hub state — nothing to extract until Tier 0 is
named, after which they become services taking the hub as a parameter.

## Three things to know before starting

**Markers drift, and one of them lies outright.** *Stroke stabilizer (input
smoothing)* at `:5581` runs 530 lines and declares `_sidebarVisible`,
`_sidebarOnRight`, `_isPlaying`, `_playbackSpeedPercent`, `_activeLayerIndex`,
`_tweenCount` and `_tweenEasing` — none of which have anything to do with
stabilization. *Video clip bars* hides the render core, as above. Every
extraction re-derives its boundary from `monolith.py` rather than from a comment,
and should expect to find unrelated state parked inside the region it is quoting.

**Two mechanisms must not move.** `RequestSnapshot` at `:7527` posts at
`DispatcherPriority.Input`: this is B73 — Avalonia puts `Default` above `Input`,
so posting at `Default` made eleven pointer events produce eleven publishes, and
`StrokeLatencyTests` guards it. And `_committingScopedEdit`, declared at `:10522`
and read in `OnDocumentChanged` at `:10531`, is what keeps a stroke commit cheap;
without it the handler re-registers every resource registry, reloads two dockers,
rebuilds the markers view and invalidates the whole canvas. Both are easy to lose
in a move and neither fails loudly.

**The safety net is real, and it is why this is affordable.** 71 test files
construct a `MainViewModel` and 151 reference it. Run the full suite plus the
performance-tagged budgets on each branch, and put `leak-hunter` on any leaf that
touches the paint path.

## Order of work

1. ~~**The ratchet.**~~ **Done.** Stops the files growing while the rest proceeds.
2. ~~**Split `MainWindow.axaml.cs` into partials**~~ **Done** — 5,544 → 429 across
   fifteen partials, budget lowered to 429 in the same commit.
3. ~~**Name Tier 0**~~ **Done** (Q77) — the live-paint engine moved next to its
   state, the render core got a marker, `RequestSnapshot` moved beside
   `PublishSnapshot`. Pure code motion; nothing executes differently.
4. ~~**Extract `LivePaintSession`**~~ **Done** (Q77) — 22 fields and four lifecycle
   methods left the class. `MainViewModel.cs` 13,141 → 12,919, private fields
   143 → 122, and single-section fields 53% → **63%**.
5. ~~**Extract the render orchestrator**~~ **Done, and not as an orchestrator** —
   `PublishState` took the seven bookkeeping fields; the sequencing stayed put, for
   the reasons above. `MainViewModel.cs` 12,919 → 12,878, fields 122 → 118.
6. **Tier 1 leaves**, one per branch (Q78). **In progress** — `GuideSnap` landed
   first. **Read the correction under the table before planning the rest.**
7. **Tier 2 clusters** as collaborators; Tier 3 is now unblocked — its sections have
   a named hub to take as a parameter.

One leaf per branch, because a branch is one objective and "extract two leaves"
needs an "and".

Worth saying last, because it is the reason to do this at all on a desktop-only
product: **this is not cloud-contingent spending.** It is the one refactor that
pays for itself in a codebase that is never hosted — the hottest file in the
repository stops being the place every feature has to be added — and it happens
to be the precondition for a headless split if that is ever wanted.


## Re-applied on top of main, and what that cost

**The whole restructure was re-derived against `main` after 52 commits landed under it,
PR222 included.** That is worth recording because the alternative was tried first and
abandoned for a reason.

PR222 rewrote the live-post pipeline and added publish pacing — the exact code this
document's Tier 0 had extracted. `RenderLivePostProcess`, a method moved in step 3, **no
longer exists on main**. Merging main into the restructured branch produced 41 hunks
against `MainViewModel.cs`, two of which were rewrites rather than additions (+195/−39
and +112). Routing those into nineteen partials would have meant hand-reconciling two
independent rewrites of the same pipeline, with no mechanical check that PR222's
behaviour survived.

So the merge took main's two files **verbatim**, dropped this branch's 34 partials, and
the restructure was re-applied on top. PR222's behaviour is then intact by construction
rather than by inspection — the only claim that can be made cheaply and checked.

**Two things came out better for having been redone.**

The split is now one **name-driven tool** rather than two line-number scripts, because
having to redo it is exactly what made line numbers the wrong interface. It takes groups
as lists of section names, refuses to write unless the class body is identical as a
multiset of lines, and refuses if any marker sits at a brace depth other than 1.

And **the split went first this time**, reversing the original order. Each of the five
collaborator extractions then landed in an 800-to-1,800-line file instead of a
13,000-line one. Q78 recorded the cost of the original ordering; this is that lesson
applied rather than restated.

**PR222's new state went into the collaborators, and improved two of them.**
`PublishState` absorbed `_presentedSeq`, `_publishWhenPresented`, `_lastPublishTicks` and
`_damTimerArmed` — which belong with `_publishSeq` rather than beside it, since
`CanvasIsBehind` compares three of them at once and a deferral released twice puts a
second frame in flight. `LivePaintSession` absorbed `_livePostGeneration`, the staleness
counter, whose only trigger is this state being reset — so keeping them apart is how a
reset that forgets to invalidate in-flight work happens.

| | Lines |
| --- | --- |
| `MainViewModel.cs` | 13,628 → **692** across 18 partials |
| `MainWindow.axaml.cs` | 5,706 → **455** across 15 partials |
| largest partial (`Painting`) | 1,840 |

Full suite green throughout: **4,191 passed**, including every one of PR222's own guards —
`PublishPacingTests`, `LivePostAsyncTests`, `LivePostWedgeReproTests`, `StrokeToScreenTests`.

## Decoupling after the split, and the seam that turned out not to be one

The split answered "is the file too big". It did not answer "is the class too coupled",
and those are different questions: nineteen partials of one class share every field, so
the shared-state block at the top of `MainViewModel.cs` is the whole coupling made
visible rather than reduced. This pass went after that block, which came down from 29
fields to 21: seven collapsed into two collaborators, and three moved into the one
partial that used them. A tenth field elsewhere in the hub turned out to be used by
nothing at all.

**The test for whether a cluster is worth a class is not how many files touch it. It is
whether it has an invariant somebody is maintaining by hand.** Both extractions here had
one, and both were being got wrong or were one throw away from it.

**`BrushWorkingSet`** — `_brushWork`, `_eraserWork`, `_userPresets`, `_applyingPreset`.
The guard is why it is a class. Applying a preset assigns the bound properties, and
every one of their setters writes back into the working settings, so the assignment has
to be fenced or choosing a preset immediately edits it. That fence was **nine
hand-written `_applyingPreset = true; …; _applyingPreset = false;` pairs, none of them
in a `try`/`finally`** — in a region that reaches `Settings.Save()`, which is file I/O
and can throw. One throw leaves the guard raised for the rest of the session, at which
point every bound brush property silently does nothing: the size slider moves and the
brush does not change, with no error anywhere. That is B39's shape exactly. The raise
and the lower are now one method with a `finally`, and the flag has no public setter.

**`TransformSession`** — `_transformFrames`, `_transformFilter`, `_transformPreview`,
`_transformParts`. Same shape as `LivePaintSession`: a gesture whose state has to be
raised together and dropped together, spread across four files. The invariant is
ownership — `Parts.Owned` separates the bitmaps the session rasterised, which it must
free, from the frame cache's own bitmap, borrowed when the whole frame moves. Both ways
of getting it backwards are silent: freeing a borrowed bitmap hands the compositor a
disposed one, and not freeing a rendered one leaks a full-canvas bitmap per in-scope
frame per gesture — 33 MB a time at 4K, seen as memory climbing while an artist nudges a
drawing around. It was correct, in one place, with nothing asserting it.

**The selection cluster was named above and is not being extracted, because the name was
wrong.** *Tier 2* lists `_selectionContours` and `_selectionManager` together as "the
same feature half-extracted". They are not the same feature. `SelectionManager` holds
**object** selection — placements, guides, reference boxes, anchors, collision shapes,
stroke ids — and `_selectionContours` is the **region** selection, the marching-ants
outline that clips painting. They share a word and nothing else; `Deselect` clears both
precisely because they are two things an artist would call "the selection".

Extracting `_selectionContours` into a wrapper would move a plain `List<List<StrokePoint>>`
that has no lifetime, no derived cache to invalidate and no guard to leak. It would score
well on the field count and buy nothing, which is the failure mode this pass is most
likely to fall into: **motion that looks like decoupling because a number went down.** So
it stays, and this paragraph is here instead of a class.

### The field count is now guarded the way the line count was

`MonolithRatchetTests` capped the hub's length and, at 670 lines, has little left to do.
What can still rot is the rule: *a section's own state travels into the partial that owns
it; only shared state stays in the hub.* `SharedStateRatchetTests` derives that rather
than asserting it — it reads the field declarations out of the hub and the usages out of
the partials, so it cannot be satisfied by editing a list.

It found four fields that had drifted the wrong way and would not have been found by
reading: `_untitledCounter` (used only by `Documents`), `_snapshotQueued` and
`_stabilizer` (only by `Painting`), and `_featureDefaults`, **used by nothing at all** —
`Diagnostics.cs` constructs its own.

The rule it checks is "used by the hub, or by two or more partials", and the looseness is
deliberate. The frame caches, the prewarmer and the tile fallbacks are read by exactly one
partial each and heavily by the hub's own invalidation funnel, which is where they belong:
a stricter rule would demand they move and would be wrong.

| | |
| --- | --- |
| `MainViewModel.cs` | 692 → **670** |
| shared-state block | 29 → **21** fields, ratcheted |
| collaborators extracted | 5 → **7** |
