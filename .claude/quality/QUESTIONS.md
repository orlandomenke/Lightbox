# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`DECISIONS.md`.

---

## Q31 · Does a frame remember that a model made it? — **answered (a)**

**Answered 2026-08-07: (a), stored on the frame, absent unless AI touched it.**
A hand-drawn frame writes no key, so a document that never used the AI is
byte-identical to one from before the feature existed — the camera's rule.

**Blocks:** nothing. Phase 0 can proceed.

`docs/DESIGN-ai-correctness.md` puts a verifier and a deterministic fallback
behind every AI inbetween, which means three frames can look alike and have very
different histories: one the model got right, one it got wrong and was repaired,
one that fell back to the deterministic engine entirely.

**(a) Stored on the frame, absent unless AI touched it.** An artist returning
after a month knows which frames to trust and which to look at again. It is a
new key in the document, so hand-drawn frames write nothing — the camera's rule.

**(b) Session-only.** The timeline marks AI frames while the app is open and
forgets on reload. No format change; the information vanishes exactly when it is
most wanted.

**(c) Not tracked.** An inbetween is an inbetween.

**Recommend (a).** The whole feature is a claim about trust, and a claim you
cannot audit a month later is not one. Note the cost honestly: it is a document
format change, and *derived* data in the record is the mistake Q16 avoided for
placement readings — the defence here is that provenance is not derived from
anything, it is a fact about how the frame came to exist.

**Blocks:** phase 0 of the correctness pipeline.

## Q32 · What happens to a frame that fails verification and cannot be repaired? — **answered (c)**

**Answered 2026-08-07: (c), insert nothing and say why.** Against the
recommendation, and **the owner's reasoning defeats the recommendation rather
than merely overriding it**, so it is recorded in their words:

> *"For complex subjects — a human, a dog or something else complex and organic
> — I believe the deterministic inbetweener is prone to make mistakes. I'd
> rather have nothing than a frustration."*

The recommendation assumed the deterministic engine is a **floor**. On a box it
is. On a dog it is not — it is a confident wrong answer, and **B113 is the proof
rather than a worry**: four straight lines making a box, and the matcher crossed
the top and bottom edges over and collapsed the shape mid-motion. Whatever that
does to a quadruped's legs, it does silently.

So substituting it under a failed AI request is not a safe default, it is
swapping one unreliable answer for another and labelling the swap as safety. A
frame that is absent costs the artist a minute; a frame that is subtly wrong on
a dog costs them the time to notice, plus their trust in every other frame.

**What it costs, stated so nobody rediscovers it as a bug.** The artist asked
for four inbetweens and may get three. The status has to name which `t` was
refused and why, or the gap is a puzzle rather than a decision — "frame 3 of 4
was refused: the near arm did not stay between the keys" is the bar.

**Read per frame, not per batch:** the frames that passed are inserted and the
ones that failed are not. All-or-nothing would throw away good work because one
frame was bad. Say so if that reading is wrong — it is the one part of this
answer that was inferred rather than given.

**Blocks:** nothing.

**(a) Insert the deterministic answer, flagged.** Four inbetweens asked for, four
delivered, the fallen-back ones marked. Nothing silently missing, nothing
silently wrong.

**(b) Insert it silently.** No visual noise; the artist is never told the AI
failed, which makes the feature look better than it is.

**(c) Insert nothing and say why.** Strictest reading of "reliable", but a gap
in the timeline is work to find and fill, and the deterministic answer was
available the whole time.

**Recommend (a)**, and it depends on Q31: "flagged" needs somewhere to record
the flag. If Q31 lands on (c), this collapses into (b) whether we like it or not.

**Blocks:** phase 0.

## Q33 · An AI answer nearly identical to the deterministic one — reject or report? — **answered (a)**

**Answered 2026-08-07: (a), report only, never reject.** Distance from the
deterministic answer is a cost signal and a diagnostic, never a veto.

**Blocks:** nothing.

The deterministic engine is both the fallback and the reference, so distance
from it is free. Too far is suspicious. Too close means the model added nothing.

**(a) Report only, never reject.** Agreeing with the cheap engine is not
incorrect. Surface it as a cost signal — *"this model added nothing on 9 of 12
frames"* — and let the artist decide.

**(b) Reject and fall back.** Cleaner cost story, at the risk of throwing away
answers that were right.

**Recommend (a).** Rejecting a correct answer for being unimaginative is
indefensible on correctness grounds, and the cost argument is fully served by
saying so out loud. The threshold for "nearly identical" is also exactly the
sort of number that gets tuned until it passes.

**Blocks:** nothing — this can be added after phase 0.

## Q34 · Does the golden set ship with the app? — **answered (a)**

**Answered 2026-08-07: (a), it ships.** Grading an artist's own model is the
bring-your-own-model story rather than a development convenience, and it is what
separates *connectable* from *usable*. The obligation that comes with it: the
set has to stay honest, so it is committed, reviewed, and changes to it are
changes to a published claim.

**Blocks:** nothing. Phase 2 can proceed.

A committed set of keyframe pairs with known-good answers, scored by the
verifier, is what turns "reliable" into a number per model.

**(a) Ships.** Point Lightbox at any local or hosted model and it reports what
that model can and cannot do. This is the bring-your-own-model onboarding story,
and the difference between *connectable* and *usable*.

**(b) Development artifact only.** Stops regressions in the built-in providers;
an artist with an unusual local model is back to trial and error.

**Recommend (a).** Constraint 2 is that artists bring their own model; shipping
the grader is what makes that a feature rather than a shrug. Cost is install
size and the obligation to keep the set honest.

**Blocks:** phase 2.

## Q25 · Is a character sheet a document, or part of one? — **answered (a)**

**Answered 2026-08-04: (a), it stays part of a document.** No format change, no
new project-manifest slot, and no new docker row type that is not a file. The
reported pain is losing work — *"character sheets are not saved to disk"* — and
that is fixed by making sure there is a file behind the document the sheet lives
in, which costs one prompt.

The docker-visibility half of the report is answered rather than implemented: a
character sheet **is** visible in the project docker, as the document that
contains it. If sheets later need to be shared between documents, that is the
argument for (b) and it is a better one than this.

