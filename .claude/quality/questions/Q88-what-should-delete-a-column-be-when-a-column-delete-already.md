# Q88 · What should "delete a column" be, when a column delete already exists? — **answered 2026-08-14: expose the existing one properly**

Raised by the owner: *"we should also have a command for deleting a column at
once in the timeline."*

The capability is already there and unreachable, which is why it reads as
missing. `DocumentEditor.DeleteFrame` removes the frame from **every** layer
and ripples the rest back — a column delete in full — and the view model wraps
it as `DeleteFrameCommand`. It is exposed as one 🗑 button on the timeline bar
and **is not in `ShortcutMap`**, so it cannot be bound, searched or found. The
X-sheet's own right-click *delete* is the row-scoped one (that layer's cels,
pulling the row back), which is the one an artist finds first — hence the
impression that only a row delete exists.

**Answered: expose the existing one properly** — register it in `ShortcutMap`
so it is bindable and searchable, and add it to the X-sheet's right-click menu
as *Delete column*, worded to separate it from the row-scoped delete beside it.

No new behaviour, which is the point: the alternatives offered were a
non-rippling *clear the column in place* variant and a selection-driven
multi-column delete. Both are defensible features and neither is what was
missing here; adding one would have shipped a second thing to learn alongside
the fix for the first. They stay unbuilt until asked for.

---
