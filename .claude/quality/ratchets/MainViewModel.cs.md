# src/Lightbox.App/ViewModels/MainViewModel.cs

budget: 611

## Why it has moved

Newest last. Both sides of a merge keep their entry — taking one deletes the
other's reason and leaves a number nobody can account for.

- **13,110 → 13,141.** Naming Tier 0 re-marked the live-paint engine out from
  under a heading reading "the shape tool" and gave the render core a marker of
  its own. The motion itself *shrank* the file by five lines; the 38 lines of
  comment explaining why the old map was wrong is what took it over. Raised
  rather than absorbed by trimming that comment, deliberately: the budget exists
  to stop feature code accumulating in a file nobody can read, not to price the
  documentation that makes it readable — and padding prose down to fit a number
  set the day before is the "fixing badly to keep a number down" failure the
  project warns about, dressed up as discipline.
- **13,141 → 12,919**, when `LivePaintSession` took 22 fields out of the class.
  A budget that goes up once for documentation and down by 222 for an extraction
  is doing its job; one that only ever goes up is a comment.
- **12,749 → 655**, when the class was split into nineteen partials (Q78). That
  is the end of this budget's usefulness rather than a milestone in it: the file
  it was guarding no longer exists in the form that needed guarding.
- **693 → 670** when four more fields left the hub — two collaborators
  (`BrushWorkingSet`, `TransformSession`), three fields moved into the one
  partial that used them, and one that was used by nothing.
- **670 → 674** for B202's `ThumbnailCache`: the field itself plus its two calls
  on the frame-invalidation funnel. Irreducible here rather than extractable —
  two partials read the field, so the decomposition convention puts it in the
  shared block, and the funnel exists precisely so no cache can be invalidated
  without the others. Everything else about the fix, including all of its
  reasoning, is in `ThumbnailCache.cs`.

## What is not budgeted, and why that is arguable

**The nineteen partials are deliberately not here.** The objection is real —
growth will now land in whichever partial owns the feature, so the mechanism
that capped it has nothing to cap. Two reasons it is still right. First, that is
the *intended* destination: a feature's code going into the file named for its
concern is the split working, not leaking. Second, the largest partial is 1,310
lines, which is a file a person can read — the ratchet exists for files nobody
can, and pre-emptively budgeting nineteen readable files is the kind of thing
that looks like discipline and is noise. If one of them reaches a size that
stops being readable, add it then, with the number that made it necessary.

At 674 lines this file is readable, and what can still rot is whether the state
left in it is genuinely shared. `SharedStateRatchetTests` measures that, and it
is the one to read before adding a field.
- **636 → 639** (2026-08-17, Q110): +3 where the viewport funnel tells the
  navigator its rectangle moved. Irreducible here rather than extractable: this
  is the one place the viewport is set, and the whole of the navigator's own
  code is in `MainViewModel.Navigator.cs` beside it.
- **639 → 611** (2026-08-22, Q147): `CompositeBelowActiveLayer` moved to
  `MainViewModel.Rendering.cs`, where the render path lives. Prompted by the
  masks change growing it past the ceiling here — the growth went with it,
  and the ratchet banks the move.
