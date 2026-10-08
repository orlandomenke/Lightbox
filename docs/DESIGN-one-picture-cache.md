# One picture cache: the paused canvas on tiles

The owner chose this as the next memory work (Q222, 2026-10-08): still images
stored sparsely, as tiles. It is phase 5 of `docs/DESIGN-playback-first-loop.md`,
which deferred it until "after B125/B167, or folded into them". This note folds it
in.

## What is held twice today

Playback draws from `TileFrameCache`: each drawing is a sparse grid of 256-px
tiles, and a tile no ink touches is never allocated. Everything else — the paused
canvas, drawing, undo pixels, fills, the colour pick, export — draws from
`FrameBitmapCache`: one bitmap the size of the canvas per layer per drawing. So a
drawing that has been played and then looked at is in memory twice, and the still
copy pays for every pixel of paper around the ink.

Measured on the lab's owner-shaped fixture (64 drawings at 1920×1080, a
throwaway console over the stroke record, brush radius included):

| | memory |
|---|---|
| all 64 drawings as full-canvas stills | 506 MB |
| as the 256-px tiles their ink touches | 141 MB (28%) |
| as one box around each drawing's ink | 98 MB (19%) |

The fixture copies the owner's counts, not their art; a real sheet may cover more
of the canvas. The paper layer always covers all of it.

## Exact first

The paused canvas is where an artist judges a line, so it must not change by a
pixel. The codebase pins what makes that possible:

- **Tiles are bit-identical to a whole render** (`ATiledRenderIsBitIdenticalToAnUntiledOne`,
  `TilePyramidTests`).
- **The tiled picture on screen is not, when zoomed out further than 50%.** The
  tiled route composes from the pyramid level nearest the screen; the still route
  scales the full-size bitmap. Q218 measured the difference: none at 50% and
  above, up to 64/255 at stroke edges at 25%. `ScrubTileModeTests` records the rule
  that follows: *a tiled still must never be left on screen.*

So **a paused canvas on tiles always composes from level 0**, the full-size tiles,
flattened over the visible region and then scaled by the same transform the
still route uses. That is the still route's arithmetic with tiles as its source,
so it is exact by construction, and phase 1's gate measures it rather than
assuming it. Playback keeps choosing the nearest level; that is its quality and
its speed.

The flatten at level 0 is viewport-sized at document resolution, so zoomed out on
a 4K document it is as large as a still — but only for the frame on screen, and in
`TileFlattenCache`, whose budget the overall limit already governs (Q221). Today
the stills of every frame recently shown, ghosted or warmed stay resident.

## GPU, and B167

B167 phases 1–4 have landed: tile passes are flattened before the composite, the
composite runs on the render thread, and with GPU compositing on it reaches the
card (confirmed 2026-08-12, 310 of 310 publishes). So moving the paused canvas
onto the tiled route does not move it off the GPU. B167 phase 5 (resident tiles
instead of flattens) stays blocked on its own measurement and is not needed here.

## Phases

Each lands alone, behind the tests named, and changes nothing an artist sees.

1. **Landed as: tiles until the frame on screen has its stills, and no stills
   for frames off screen.**
   - Arriving at a frame (a step, a jump, a stop) shows it from level-0 tiles at
     once, rather than rendering its stills on the UI thread. Its stills are
     warmed in the background, because the next stroke needs them: the ring and
     the layer-stack bake compose from them.
   - When they arrive, one publish goes through the still route and bakes the
     stacks there and then (`LayerStackBake.Eager`). This is where the plan
     changed. Shown from tiles all the way to pen-down, the first dab rebuilt the
     ring and the bake: 3.7 → 47 ms at 1080p and 11 → 186 ms at 4K, with seven
     layers. That is the worst place for it.
   - Every paused publish drops the stills of drawings the tiles already serve,
     except the ones on screen (`FrameBitmapCache.DropWhere`). Drawings that
     cannot tile keep theirs, or the idle warm would remake them on every publish
     (B408's loop).
   - Stop keeps Q218's hold. What it settles on is now this route.
   - **Gate:** the paused tiled composite against the still route at 100%, 50%,
     33% and 25%, onion on and off, gives zero differing values
     (`PausedOnTilesTests`).
   - **Measured:**

     | | arriving at a frame | first dab after idle |
     |---|---|---|
     | 1080p, before | 168 ms | 3.8 ms |
     | 1080p, after | 6.3 ms | 3.9 ms |
     | 4K, before | 470 ms | 11.7 ms |
     | 4K, after | 11.2 ms | 12.0 ms |

     Seven layers, local probe. Memory is measured by the lab's
     `memory-session`, A/B.
   - **A stroke begun before the stills arrive** waits for the warm rendering
     them (`JoinStillsForStroke`), rather than cancelling it and rendering them
     itself one by one. Perf-warden measured that version at 271–367 ms at
     pen-down at 1080p, against main's ~90. What is left is a single moment,
     which a sweep of the gap between arriving and pen-down shows (seven
     layers, minimum and median of four):

     | pen-down after arriving | 0 ms | 100 ms | 250 ms | 500 ms | 1 s |
     |---|---|---|---|---|---|
     | 1080p | 238 / 363 | 4.9 / 5.0 | 4.9 / 5.2 | 4.3 / 4.7 | 4.1 / 5.5 |
     | 4K | 483 / 681 | 44 / 55 | 38 / 48 | 46 / 56 | 39 / 53 |

     On main the UI thread is frozen for about 210 ms on arriving at 1080p, so
     no stroke can start sooner than that, and then pen-down costs about 90 ms
     whatever the gap. A pen that lands in the same instant as the frame pays
     roughly what main paid in total (arrival plus pen-down), and from 100 ms on
     both moments are cheaper. That instant is not fixable here: the warm's
     in-flight tile renders hold the cores while the ring and the layer-stack
     bake are rebuilt.
2. **Readers stop materialising stills** for frames that have tiles: one pixel
   (colour pick, brush ring), a region (eraser probe, flood fill's region, the
   effect brush's backdrop) and undo pixels. `TileStore` reads a pixel or copies
   a region from the tiles it has; a region with no tile is transparent, which is
   what the still holds there.
3. **The drawing path** (`ComposeRing`, `LayerStackBake`) reads the layers around
   a stroke from tiles for the dirty region only. B121's numbers are the budget:
   the ring is cheap because it patches a region, and it must stay so.
4. **Whole-picture readers** (export, MCP `render_frame`, the Navigator,
   thumbnails) compose a temporary picture from tiles and do not keep it.
5. **Narrow the exclusions** one at a time, each its own branch: layer masks and
   clipping, live effects, effect strokes, placements, bound strokes, camera,
   baseline (`TileFallback.Reason`). Until each lands, those frames keep the still
   route, which is correct, only larger.

## What it costs

- **Zoomed out on a large canvas, paused:** a level-0 flatten of the visible
  region per changed layer, where the still route already had the bitmap. A flatten
  copies tiles; it does not replay strokes, so it is cheaper than the still it
  replaces when that still was not cached.
- **More small textures** if B167 phase 5 ever lands; not part of this.
- **Two routes stay** until phase 5 here has narrowed the exclusions. The still
  cache shrinks to what the excluded frames and the drawing path ask for.

## How it is known to work

- The identity gate in phase 1, and the existing tile pixel tests.
- The lab's `memory-session`, A/B after phase 1 and again after phase 2.
- `StopOnTilesTests` changes meaning in phase 1: there is nothing to swap to.
