# Q249 · One style for everything drawn over the canvas: the accent, the handles, and how it lands — **answered 2026-10-12**

**Answered: one shared style, in `OverlayStyle`. The accent is the palette's cyan, every line is cased, squares grab and dots steer, and it lands on the pen and the white arrow first with the rest of the canvas following on its own branch.** All three as recommended.

Raised by the owner, 2026-10-12, after the pen's overlay was put in front of them as screenshots from the perf lab: *"I think we should align all overlays in the app. Transform, Pen tool, guides? and more. They should have a singular style."*

What it blocks: the pen's legibility fix, which would otherwise have been restyled twice, and every overlay that follows it.

## What was found first

An inventory of the canvas on main that day: **thirty-two overlays**, drawn from about ten blues (`SKColors.Cyan`, `DeepSkyBlue`, `#40C4D4`, `#00D1BC`, `#4DC4FF`, three guide blues …), twelve ambers, eight handle sizes between 8 and 10 px in squares and 2.5 to 5 px in circles, and grab distances of 5, 6, 7, 8, 9 and 10 px. No shared constants existed; `DESIGN.md` said nothing about what is drawn on the canvas. Most overlays have no contrasting second pass, so they vanish on artwork of their own colour.

## What was asked, and what was answered

**1. How should it land?** *Pen first, then the rest* (recommended) — one definition, applied to the pen and the white arrow on the legibility branch, then a second branch for transform, guides, selection, crop, camera, symmetry and the rig. Against: everything in one branch (one large diff across the two riskiest files, nothing ships until all of it is right), or the pen alone with the unification filed (the pen gets restyled twice).

**2. Which colour means "this is chrome you can edit"?** *Cyan `#00D1BC`* (recommended) — already the palette's `AccentCyan` and what the design calls the chosen-state accent, so no new colour enters the application. Against: the `DeepSkyBlue` the pen used, which is Illustrator's and is not in the palette; or a colour per layer, which is a setting, a picker and a record change — a feature, not a restyle. Colours that carry a meaning keep it: onion red and blue, the heat map, the camera's orange, the brush ring.

**3. What shapes?** *Squares grab, dots steer* (recommended). Anything moved or resized is a square grip; a smooth point stays round, so a corner can still be told from a curve without clicking, which the manual promises. A curve handle is a small solid dot on a stem, clearly smaller than a grip. Selected is filled, unselected is white inside an accent outline. Against: Illustrator exactly (every anchor square, which loses corner-versus-smooth at a glance), or keeping the shapes and fixing only size and fill (transform, crop and camera keep their own).

## What it costs

- **The pen and the white arrow change colour**, from blue to the cyan, and their handles change shape. Nothing in a document changes: this is chrome.
- **Thirty overlays are still in their own colours** until the second branch, so for a while the canvas has one overlay in the new style beside the rest in the old ones. The roadmap item stays partial until then and names what is left.
- **The semantic colours need a ruling each** when their overlay is reached — the transform pivot's amber, the guides' three states, the green, orange and magenta selection marks. Those are the second branch's questions, not settled here.
