# Q200 · How far the UI scale reaches, and its range — **answered 2026-10-07**

**Answered:** the scale reaches **all of the chrome and never the canvas**: the
dockers, floating panels, toolbox, tool-options bar, the strips above the canvas,
the timeline and the bars that float over the canvas. It runs **75–200% in 5%
steps, as a slider with a field**, and applies live while dragging. Both
recommendations were taken.

Raised by the owner: *"I want to be able to change the scale of the UI. Ideally
with a slider, but I can imagine that might cost in the performance side. So a
general value can be as well. I should change the height and width of the
dockers and their contents."*

## The decisions, and what each costs

1. **All chrome, not the canvas.** One text size on screen, which is the point of
   a scale; scaling only the dockers would put 150% panels beside a 100% toolbox.
   Cost: more surfaces to wire, and context menus and tooltips are separate
   popup windows that need their own hook. The canvas stays out because a scaled
   canvas would be resampled — blurry and slower — and its zoom already does the
   job a scale would do there. The alternatives were dockers only (smallest,
   inconsistent) and dialogs too (most work; each dialog has its own fixed sizes).

2. **75–200%, slider plus field, live.** DESIGN.md's rule: a value explored by
   feel gets both. The performance worry in the request does not apply to the
   drawing: the canvas is outside the scaled region, so a stroke costs exactly
   what it did. Changing the value costs one relayout of the chrome, paid while
   the slider moves and not while painting. Fixed presets were the alternative;
   they lose the in-between values for no saving.

## What is not a question

- It is a **preference**, not a document setting: it changes no pixel of the art
  and is stored with the other per-machine settings.
- A docker's saved width is kept **unscaled**, so a layout saved at 150% opens at
  the right proportions at 100%.
