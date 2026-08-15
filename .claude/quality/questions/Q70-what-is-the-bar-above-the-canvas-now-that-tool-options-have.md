# Q70 · What is the bar above the canvas, now that tool options have a docker? — **answered 2026-08-12**

The docker work (#195) gave every tool's full vocabulary a panel, which left
the old tool-options bar ambiguous: a second copy of the panel, or something
else? Asked with three shapes — mirror of the docker, quick-access strip, or
status-only — recommendation on the strip.

**Answer: the quick-access strip, and the owner sharpened it in three ways
the question did not ask.** Near-verbatim: *"Quick option. But designed to
the workflow. Should still be customisable but at least a first quick option.
For example; the select has the marquee function, put that there for
illustration. Like a smart bar per workspace, with the exception of size and
transparency. Also Transform should have it's own tool option and be removed
from the quickbar and into the tool options docker."*

Read out as rules:

1. **The bar is the Quick options bar** — per-tool quick controls, not the
   full vocabulary. The docker owns depth; the bar owns reach.
2. **Size and opacity are pinned**, outside the overflow, for every painting
   tool — the same argument that pinned the colour swatches (B77): the two
   things a hand reaches for mid-stroke must never fold into a "More" menu.
3. **Customisable per workspace, later.** Drag-and-drop of which options sit
   on the bar, saved with the workspace, is stage 2 on its own branch — with
   size and opacity explicitly non-removable. Stage 1 ships the fixed layout
   so the bar is immediately useful.
4. **Transform leaves the bar.** A transform session's controls live on a
   page in the Tool options docker, and `BeginTransform` opens that docker so
   Ctrl+T never strands the artist without Apply/Cancel in sight.

### What stage 1 deliberately does not build

The per-workspace smartness and the drag-and-drop customisation are one
feature (customisation *is* the per-workspace part — a default layout that
differs by workspace with no way to change it would be a guess about
workflows), so both land together in stage 2, with the registry of offerable
options built then, when something exists to enumerate it.
