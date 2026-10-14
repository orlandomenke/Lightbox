# Q268 · How Split group takes a merge back out, and what a click on a merged line picks — **answered 2026-10-14**

**Answered: Split takes a whole merge back out, every cel, onto a layer like the one that was merged; marks drawn afterwards stay where they are and erasers that reached the group follow it; and a click on a merged line selects the whole group.** Three questions asked together with `AskUserQuestion`, all as recommended.

Raised 2026-10-14 when building what step 1 of Q265 still owed (Split group and whole-group picking), which the first pull request (#735) left out.

What it blocks: Split group, and the picker's handling of groups.

## 1. What one Split takes out

A merge leaves a group in every cel where the upper layer had a drawing, so "split" has to mean a set of groups, not one.

- **The whole merge, every cel** (recommended) — **chosen.** Each group records the id and name of the layer it came from; Split recreates that layer directly above, with the blend, opacity, clip, mask and effects the groups carry, and takes that layer's groups out of every cel. The merge undone without undo. Costs two fields on a group, written only on groups.
- *This drawing only.* One cel at a time; a 24-drawing merge takes 24 splits and leaves 24 one-drawing layers.
- *The layer's last merge.* No new field: the last group in every cel. Wrong when cels were merged from different layers.

## 2. Marks drawn after the merge

They sit on top of the group in the merged drawing, and once the group is a layer above, they are under it.

- **Stay, and erasers follow** (recommended) — **chosen.** Later strokes stay on the merged layer. A later eraser that reached the group is copied onto the split layer too, so what was rubbed out stays rubbed out. A later brush mark that overlapped the group now sits under it, and Split says how many.
- *Refuse if anything reaches it.* Always exact; blocks splitting after a few fixes.
- *Move them up too.* Exact over the group; moves marks drawn over the lower drawing off their layer.

## 3. A click on a merged line

- **Select the whole group** (recommended) — **chosen.** It highlights as one thing and moves, copies, deletes or splits as one. Step 2 adds a way into it.
- *Nothing, as today.* Groups invisible to the picker until step 2.
