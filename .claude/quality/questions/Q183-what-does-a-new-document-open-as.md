# Q183 · What does a new document open as

Raised by: the owner, asking for the File → New defaults to change — 120 or
180 ppi instead of 72, and mid grey paper instead of white.

What it blocks: the defaults in `NewDocumentPanel`, shared by File → New and
the start screen.

**Recommendation:** 180 ppi, and one grey for every project type.

Two halves, asked together because they are one form.

## Resolution: 180 ppi

**Answered: 180.**

PPI is metadata today — it decides only what physical size a document *claims*,
and changing it moves no pixel. So the question is which claim is least
misleading:

| ppi | What 1920 × 1080 claims to be |
| --- | --- |
| 72 | 26.7 × 15 in — a poster nobody asked for |
| 120 | 16 × 9 in — a broadsheet |
| **180** | **10.7 × 6 in — a page** |
| 300 | 6.4 × 3.6 in |

72 is the screen convention of a generation ago and is a number nothing prints
at. 120 is closer to what a screen actually is (96), but reads as neither a
screen number nor a print number. 180 is a real print resolution and puts an HD
canvas at a plausible sketchbook page.

**What the alternative cost:** 120 would have made the claimed size nearer to
what you see on screen, which matters to nobody while PPI is metadata and stops
mattering the moment print sizing arrives.

## Paper: mid grey, for every type

**Answered: grey everywhere.** This went the way the recommendation did, and the
alternative was live: make the default follow the *For* box — grey for
Illustration and None, white for Animation, Storyboard and Comic — which is
exactly the shape `CLAUDE.md`'s "a project type sets defaults, never
availability" endorses.

It was not taken, for one reason: the Background field would then change under
you while you are filling the form in. A field that silently rewrites itself
when you touch an unrelated combo box is worse than a default that is
occasionally wrong, because the wrong default is *visible* and one keystroke
from being right.

**The tension this leaves, stated rather than hidden.** Frame-by-frame
animation is the first of the two first-class purposes, and line tests are drawn
on white paper. An animator now types `#ffffff` — or presses **Transparent** —
on every new document. If that becomes an irritation the fix is a *preference*,
not a project-type rule: the Configure window already carries "New grids", "New
vanishing points" and "New character height scales", and "New documents" would
sit beside them. That is a separate objective and is not built.

**Why `#808080` and not `#bcbcbc`.** "Half black, half white" is 128/255 — mid
grey by *value*, which is what an artist means by 50% grey and what a 50%-grey
fill gives them in every other tool. Mid grey by *luminance* is around 188, and
is a defensible-sounding correction that would hand over a visibly lighter
paper. `FiftyPercentGreyIs128PerChannel` pins the number against the parsed
channels, so it cannot pass by comparing a typo with itself.

## What was not changed, on purpose

`Scene.DefaultBackgroundColor` and `Scene.Ppi` — the *document model* defaults —
stay at `#ffffff` and 72. Moving those would retroactively repaint and re-label
every saved file that omitted the key, which is invariant 4's territory: a
preference must never alter existing art. The defaults that moved are the
dialog's.
