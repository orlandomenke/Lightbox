# The scanned papers, and where they came from

Five of Lightbox's papers are measured surfaces rather than constructed ones.
Each is the displacement map of a material from
[ambientCG](https://ambientcg.com), which publishes everything under the
**Creative Commons CC0 1.0 Universal** licence — copy, modify and distribute,
including the raw files, without asking. Credit is not required and is given
anyway.

| Paper in Lightbox | Source | What it is |
| --- | --- | --- |
| Plain | [Paper 001](https://ambientcg.com/a/Paper001) | White paper: a fine, even pebbled tooth. The pencil's sheet. |
| Kraft | [Paper 002](https://ambientcg.com/a/Paper002) | Brown carton paper: short dense fibres. |
| Fibre | [Paper 006](https://ambientcg.com/a/Paper006) | Beige paper with long visible fibres. |
| Linen | [Fabric 061](https://ambientcg.com/a/Fabric061) | A tight plain weave, about 40 cm across the tile. |
| Plaster | [Plaster 001](https://ambientcg.com/a/Plaster001) | Trowelled plaster: scratches and hollows, for heavy paint. |

Created using Paper 001, Paper 002, Paper 006, Fabric 061 and Plaster 001 from
ambientCG.com, licensed under the Creative Commons CC0 1.0 Universal License.

## What was done to them

Only the displacement map of each 1K set is used. `scripts/make_papers.py`
turns it into the file under `src/Lightbox.Raster/Media/Papers/`:

1. Undulation longer than 256 pixels is removed, with a smooth high-pass in
   the Fourier domain so the result still tiles. A brush wants the sheet's
   tooth; a light patch the size of the tile would repeat across a canvas.
2. The result is stretched between its 0.5th and 99.5th percentile, so every
   paper uses the whole range and *Depth* means the same on each.
3. It is saved as an 8-bit grey PNG, white for the peaks of the sheet. A PNG
   and not a JPEG, because a stroke has to render the same on every machine
   and a JPEG's pixels depend on the decoder.

Paper 001 and Paper 002 are not square and are kept in their own proportions:
squaring them would stretch the grain.

## Why these and not others

The first papers tried were a purchased set whose licence grants commercial
use and asks that the files not be redistributed. A public repository hands
its files to everyone, under a licence that tells them they may pass them on,
so nothing from that set is here. Any paper can still be used on your own
machine through **Import paper** in the brush editor, which stores it in the
document that uses it.
