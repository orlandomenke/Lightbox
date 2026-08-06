# Decomposing MainViewModel: which pieces come out, and in what order

Status: **measured, not started.** Nothing here has been extracted. The document
exists so that when it is attempted it is not attempted as one change, and so
the ordering is argued from field ownership rather than from what looks tidy.

`ViewModels/MainViewModel.cs` is 10,098 lines and is the hottest file in the
repository — `HOTSPOTS.md` puts it at heat 0.76 across 30 commits, four of them
fixes. It is at once the document API, the tool state machine, the render
scheduler and the binding surface, and there is no interface between those four
roles.

## The measurement that decides the shape

58 section markers, 343 private field declarations, 63 `[ObservableProperty]`,
73 `[RelayCommand]`, 440 public members.

Counting which private fields each section actually touches:

- **135 distinct private fields** are referenced across the sections.
- **54% of them are touched by exactly one section.**
- **Nine** are touched by five or more: `_editor`, `_cache`, `_dirtyThumbIds`,
  `_composeRing`, `_strokeBuilder`, `_autosave`, `_applyingPreset`,
  `_lastStrokeEnd`, `_liveScratch`.

**So the coupling runs section → hub, not section → section.** The file is
large, and it is shallow. That is the whole reason an incremental extraction is
viable: most sections can leave without disturbing their neighbours, because
they were never talking to their neighbours.

## The recommendation, in two halves

**More partial files is not the split worth making.** It is trivial — the class
is already partial across `MainViewModel.cs`, `MainViewModel.Symbols.cs` and
`MainViewModel.Rig.cs` — and it buys navigability with **zero** decoupling. All
135 fields stay in one scope, every section keeps its licence to touch every
field, and the file boundary hides the coupling instead of reducing it. A
2,000-line partial that reaches into eight others is worse than one honest
10,000-line file, because it looks solved.

**Extracting collaborators that own their state is the split worth making**, and
the pattern is already proven in this codebase rather than imported. `ViewModels/SelectionManager.cs` is 250 lines, owns five private
sets, exposes read-only views of them, raises `SelectionChanged`, and is handed
to the canvas through `CanvasControl.SetSelectionManager`. Every extraction
below should come out looking like that.

## Tier 0 — the hub. Not leaves, and first

Two clusters have to be named before anything large moves. Neither is a leaf and
neither should be split internally.

**The live-paint state machine — 24 `_live*` fields**, plus `_stabilizer`,
`_strokeBuilder`, `_liveDensify` and `_snapshotQueued`. It spans four sections:
*painting* (`:4610`), *live post-processing* (`:4806`), *gradient tool*
(`:5003`) and *the shape tool* (`:5161`). The shape tool alone touches **32**
shared fields, the highest count in the file. These four look like four
features and are one mechanism; separating them would cut through the knot
rather than around it.

**The document and render core** — `_editor`, `_cache`, `_composeRing`,
`_dirtyThumbIds`, `_allThumbsDirty`, `_committingScopedEdit`,
`_applyingEditScope`, `_lastStrokeEnd`. This is the thing every leaf below calls
into, and naming it is what makes the leaves' surfaces small enough to be worth
writing down. It is also obstacle 1 in `DESIGN-cloud-readiness.md` — the same
work, arrived at from the other direction.

## Tier 1 — the leaves

Own state, few hub touchpoints, extractable one at a time. One branch each.

| # | Leaf | At | Size | Fields it owns | What it needs from the hub |
| --- | --- | --- | --- | --- | --- |
| 1 | Frame markers | `:7991` | 185 ln, 3 cmd | `_markersView` | `_editor` |
| 2 | Playback transport | `:5968` | 161 ln, 8 cmd | `_playDirection`, `_playbackStartFrame`, `_playbackEndFrame` | `_clock`, `_strokeBuilder` |
| 3 | Camera | `:9715` | 196 ln, 4 cmd | `_viewThroughCamera` | `_autosave`, `_composeRing` |
| 4 | AI | `:8176` | 169 ln, 3 cmd | `_aiBusy`, `_aiCts`, `_aiEnabled`, `_aiPrompt`, `_aiProviderLabel`, `_aiStatus` | `_artist`, `_cache`, `_dirtyThumbIds`, `_editor` |
| 5 | Timing presets | `:6216` | 315 ln, 3 cmd | `_celClipboard`, `_newTimingPresetName`, `_newTimingPresetPattern`, `_selectedTimingPreset` | `_allThumbsDirty`, `_dirtyThumbIds`, `_editor` |
| 6 | Fill tool | `:3410` | 253 ln, 5 prop | `_fillTolerance`, `_fillGapPx`, `_fillGrowPx`, `_fillBelowLines`, `_smartFill` | `_cache`, `_editor`, `_dirtyThumbIds`, `_committingScopedEdit`, `_selectionContours` |
| 7 | Guides | `:8697` | 405 ln, 4 cmd | `_activeReferenceIndex`, `_guideDragTotal`, `_referenceAlignMode`, `_referenceColumns`, `_referenceRows`, `_snapToGuides` | `_editor`, `_lockDecided`, `_lockedGuide`, `_snapTolerance`, `_strokeAnchor` |
| 8 | Move tool | `:6589` | 438 ln, **0 cmd, 0 prop** | `_moveAnchor`, `_moveDelta`, `_guidesMoveDelta`, `_anchorsMoveDelta`, `_refBoxesMoveDelta`, `_shapesMoveDelta` | `_editor`, `_celRange`, `_selectionManager`, `_transformFilter`, `_transformFrames` |

