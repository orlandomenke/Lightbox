# Decisions

Answers to `QUESTIONS.md` entries, recorded here when the question is removed.
The point is the *reasoning*, not the verdict — a verdict alone gets
re-litigated the first time somebody finds it inconvenient.

## Q6 — what a sampled smudge re-reads · answered 2026-08-02 · shipped

**(c): both, chosen per stroke.** Live and Baked are different intentions
about the same gesture — a smudge blending a character into a background wants
to follow the background when it is repainted; a smudge nudged until it looked
right wants to stay exactly as it was — and the app already records intentions
per stroke (invariant 4, the same reason anti-aliasing lives there).
`BrushSettings.SampleSource` is `ThisLayer` (default, and what every stroke
predating this is), `AllLayersLive` or `AllLayersBaked`.

The control sits in **Edit → Configure → Drawing**, not the tool options bar:
it is a decision about how a tool behaves, made rarely, and the options bar is
already the busiest strip in the window.

**Live is built as auto-rebake, not as a render-time cascade.** The question
framed (a) as re-sampling at render time, with the frame cache keyed on the
backdrop's identity. Rejected: that means handing a backdrop to four render
paths — canvas, PNG render, sequence export, sprite-sheet export — that have
to agree forever, and the failure mode is an export that differs from the
canvas with nothing to tell you. Instead `MainViewModel.RebakeLiveSamples` re-
freezes the live strokes at the playhead once per edit, so every render path
reproduces the mark without knowing the layer stack exists. The artist sees the
same behaviour either way.

Consequences accepted:
- The re-bake is **not an undo step**. It is derived from the layers below, not
  authored, and a history with "the background moved" between every real edit
  would be unusable. Undo re-enters the same funnel, so the sample follows the
  document back anyway.
- Only the **playhead's** frames are re-baked. A held cel shown across a range
  whose backdrop differs along it carries one sample and can answer for one
  index.
- With nothing underneath, the sample is **dropped rather than kept**: a stale
  backdrop is a mark blended with something no longer below it, and reading
  its own layer is what the stroke would have done anyway.

Rejected on measurement, not taste: a document-wide "does anything sample?"
guard in front of the re-bake. It walked every cel and every stroke in the
scene, which on a long sequence is more work than the loop it protected, and
it ran on every edit. The per-frame check does the same job at O(layers).

## Q9 — who owns brush settings · answered 2026-08-02 · shipped

**A variation on (c): global or PER PROJECT, defaulted by project type and
overridable in Configure.**

*Revised the same day, from per-document to per-project.* Per-document fixes
the page you already drew and leaves the next one starting from whatever you
last used elsewhere — the same problem one file later. The brush has to reach
the pages that do not exist yet, and Pillar 1 had already said so: a
character's animations share one palette, one brush set, one set of
references. The store moved from `Doc.Brush` to `ProjectManifest.Brush`, beside
the shared palettes, and `Doc.Brush` was deleted rather than left unused. With
no project open there is nowhere to keep a brush, so the effective scope is
Global whatever the setting says. Neither fixed answer is right, because the two
halves of this application want opposite things from the same control, and the
same person does both.

The case that decides it is not preference, it is memory. Coming back to a
comic page or a game asset after a fortnight, the question is not "which brush
do I like" but "which brush is *this* drawn with" — and on work where the
character of the stroke is part of the style, guessing wrong is visible in the
result. Photoshop and Krita both make you remember; that is the gripe.

Defaults: Illustration, Comic, Game Art and Asset Library keep the brush with
the drawing, because those are documents with gaps between sittings. Animation
and Storyboard keep one brush for the tool, because you are switching
documents constantly and want the same pencil in each. No project open means
Global — what the application has always done.

`Doc.Brush` is a nullable `BrushSettings`, absent from the file by default, and
written **on stroke commit rather than on save**: the session this exists for
is the one that ended without a save. It is not a breach of invariant 4 —
nothing renders from it, every stroke still carries its own settings, and
changing it repaints nothing.

Rejected:
- **A fixed global-with-per-document-override**, the original (c). It makes the
  artist configure the same thing per project when the project type already
  says what kind of work it is.
- **Recording the brush from `AppendExternalStrokes`.** That is the AI and MCP
  path; a stroke the artist did not paint is not the brush they were painting
  with, and letting an agent rewrite the tool bar's memory would undo the point.

Two bugs found while wiring it, both from the same cause — the free-hand
`EndStroke` was missed when a commit-time hook was added to `EndGradient` and
`EndShape`:
- **`FreezeSampledBackdrop` was never called for a hand-drawn stroke**, so
  `AllLayersBaked` — shipped two commits earlier as working — froze nothing and
  fell back to reading its own layer. Live covered for it, because the re-bake
  runs off the edit funnel, so the half with an end-to-end test worked and the
  half with only engine tests did not.
- **Applying a preset reset the sample source**, which made Configure's claim
  that the choice applies to the next mark true only until you changed brush.
  Anti-aliasing was already carried across a preset for exactly this reason;
  sample source was missing from that list.

## B17 and B8 — the two "manual" bugs, both testable after all · 2026-08-02

Both were open only because the code they lived in could not be reached by a
test, and in both cases the fix was to move the decision somewhere a test can
call rather than to accept the label.

**B17** (guides invisible over the drawing) was already fixed in the source and
had been for a while — the draw op painted guides after the artwork,
translucent — but nothing said so, so the box stayed open. `GuidePainter` is
that painting pulled out of `CanvasControl.DrawOp` into pure Skia, and
`PaintDocument` owns the checkerboard/artwork/guides order deliberately,
because splitting those three apart is precisely how the bug happened. Putting
the guides back underneath fails five of seven tests.

**B8** (timeline submenu flickers under a pen) had "cause: not investigated"
and a guess about spurious leave events. The guess was wrong. A pen right-click
is a press-and-hold: the press armed the cel drag, the hold opened the menu,
and moving towards "Insert frame" crossed the six-pixel threshold and started a
drag that seized the pointer and shut the menu. "A mouse is fine" was the
detail that pinned it — a mouse right-click never passes the left-button guard,
so it never arms anything. Two rules now, both in `CelDragGesture` rather than
in a handler: opening a context menu cancels the gesture, and so does letting
go.

Rejected: a source-order test asserting `DrawGuides` appears after `DrawImage`.
It would have caught a reordering, and charter **O2** says tests that assert
internal call order are a liability. Making the order a single function's job
achieves the same thing without the brittleness.

Worth noting for the next round: this is the second time the answer to "no
headless test can reach it" has been an extraction, and the scout report flags
six pointer state machines still sitting in `MainWindow.axaml.cs`. `CelDragGesture`
is one of them; the other five are the same shape.
