# Q77 · Naming Tier 0: which cluster, in how many steps, who owns the state, and where B73 lives — **answered 2026-08-13**

**Asked with the question prompt before any code was written; all four answers
took the recommendation.** Step 3 of
`docs/DESIGN-mainviewmodel-decomposition.md`. Two findings reframed the questions
before they were put, and both are why the answers came out as they did:

- **The two Tier 0 clusters are in opposite states.** The render core's *state* is
  already owned — `_composeRing`, `_cache`, `_tileFlats`, `_stackBake`, `_prewarm`
  and `_tileFallbacks` are all collaborators declared at the top of the class. What
  is missing there is sequencing, so it wants an orchestrator rather than a new
  owner of state. The live-paint machine is the opposite: 24 raw SkiaSharp fields
  with no owner at all.
- **A second marker was lying, worse than the one found in the review.** The
  section headed *the shape tool* ran 804 lines, of which only ~180 were the shape
  tool. The rest — `MoveStroke`, `FlushLivePreview`, `StampLiveDabs`,
  `StampLiveSmudge`, `EndStroke`, and `RequestSnapshot` — was the live-paint
  engine, 800 lines away from the state it mutates. That is the entire reason the
  shape tool measured 30 foreign field touches and read as a tool tangled into the
  paint path, when in truth it *was* the paint path with a tool on top.

Four questions, four answers:

1. **Which cluster? — (a) live-paint, as recommended.** It is the one with
   genuinely unowned state, and it is the knot the shape and gradient tools are
   caught in. The render core is smaller work than the design document assumed for
   the reason above, so it can wait; both in one branch was declined as the
   one-objective rule broken on the riskiest change in the plan.
2. **One step or two? — (a) re-mark first, extract second, as recommended.** This
   branch is pure code motion: the engine moved next to its state, the live-post
   methods and the gradient methods went back under their own headings, and the
   render core got a marker. **No line of code changed** — verified the way the
   view split was, by showing the file identical as a multiset of lines. The
   extraction is its own branch. The alternative put a 580-line move and a
   state-ownership change in one diff on the hottest path in the application,
   where nobody could tell which lines changed behaviour.
3. **What owns the state? — (a) a `LivePaintSession` collaborator, as
   recommended**, in the manner of `SelectionManager`: one long-lived object, no
   per-event allocation, so the paint path pays nothing for it. **What that choice
   costs:** its public surface has to be wide enough for the shape and gradient
   tools, which is the thing to watch when the extraction lands. Keeping the fields
   and extracting only methods was declined as navigability without decoupling —
   the exact thing the document was right to refuse about partials-for-hubs.
4. **Where does `RequestSnapshot` live? — (a) stays in the view model, as
   recommended.** It schedules a publish, so it belongs beside `PublishSnapshot`,
   and it moved there in this branch rather than travelling with the paint path
   that calls it. Its `DispatcherPriority.Input` is B73 and does not fail loudly,
   so the live-paint extraction now does not touch it at all.

**What the re-mark bought, measured:** the shape tool went from 804 lines and 30
foreign field touches to 184 and 5 — from the widest section in the file to an
ordinary Tier 1 leaf. `painting` went from 195 lines to 605 and now holds the
engine beside the 19 fields it mutates. The render core went from anonymous to 785
named lines. Nothing was extracted and nothing executes differently.

**Answer 3 has since been carried out, and the numbers are worth keeping here.**
`ViewModels/LivePaintSession.cs` took 22 fields and four lifecycle methods:
`MainViewModel.cs` 13,141 → 12,919, private fields 143 → 122, fields touched by
exactly one section 53% → **63%**, and *live post-processing* went from reaching 19
foreign fields to 6. The full suite, the performance-tagged budgets and
`StrokeLatencyTests` are all green, and no per-event allocation was added — the
session is one long-lived object and the properties are auto-properties the JIT
inlines.