**B66 is unblocked** and is now two ordinary pieces: ask for the name before
writing anything (B65's rule on another surface), and prompt to save a document
that has never been saved so the sheet has somewhere to live.

**Blocks:** nothing.

The report says: *"Outside of a project (single file) a character sheet is a
manually saved document. Creating a character sheet should directly prompt
saving. In a project, a character sheet is directly added, similar to how the
project dockers add them directly."*

That describes a character sheet as **a document with its own file**. The code
has it as **part of a document**: a `ReferenceSheet` lives in
`Doc.ReferenceSheets`, so it is saved when its document is saved and has no file
of its own. The project manifest holds `DocumentRef` (animations, shots,
project documents) and `Character` — there is no slot a reference sheet could
occupy, which is why it cannot appear in the project docker today.

So the two halves of the report need different things, and only one is a defect:

**(a) It stays part of a document, and the bug is that an unsaved document loses
it.** Then the fix is the prompt: creating a sheet on a never-saved document
prompts to save, so there is a file behind the work. Nothing in the format
changes, nothing new appears in the project docker, and "not visible in the
project docker" is answered with *it is inside a document, and the document is
listed*. Cheapest by a wide margin.

**(b) It becomes a document in its own right** — its own file, its own
`DocumentRef`, listed in the docker beside animations. Matches the report's
wording most literally and makes "add it directly in a project" fall out for
free. It is a **format change**: sheets move out of `Doc`, existing documents
need migrating, and `CLAUDE.md`'s rule that a proposal requiring a format change
has "drifted into redefining what a document is" applies squarely.

**(c) Both — it stays in the document and the docker learns to show it.** No
format change, and the docker gains a row type that is not a file, which every
path that maps a row to a path (`PathOf`, reveal, copy path, rename in **B64**)
then has to have an answer for.

**Recommend (a)**, because the reported pain is losing work — "character sheets
are not saved to disk" — and (a) fixes exactly that at the cost of one prompt.
The docker visibility that (b) and (c) buy is a smaller complaint, and (b) spends
a format migration on it. If sheets later need to be shared between documents,
that is the argument for (b) and it is a better one than this.

## Q22 · Is a "Document" called a Workfile, and what else is in that menu? — **answered (a)**

**Answered 2026-08-04: (a), *Document* stays.** Fix the grouping and the dead
entries; do not rename. The report says the *menu* is undecipherable rather than
the *word*, and folders being visually indistinguishable from files is the
complaint the fix should answer first. (b) stays available if the confusion
survives that — but two names for one thing is usually the cause of the next
confusion, and `Document` is load-bearing in the manual, the roadmap,
`DocumentRef` and the MCP surface.

**B63 is unblocked entirely**: both halves are now ordinary work.

**Blocks:** nothing.

Raised in a report: the create-in-project menu is "undecipherable — what is a
folder and what is a workfile", with a suggestion to rename *Document* to
*Workfile*.

The defect underneath is filed (**B63**: entries that produce nothing, and no
visual split between folders and files). What cannot be decided from the code is
the vocabulary. *Document* is used throughout the manual, the roadmap and the
serialization (`DocumentRef`, `NewDocumentSettings`), so a rename is not a label
change — it is a rename across the UI, the docs and the artist's mental model.

**(a) Keep *Document*.** Fix only the grouping and the dead entries. Cheapest,
and the word is already established everywhere else in the product.
**(b) Rename to *Workfile* in the UI only.** The record keeps `Document`. Solves
the reported confusion at the cost of two names for one thing, which is the
thing that usually causes the next confusion.
**(c) Rename everywhere.** Consistent, and it touches the manual, the roadmap,
the MCP surface and every serialized name — an expensive change to make for a
menu.

**Recommend (a) plus B63's grouping fix**, on the grounds that the report says
the *menu* is undecipherable rather than the *word* — folders and files being
visually indistinguishable is the complaint the fix should answer first. If the
confusion survives that, (b) is still available.

## Q23 · How does a tab say whether its document belongs to a project? — **answered (a)**

**Answered 2026-08-04: (a), a badge on the tab.** What was asked for, and the
whole of the reported need: self-contained, no OS interaction, sitting exactly
where the ambiguity is. The window title (b) is deliberately not taken now —
Avalonia sets it per window rather than per tab, so with several tabs open it can
only ever describe the active one, and that is a second design rather than a
free addition.

Worth building *after* **B67**, not before: when dockers become document-scoped
the panels visibly change as tabs switch, and the badge is what stops that
reading as a bug. Filed as roadmap work rather than a bug — nothing is broken,
something is absent.

**Blocks:** nothing.

Reported alongside **B67**: "there is no good way to identify open documents
(tabs) as part of a project or not. A small boxed P in the tab would already
help a lot. In the title bar of the OS would be a great additional position."

The reporter has proposed a design, which makes this a question about *how far*
rather than *whether*. It matters more once B67 lands, because when dockers
become document-scoped the panels an artist sees will change as they switch
tabs, and a visible reason for the change is what stops that reading as a bug.

**(a) A badge on the tab.** What was asked for. Self-contained, no OS
interaction, and it sits exactly where the ambiguity is.
**(b) Badge plus the window title.** The title bar is where every other
application says which file is open, and Avalonia sets it per window rather than
per tab — so with multiple tabs it can only describe the active one. That is
probably fine and is worth saying out loud rather than discovering.
**(c) The project name rather than a badge.** More informative and much wider;
tabs are already short on room.

**Recommend (a) first**, because it is the whole of the reported need and is
cheap, with (b) as a follow-up once B67 makes project membership matter visibly.

## Q24 · What is a saved brush setting scoped to, and does saving it need a button? — **answered: automatic**

**Answered 2026-08-04: automatic persistence, no button.** Brush tuning survives
a restart on its own; there is no explicit *save settings* action and therefore
no second mechanism with a different lifetime competing with the first. The
reported pain was losing settings on restart, and that needs no new concept.

The scope question the button would have forced is deferred with it. `BrushScope`
already feeds a new document the project's brush
(`ANewDocumentInTheProjectIsFedThatBrush`), so per-project exists; per-file does
not, and nothing now requires choosing between them. **B71** is therefore the
whole of the work, and it keeps the rule that a brush left at its defaults writes
no keys.

**Blocks:** nothing.

Reported: "individual brush settings need to be cached for the duration of the
session… when brush settings are changed, present the user a save settings
button next to the all brush settings. This is stored per file and/or per
project."

Two decisions are tangled here and only the first is required by the bug.

**Automatic or explicit.** B71 as filed makes tuning survive a restart
automatically. An explicit *save* button is a different promise — it says the
tuning is a named thing an artist commits to, and that unsaved changes are
discardable. Both are defensible; shipping both without deciding gives an artist
two mechanisms with different lifetimes and no way to tell which one is holding
their brush.

**And what the scope is.** `BrushScope`/`BrushScopeDefaults` already exist —
a project feeds a new document its brush, guarded by
`ANewDocumentInTheProjectIsFedThatBrush` — so *per project* is built. *Per file*
is not, and the report asks for "per file and/or per project", which is the part
that needs a person: the two disagree the moment a document in a project is
opened, and something has to win.

**Recommend automatic persistence (B71) first and the button deferred**, because
the reported pain is losing settings on restart and that needs no new concept.
If the button still seems wanted afterwards, it is a small addition to a
mechanism that exists rather than a second one competing with it.

## Q11 · What a "reusable animation preset" would be that a cycle symbol is not — **answered (b)**

**Answered 2026-08-03: (b), a timing preset, and the other line is struck as (a).**
One item, specified: *save an exposure pattern and apply it to a range of cels*.
It re-exposes drawings that already exist, which is the half of frame-by-frame
work a symbol cannot carry — a symbol carries drawings, a timing preset carries
their spacing.

## Q20 · What frame bounds does an Asset project export from an unbounded canvas? — **answered (b), and the question was half wrong**

**Answered 2026-08-04.** Two corrections, and the second one dissolves most of it.

**The premise was too narrow.** This was framed as a game-animation problem, and
the tool is a drawing, painting *and* animation application. An infinite canvas
belongs to the **Shot** target — a world the camera frames, delivered as video —
not to the Asset one, where the canvas *is* the output. The game export pipeline
is already built and is not what this feature serves.

**And the conflict is not a project-type gate.** An unbounded canvas and a fixed
frame-bounds sprite export are **mutually exclusive by construction**, in every
project type — that is a fact about the two features, not about a manifest. So
nothing is gated: the pair is declared incompatible, the refusal names the fix,
and authoring an export region resolves it. Reach survives untouched, and
*Making reach unconditional* stands as written. Recorded as its own roadmap item.

So the answer is **(b), an authored export region** — arrived at from the other
direction than expected. It is not the rule for deriving bounds from an
unbounded canvas; it is the thing an artist authors to *make* the canvas bounded
where a bounded answer is required. (a) is still the right starting value for
that region — bounds-of-ink as a first guess, then draggable — but it cannot be
the mechanism, because a derived bound changes silently when a stray mark lands
in frame 40, and a game build cannot take that.

**Blocks:** nothing now. `docs/DESIGN-infinite-canvas.md` can be built against
this.

*The analysis below is what the answer was reached from, kept for the reasoning.
Its framing is the one the answer corrects: it treats the asset case as central.*

`CLAUDE.md` makes both of these first-class, and this is the one place they meet
head-on. **Assets** — "the canvas *is* the output. There is no camera, frame
bounds must stay consistent, and every frame is a deliverable." An **infinite
canvas** is defined by not having bounds. A sprite sheet is defined by having
consistent ones. So "the asset workflow loses" is not an available answer.

It cannot be answered from the code, because the code has never had to say what
the edge of a drawing is — `Scene.Width`/`Scene.Height` have always answered it
and an unbounded canvas removes the answer rather than changing it.

**(a) Bounds of ink, per scene.** Export the rectangle that encloses every
stroke in the sequence, identical for every frame. Needs nothing authored and it
is what an artist means by "the drawing". The risk is that it is *derived*: add
one stray mark in frame 40 and every previously exported frame changes size,
silently, which is exactly the kind of thing that breaks a game build.

**(b) An authored export region.** A rectangle the artist places once, saved with
the project. Stable by construction — the property assets need — and it makes
the bounds a thing you can see and drag rather than a consequence. Costs a UI
surface and one more thing to set up before the first export.

**(c) The camera, when one exists; ink otherwise.** Reuses machinery that is
already built, keyframed and exported. But `CLAUDE.md` says a camera is
shot-level machinery that must stay absent from asset work — this would make the
asset target depend on the one thing it was defined as not having.

**Recommend (b)**, on the grounds that consistency is the requirement rather
than convenience, and only (b) gives it by construction. (a) is the better
default *inside* (b) — an authored region that starts at the bounds of ink is
one click rather than a blank rectangle.

## Q21 · Is the infinite canvas a document property or a project-type default? — **answered (c), both, and they are not alternatives**

**Answered 2026-08-04: both — and the question contained a false choice.**
"Document property *or* project default" reads as two designs; it is one. The
reach rule already says exactly this: a project type decides *what is on, what
is in front of you, and what a new document starts with — never what the
application can do*. So the **property lives on the document** (that is the
capability, available everywhere) and a **project supplies the default** (that
is what a new document starts with). Answering "both" is the rule applied, not
a compromise between two readings.

Both cases the answer came from are real and neither needs a mechanism the
other lacks: somebody making *one* infinite-canvas animation turns the property
on for that document, and somebody producing a run of product or service
animations sets it once on the project so every new document starts that way
rather than switching it on each time.

**The mechanism exists and is proven, which is why this is cheap.** A project
already feeds new documents a default brush — `BrushScope`,
`BrushScopeDefaults`, guarded by `ANewDocumentInTheProjectIsFedThatBrush`, and
by `AProjectThatNeverAsksForThisWritesNoBrushKey` so an unused default writes
nothing. An infinite-canvas default is the same shape against the same
precedent. Note it is the **project** that carries it, not only the project
*type*: a studio's own project can default to unbounded whatever type it is,
which is what the reach rule means by defaults never deciding availability.

**Blocks:** nothing. It was never a blocker — a project type can only default a
property that exists, so the document property comes first under either answer.


*The analysis below is what the answer was reached from, kept for the reasoning
rather than as a live recommendation — (a) and (b) turned out to be one design.*

The reach rule settles the hard half already: every feature is reachable in every
project type, so this is not "who is allowed an infinite canvas". It is what a
new document starts with, and whether turning it on later is a document edit or a
project setting.

**(a) A document property, off by default everywhere.** Matches the camera
exactly — absent from the file until authored, askable for anywhere. Simplest,
and "optional means absent" falls out for free.

**(b) A project-type default.** A storyboard or an illustration starts unbounded,
a sprite project starts fixed. More convenient on day one, and it puts a
behaviour an artist has to reason about into a manifest they rarely open.

**Recommend (a)** until somebody asks for (b), because (a) is a prerequisite for
(b) rather than an alternative to it: a project type can only default a property
that already exists.

## Q19 · Are Linux and macOS shipping targets, or only development ones? — **answered (a)**

**Answered 2026-08-04: (a), development targets only — Windows is what ships.**
The glibc floor is accepted and closes as not-applicable, on this question's own
reasoning rather than in spite of it: `build.yml` publishes exactly one artifact,
`win-x64`, so nothing crosses the floor and a rising one cannot lose a user who
has nothing to download. Anyone on Linux today built from source and therefore
has a .NET SDK, which puts their distro far above either number.

The consequences, so they are not re-derived: the `net10.0` upgrade is unblocked
and has landed; **B32**'s fix points **up** (the solution moved to `net10.0`
rather than the MCP server moving down to `net8.0`); and a `linux-x64` publish
job stays the separate concern `DESIGN-net10-upgrade.md` files it as, rather than
becoming part of the upgrade. Revisit if a Linux or macOS artifact is ever
shipped — that, not the glibc number, is the thing that would make the floor
matter.

