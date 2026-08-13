# Decomposing the two big files: which pieces come out, and with which tool

Status: **reviewed 2026-08-13, not started.** Nothing has been extracted. This
revision exists because the first one went stale before it was merged, and
because it answered "how do we split this" with one tool when the two files
involved need different ones.

Two files are in scope, and the whole point of this revision is that they are
not the same problem:

| File | Lines | Shape | Tool | State |
| --- | --- | --- | --- | --- |
| `ViewModels/MainViewModel.cs` | 13,110 | large, shallow, one hub | collaborators for the hub, partials for the leaves | not started |
| `Views/MainWindow.axaml.cs` | 5,544 → **429** | 37 near-independent sections over one field | partials, and that is the whole job | **done** |

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

**So the tool is chosen per section by what the fields say** (Q73):

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

**Both are now named** (Q74), which was step 3 and is **done**. Naming was a pure
code-motion change: no line of code was altered, verified by showing the file
identical as a multiset of lines. The extraction is the next branch.

**The live-paint state machine** — `painting` (`:6285`), which owns the 19 `_live*`
fields, together with `live post-processing` (`:6891`), which reads all of them.
Those two are one mechanism and the future `LivePaintSession`.

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

**And it needs different work from the other cluster.** Its state is *already*
owned: `_composeRing`, `_cache`, `_tileFlats`, `_stackBake`, `_prewarm` and
`_tileFallbacks` are all collaborators declared at the top of the class. What is
missing is the sequencing — `PublishSnapshot`, `FlattenTilePasses`,
`ComposeViewportCulled`, `MarkDirtyRegion`, `InvalidateWholeCanvas`, the prewarm
drive. So it wants an **orchestrator holding those six**, not a new owner of state,
and it is less work than this document originally assumed.

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
3. ~~**Name Tier 0**~~ **Done** (Q74) — the live-paint engine moved next to its
   state, the render core got a marker, `RequestSnapshot` moved beside
   `PublishSnapshot`. Pure code motion; nothing executes differently.
4. **Extract `LivePaintSession`** — the collaborator that owns the 24 `_live*`
   fields, per Q74. **Next**, and the first branch here that changes behaviour
   rather than ordering: it runs per pointer event, so it needs `leak-hunter`, the
   performance-tagged budgets and `StrokeLatencyTests` on every push. Watch its
   public surface — it has to serve the shape and gradient tools, and a wide
   surface would reproduce the coupling with extra steps.
5. **Extract the render orchestrator** holding the six existing collaborators.
6. **Tier 1 leaves**, one per branch, guides and frame markers first — the shape
   tool is now one of them.
7. **Tier 2 clusters** as collaborators; Tier 3 becomes possible once 4 and 5 land.

One leaf per branch, because a branch is one objective and "extract two leaves"
needs an "and".

Worth saying last, because it is the reason to do this at all on a desktop-only
product: **this is not cloud-contingent spending.** It is the one refactor that
pays for itself in a codebase that is never hosted — the hottest file in the
repository stops being the place every feature has to be added — and it happens
to be the precondition for a headless split if that is ever wanted.