**What it did not buy, stated plainly:** `_live` now crosses seven sections, so the
coupling did not disappear — it became one typed reference in place of 22 raw
fields. The session is not an encapsulation boundary either; the engine mutates its
properties directly. What is genuinely better is that `ClearEffectState` cannot be
got half-right any more, which is B39's exact failure mode.

**Two mistakes were made writing it, both silent, and both are now pinned by
`LivePaintSessionTests`.** The first draft of `ResetPostProcess` disposed the pooled
`PostScratch` instead of wiping the region the last stroke used — a 33 MB
allocation on every pen-down at 4K, with the whole suite green. The second replaced
Skia's mutating `SKRectI.Union` with hand-rolled min/max that skipped empty rects;
measured, `(0,0,0,0) ∪ (5,5,9,9)` is `(0,0,9,9)` in Skia and `(5,5,9,9)` under the
rewrite, because Skia's union is a plain min/max over corners and a default
`SKRectI` is empty *at the origin*. Both were caught by reading the originals rather
than by any test, which is the argument for the tests that now exist: this class's
job is to make expensive things cheap by keeping them alive, and a correctness test
cannot see the difference between keeping a buffer and reallocating it.

**One thing the naming step cost, recorded because the mechanism is one commit old:**
the ratchet budget for `MainViewModel.cs` went *up*, 13,110 → 13,141. The motion
shrank the file by five lines; the 38 lines of comment explaining why the old map
was wrong took it over. It was raised rather than absorbed by trimming that
comment, on the grounds that the budget exists to stop feature code accumulating
in a file nobody can read, not to price the documentation that makes it readable.
That is the only legitimate reason to raise one — the file got more legible and
slightly longer. "A feature needed the room" is not on the list.

It came back down to 12,919 in the branch after, when `LivePaintSession` landed. A
budget that rises once for documentation and falls by 222 for an extraction is doing
its job; one that only ever rises is a comment.

**Answer 1's second half — the render core — has since been carried out, and it
contradicted the plan recorded above.** Q77 said that cluster wanted "an
orchestrator holding those six collaborators, not a new owner of state". Reading
`PublishSnapshot` end to end says otherwise on two counts, and both are worth
keeping because the mistake is a reusable one:

- **The state was not all owned.** The six collaborators own the *caches*. The
  *bookkeeping* — `_pendingDirty`, `_dirtyIsWholeCanvas`, `_pendingViewport`,
  `_publishSeq`, `_lastPublished`, `LastPublishClip`, `FramesReused` — was seven raw
  fields belonging to nothing. So "its state is already owned" was a claim read off a
  collaborator list rather than checked against the code.
- **An orchestrator is the wrong shape.** `PublishSnapshot` reads about fifteen
  pieces of view-model state, so an orchestrator must be handed them per call or hold
  a reference back. The second is a second view model with circular coupling; the
  first allocates a request per publish, and the code next door already refuses that
  trade — the transform-split delegate is cached in a field rather than written as a
  lambda, because "a lambda capturing `this` allocates a closure and a delegate on
  every publish, and a publish happens per pointer event while drawing".

So `ViewModels/PublishState.cs` took the bookkeeping and the sequencing stayed in
`PublishSnapshot`, reading the view model directly and allocating nothing.
`MainViewModel.cs` 12,919 → 12,878, private fields 122 → 118.

**`TakeDirty` is why this is a class rather than seven fields moved sideways.**
Reading the dirty region and clearing it is three statements that must happen
together, and both ways of splitting them are silent: clear without reading and the
next publish repaints nothing that changed; read without clearing and the dirty rect
grows forever, so painting stops being bounded work. Invariant 6 rests on that one
method. `PublishStateTests` sabotages it both ways, and also pins the one-line
difference between `InvalidateWholeCanvas` and `RepaintEverythingThisPublish` — the
fold transition needs the flag without losing the fingerprint, which is "equivalent
today" only because no early return sits between the two points in `PublishSnapshot`.