**Blocks:** the `net10.0` decision in `docs/DESIGN-net10-upgrade.md`, and
whether **B32**'s fix points up or down. *(Both now resolved by the answer above.)*

The upgrade is otherwise clean. Avalonia 12.1.1 and SkiaSharp 3.119.4 both
publish explicit `net10.0` dependency groups, every .NET 9 and .NET 10 breaking
change on the official lists was checked against real code and none apply, and
.NET 8 leaves support in November 2026 — so standing still is also a decision
with a date on it. One consequence needs a person: a self-contained `net10.0`
Linux build requires **glibc 2.27** (Ubuntu 18.04-class) where `net8.0` needed
**2.23** (Ubuntu 16.04-class). Windows and macOS floors do not move.

**The reason this is a question and not a footnote is that it cannot be
answered from the code.** It depends on who runs this, and nothing in the
repository records that.

What the code *does* say is that the floor is currently theoretical.
`build.yml` publishes exactly one artifact, `win-x64`, cross-compiled from
Ubuntu. **There is no Linux build and no macOS build shipped at all**, so
today a rising Linux floor cannot lose a user who has nothing to download.
Anyone running this on Linux right now built it from source, which means they
have a .NET SDK, which means their distro is far newer than either floor.

So the glibc number is the wrong thing to decide. The thing to decide is
whether the missing Linux and macOS artifacts are an omission or a choice —
because that is what makes the floor matter, and it is also what decides
whether the publish-path half of **B32** should grow a `linux-x64` job.

