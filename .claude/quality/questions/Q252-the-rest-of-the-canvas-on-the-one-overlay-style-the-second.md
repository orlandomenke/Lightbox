# Q252 · The rest of the canvas on the one overlay style: the second colour, hover, guides and the duplicate marks — **answered 2026-10-12**

**Answered: amber `#FFB020` is the style's one second colour; hover grows and thickens without changing colour; guides stay quiet at rest and take the style when grabbed; the duplicate selection marks go.** All four as recommended.

Raised by Q249's own costs, 2026-10-12: the style landed on the pen and the white arrow with one accent and two states, and the review of it (the ui-critic) named what the remaining thirty overlays would otherwise invent one at a time — a hover state, and a colour for a mark that means something other than *editable*.

What it blocks: moving the transform box, bands and cage, guides, selection outlines, crop, camera, symmetry, the gradient axis, reference boxes and the rig onto `OverlayStyle`.

## What was asked, and what was answered

**1. The second colour.** Pivots, the warp bands and cage, and a guide's anchor are drawn in about a dozen ambers. *The palette's `AccentAmber`, `#FFB020`* (recommended) replaces all of them and means *a reference point, not the thing itself*. Against: no second colour (a pivot told apart by shape alone sits inside a box of same-coloured handles and is easy to lose), or coral (already the primary action in the panels and the balance arc's warning on the canvas).

**2. Hover.** *Grow and thicken* (recommended): a hovered grip is two pixels bigger and a hovered line goes from 1.5 to 2.5 px, in the same colours. It cannot be confused with selected, which is filled. Against: turning white (white already means an open, unselected grip), or no hover at all (guides and whole lines light up on hover today and would lose it).

**3. Guides.** *Quiet at rest, styled when grabbed* (recommended). A guide being drawn against stays a faint single-pass line in one fixed colour, so a perspective rig never competes with a selection; with the Move tool in hand, the hovered and the selected guide use the style's cased line and grips. Against: fully on the style (thirty cased cyan lines read as thirty selected things), or leaving guides alone (the largest overlay left outside the rule).

**4. The duplicate marks.** A green box over reference boxes, an orange circle over anchors, a magenta box over rig shapes and a fixed 40-pixel cyan box on placements are each drawn over an overlay that already shows its own selected state; `SelectionRenderer` has no callers. *Remove the duplicates, restyle the rest* (recommended): the three coloured marks go, the placement box moves onto the style, the unused painter is deleted. Against: restyling all four (three overlays keep saying *selected* twice), or leaving them for later.

## What it costs

- **The transform's pivot, bands and cage change from `#E0A030` to `#FFB020`**, and every other amber on the canvas with them. A small shift for the transform; a larger one for the motion arc and the analysis marks if they are ruled to be reference marks too — they are judged against rather than edited, and are decided when reached.
- **A selected reference box, anchor or rig shape loses its second outline.** Each keeps the selected state its own painter draws.
- **Guides at rest keep two quiet tones, not one**: the lines are the faint blue, and a guide's own marks — a vanishing point's cross, a height scale's post — are the reference amber turned down. Built that way because the first answer makes a guide's anchor a reference mark, and a vanishing point that could not be told from the rays through it would have lost the reason it is marked.

## What turned out differently when it was built

**The fourth answer was asked on a wrong premise, and was not carried out as asked.** The three coloured marks are not duplicates. A rig mark's own white *selected* look follows `SelectedRigMarkId` — the one mark the rig panel is pointed at — while the green, orange and magenta outlines follow the arrow's selection, which can hold several things and is what a drag then moves. Removing them would have left the arrow's selection of anchors, shapes and reference boxes with nothing on screen. The inventory that called them duplicates had read the two painters and not their sources.

What was built instead keeps the intent — fewer colours, one meaning: all four kinds are drawn as **one styled outline**, the cased accent line, so the arrow's selection looks the same whatever it holds. `SelectionRenderer`, which had no callers, is deleted as asked. Whether the two selections should become one is a larger question about the rig panel and is not answered here.

**The arrow's outline of a drawn line became a third kind of line, by a later ruling.** The first render put the cased line over a picked stroke and hid the stroke — a five-pixel line of ink under three and a half pixels of chrome. Shown it, the owner said: *"make the outline thinner so the ink shows."* So the outline of a picked or hovered drawn line is one pixel of the accent with no casing (two when hovered), and the cost was named before it was chosen: it can be lost on artwork of the accent's own colour. Everything that is not sitting on ink keeps the cased line.
