# Q95 · Guides have two selections, two hit tests and two painters — which one survives? — **answered 2026-08-15: the manager, re-keyed to ids, and the Arrow moves them too**

Asked from an audit of the tool surface — *"are there gaps in usage; do tools have their
own systems instead of reusing existing ones"* — and this was the worst case it found.
Two tools reach guides and they shared nothing:

| | Move tool | Arrow tool |
| --- | --- | --- |
| Selection | `MainViewModel.SelectedGuideId` — one string id | `SelectionManager` — a set of `int` positions |
| Hit test | `GuideAt` — 6 screen px ÷ scale, every kind, respects the locks | `PickGuideAt` — 5 document units, `Angles[0]` only, no locks |
| Painting | `GuidePainter` with its `Emphasis` | inline gold `SKPaint` in `CanvasControl` |

Neither selection wrote the other, so the options bar stayed blank for a guide picked
with the Arrow, and `BeginGuidesMove` was unreachable with the only tool that filled the
set it reads. The full diagnosis is **B215**; what needed deciding is what to unify onto.

## Which model wins

| | What it costs |
| --- | --- |
| **`SelectionManager`, re-keyed to ids** (recommended, **chosen**) | The larger diff. Multi-select is the more capable model and already drives the group move, and re-keying applies the class's own stated rule for strokes to the category that had been breaking it. |
| **`SelectedGuideId`, extended to a list** | Smaller change to the options bar, but `SelectionManager` keeps five other categories and guides become the odd one out — the same divergence one file along. |
| **Keep both, sync them** | Cheapest, and it leaves two states that must agree. They already had to agree and did not; a mirror is a rule that has to be remembered at every new write site. |

**The id-keying is the half that matters most and it was the least visible.**
`SelectionManager`'s doc comment argues at length that a selection must not be held by
position — *"delete one stroke and every position after it shifts, so a selection held by
position would silently come to mean different strokes"* — and then held guides and
reference boxes by position. `RemoveGuide` exists, so it was live, and the failure is the
silent kind: the next drag moves a guide the artist never picked. `MainViewModel.SelectedGuides`
had already worked around it at the *move* site by resolving to references before a drag;
the seam it could not defend was the selection itself.

**`SelectedGuideId` is now derived and null on a multiple selection.** That is the honest
answer rather than picking one arbitrarily: the options set *a* guide's numbers — this
grid's pitch, this chart's head count — and there is no single guide for them to mean.
The group is still perfectly movable; it is the numbers that need one.

## Whether the Arrow should move guides or only select them

| | What it costs |
| --- | --- |
| **The Arrow moves them too** (recommended, **chosen**) | One more tool that can nudge a guide. `ToolId.Arrow` already documents picking guides as part of what it does, so a tool that can select but not move was the inconsistency, and the two-tool dance disappears. |
| **The Move tool keeps the drag** | Preserves the split, and then the fix is to make the Arrow's pick honour the locks and light the options bar — a rule an artist has to learn instead of a behaviour they can guess. |
| **The Arrow should not pick guides at all** | Simplest surface, and it deletes a capability `ToolId.Arrow` describes as intentional. |

`ReachesGuides` is what came out of it: one property, asked by the grab gate, the
emphasis, the options bar and the options panel. Those four had each asked `IsMoveTool`
separately, which is how they drifted apart in the first place — the point of a single
property is that they cannot drift again.

## The part that was not a preference

Whichever model won, **the leave-tool rule had to cover more than one category.**
`LeaveToolStateBehind` drops the line selection when the Arrow is put down, with a
comment explaining that drawn state must not outlive the capability it points at — and it
covered one of `SelectionManager`'s six categories. A guide picked with the Arrow stayed
gold while the brush painted over it. The rule now reads *survives between the two tools
that can act on it, dropped by every other one*, which is the same sentence the stroke
selection was already getting.

## What was deliberately left

**Reference boxes are still keyed by position.** A `ReferenceBox` has no id to key on, so
re-keying it is a record change and a second objective — its own branch, not an "and"
bolted onto this one. The argument above applies to it unchanged.

**The other gaps the audit found are not this branch either**, and are written up in the
thread that raised this: guide snapping reaches three of thirteen tools, angle constraint
is implemented three times with three constants, hover preview reaches one of the three
tools that grab a line, and the quick-bar catalogue has no entry for the pen, the width
tool, the white arrow or the bone tool.
