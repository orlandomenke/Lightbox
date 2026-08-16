# Q84 · Camera and scene: the four remaining wishes, and where animation pegs belong — **answered 2026-08-14**

The *Camera and scene* section held four `[?]` wishes — safe area guides, zoom
preview, camera shake preview, scene preview — and the owner asked, reading
them, whether **animation pegs** fit here as well. They do, and the pegs
question turned out to be the largest of the five and the only one that touches
work already in flight. Four questions, prompted and answered in one exchange.

- **Pegs and bones** — *separate record, shared ops* (recommended, accepted).
  A peg hierarchy and a bone hierarchy are the same data structure — named
  nodes, a parent, a keyed and interpolated transform — and `Doc.Armature`,
  `Scene.PoseTrack` and `ArmatureOps`' FK solve already exist. So this was a
  real risk of building one thing twice, the mistake Q11's "reusable animation
  presets" and the parallel `ReferenceAnimation` record were struck for.
  Decided as Toon Boom arranges it: distinct node types over shared transform
  machinery. A `Peg`/`PegKey` record reuses the interpolation shape
  `CameraOps` already has, and `Armature.PegId?` hangs a rig off a master peg.
  Costs accepted, and sharpened by Q81's UI landing the same day: **coarse
  assignment already ships**, so a rigged character's rigid part movement is
  covered and the peg must not become a second way to do it. The peg's
  territory is the layer with *no* armature and no weights — a background pan,
  which today would mean creating an armature and binding strokes just to
  slide a painting. The two hierarchies must also keep sharing one graph
  editor or they will drift apart. One question is deliberately left for when
  pegs start: whether a peg auto-keys at the playhead like a bone or takes
  explicit keys like the camera. Q81 decision 2 made those two different on
  purpose, and a peg sits between them — a pan is authored as deliberately as
  a camera move, but dragging one is as frequent as posing.
  The alternative — one `TransformNode` type — is cleaner on paper and was
  declined for timing: merging the two records while the bone system is
  mid-flight buys a unification that shared ops already deliver, and it would
  put peg-shaped nullables into `Armature`'s bind-pose semantics. Waiting for
  bones entirely was declined outright: a shot cannot pan a background until
  cost-L skinning lands, and rigging a skeleton to slide a background layer is
  the wrong shape of work for the commonest camera-department job there is.
- **Zoom preview and scene preview** — *scene panel, and strike zoom*
  (recommended, accepted). *Zoom preview* is struck as a duplicate of the
  shipped `Camera preview / view through camera`, on the Frame-tagging and
  Timeline-bookmarks precedent: a wish indistinguishable from a built feature
  is the wish list the checkbox rules exist to prevent. *Scene preview* is
  absorbed into **Multiplane parallax** as the authoring surface stage 1
  otherwise lacks — the "Scene panel" `docs/DESIGN-3d-space.md` already names,
  a schematic of the layer stack with depths and the camera's path. Net: one
  item struck, one absorbed, no new item. The competing reading — that both
  were delivery-quality *preview render* (playblast) features — was considered
  and declined; ordinary playback plus view-through-camera covers what it was
  for, and a cached render tier is its own design.
- **Camera shake** — *a nullable modifier on the camera* (recommended,
  accepted). `Camera.Shake?` (amplitude, frequency, decay) evaluated inside
  `CameraOps.At`, its offset seeded from the frame through `Hash01`. Invariant
  2 is what makes this **better** than the field's version rather than a tax on
  it: a shake nobody can reproduce cannot be re-rendered at 4K or handed to
  anyone, and this one is identical every time. Seeding from the frame index is
  legitimate here where it would not be for a dab, because there is exactly one
  camera — nothing can flicker relative to a sibling. The *preview* half of the
  wish's name then costs nothing: a render-time modifier is visible in ordinary
  playback. Baking to keys was declined as the primary shape — 24 keys a second
  floods the graph editor and makes the underlying move unrecoverable, so
  re-tuning amplitude would mean undo and re-apply. A bake command remains
  available later as an addition, not a replacement.
- **Safe areas** — *nullable percentages on the camera* (recommended,
  accepted), with visibility as a view toggle. A delivery spec travels with the
  shot — broadcast and a web short want different safes — and `Camera` already
  carries `OutputWidth`/`OutputHeight`, which is the same kind of fact. A pure
  view preference was declined because the spec is then lost on handoff. Real
  `Guide` objects were declined as an outright defect: safes must follow the
  camera through a pan, push and roll, and guides snap strokes, so a
  compositional boundary would start grabbing linework.

Two consequences recorded because they reach past this section:

- **Invariant 5 will name two transforms, not one.** `CLAUDE.md` says the
  camera "is the one transform that is not" view-only. A peg is authored,
  keyframed, saved and exported on exactly the same terms, so that sentence is
  reworded as part of landing pegs rather than afterwards.
- **Pegs break this section's free-for-assets pattern, once.** Depth without a
  camera does nothing, which is why multiplane never taxes an asset document. A
  peg without a camera *does* something — it moves content on the canvas, so it
  exports. That is correct (an artist who pegs a layer meant to) and it means
  pegs are the one item here that is not free for the asset target.
