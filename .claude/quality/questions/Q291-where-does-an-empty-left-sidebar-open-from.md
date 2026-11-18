# Q291 · Where does an empty left sidebar open from? — **answered 2026-10-23: beside the tool rail**

Raised by: the same report as Q290.

`DockZones.NearestEdge` measures its 56 px `EdgeBand` from the edge of the
content rectangle, which was the whole `RootGrid`. On the right that edge is
the right sidebar, so the band is where the sidebar will be. On the left it is
the tool rail: with nothing docked there, the only place a drop opened the left
sidebar was over the tools, and the canvas just beside them did nothing.

What it blocks: opening the left sidebar at all, unless you know to drop on the
tools.

| | What it costs |
| --- | --- |
| **Measure from the rail's right edge** (recommended, **chosen**) | Crossing the first ~56 px of canvas beside the tools lights up the preview. The rail itself stops being a target. |
| The rail and the strip beside it | The biggest target, and any drag that crosses the rail lights up the left side. |
| Leave it on the rail | Nothing to build; the left stays hard to open. |

## What was built

`MainWindow.LeftDropEdge` is the right edge of the rail's two columns (the rail
and its splitter), about 4 px short of where the left host's margin starts. It
is passed to `DockZones.Resolve` as `leftEdge`, which moves only the left band
and the left preview.

A first cut narrowed the whole drop rectangle instead, and review caught what
that cost: the top strip runs the full width of the window above the rail, so
its preview came out a rail's width short and the top edge above the rail was
no longer a target. `TheTopStripStaysFullWidthAboveTheToolRail` keeps it.
