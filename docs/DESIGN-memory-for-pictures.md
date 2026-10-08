# One memory limit for pictures

The owner asked to shrink an open document's memory, and chose one overall limit
first (Q221). This note says why the caches add up to so much, what the limit is,
and how six caches that each manage themselves give memory back under it.

## What was measured

The perf lab's `memory-session` (#620) uses the owner-shaped document on a 32 GB
machine. At the end of an ordinary session the process held 3.1 GB:

| | end of session | its own cap here |
|---|---|---|
| still images (`FrameBitmapCache`) | 612 MB | 4 GB (1/8 of RAM) |
| layer textures (`LayerTextureCache`, GPU) | ~1 GB, inferred | 1 GB (1/16) |
| flatten cache (`TileFlattenCache`) | 491 MB | 512 MB (1/64) |
| playback tiles (`TileFrameCache`) | 172 MB | 1 GB (1/32) |
| compose cache (`ComposeCache`) | 92 MB | 1 GB (1/12) |
| undo pixels (`MarkSnapshot`) | 0 here | 512 MB (1/64) |

**Each cap was sized on its own merits and nothing sums them.** Their fractions
add up to a third of RAM: 10.7 GB here and 2.7 GB on the 8 GB minimum spec. Each
cache also evicts only its own entries, and only when something new arrives.

## The limit

**Memory for pictures: 1/8 of RAM by default.** That is 4 GB on a 32 GB machine
and 1 GB on the minimum spec. The artist may set it between 512 MB and half the
machine (at most 16 GB). A figure from the settings file is held to that range on
load, since one set on a bigger machine arrives unchanged. It is one setting, in Configure → Performance, and it
replaces the still-image "frame cache budget" (Q221). That figure was never
saved, so nothing carries over; the new one is saved only once the artist sets it. Undo's *document copies* are not pictures; they are the
next piece of work, not this one.

## How the caches give memory back

**The four UI-thread stores are brokered.** Still images, playback tiles,
flattened views and undo pixels each implement `IPictureStore`:

- `Bytes`: what they hold.
- `OldestEvictable`: when their least recently used entry that may go was last
  used, on one shared clock (`PictureMemory.Clock`), or null if nothing may go.
  Pinned entries, entries in use, and a store's own minimum (the still cache keeps
  6 frames) never count as evictable.
- `EvictOldest()`: drop that entry, and return the bytes freed.

Each is already LRU within itself, so the oldest entry overall is always one
store's oldest. `PictureMemory.Enforce()` runs on the UI thread after a publish.
While the total is over the limit, it evicts from whichever store's oldest entry
is the oldest, and it stops when nothing more may go.

**The two render-thread caches get a slice each.** Layer textures and finished
frames live on the render thread, where the UI thread cannot evict safely. Their
budgets become fixed slices of the limit: an eighth each, which is 512 MB on this
machine and 128 MB on the minimum spec, against 1 GB each before. The brokered
stores share the remaining three quarters (`PictureMemory.Brokered`), so the six
together stay within the limit. The still cache's own cap is set to that share:
which store gives way is the broker's call, not a fixed split.

**Playback is left alone.** While playing, the still and tile caches evict the
*most* recent entry on purpose (B28: an LRU thrashes a loop longer than the cache).
A global LRU would undo that. During play each cache keeps to its own cap, and the
broker catches up at Stop. Between Play and Stop the total can exceed the limit by
whatever playback brought in.

## What it costs

- **More re-rendering on scenes bigger than the limit.** That is the point: before,
  the same scene simply used more memory.
- **One pass per publish while over the limit.** It is a few comparisons per store,
  and it does nothing when under the limit.

## How it is known to work

- `PictureMemoryTests` covers the broker against fake stores: oldest-first across
  stores, pinned and floor entries kept, stops when nothing may go.
- Each store's `IPictureStore` is tested against its pins and floors.
- `memory-session` is re-run with a limit below what the session used: process
  memory follows the limit, and the session still completes.
