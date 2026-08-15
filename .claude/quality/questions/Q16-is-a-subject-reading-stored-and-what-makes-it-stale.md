# Q16 · Is a subject reading stored, and what makes it stale? — **answered (c)**

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