Why this order:

- **1 is the proof of pattern.** One field of its own, one field from the hub,
  185 lines. If the first extraction is contentious the argument is about the
  approach; this one cannot be.
- **8 is the largest pure-logic win.** 438 lines with no commands and no
  observable properties, which means nothing in XAML moves and the diff is
  confined to C#.
- **4 must go through the pair.** Charter gate G12 requires `ai-engineer` and
  `art-director` on any diff touching an AI path in the view model, and this is
  one, refactor or not.

## Tier 2 — clusters, extracted whole or not at all

Two groups share state across sections and must move as single collaborators.

**Brush preset state** — *brush tool state* (`:2005`), *tags* (`:2968`) and
*whose brush is it (Q9)* (`:470`), sharing `_brushWork`, `_eraserWork`,
`_applyingPreset` and `_userPresets`.

**Selection and transform** — *selection* (`:3719`), *move tool* (`:6589`) and
*live transform preview* (`:7027`), sharing `_selectionContours`,
`_selectionManager`, `_transformFrames`, `_transformFilter` and
`_transformPreview`. `SelectionManager` already exists as the seed of this one;
the work finishes what it started rather than beginning something.

Note that the move tool appears in both Tier 1 and Tier 2. Taken alone it is a
clean 438-line leaf; taken with selection it is part of a larger and better
boundary. Either is defensible — what is not is extracting it twice.

## Tier 3 — not leaves at all

These sections have **zero** fields of their own. They are pure behaviour over
hub state, so there is nothing to own and nothing to extract until Tier 0 is
named; afterwards they become services that take the hub as a parameter.

*Layer folders* (`:7277`, 343 ln and **18 commands**), *active layer
compositing* (`:7620`, 315 ln), *what you had open last* (`:978`, 255 ln),
*whose brush is it* (`:470`, 236 ln), *tags* (`:2968`, 217 ln), *templates*
(`:799`, 179 ln), *gradient tool* (`:5003`, 158 ln).

## Two things to know before starting

**The section markers drift, so they are a map and not a boundary.** The marker
*stroke stabilizer (input smoothing)* at `:4212` runs 317 lines, and by `:4377`
it is holding `_sidebarVisible`, then `_newLayerKind` at `:4463`,
`_playbackSpeedPercent` at `:4466` and `_tweenCount` at `:4514` — none of which
have anything to do with stabilization. Every extraction must re-derive its own
boundary from which fields are actually used, and should expect to find
unrelated state parked inside the region it is quoting.

**Two mechanisms must not move.** The dispatcher priority in `RequestSnapshot`
at `:5846` is B73: Avalonia puts `Default` above `Input`, so posting at `Default`
made eleven pointer events produce eleven publishes, and `StrokeLatencyTests`
guards the fix. And the `_committingScopedEdit` flag set at `:5936` and read in
`OnDocumentChanged` is what keeps a stroke commit cheap — without it the handler
re-registers every resource registry, reloads two dockers, rebuilds the markers
view and invalidates the whole canvas. Both are easy to lose in a move and
neither fails loudly.

## Safety net and method

**70 test files construct a `MainViewModel`** and 106 reference it, so a change
in behaviour is caught rather than shipped. That is the strongest argument that
this refactor is affordable at all.

One leaf per branch, because a branch is one objective and "extract two leaves"
needs an "and". Run the full suite plus the performance-tagged budgets on each,
and put `leak-hunter` on any leaf that touches the paint path.

Worth saying last, because it is the reason to do this at all on a desktop-only
product: **this is not cloud-contingent spending.** It is the one refactor that
pays for itself in a codebase that is never hosted — the hottest file in the
repository stops being the place every feature has to be added — and it happens
to be the precondition for a headless split if that is ever wanted.
