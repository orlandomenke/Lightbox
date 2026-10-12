# Q250 · Where are the shortcut bar's snap and camera-view switches remembered? — **answered 2026-10-12: both in the app settings**

Raised by: the owner's report, 2026-10-12 — *"Shortcut bar's states are not saved on
save. Giving me quite some annoyances. In this case specifically with the snap to
guides."* (**B455**.) "Snap to guides" came back on at every launch, and "View
through the camera" came back off.

What it blocked: B455. The report says *on save*, which reads as the document; the
code's own comment on `SnapToGuides` said *settings*, and nothing implemented
either.

Two questions, asked together with `AskUserQuestion`.

## Snap to guides: where?

| | What it costs |
| --- | --- |
| **App setting** (recommended, **chosen**) | One switch for every document: off in one is off in all. Sticks for a document that is never saved. |
| **In the document** | Each file remembers its own, written only when off. Toggling it dirties the document, and it is lost without a save. |

The switches beside it on the bar already live outside the file — show guides,
lock guides and lock references in the workspace layout — so this puts the bar on
one footing. It is also what the property's comment had promised since it was
written: the guides are authored and saved, whether you are drawing against them
this minute is how you are working.

## View through the camera: remember it at all?

| | What it costs |
| --- | --- |
| **No** (recommended) | It stays a peek: every launch opens on the world you draw in. |
| **Yes, the same way as snap** (**chosen**) | A launch can open looking through the camera, with the world outside the frame out of sight until the switch is turned off. |

**Decided against the recommendation.** The owner wants the bar to come back the
way it was left, this switch included. It is an app setting like the other, which
is safe for a document with no camera: with nothing to look through the switch
does nothing and its button is absent.

## What this did not answer

**Reference-grid edit mode** is also a toggle on the bar and is still not
remembered. It is a mode you leave with Esc rather than a way of working, and
nobody asked for it; it was left out of the question on purpose.
