# Q300 · Linking a Unity project: open its clips, redraw them, write them back in place — **answered 2026-11-27: linked folder, any sprite clip, reference layer, overwrite in place**

Raised by: the owner, replacing placeholder animations in a Unity game with
their own. The placeholders are generated sprite sheets; the owner wants to pick
a character's clips in Lightbox, get every frame on the timeline, add layers
over them, and have the result land back in the game. Design:
`docs/DESIGN-unity-link.md`.

What it blocks: the whole feature. Four decisions, asked together.

| Decision | Chosen | What it costs |
| --- | --- | --- |
| Where the result goes | **Overwrite the sheet in place** (recommended) | Lightbox writes into a file it does not own. The clip, controller and prefabs keep working with no Unity edit, because the sprite names and ids are kept; the price is that a bad write damages a game asset, which is why write-back verifies itself (below). Version control in the game project holds the placeholder. |
| | A new sheet beside it | Nothing overwritten, but every clip rewired by hand in Unity. |
| | Export folder only | What the Unity export preset already does; no link at all. |
| How the placeholder appears | **A locked, dimmed reference layer, pinned out of export** (recommended) | Tracing only; touching up the placeholder's own pixels means unlocking it, and then it is exported like any layer. |
| | Editable pixels | Quicker touch-ups; placeholder ink mixed into the final frames. |
| Which clips are readable | **Any sprite-swap `.anim`** (recommended) | Reading Unity YAML and `.meta` slicing, not just the generator's `.json`. Real per-key timing and hand-sliced atlases come with it. |
| | The generator's PNG + JSON pairs only | Simple, but the hand-drawn clips stay out of reach. |
| How the link is made | **A linked folder on the Lightbox project** (recommended) | One more optional field on the manifest, absent until set. |
| | A one-off import dialog | Nothing remembered; write-back must ask where, every time. |

**The owner's condition on the scope answer, and it is load-bearing:** a clip
can point at one sprite of a many-sprite sheet or at a single-sprite PNG. On a
sheet, write-back must split it so each frame overwrites exactly its own sprite
and nothing else — "that might need additional visual checks". Surveyed the
same day in the game project: one character's dozen clips share **one**
atlas of a few dozen sprites, with hand-sliced cells of different sizes, so this is the common case, not
the edge one. The design answers it with a write that touches only the clip's
own rects, a pixel diff proving every byte outside them is unchanged, and a
before/after strip the artist confirms before anything is written.
