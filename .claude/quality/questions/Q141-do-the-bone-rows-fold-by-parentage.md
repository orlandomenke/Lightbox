# Q141 · Do the timeline's bone rows fold by parentage? — **answered 2026-08-21: yes, a collapsible tree**

The owner expected "a collapsable hierarchy for the bone system in the
timeline" (2026-08-21). The Bones toggle existed but was all-or-nothing: a
flat list, twenty rows or none.

| | What it costs |
| --- | --- |
| **Collapsible hierarchy** (recommended, **chosen**) | Rows in depth-first parent order, a chevron on each bone with children, per-bone fold state. One visible-bone walk must feed the rows, the routing and the selection alike, or a ring lands on a different bone than a drag retimes. |
| Keep the flat list | Cheapest; a big rig stays 20+ rows or nothing. |
| Flat + per-bone pinning | Fine-grained, but a second mechanism beside the toggle instead of reusing the parenting the rig already has. |

Folding is a view's memory, never the document's — a folded bone's keys still
play and export, and its selected dots simply fold away, the same answer
`PoseRowsExpanded` already gave. The summary row's chevron *is* the Bones
toggle wearing its tree face, so there is one fold vocabulary rather than two.
Evidence: `VisiblePoseBones` in `MainViewModel.Painting.cs`,
`TimelineUxTests.FoldingABoneHidesItsSubtree`.