**(a) Development targets only — Windows is what ships.** Take the floor; it
costs nothing measurable, because nothing crosses it. Linux stays what it is
today, the place the tests run and the Windows bundle is built. The devcontainer
serves that fully and the glibc question closes as not-applicable.

**(b) Shipping targets, not yet built.** Then the floor is real but still
almost certainly fine — Ubuntu 18.04 left standard support in 2023, and an
application that wants a tablet and a GPU is not being run on an eight-year-old
distro. Worth saying out loud rather than assuming, and it makes a `linux-x64`
publish job part of the upgrade rather than the separate concern
`DESIGN-net10-upgrade.md` currently files it as.

**(c) Stay on `net8.0`.** Keeps the floor and keeps the smaller diff, which is
**B32**'s own prescription. It buys three months and pays a migration's
verification cost twice — once to prove a downgrade changed no pixels, again in
November to prove the upgrade did not.

**Recommend (a)**, on the evidence that the only artifact anyone can download
is a Windows one and no issue in the tracker asks for another. It is the one
reading that makes the glibc floor a non-question rather than a small risk
taken quietly — and if (b) turns out to be the truth, the floor is still very
likely fine and the thing that changes is scope, not safety.

**Blocks:** the last `[?]` but one in Pillar 3.

The pillar lists *Reusable animation presets* and *Animation templates* as
separate from the Animation library — but the Animation library shipped, and
what it delivers is a multi-frame symbol placed with a frame offset, which is
already a reusable animation. Two placements of one cycle run the same drawings
out of step. Whatever these two items are for, it is not that.

The reading that survives is that they are about **timing rather than
drawings** — the part of frame-by-frame work that a symbol does not carry:

- **(a)** *Strike it.* The Animation library is the reusable animation, and
  these two lines are a pre-implementation guess that the design outgrew. A
  roadmap that keeps items nothing can distinguish from shipped ones is the
  wish list this file's checkbox rules exist to prevent.
- **(b)** *A timing preset* — a saved exposure pattern (on 1s, on 2s, a
  slow-in of 1-1-2-3-4) applied to a selected range of cels, re-exposing the
  drawings that are already there. This is a real animator's tool, it is
  genuinely absent, and it is nothing a symbol can express, because a symbol
  carries drawings and this carries their spacing.
- **(c)** *A motion preset* — keyframed placement transforms, so a symbol can
  be told to arc across the frame over twelve cels. This is the largest of the
  three and it needs a decision about whether placements become animatable at
  all, which is a pillar-4 question wearing a pillar-3 hat.

**Recommend (b), and strike the other line as (a).** One item, specified:
*"Timing presets — save an exposure pattern and apply it to a range of cels."*
It is the only one of the three that is both absent and unambiguous.

## Q12 · Whether an animation template is a document or a project type — **answered (a)**

**Answered 2026-08-03: (a), a document with a flag.** Designed out in
`docs/DESIGN-templates.md`, because "changeable on the fly" was asked for
explicitly and it is the property that decides the mechanism: a template is
**copied, never referenced**, so editing one is safe precisely because it
cannot reach back into work already started from it. (c) stays available —
a starter pack is (a) plus content, and needs no change to the mechanism.


**Blocks:** the last `[?]` in Pillar 3.

*Animation templates* — starting a new animation from a skeleton rather than an
empty document — is real and absent. What is undecided is where it lives, and
the app already has two mechanisms that overlap it: `NewDocumentSettings`
(size, fps, frame count) and project types (which decide the workspace).

- **(a)** *A document in the project marked as a template.* Copy it, rename it,
  start drawing. Costs nothing new — a template is an ordinary animation with a
  flag — and an artist can make one out of work they have already done, which
  is where real templates come from.
- **(b)** *A built-in list* (walk cycle 8 on 2s, run cycle 6, blink 4, take 12).
  Better on day one, worthless on day two: every studio times its own walk
  differently, and a list nobody can add to becomes a list nobody uses.
- **(c)** *Both* — built-ins that are seeded as project documents on first use,
  so they are editable from the moment they appear.

**Recommend (a).** It is the smallest thing that is not a guess about how other
people animate, and (c) is (a) plus a starter pack, which can be added later
without changing the mechanism.

---

## Q10 · Does wet paint survive between strokes — **answered (c), not yet buildable**

**Answered 2026-08-02: (c), a bounded wet window, with the size of the window a
brush setting.** `0` means the paint is dry the moment the pen lifts — exactly
today's behaviour — and `N` means the next `N` strokes can still pick it up.

Kept here rather than moved to `DECISIONS.md` because the decision is settled and
the *implementation is not startable*: `MediumSimulator` is a static pure
function of (coverage, existing pixels, paper, settings) that builds its
lattice per stroke and discards it. There is no state between strokes for a
window to bound. Adding the setting now would put a control in the brush
options that changes nothing, which charter **O7** exists to stop.

What the answer already constrains, so the fluid pass does not have to
re-litigate it:

- **The window size is stored per stroke** (invariant 4), not read from the
  tool at render time. Changing your brush must never re-wet a painting you
  finished last month.
- **Default 0 keeps every existing document byte-identical.** Absent by
  default, the camera's rule again.
- **A stroke's render depends on the previous N strokes**, which is the real
  cost. Re-rendering a frame already replays in order, so that part is free —
  but editing or undoing a stroke in the middle now invalidates the *next* N
  as well, and the frame cache and invariant 6 have to know it. Bounded by N
  rather than by the whole history is precisely why (c) was chosen over (b).

---

## Q13 · What counts as the same sheet of paper — **answered (c)**

