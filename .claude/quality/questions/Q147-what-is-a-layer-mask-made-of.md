# Q147 · What is a layer mask made of? — **answered 2026-08-22: strokes, like everything else**

A painted mask is painted content, and invariant 1 says painted content is a
stroke record with the pixels derived. The alternative — a stored grayscale
bitmap, which is what Photoshop keeps and what PSD import would hand over —
would be the first underived pixels in the document.

| | What it costs |
| --- | --- |
| **A stroke-recorded mask** (recommended, **chosen**) | The mask is a `Frame` of ordinary strokes rendered to alpha through `BrushEngine` — deterministic, undoable, replayable, and reachable by the inbetweener for free, because it is the same record everything else already is. The cost is interop: PSD raster masks cannot be imported losslessly in v1 (defer, or vectorize when that lands). |
| A raster bitmap mask | Cheap PSD import, and one less render at composite time — but replay, determinism (invariant 2) and AI inbetweening stop covering masks permanently, and "the stroke record is the document" acquires its first exception. An exception to invariant 1 is a hole, not a variant. |

Luminance convention: the mask frame renders to a single alpha channel —
coverage is opacity, so painting shows and erasing hides, matching how every
other tool here already thinks. There is no white-reveals/black-conceals
channel to explain, and inverting is a mask-level flag rather than repainted
strokes.
