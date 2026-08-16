# Q107 · Resize cursors: the platform's eight, or drawn at the real angle? — **answered 2026-08-16: drawn, at the exact angle**

Asked during **B241**, which gave a cursor to every grabbable thing on the
canvas. Most of them are stock — a four-way arrow for a move, a hand for a pan —
and one is not: a handle that resizes has a *direction*, and the transform box
can be turned to any angle, on a canvas that can itself be rotated and mirrored.

| | What it costs |
| --- | --- |
| **Drawn at the exact angle** (**chosen**, against the recommendation) | Our bitmaps on every platform. |
| **The platform's eight** (recommended) | Wrong by up to 22.5° whenever the box or the view is off a 45° step. |
| **Stock, plus a drawn one only past some rotation** | Both costs and a seam: the cursor changes *character* mid-drag. |

`StandardCursorType` offers `SizeWestEast`, `SizeNorthSouth` and four corner
arrows — 45° steps. That is exact for an unrotated box and for the four corners
of one, which is the overwhelmingly common case, and it inherits the user's
cursor theme for free.

**The owner chose drawn, and the reason is that the common case is not the one
that needs help.** An artist who has rotated the box knows they have rotated it;
what they cannot tell without looking is *which way this particular handle
travels* — and that is precisely the case where the stock set is at its worst.
A cursor that is right when you do not need it and off by 22° when you do is
inverted.

## What it costs, recorded because it was the argument against

These are our bitmaps, and that is not free:

- **No system cursor theme.** A user who has chosen a large, high-contrast or
  themed pointer set gets ours for these handles, and only for these handles.
- **No accessibility cursor size.** A cursor-size setting scales the platform's
  cursors and not a bitmap we hand it. 32×32 is what everyone gets.
- **The hotspot is ours to keep correct.** A drawn cursor whose visible centre
  and acting point disagree is worse than a plain crosshair, and nothing but a
  test will catch it drifting.

The mitigations are the ones `Badged` already established one cursor along, and
they are reused rather than reinvented: the same halo-under-dark double pass, so
the arrow survives any drawing beneath it; the same hotspot as every other canvas
cursor; and the same catch-and-fall-back to a stock cursor, because a pointer is
the one thing that must never take the window down. A headless run has no render
surface, falls into that catch, and gets the four-way arrow — which is why the
tests assert against the *choice* rather than against the bitmap.

**Cached per whole degree, keyed mod 180.** A double-headed arrow has no front,
so 200° is the arrow at 20°: at most 180 entries of 32×32, and a rotation drag
re-uses one the moment the angle repeats. Rendering a bitmap per pointer move is
exactly the per-event cost invariant 6 rules out.

## What this did not decide

**Nothing about the rotate cursor**, which was a separate call in the same
conversation and went the same way for a different reason: `StandardCursorType`
has no rotate *at all*, so the choice there was between drawing an arc and going
on using `Hand` — which reads as "grab and pan" on every platform where a hand
means exactly that, and is the one thing a turn handle must not be confused
with. There was no stock option to weigh.

**Nothing about the brush ring**, which is drawn by the render op at the brush's
real size and is not a cursor at all.
