# Undo stores only what changed

The owner chose this as the second step in shrinking an open document's memory,
after the overall limit for pictures (Q221). This note covers three things: what
an undo step holds today, how a step can hold only the drawings that changed, and
what that costs.

## What a step holds today

There are two kinds of step (`DocumentEditor`):

- **Delta steps** carry an apply and revert pair and hold almost nothing. A stroke
  commit is one.
- **Snapshot steps** (`Perform`, 146 call sites) hold a whole copy of the document
  (`Doc.Clone`). Every point of every stroke is copied. Undo and redo swap that
  copy with the live document.

These figures were measured on the lab's owner-shaped fixture: 64 drawings, 1,261
strokes and 189,004 points, run with a throwaway console against `Doc.Clone`.

| | per structural edit |
|---|---|
| `Doc.Clone` | 13.4 ms, **14.1 MB** |
| a compare of every point against a copy | 13.2 ms, 0 bytes |

The lab's memory session saw the same: about 14.5 MB more per structural edit. The
stack keeps 64 steps, so a session of layer, timeline and palette edits can hold
**about 900 MB** of document copies. Most of those copies are identical drawings,
because adding a layer or renaming one changes no drawing at all.

## Why the copies cannot simply be shared

The obvious saving is to let a step reuse the live document's unchanged frames.
That is wrong, because the live document is mutated **in place**:

- A stroke commit appends to `frame.Strokes`.
- A transform moves points.
- An undo of a delta reverts in place.

A shared frame would change under the step that holds it. Sharing with the copy in
the previous step fails the same way: on undo, a snapshot's document *becomes* the
live one and is then edited in place.

So anything shared must be **frozen**: a copy that is never handed out to be
edited.

## The design

- **A step holds a frozen document.** That is a skeleton (the document and scene
  without their drawings) plus one frozen copy per drawing, held by id.
- **Frozen drawings are reused.** The editor keeps the latest frozen copy of each
  drawing. When a snapshot is taken, each live drawing is compared with that copy:
  - If it is the same, the step references the existing frozen copy.
  - If it is not, the drawing is frozen anew, and that one copy is the only cost.
  A structural edit that changes no drawing stores a skeleton and nothing more.
- **Undo and redo thaw.** Thawing builds a fresh live document from the frozen
  one, cloning each drawing so that nothing live aliases anything frozen. The
  document being left is frozen in the same way, with the same reuse. Frame ids
  are carried, so caches keyed by id stay valid, as they do today (B202).

## "The same" has to be complete

A compare that misses a field would treat a changed drawing as unchanged. Undo
would then restore the wrong drawing, silently. That is the B379 shape, where a
hand-listed clone missed a deep field. So the compare is **complete by
construction**: it walks every property the serializer writes, by reflection
compiled once per type, rather than a list someone has to keep up to date.

- **Points take a fast path:** a span loop comparing the bits of every field.
- **Doubles are compared bit for bit.** `Hash01` seeds every dab from those bits,
  so two values that are equal as numbers but differ in bits are different
  drawings.
- **Errs strict.** Anything the walker cannot classify counts as changed. A wrong
  "changed" costs one copy; a wrong "same" costs a drawing.

## What review found

The sensitivity review blocked this change once, and two of its notes changed code
as well as text.

- **Redo no longer returns the objects that were live before the undo.** On
  main, redo handed back the very document instance. Now it thaws a new one. 19
  delta steps wrote to objects they had captured (guides, reference strips,
  weight painting), so redoing them after a redone structural edit would change
  nothing on screen. That was already reachable on main in the other order, and
  is fixed separately as B413, which this change needs first.
- **Shared samples were mutable.** `BakedSample` and `StrokeCheckpoint` are shared
  by every copy of a stroke, so a change in place would rewrite every undo state
  at once, and the compare could never notice, because the two sides are the same
  object. Both are now init-only. Making them so found one writer, the nudge to
  spacing (B414).
- **A type with nothing to compare fails closed.** A type with no public property
  would otherwise compare equal for any two instances.
- **Cels sharing one frame object** are split into two frames with the same id
  on redo as well as on undo. Save and load split them the same way, so this
  matches the file.

## What it costs

- **Structural edit:** the compare replaces most of the clone, at about the same
  time (13 ms against 13.4 ms on the fixture). Allocation drops from 14 MB to the
  drawings that changed, which is usually none or one.
- **Structural undo and redo:** these are a swap today, so free. They become a
  thaw of about one clone, roughly 13 ms on the fixture, before the re-render that
  already follows them. This is the one thing that gets slower.
- **Delta steps:** unchanged.

## Measured

These numbers were taken on the owner-shaped fixture, with a throwaway console
driving `DocumentEditor`, against main. The pattern was 23 renames, then 20 undos,
run twice; the figures agreed to within a millisecond.

| | main | this change |
|---|---|---|
| what a rename stores | 14.4 MB | **17 KB** |
| rename, min / median | 13 / 48 ms | 18 / 19.5 ms |
| undo, min / median | 0 / 0 ms | 10 / 27 ms |
| managed memory held afterwards | 352 MB | **60 MB** |

- **The rename median improved.** On main it was mostly garbage collection paying
  for the copies. The minimum rose by the compare's cost.
- **Undo is the one thing that got slower.** It now pays for a thaw before the
  re-render that already follows it.
- **The first build of the compare cost 46 ms,** twice the clone. Most of that was
  one getter: `BrushSettings.MediumOnDisk` decides "untouched" by serializing the
  medium, and the compare read it twice per stroke. It is now compared through
  `Medium`, which gives the same answer (`ContentEquality.ComparedThrough`). That
  brought the compare of 64 drawings to 22.7 ms, allocating 6 KB.

## How it is known to work

- **`UndoSnapshotFidelityTests` still holds.** It compares whole serialized
  documents across the round trip and does not care how the copy was made, as it
  did across B142's two fixes.
- **A reflection guard.** For every property the walker sees on `Frame`, `Stroke`
  and the types beneath them, perturbing that property alone must make the compare
  say "changed". A property added later is covered without anyone remembering to.
- **Sharing is real:** a structural edit that changes one drawing allocates about
  one drawing.
- **Nothing aliases:** after any undo, mutating the live document leaves every
  frozen copy unchanged.
- **Measured:** the lab's `memory-session` runs again, A/B.
