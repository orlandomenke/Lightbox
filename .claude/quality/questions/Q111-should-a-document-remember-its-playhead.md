# Q111 · Should a document remember its playhead? — **answered 2026-08-17: remember the playhead** (owner, via prompt)

## The question

The owner reported bones "sometimes" loading in a different spot than they
were saved in (B251). The record was proven innocent —
`ArmatureRoundTripTests.TheRigStandsWhereItWasSavedAfterAReload` solves a rig
with every joint flavour bit-identically across a save/load — which left the
view: the app reopened every document at frame 0 in Bind position, so a rig
posed at frame 30 stood at rest somewhere else than the artist last saw it.
Should the file remember where the artist was parked?

Options put to the owner:

- **(a) Remember the playhead** — recommended. One nullable key
  (`playheadFrame`), absent at frame 0, stamped at save, restored clamped on
  open. "An artist who returns to a scene after a month must find it exactly
  as they left it" is already the codebase's rule for pixel-reaching state;
  this extends it to where-you-were.
- **(b) Also remember the mode** (tool context, Pose/Bind) — fuller restore,
  but tool state in the document blurs workspace scope into file scope.
- **(c) Keep opening at frame 0** — no file change, accept the jump.

## The answer

**(a), remember the playhead.** The owner also answered "not sure" on whether
the sightings involved a posed rig, so (a) is both the fix for the likeliest
cause and cheap enough to carry if a second cause surfaces later — B251 stays
open-eyed about that: if bones move again *with* the playhead restored, the
report becomes a repro case the roundtrip test needs.

What (b) would have bought — reopening in Pose mode with the Bone tool armed —
is deliberately not taken: the mode switch is one click, and a document that
re-arms tools on open decides things a workspace should own.

## Where it landed

`Doc.PlayheadFrame` (nullable, absent at 0 — `IsTemplate`'s serializer
reason), stamped in the save funnels (`StampPlayhead`), restored clamped in
`OpenDocumentTab` / `ReplaceDocument`. Evidence:
`PlayheadPersistenceTests`.
