# Q136 · Where do pose references come from — a public API, or a library of our own? — **answered 2026-08-20**

The owner asked how a pose reference should be designed: connect to a public
pose API, or introduce a pose library. Three shapes were prompted — library
only (recommended), public API, and a hybrid.

**Answered: the hybrid — the library is the record of truth, plus an opt-in
fetch from a named external source if a properly licensed one is found.**
This goes one step past the recommendation, and the cost is stated below so
the choice stays a choice.

## What the library half is (buildable now, and most of it exists)

- **A rig pose is already a record.** `PoseKey` on a `PoseTrack` captures
  bone-local transforms at a frame; a library pose is that row given a name
  and lifted out of the timeline. "Capture pose" reads the armature at the
  playhead; "apply pose" writes one `PoseKey` through the existing
  bone-gesture editor step — one undo step, strokes never touched (invariant
  1; the drawings follow through the binding, Q90).
- **Skeleton mismatch resolves by bone name**, unmatched bones untouched and
  said out loud — the exporters' refusal honesty, not a silent best effort.
- **Image poses are already references.** A photographed or drawn pose sheet
  is `ReferenceSheet`/`ReferenceStrip` work; the library does not duplicate
  it. Drag-from-browser already covers "grab a pose off the web" for images.
- **Scope: the character**, joining the Pillar 1 character library — an
  imported character brings its animations and palette, and poses become the
  third thing in that sentence. One file per pose in project storage (Q92's
  lesson), absent until authored, reachable in every project type.
- **Pose files are our own JSON**, like `RigExport` — never Spine's or
  Live2D's formats (the licensing wall).
- **AI reference generation stays the route for a pose you do not have** — it
  is already a pillar item, goes through the vetted artist interface, and is
  the one networked thing the app deliberately has.

## What the fetch half is, and what it waits on

An importer behind an interface (the `IArtist` pattern): absent from the UI
until a source is configured, feeding the same library records rather than a
second kind of pose. **Designed, not scheduled** — because today no canonical
licensed public pose API exists; the well-known pose sites are ToS-restricted
scrape targets, and in a public GPL repository the *content* licence matters
as much as the code licence. The connector gets built when a concrete source
with a compatible licence is named, and not before.

## The cost accepted with the hybrid

A maintenance surface is reserved for a connector that may never have a good
target, and "find a licensed source" becomes a standing errand. The library
half is not blocked on it: nothing in the record or the UI references the
fetch until a source exists.
