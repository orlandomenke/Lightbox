# Q154 · How far does the bevel go? — **answered 2026-08-23: smooth inner/outer bevel only**

Asked alongside Q153. Photoshop's Bevel & Emboss is the deepest style it has:
five modes (inner, outer, emboss, pillow, stroke emboss), a technique picker, a
contour curve, a gloss contour, and per-light shading controls. How much of
that does the v1 style take?

| | What it costs |
| --- | --- |
| **Simple smooth bevel** (recommended, **chosen**) | No contour curves, no gloss, no pillow/emboss modes — an artist wanting those waits. |
| **The fuller Photoshop set** | Considerably more machinery and UI for options an animator rarely reaches for; the fidelity-over-expression trap the charter names — and a contour *curve editor* built ahead of the timeline's curve editor, which is the same widget. |

The chosen surface: **direction** (inner/outer), **depth**, **size**, **light
angle**, and highlight/shadow colours. That is the part of a bevel that makes a
mark say something — a cel flat reading as raised, a title with an edge — and
it survives being replayed across two hundred frames because it is a pure
function of the silhouette and five numbers.

The contour and gloss editors are deliberately deferred, not refused: they are
curve editors, and the timeline's curve-editing work (the same dependency
Q153 notes for colour keys, and the manual's *Planned* marker for keyed effect
parameters) is where a curve widget enters the app once, for everything that
needs one.
