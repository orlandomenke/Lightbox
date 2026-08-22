# Q148 · Do layer masks animate? — **answered 2026-08-22: not in v1 — a static drawing per layer; the animated case is a clipping mask**

Photoshop's mask is per-layer and static because Photoshop has no timeline.
Here every layer is a cel stack, so the obvious generalisation — the mask gets
its own cel list with the exposure model — was on the table.

| | What it costs |
| --- | --- |
| **One static mask drawing per layer** (recommended, **chosen**) | Covers the Photoshop use (vignette a layer, knock a shape out of it) with no new timeline machinery: no mask cels, no mask exposure column, no mask hold semantics, nothing new for the sheet, playback, or push-across to learn. The animated case is *not lost* — it is a clipping-mask arrangement, where the matte is an ordinary layer that animates with all the existing cel machinery and the masked layer clips to it. |
| A mask with its own cels | Strictly more powerful on one layer, but it duplicates hold/exposure logic inside the mask and every timeline surface has to grow a second lane now, for a case clipping already covers. |

The record is shaped so the stronger form is an addition rather than a
migration: the mask block is nullable and absent until authored, so per-frame
masks later mean a new optional field inside it, not a re-shape of what
existing documents already wrote.
