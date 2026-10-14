# Q265 · How far does a merge go to keep strokes? — **answered 2026-10-13**

**Answered: all the way. A merged drawing keeps the upper layer's strokes in a group — an entry in the stroke list with its own blend, opacity, clip, mask and effects — so a merge never turns strokes into pixels.** Five questions in two rounds, all as recommended.

Raised by the owner on 2026-10-13, the day the eraser case was fixed (Q264), on seeing how many other reasons still flattened a merged drawing: *"I thought our deal was that working with the stroke record was the superpower compared to other tools like Krita and Photoshop. While keeping the superpower of editable everything everywhere. Without losing the power of digital painting."*

What it blocks: an artist merging layers without giving up the one thing the application is for; and the AI inbetweener reading merged line art.

## What was found first

Merge flattened for nine reasons, and eight of them are computed from the two drawings alone — the bake is a pure function that could be kept and run at render time instead of once at merge time. Only a folder's shape carving the upper layer depends on anything else. `docs/DESIGN-stroke-groups.md` has the table, the model, the render order, the inventory of the forty-six places that read a stroke list (six of which would do something wrong on meeting a group), and the steps.

## What was asked, and what was answered

**1. How far to go?** *Grouped merge: never rasterize* (recommended). Against: design it now and build it after the AI work; or stop at erasers and leave folders as the way to keep strokes.

**2. Where do groups live?** *An entry in the stroke list* (recommended). Against: keep the layers and show them as one — far less code, nothing new in the file, but anything drawn or erased after merging lands on one inner layer only, so it is a tidier folder rather than a merge.

**3. What ships first?** *Exact merge, whole-group editing* (recommended): a group moves, transforms, resizes, copies and splits back out as a unit, and the AI reads through it; reshaping one stroke inside a group follows, tool by tool. Against: holding everything until every tool reaches inside; or erasers and blend/opacity only.

**4. Older builds?** *Mark only documents that contain a group as newer* (recommended), so every other file stays openable everywhere and an older build refuses a grouped one rather than opening it wrongly. Against: also building a "save for older builds" export that flattens groups.

**5. The eraser cutter (Q264, PR 732)?** *Merge it; retire its dialog later* (recommended). It works today; once merge is exact either way the dialog goes, and the cutter stays as how the inbetweener reads an erased group and as an optional simplify command.

## What it costs

- **It changes what a stroke list can contain.** Forty-six places read one. Six must be fixed with the first step; the rest ignore a group safely, which is also the definition of "not yet editable there" — each editing tool has to be taught to reach inside, and until it is, **Split group** is the way to edit a stroke that is in one.
- **A document with a group cannot be opened by an older Lightbox.**
- **Rendering a grouped drawing costs a surface per group**, bounded by the region being repainted, and a grouped drawing may fall off the tiled, checkpointed and region-repaint fast paths where its contents would have.
- **One case still flattens**: a folder's shape carving the upper layer.
- **The overview picture, layer roles and folder inbetweening wait behind it.**

## What the first pull request did with that, and where it departs

Recorded 2026-10-13 with the build, because three of the lines above turned out differently and a decision file that still says the plan is worse than none.

- **"Whole-group editing" landed in part.** Built: a group moves, transforms and resizes with its drawing, and under a selection it is cut at the selection's edge like a single stroke — that last one was not in the plan and was added because flattened pixels already did it (B433), so a group that did not would have been a step back. **Not built yet: Split group, and the picker selecting, copying or deleting a group on its own.** Until they are, undo is the only way back from a merge. Nobody decided that; it is the order the work fell in, and it is the next branch.
- **"One case still flattens" is four.** A folder's shape, as expected; a stroke sampling all layers live, which the design note said it would measure; and two found while building — a placed symbol on a drawing that needs a group, and a layer clipped onto a layer that has effects. Each is named in the design note with what would cure it.
- **"An older build refuses a grouped one"** is true, and blunter than the answer assumed: it fails with a conversion error naming the stroke kind, not with "saved by a newer Lightbox", because no build before this one read the version before parsing. Nothing is lost by it. This build reads the version first from now on.
- **The eraser dialog stayed, reworded.** Both of its answers keep strokes now: *cut the lines* (plain strokes, nearly exact) or *keep the erasers, in a group* (exact). It goes when a line inside a group can be picked on its own, which is when the two answers stop differing in what the artist can do next.