**Answered 2026-08-02: (c).** The wet window is per frame and per layer, and
**generated strokes never carry wetness** — the inbetweener and the MCP surface
write `WetStrokes = 0` whatever the source stroke said.

Per frame and layer because a cel is a separate drawing and a layer is not
paper; it is the same answer Q6 gave for what a smudge samples, and it keeps
the replay trivially bounded. The extra clause is a determinism one: an
inbetween whose appearance depended on how many strokes the generator happened
to emit before it would diverge between runs, which is invariant 2 broken by a
side door.

## Q14 · What an eraser does to wet paint — **answered (a)**

**Answered 2026-08-02: (a).** An eraser is a stroke like any other. It spends
one of the window's `N` and removes pigment; the moisture goes with the pigment
it belonged to.

The physical answer — an eraser that smears wet paint — is a brush somebody can
build later on top of the advection loop, not a property the eraser has to have
from the start. Recorded because it is a real limitation and an artist erasing
into a wash will find this hard-edged: **if that turns out to matter, the fix is
a new brush, not a change to the eraser.**

## Q15 · Is a mirrored stroke one stroke or two? — **answered (c)**

**Answered 2026-08-07: (c), one stroke while drawing with an explicit "break
symmetry" that expands to two.** So `Mirror` lives **on the stroke**, not on the
scene — that is the part this answer actually settles, and the reason it could
not be deferred.

Turning symmetry off is meaningful while the stroke is whole: it removes the
reflection rather than leaving an orphan. Breaking symmetry is a deliberate,
undoable act that writes two ordinary strokes and forgets the pairing, which is
correct — after the break they are two marks and pretending otherwise would owe
the artist a promise nothing keeps.

**Blocks:** nothing. Symmetry can be built.


Symmetry does not exist yet and it should — for character design, which is what
this application is for, a vertical mirror is not a nicety. What has to be
decided before anything is written is what the *record* holds when an artist
paints with a mirror on.

**(a) One stroke, rendered twice.** `Stroke.Mirror` names an axis; the engine
stamps the dabs and their reflections. The record stays the size of what was
drawn, and turning symmetry off afterwards is meaningful — it removes the
reflection rather than leaving an orphaned copy. Invariant 1 pushes here: the
mark is one gesture, so one entry.

**(b) Two strokes, emitted at commit.** Simpler in the engine, and the artist
can then edit, erase or transform the halves independently — which they
frequently want, because symmetry is usually a scaffold rather than a promise.
The cost is that the record has no memory of the pair, so "turn symmetry off"
cannot mean anything and the two halves drift as soon as either is touched.

**(c) Both — (a) while drawing, with an explicit "break symmetry" that expands
to (b).** Probably where this ends up, and worth naming as a target rather than
arriving at by accident, because it decides whether `Mirror` is on the stroke or
on the scene.

The reason it is a question rather than a guess: (a) and (b) are not
interchangeable later. A file written under (b) cannot be read back as (a), so
picking the easy one first forecloses the other.

## Q16 · Is a subject reading stored, and what makes it stale? — **answered (c)**

**Answered 2026-08-07: (c) for placement, on the character for taxonomy.** The
split the design already made decides the storage, so there is one answer per
half rather than one answer:

- **Taxonomy lives on the `Character` in the project manifest.** Durable, small,
  reviewable, and the one thing here an artist may correct by hand — so it goes
  where their correction survives a cache wipe, a reinstall and a clone. It is
  authored data the moment they touch it, and authored data belongs in the
  record.
- **Placement lives in a cache beside the autosave, never in the
  `.lightbox.json`.** Keyed by a content hash of the frame's effective strokes.
  Staleness is then not a problem to solve: a hash that no longer matches is a
  cache miss, and a cache miss costs one call. Losing the whole cache costs
  nothing but time, which is exactly the property that makes it safe to throw
  away whenever anything is uncertain.

**Why not (b) — stored in the document with a hash.** It buys the same cheap
batches and charges the document for them: every frame carries a reading, the
file grows with something no render reads, and a merge between two branches has
to reconcile two models' opinions about the same drawing. The hash makes
staleness detectable, not free.

**The consequence that makes this more than a preference:** invariant 1 says the
stroke record is the document, and a placement reading is *derived from* the
record. Putting derived data in the record is the mistake the codemap merge
driver exists to undo elsewhere in this repo. Taxonomy escapes that test because
it is not derived from any one document — it is a statement about a character,
and once an artist edits it, it is theirs.

**One thing this answer does not decide,** because it is not a preference: the
deletion test still governs both halves. Delete every reading — cache and
taxonomy alike — and a finished document must re-render byte-identical. If that
ever fails, something is reading the analysis at render time and invariant 2 is
gone. It is the reading's first test, before any of the storage above.

**Blocks:** nothing now. The reading is buildable; **Q17** still blocks the
inking half only.

`docs/DESIGN-subject-reading.md` splits the reading into **taxonomy** (per
character, stable) and **placement** (per frame, disposable). The taxonomy is
clearly worth storing — it is reviewable, correctable, and true of every frame.
The placement is the question.

**(a) Never stored.** Every operation reads the frame it is about to work on.
Always fresh, nothing to invalidate, and no new keys in the file. The cost is
that two runs of the same inking pass on the same drawing can differ, and that
a batch across two hundred frames pays for two hundred readings.

**(b) Stored with a content hash of what it read.** Staleness detection is then
free: the hash of the frame's strokes no longer matches, so the reading is
discarded rather than trusted. Batches get cheap. The cost is file size and one
more thing that can be subtly wrong — a reading that matches the hash but was
produced by a model that has since changed its mind.

**(c) Stored, but only as a cache outside the document** — beside the autosave
rather than in the `.lightbox.json`. Keeps the record clean, keeps the batch
cheap, and makes "it went stale" a non-event because losing the cache costs
nothing. The cost is that a reading an artist corrected by hand would live
somewhere that gets deleted, which argues that corrected readings are taxonomy
and belong in (a)'s per-character half anyway.

Leaning (c) for placement and stored-on-the-character for taxonomy, because it
puts the durable half where an artist can edit it and the disposable half where
losing it is free.

## Q17 · Does an inking pass replace the pencils or land on its own layer? — **answered (c)**

**Answered 2026-08-07: (c), one Ink layer for the whole sequence**, its cels
lined up with the pencils'. Non-destructive without the two-hundred-layer
problem, and it uses the layer model as it already stands.

**It carries a UI commitment, and that is the half worth writing down:** an
inking pass runs over a **range**, not a frame. A per-frame gesture would make
one layer per frame by accident, which is the option this answer rejected. So
the surface that starts an inking pass takes a range the way the exposure-sheet
operations already do.

**Blocks:** nothing. Inking is unblocked — it was the last thing waiting on this.


**(a) Its own layer, pencils untouched and hidden.** What an inker does on
paper, non-destructive, and the artist can re-run with a different style
without losing anything. Costs a layer per inked frame, which over two hundred
frames is a layer count nobody wants to scroll.

