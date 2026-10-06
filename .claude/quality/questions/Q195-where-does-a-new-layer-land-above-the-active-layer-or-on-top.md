# Q195 · Where does a new layer land: above the active layer, or on top of the stack?

**Answered 2026-10-06: above the active layer, and in the active layer's folder
exactly when the active layer is in one.** In the owner's words: "If I have
selected a layer it should go into the [folder], above the active layer. If the
active layer is outside of the folder, the new layer will be created outside of
the folder." A folder picked on its header puts the new layer on top of that
folder.

Raised by: B384, the owner's report that "adding a new layer while selecting a
folder does not create the layer inside the folder". `AddLayer` appended to the
top of the whole stack whatever was active, so a layer made while working in a
folder arrived outside it, above everything, and had to be dragged home.

What it blocks: nothing now. It was the one change on
`fix/layers/layer-docker-overhaul` an existing habit could notice — a new layer
made with a lower layer active used to go to the top and now goes beside the
work.

**Recommendation, which was taken:** above the active layer, inheriting its
folder — the placement every painting app has taught. The alternative, keeping
"top of the stack" and adding a folder rule on top of it, would have made a
layer added from inside a folder jump to the top of that folder rather than
beside the layer being worked on, and kept the out-of-folder case landing far
from the work.

Evidence: `ANewLayerGoesAboveTheActiveLayerAndIntoItsFolder`,
`ANewLayerOnALooseLayerStaysLooseAndLandsAboveIt`,
`ANewLayerWithTheFolderPickedGoesInsideIt`.
