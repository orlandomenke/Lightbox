# Q210 · Folder header drop zones: thirds, above / into / below — **answered 2026-10-07**

**Answered:** a folder header splits into **thirds** — top: above the folder;
middle: into it; bottom: **below the whole folder** — for an open folder as
well as a collapsed one, and for a carried layer as well as a carried folder.
The recommendation was taken, over keeping quarters with a "below" quarter
added, and over a Krita-style drop where the release's indent picks the level.

Raised by the owner, 2026-10-07, on build `ba0ca2be`: *"folders cannot be moved
above or below (in between) other folders to change the hierarchy"*, and then
*"This dragging and zones should be applied to layers as well. Layers can be
placed above or below other layers or folders and placed into a folder."*
Asked what a folder drag did, the owner answered that it **only goes inside**.

## What was there, measured

Q204's table, carried folder over every docker row, two folders side by side:
over a header only the top quarter meant "above"; the rest meant "into"; an
**open** folder had no "below" on its header at all (a past bug — a lower
quarter that drew its line under the header and landed under the last member —
had been fixed by removing it). Rows inside a folder put the carried folder
inside that folder. So "Folder 2 below Folder 1" was reachable only through the
top sliver of whatever row followed Folder 1. Every slot existed; none could be
hit on a compact row.

## What it costs

- **Into** shrinks from three quarters of an open header to its middle third.
- **Just under an open header** now means below the whole folder rather than
  first inside it. The old reason for refusing that — a line under the header
  and a landing under the last member — is answered by drawing the "below" line
  under the folder's **last row** (`ShowLayerDropHint`), so line and landing
  agree.

## Layers

The owner's second message is satisfied by the same change: one table serves a
carried layer and a carried folder (Q204), so a layer gets the thirds on a header
too. A layer row cannot be dropped into, so it keeps halves: above / below.