**(b) Replaces the strokes in place, one undo step.** Matches "the stroke record
is the document" — the inked lines simply *are* the frame now. Cheap, tidy, and
the artist keeps the pencils by duplicating the layer first if they want them,
which is a thing they already know how to do.

**(c) Its own layer, but one layer for the whole sequence** — an "Ink" layer
whose cels line up with the pencils'. This is what the layer model already
supports and it is probably the answer, but it assumes an inking pass is run
over a range rather than a frame, which is a UI decision as much as a record
one.

The reason it cannot be deferred: (a) and (b) produce different documents from
the same gesture, and a file written under one cannot be reinterpreted as the
other. Pick before the first pass ships, not after.

## Q18 · Do flat point arrays cost schema adherence? — **answered (c)**

**Answered 2026-08-07: (c), flat arrays for points only, objects for everything
else.** Points are 99% of the volume and the only part that repeats; `tool`,
`color` and `label` keep their names, so the field whose loss actually costs an
inbetween keeps its key.

**Adopt it with the measurement rather than instead of it.** The adherence
claim in `StrokePayload.cs` was undated and unmeasured, and this answer does not
make it true — it makes the risk small enough to take. The golden set (Q34) is
the natural place to watch it: **label retention** belongs in the scores, so a
regression shows up as a number rather than as a bad inbetween somebody notices
weeks later.

**Blocks:** nothing.


The measurement is settled and the trade is not. `docs/DESIGN-ai-payload.md`
has the numbers: writing a point as `[123.4,567.8,0.55]` instead of
`{"x":123.4,"y":567.8,"pressure":0.55}` takes **57%** off the payload, and at
2560 points in a 40-stroke frame pair that is the largest encoding win
available — 102 KB down to 44 KB, ~26k tokens down to ~11k.

Against it, `StrokePayload.cs` says the wire shape mirrors the document format
because it "measurably improves schema adherence". That claim is undated,
unmeasured anywhere in this repo, and entirely plausible: a model has seen a
great many `{"x": …}` objects and very few positional triples, and positional
encodings invite exactly the failure that matters most here — a transposed
coordinate, or a dropped `label`.

**A lost label is a lost correspondence**, which is a worse inbetween. So this
is not "57% cheaper, ship it"; it is 57% cheaper against a quality risk nobody
has quantified.

What would settle it: the same twenty frame pairs through both encodings on at
least two providers, scoring label retention, point-count fidelity and whether
the inbetween lands between its keys — the check `AiConnectionTester` already
implements. Real API calls, so it is a deliberate spend rather than something
to slip into an unrelated commit.

Three ways it could land:

**(a) Keep objects.** Adherence is worth more than tokens, and the bigger win
is sending fewer strokes anyway — six times bigger, per the same document, and
with no format risk at all.

**(b) Flat arrays everywhere.** If adherence holds across providers, 57% is not
a rounding error and refusing it out of caution is superstition.

**(c) Flat arrays for points only, objects for everything else.** Points are
99% of the volume and the only part that repeats; `tool`, `color` and `label`
stay named, so the field most at risk keeps its key. Probably the answer, and
it is still a guess until somebody runs it.

This is the standing disagreement between **ai-engineer** and **art-director**,
and it is written here rather than settled by whichever of them ran last.

## Q26 · When a textured line is re-shaped, may its texture change? — **answered (a)**

**Answered 2026-08-07: (a), accept it. The grain belongs to the canvas.** A mark
is a function of where it is, the way a real pencil's grain is a function of the
paper's tooth under it.

**This closes the question rather than deferring it, and that is worth the
sentence:** (b), (c) and (d) are now *rejected*, not "later". Nothing needs a
seed origin on the stroke, nothing needs arc-length seeding, and — the one that
matters most — **no tunable radius enters the render path**. Invariant 4's
suspicion of hidden knobs is upheld for free, and invariant 2 stays exactly as
written, with no second costume to check for.

**What it obliges.** Pillar 0's re-shaping item ships with a manual line saying
that moving a textured line changes its grain, and saying *why* — not as an
apology but as the same fact as the paper's tooth. An artist who wants the mark
preserved exactly moves the layer rather than the line, which is a real answer
and should be the one the manual gives.

**Blocks:** nothing. Re-shaping is unblocked.


Invariant 2 seeds every dab dynamic — scatter, size, roundness, rotation, all
three colour jitters — from the dab's position via `Hash01`. That is what makes
a mark reproducible on reload, on undo and through the inbetweener, and it is
not negotiable.

The consequence nobody has had to face yet is that **it also means moving a line
changes what the line is made of.** Drag a control point and the dabs near it
re-seed: the grain shifts, the scatter lands elsewhere, a bristle that was
splitting now does not. Correct by the invariant, and wrong to the artist, who
expects to nudge a line and see *the same line, somewhere else*. Pillar 0's
re-shaping item cannot ship without an answer, and the answer changes the
record, so it cannot be retrofitted.

**(a) Accept it, and say so in the manual.** The mark is a function of where it
is, the way a real pencil's grain is a function of where the paper's tooth was.
Free, honest, and it makes re-shaping feel unreliable for exactly the brushes
people would most want to re-shape.

**(b) A seed origin stored per stroke.** Hash from `position − origin` rather
than from position, and carry the origin through an edit. The texture then
travels with the line. Cheap, and it changes the meaning of every existing
stroke unless the origin defaults to zero — which it can, so old documents
render identically. The catch: two strokes drawn in different places with the
same shape now have the *same* texture, which is the flicker invariant 2 exists
to prevent, in a new costume.

**(c) Seed from arc length along the stroke instead of from position.** The
grain belongs to the stroke rather than to the canvas, so it survives any edit
including a wholesale move. Also kills (b)'s duplication problem, because two
strokes still differ if anything else about them differs. The cost is real: a
dab's seed now depends on every point before it, so an edit near the *start*
re-seeds everything after it — the opposite failure, and arguably worse.

**(d) Re-seed only what moved, and blend.** Keep position seeding, but let dabs
within some radius of an untouched point keep their old values. Preserves the
line away from the edit at the price of a rule with a tunable in it, and a
tunable in the render path is the thing invariant 4 is suspicious of.

Not answerable by measurement, which is why it is here: every option renders
*something* defensible, and the question is which one an animator would call
the same line. **art-director holds the veto on that**, and **ai-engineer holds
it on whether the chosen seeding still reproduces exactly** — an inbetween of a
re-shaped stroke has to land where the record says. (c) is the one I would open
the argument with, on the grounds that grain belonging to the stroke is what it
belongs to on paper, but the start-of-stroke cascade may sink it.

## Q27 · How big does a reference the model has to read need to be? — **answered (d)**

