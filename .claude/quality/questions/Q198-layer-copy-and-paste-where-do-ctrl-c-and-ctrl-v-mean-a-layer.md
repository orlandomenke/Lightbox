# Q198 · Layer copy and paste: where do Ctrl+C and Ctrl+V mean a layer, and how far does a paste reach — **answered 2026-10-07**

**Answered:** a whole layer can be copied and pasted from **both the layer docker
and the X-sheet, by Ctrl+C / Ctrl+V and by right-click**. Ctrl+C/V mean a layer
when a layer surface has focus and are otherwise exactly what they were (cels,
selected lines). A paste is **within the document**, lands **above the active
layer**, and carries everything. The X-sheet's layer selection is **the docker's**.
Every recommendation was taken.

Raised by the owner: *"I am missing an option to copy/paste an entire layer.
Which I want to be able to do in both xsheet and layer with both RMB as with
CTRL + C/V"*, alongside *"fix multiselect in layer and xsheet"* (B390).

## The decisions, and what each costs

1. **One selection, two places.** Ctrl/Shift+click on an X-sheet layer name picks
   layers on the same set the docker keeps, so every layer verb sees the same
   thing from either place. Cost: none found. The alternative, a separate sheet
   selection, could disagree with the docker, which is the inconsistency the
   report was about.

2. **Ctrl+C/V are routed by focus on a layer surface.** A docker row, or the
   *name* of a layer on the sheet, having focus makes the key a layer verb; a cel
   having focus leaves it the cel's, and nothing else changes. Cost: this is
   focus, where the existing line/cel routing is deliberately "what is
   selected on screen", so a layer row focused while lines are selected copies
   the layer, not the lines. Chosen because focus on a layer row is the
   artist's own statement of what they are working on, and the marching ants
   are not. Ctrl+X is not routed: cut was not asked for. The alternative, a
   separate key pair for layers, was not taken because the owner named Ctrl+C/V.

3. **Within the document, everything, above the active layer.** A paste is
   fresh ids at every level (layer, drawings, strokes, mask) and a numbered name
   (`Ink copy`, `Ink copy 2`); it does not join the original's link or fluid
   group and is never the paper, and it keeps the rig it followed. Several
   layers paste in their stacking. One undo step; the pasted layers become the
   selection. Cost: **a layer cannot be carried to another document yet.** A
   stroke's clip region is an entry in its own document's table, so a layer
   pasted elsewhere would silently stop being clipped; the paste refuses in
   words instead. Via the system clipboard it would need a serialised form and
   would take untrusted input, so it is the larger change if wanted.

4. **Two branches.** Multi-select (B390, PR #560) first, then this.

## What this does not do

- No MCP verb: a clipboard is the artist's. A *duplicate layer* for an agent is a
  separate question.
- No cut for layers. Delete already exists and covers it.
