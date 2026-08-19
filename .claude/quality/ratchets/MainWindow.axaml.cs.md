# src/Lightbox.App/Views/MainWindow.axaml.cs

budget: 438

## Why it has moved

Newest last. Both sides of a merge keep their entry — taking one deletes the
other's reason and leaves a number nobody can account for.

- **5,544 → 429** when the view was split into fifteen partials. That is the
  ratchet behaving as designed: the budget moves with the extraction that earned
  it, in the same commit, and the file can never climb back. The partials are not
  budgeted — the largest is 747 lines and none is near the size that makes a file
  unreadable. A budget belongs here when a file is already too big, not as a cap
  on every file in the project.
- **449 → 427** when the bone gesture handler followed the overlay-gesture wiring
  into `MainWindow.Overlays.cs`. A merge had left that one block behind in the
  code-behind while its own siblings — the press, the weight stroke — sat in the
  partial, so reuniting them paid for the branch's new line and 21 more besides.
- **→ 436** (2026-08-15, B216/B217): +9 for two wirings in the constructor —
  the point snapper the canvas builds its marquees through, and the whole-line
  hover's two halves. This file's job *is* the wiring: every handler the canvas
  delegates to the view model is hooked up here, so a new delegated decision
  costs lines here by construction and there is nowhere else for them to be.
  Nine lines, six of which are the comment saying which bug they serve.
- **→ 440** (2026-08-16, B223): +4, and it is one subscription becoming four.
  The line drag used to report a single delta on release (`SelectedLinesDragged`)
  and now has a beginning, a middle, an end and a cancel, because it is a
  session rather than an event. This file's job is exactly that wiring, so a
  gesture gaining phases costs lines here by construction.
- **→ 437** (2026-08-16, Q104): +5 for one wiring line and the four explaining
  it — Ctrl-inside-a-marquee rides the line drag's existing move/commit/discard
  channel rather than growing one of its own, and that is exactly the kind of
  thing a reader of this file has to be told, because the evidence for it is in
  the *absence* of four more subscriptions.
- **→ 432** (2026-08-16, board projection): +8 for two canvas wirings in the
  constructor — the align-mode pick and the reference stack menu — beside the
  reference-drag wiring they extend. Delegated decisions are hooked up here by
  construction, the same pricing as B216/B217's entry above.
- **437 → 438** (2026-08-18), one line: the timeline track view's
  `KeyMenuRequested` subscription, beside the `KeyDragged` one it belongs with.
  The handler itself lives in `MainWindow.Transform.cs` with the other track
  handlers; only the wiring has to be here, because this is where the control
  is in scope.
