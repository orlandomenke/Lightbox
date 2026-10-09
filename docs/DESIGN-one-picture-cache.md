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

## Phase 1 was tried and withdrawn (Q223, 2026-10-08)

Phase 1 was built (#632) and closed after the lab measured it in the real app.

- **Exact:** the paused tiled canvas was the still route's bytes at every zoom,
  so the identity gate held.
- **Process memory barely moved.** On the owner-shaped document the still cache
  fell from 620 to 252 MB, but process memory went from 2.53–2.63 GB to only
  2.38–2.50 GB.
- **The app answered worse.** Pressing Play went from 200 ms to 2.7 s, Stop
  from 3.6 to 7.7 s, and jumping to a frame from 290 ms to 4.3 s before the
  fix for cold frames.
- **Best explanation:** dropping the stills of frames off screen makes Play,
  Stop and jumps re-render what main kept.
- **Why the probes missed it:** the local probes never reproduced that, because
  their frames always arrived with playback tiles in hand.

The phases below are kept as the record of the plan. Do not resume them before:

- the remaining process memory is accounted for (Q223's chosen next step);
- a probe reproduces the lab's jump, Play and Stop scenarios on a cold cache.

## Phases

Each lands alone, behind the tests named, and changes nothing an artist sees.

1. **The paused canvas composes from level-0 tiles** for every layer the tiled
   route can draw (`TileFrameCache.CanTileFrame`), whenever no stroke is in
   progress. Stop no longer needs to swap: the held tiles *are* the paused canvas,
   so `_holdTilesAfterStop` and its wait for stills retire. Stills are then asked
   for only by the readers below, the drawing path and the frames that cannot
   tile.
   - Gate: a pixel test comparing the paused tiled composite with the still route
     at 100%, 50%, 33% and 25%, onion on and off — **zero differing values**.
   - Measure: the lab's `memory-session`, A/B, still cache bytes at the end.
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