**Answered 2026-08-07: (d), choose per view from what is in it.** Measure thin-
line density and pick the cap, rather than sending a face turnaround and a
walk-cycle sheet at the same size.

**The objection recorded against (d) still stands and has to be answered in the
build, not argued away:** a heuristic that decides what leaves the machine is
unpredictable in the way invariant 4 distrusts, one level up from pixels. It is
answerable, and cheaply — **make the choice visible and overridable**:

- The request shows what cap each view got, so the artist can see that the face
  sheet went at 1024 and the silhouette at 512.
- A view can be pinned to a size, which is (c) surviving inside (d) as the
  escape hatch rather than as the mechanism.
- The heuristic is a pure function of the view, tested on the same fixtures
  `RenderReferenceViewPng` already has, so it is inspectable rather than felt.

**That guard is inferred rather than given** — say so if the intent was the bare
heuristic. Without it this is a number nobody can predict changing what the
model sees, which is the one failure mode the objection named.

**Blocks:** nothing.


B31 capped reference views at **768 px on the long edge** on the way into a
request, and the number is doing real work: providers bill by area regardless of
file size, so 768 is 442 image tokens against 691 at 960, and 7 KB against
115 KB on a 1080p sheet. The cap is on the request, never on the view — the
artist's sheet stays whatever they drew.

**art-director's objection, with pictures.** Rendered through the real
`RenderReferenceViewPng(view, longEdge)` path and compared at authored, 768 and
512: a body silhouette at `Size 3–4` survives even 512, and this is the case the
cap was measured against. A **face close-up** and a **head drawn at natural
scale on a full-body sheet** do not — eyebrows go, the eyes reduce to grey
smudges, cheek hatching disappears. Mipmapped linear minification greys a thin
dark line toward the ground rather than keeping it crisp-and-small, which reads
to a model as *the line is not there* rather than *the line is thin*. The
failure it predicts is the quiet kind: an inbetween that goes subtly off-model
on the face, with nothing in the request or the response saying why.

**The caveat I owe the measurement, because it is this repo's recurring trap.**
The fixture's thin lines were drawn at pressure 0.25–0.5, and pressure drives
both size *and* flow here, so they are already hairline-faint at authored
resolution — visible, but at the edge of it. The cap made a marginal line
invisible; it did not make a solid line marginal. That is still a real cost and
it is a smaller one than the images alone suggest. *The number was real and the
attribution was partial* — same shape as the saturation trap, and worth stating
before anyone re-derives the conclusion from the pictures.

**ai-engineer's position** is that the token cost of a bigger cap is small in
context: 768→1024 is roughly 442→786 image tokens against the ~26k a stroke
payload already spends, so about 1.3%. It does not want the cap removed —
uncapped means a 4K sheet billed as a 4K sheet — and it will not spend the
budget on a number nobody has A/B'd against a real provider's output.

Four ways out:

**(a) Leave 768.** Cheapest, and defensible for pose, silhouette and
proportion — which is most of what a reference is asked for. Accepts the
facial-detail loss.

**(b) Raise it to 1024.** ~1.3% more tokens, and the two failing cases above
come back inside legibility. Still one number for every kind of view, which is
the thing art-director actually objected to.

**(c) Per-view opt-out, absent until asked for.** A view marked *detail* is sent
at its authored size. Fits the house rule exactly — the record grows a key only
for a view that used it — and puts the trade where the artist can see it, since
they know which sheet is the face turnaround. Costs a setting and a UI for it.

**(d) Choose per view from what is in it.** Measure thin-line density and pick
the cap. Best result on paper, and it makes what leaves the machine depend on a
heuristic nobody can predict — the *shape* invariant 4 is suspicious of, one
level up from pixels.

Not answerable by measurement in either direction: art-director can show that
768 loses facial construction and cannot show that 1024 is enough, and
ai-engineer can price a cap and cannot price a worse inbetween. **(c) is the one
I would open the argument with**, because it is the only option that does not
pretend a face turnaround and a walk-cycle sheet want the same number. (b) is
the cheap interim if a setting is too much for now.

---

## Q28 · What is a reference bound to, when a project is a production? — **answered (b)**

**Answered 2026-08-05: (b), a binding list.** A reference names any number of
targets — the project, one or more folders, specific documents — because
"multiple folder" was in the request and no single-scope field can express it
without duplicating the reference.

The reason it beat tags, which is where the wider vision points: a binding list
**grows into** tagging rather than being replaced by it. A tag becomes a fourth
kind of target, so `binds: tag/prop` arrives later without touching the model or
migrating a file. Choosing tags first would have made every reference's reach
depend on a tag somebody else edited — action at a distance, and the shape
invariant 4 is suspicious of.

What still needs deciding when it is built: what happens to a binding whose
folder is deleted while another binding survives. Not a blocker — a reference
with no remaining bindings is simply project-wide or orphaned, and either is a
one-line rule — but it should be chosen deliberately rather than fallen into.

Raised 2026-08-05, alongside Q29 and the scope note in the ledger's project
entries. **Not answerable from the code**, because the code has only ever had
one answer and it was chosen when a project meant one character.

A `ReferenceSheet` lives in `Doc.ReferenceSheets` — it belongs to **one
document**. That was coherent when Pillar 1 said a project *is* a character:
the turnaround belonged to the animation you were drawing.

It stops being coherent the moment a project is a production. The reporter's
own examples are the argument: a character sheet should reach **every animation
of that character**, a level design should reach **the environment it
describes**, and on a film an art-direction board is wanted **project-wide**.
The type is also wider than "character sheet" — level designs, world designs,
environmental sketches are the same kind of thing pointed at something else.

So the question is not *should a reference be shared* but **what does a
reference name as its scope**, and the options differ in what they cost:

**(a) A scope field on the reference: project, folder, or document.** One
nullable key, absent by default, and it reads like the camera does. Cheapest,
and it cannot express "these three folders" — which the reporter asked for by
name ("or multiple folder").

**(b) A list of bindings.** A reference names any number of folders, documents
or the project. Expresses everything asked for. The cost is that a reference
stops being ownable by anything, so deleting a folder has to answer what
happens to a reference that named it and one other.

**(c) Tags on both sides.** A reference carries tags; a folder or document
carries tags; a reference shows where the tags meet. This is where the owner's
"custom tags and be able to tag" points, and it is the only option where
"every character animation" is expressible without listing them. It is also the
one that can surprise: adding a tag to a folder silently changes what a
document sees, which is the *shape* invariant 4 is suspicious of.

**What makes this urgent rather than interesting:** invariant 1. A document
currently re-renders from its own record, and a reference resolved through a
project, a folder or a tag is a document that does not. The camera precedent
says how that is repaid — `ProjectIo.Flatten` inlines everything referenced when
a document leaves the app — so whichever option wins, **Flatten has to inline
resolved references and there must be a pixel-identity test for it**, or the
escape hatch rots silently. That part is not a preference and does not need a
decision.

