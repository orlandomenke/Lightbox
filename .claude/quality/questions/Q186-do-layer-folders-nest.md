# Q186 · Do layer folders nest? — **answered 2026-09-11: yes, build it now**

Raised while fixing the four things wrong with layer folders (B366–B371). The
report was "we have layers and layergrouping, but the logic surrounding it is
off", with a list: drag folders, drag layers into folders, make a layer inside
the folder you are working in, drag layers in and out — and make it performant.
Nesting was not on the list. It was asked about anyway, because it is the one
item that decides the shape of everything else and because the answer could not
be read off the existing rules.

## What was recommended, and what was chosen

| | What it costs |
| --- | --- |
| **Flat, and nest later** (recommended) | Everything on the list works, and a folder in a folder stays impossible. Nesting later is the same work plus a migration. |
| **Nest now** (**chosen**) | A Core model change: parentage, contiguity as an invariant, ancestor walks for visibility and locking, and every drag rule re-derived. |
| Flat now, shaped for nesting later | The recommendation plus abstraction for work that may never happen. |

The recommendation was wrong about the cost and right about the risk. It read
the change as touching everything that reads a folder; the measurement — 26
reads of `Layer.GroupId` across the whole solution, 19 of them in one file — says
the reach is small. **Costing a refactor by reading the usings guesses high**,
which is a lesson this repository has already paid for once.

What it was right about is where the danger sits, which is worth writing down
because it is not where it looks.

## The invariant is the whole of it

`Scene.Layers` is flat and must stay flat: it is the compositing order, and a
hundred call sites walk it. So nesting is **not** a change of structure. A folder
names its parent, a layer names its folder, and neither holds a list of
children — because `Scene.Layers` already is one, and *two containers where one
would do* is precisely the failure B114 is about, where a second list went
unwired and half of every project became invisible to export.

What nesting costs instead is an invariant: **every folder's whole subtree
occupies one contiguous run of `Scene.Layers`, in the order the docker draws
it.** That is a pre-order flattening of the tree, and `LayerTree.Normalise` is
the single place that restores it.

**Every operation therefore changes parentage and then re-derives the order**,
rather than splicing runs by index. The flat-folder code did the splicing — find
the run, remove it, find the anchor, insert — which is already delicate with one
level and becomes unwritable with two, because every move has to carry
descendants the call site cannot see. Re-deriving cannot leave a folder split
around a stranger, since a split is not expressible in the walk. It costs one
pass over the layer list per structural edit, against a whole-document clone the
same edit already takes for undo.

**B370 is what the alternative looks like.** The ▲/▼ buttons swapped two entries
of the layer list and touched nothing else — which even with flat folders could
leave a layer sitting between two of a folder's members while outside the folder.
Silent, permanent, and it makes the compositing order disagree with the panel.
Nesting did not cause that bug; it made it visible.

## Three smaller answers taken at the same time

- **A new layer lands directly above the active layer, in that layer's folder.**
  Photoshop's, Krita's and Clip Studio's rule. The reason to prefer it over
  "top of the stack unless the active layer is grouped" is that the second makes
  one gesture mean two things depending on a folder the artist may not be looking
  at. Its blast radius is near zero: the common `while (count < n) AddLayer()`
  produces the same stack either way, because each new layer becomes active.
- **Clicking a folder header aims at it without moving the paint target.** A
  folder is not a layer and can never be where a stroke lands, so collapsing the
  two would mean either a folder that steals the brush or a folder that cannot be
  pointed at — and an empty folder has no layer to stand in for it at all.
- **An empty folder stays, and can be made deliberately.** The alternative,
  dissolving a folder when its last layer leaves, has no orphans by construction
  and makes it impossible to set a structure up before filling it.

## What was deliberately not decided

**How deep.** `LayerTree.MaxDepth` is 8 — past anything an artist has been seen
to build, and a limit rather than none because a runaway nest is far likelier to
be a bug than an intention. It is a number to change if somebody hits it, not a
position.
