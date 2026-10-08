# Q221 · One overall memory limit for pictures, replacing the frame cache budget — **answered 2026-10-08**

**Answered: one "Memory for pictures" setting**, as recommended. It replaces the
still-image *Frame cache* figure in Configure ▸ Performance. Every picture
cache answers to it, and the pictures used longest ago make room first,
whichever cache holds them.

Raised by the owner's question "can we shrink open file sizes?", answered as
*memory while open*. The perf lab's `memory-session` (#620) measured 3.1 GB at
the end of an ordinary session on the owner-shaped document. Six picture caches
each had a cap sized on its own merits, and the caps added up to a third of RAM,
10.7 GB on the owner's machine. Nothing summed them, and each evicted only its
own entries. The full table is in `docs/DESIGN-memory-for-pictures.md`.

The owner picked four directions in one prompt. This question is the first of
them; the others are separate pieces of work:

- one overall limit (this);
- undo stores only what changed;
- smaller points in memory;
- still images stored sparsely, to be raised again later.

**Recommendation was:** one setting, with the per-cache caps kept underneath it
as safety limits. The alternatives cost:

- **One figure per cache, all six shown.** The artist would be asked to tune six
  numbers whose interactions they cannot see. This was the old shape, with five
  of the six hidden.
- **No setting, derived only.** Nobody could give memory back to another
  application on the same machine. The old setting existed for that reason.

## Implementation choices inside the answer

- **The default is 1/8 of RAM**, between 512 MB and 16 GB. That is 4 GB on the
  owner's machine and 1 GB on the 8 GB minimum spec. The figure is saved only
  once the artist sets it, so a settings file carried to another computer does
  not carry this machine's size. The old figure was never saved.
- **The artist may set 512 MB up to half the machine**, at most 16 GB
  (`MemoryBudget.PicturesCeiling`). A fixed 16 GB ceiling would let an 8 GB
  laptop be offered twice its memory, which idle warming would then try to
  fill. A saved figure is held to that range on load (`ClampPictures`), since
  the settings file is input and one set on a bigger machine arrives unchanged.
  This came from review, not from the owner; it bounds the choice without
  changing it.
- **The four UI-thread stores are brokered** (`IPictureStore`, `PictureMemory`):
  still images, playback tiles, flattened views and undo pixels. They share
  three quarters of the limit (`PictureMemory.Brokered`). After each publish
  the oldest picture across them goes first. A store's pins, entries in use, and
  floor are never offered.
- **The two render-thread caches take a slice each** of an eighth
  (`PictureMemory.RenderSlice`): layer textures and finished frames. The UI
  thread cannot evict them safely.
- **Playback is not brokered.** During play the still and tile caches evict the
  *most* recent entry on purpose (B28), and a global least-recently-used pass
  would undo that. The broker catches up at Stop.
- **Idle warming stops at the brokered share.** Warming past it would have the
  broker take back what the warm had just made, on every publish.