---

## Q29 · Is the project docker the whole surface, or the quick view of one? — **answered (a)**

**Answered 2026-08-05: (a), settle the split first and build the hierarchy
shared.** The folder hierarchy is Core model code that both surfaces read, not
a docker view model that a window later borrows.

The division: **the docker does what you do while drawing** — find it, open it,
move it, rename it — and **the window does what you do between drawings**:
bulk operations, tagging, reference binding, status across the production.

This is sequencing rather than taste. Hierarchy is the one piece both surfaces
need, so building it into the docker first is how it ends up with two
implementations — and the second one is always written by somebody who cannot
change the first.

Raised 2026-08-05 by the owner: *"the project docker is part of a larger
project window where we can do advanced operations. The docker is the quick
overview and document/hierarchy helper."*

Recorded rather than answered, because it decides where the open project bugs
land and they should not each guess. B86 (drag/drop, subfolders,
collapse/expand), B87 (permanent delete with confirmation), B64 (rename), B63
(the create menu) are all currently filed against *the docker*. If a project
window exists, some of them belong there instead — a delete that prompts about
a folder full of files is a poor fit for a sidebar, and a bulk retag is not a
docker operation at all.

The split that seems to hold, and is offered as a starting point rather than a
conclusion: **the docker does what you do while drawing** — find it, open it,
move it, rename it — and **the window does what you do between drawings**:
bulk operations, tagging, reference binding, status across the production,
whatever an artist would stop drawing to do.

The reason to settle it before B86 rather than after: hierarchy is the one
piece both surfaces need, and building it into the docker first is how it ends
up with two implementations.

---

## Q30 · When do characters and scenes become folders? — **answered (a), 2026-08-06**

Raised 2026-08-05 when `ProjectFolders` landed and parked at the owner's
request. Asked properly on 2026-08-06 with a recommendation, and the answer went
**against** the recommendation on two of three parts — recorded here as given,
with the one consequence of the combination named rather than argued.

### The answer

**Shape: (a), one hierarchy, now.** Character and Scene stop being separate
kinds of thing. The recommendation was to cascade resources first and merge
later; the owner took the merge directly, which is the stronger reading of
*nothing rigid, everything fluid, but with rules*.

Four consequences the owner specified, and they are the actual design:

| | |
| --- | --- |
| **Character library and asset library** | Become **project-based**, not character-based. Creating into them and saving to them is available **project-wide** rather than being a property of a character or of the Asset Library project type |
| **Pivot, colliders and the like** | **Folder-based**, and *enableable on a file*. Absent unless switched on — the camera's rule again, so a folder that never needs a pivot writes no key and shows no control |
| **Character sheets** | **Folder-based**, so a sheet can serve every drawing under a folder rather than one document. The owner made this conditional on it not colliding with the character library |
| **Resolution** | **Accumulate, nearest wins ties.** Every declaration in the chain registers; where two name the same swatch id the nearest one wins |

**The character-sheet condition is met — checked rather than assumed.** There is
no collision, and the reason is that Q25 already put sheets in the right place:
a `ReferenceSheet` lives in `Doc.ReferenceSheets`, so it belongs to a *document*
and there is no `Character.Sheets` for a library import to carry.
`CharacterLibrary.Import` copies `entry.Character.Animations` and their
documents, so a document's own sheets travel with it either way. Adding a folder
scope is purely additive: a folder-scoped sheet stays with the folder it was
declared on, which is correct — it belonged to the source project's structure,
not to the character being imported.

**Migration: new projects only.** Existing `.lbproj` files keep character
palettes and the character/scene layout; the new shape applies to projects made
after the change.

### The one thing the combination costs, stated once

*One hierarchy now* and *new projects only* pull against each other, and it is
worth writing down because neither option said it alone: if existing projects
keep characters, the `Character` and `ProjectScene` records — and every code
path that reads them — **can never be removed**. "One hierarchy" is then true of
new projects and false of the codebase, which keeps two of everything, which is
the cost this question was opened to remove.

Recorded as the owner's decision and not re-litigated. The narrow amendment that
would resolve it later, if it becomes annoying rather than theoretical, is to
read the old shape and write the new one — an old project stays readable and
adopts the new layout the first time it is saved. That is a one-line change of
policy in the loader, not a redesign, so deferring it costs nothing except the
duplicate paths in the meantime.

### What this does not change

`ProjectIo.Flatten` must keep inlining everything a document references
regardless of where it is filed, and `AProjectWrittenBeforeFoldersKeepsItsPaths`
must keep passing. Both are invariant 1 at the boundary where a file leaves the
app, and neither was ever a preference.

---

<details>
<summary>The deliberation this replaced, kept because the options were weighed
and (b) and (c) were rejected for reasons worth not re-discovering.</summary>

**The state of things.** The project now has two hierarchies. The folder tree is
arbitrary — any name, any depth — and `ProjectFolder`/`DocumentRef.FolderId`
describe it. Beside it, `Character` and `ProjectScene` still build paths from
fixed words in `ProjectIo`: `characters/<slug>/animations/<slug>.lightbox.json`
and `scenes/<slug>/shots/<slug>.lightbox.json`. Those constants are the last of
the naming convention.

Two hierarchies is a real cost and it is worth naming rather than tolerating
quietly: every surface has to render both, every operation has to ask which
kind of thing it is holding, and "move this into that" has four cases instead
of one.

**What makes it more than a refactor.** A character is not only a folder. It
carries a palette, a pivot, variants that inherit animations, and a
`character.json` that `CharacterLibrary` reads across projects. A scene carries
running order and a running time. Whatever replaces them has to keep all of
that, and has to open every `.lbproj` already written.

The shapes worth weighing when it is time:

**(a) A character *is* a folder with character data.** `ProjectFolder` grows a
nullable `Character` (and `Scene`), so one tree holds everything and a plain
folder simply has neither. One hierarchy, one set of operations, and the
migration is mechanical: read the old lists, emit folders, keep the ids.
Riskiest at the seam — `CharacterLibrary`, variants and `SymbolScopes` all
resolve characters by identity today.

**(b) A character *has* a folder.** The character record keeps its own life and
gains a `FolderId`; the tree is where it appears and the character is what it
is. Cheaper and reversible, and it leaves two records describing one thing —
which is the state that produced this question.

**(c) Leave them.** Characters and scenes stay a fixed top-level convention and
folders are for everything else. Honest for a game project with a flat asset
pile; wrong for a feature, where "Episode 2 / Act 1 / Sc 014" is the structure
and a character is one leaf of it.

**What does not need deciding either way**, so it should not hold the question
up: `ProjectIo.Flatten` must keep inlining everything a document references
regardless of where it is filed, and `AProjectWrittenBeforeFoldersKeepsItsPaths`
must keep passing. Both are invariant 1 at the boundary where a file leaves the
app, and neither is a preference.

</details>
