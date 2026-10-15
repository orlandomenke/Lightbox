# Q281 · Docker tab overflow: shrink with an ellipsis — **answered 2026-10-15: shrink, with an ellipsis**

Raised while building the group grip (Q278). Measured in the real window at the
default sidebar width: a Navigator + Palette header has its tabs end at 234 px
while the header buttons start at 210 px, and the colour group's four tabs run
past the sidebar's edge. The tab strip is laid out as wide as its titles and
nothing stops it. The overflow runs **under the float, close and collapse
buttons**, which are transparent until hover but still take clicks, so the end
of a tab cannot be clicked or dragged. In a narrow strip the last tab may be
unreachable except through the header menu (Q276). The gallery snapshot shows
it: the colour header reads *Color · Palette · Gradient · C*.

What it blocks: nothing new; it is an existing defect. It also decides how much
bare header the group grip can count on, which is why it was asked alongside.

| | What it costs |
| --- | --- |
| **Shrink tabs, ellipsis** (recommended, **chosen**) | Tabs narrow to share the room and long titles end in "…", with the full name in a tooltip, as browsers do. Every tab stays clickable and draggable. Costs legibility of long titles ("Reference sh…"). |
| Clip at the buttons | Smallest change. Tabs past the buttons are simply not there, so the header stops telling the truth about what the group holds, and the header menu becomes the only way to reach them. |
| Scroll the strip | Every tab keeps its full width, but a scrolled-away tab is out of sight, and the header gains a second scroll gesture. |

## What was decided

- **Tabs shrink to fit, titles cut with an ellipsis, the full title in a
  tooltip.** Its own branch, after the group grip.
