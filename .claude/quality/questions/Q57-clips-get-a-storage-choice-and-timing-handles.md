# Q57 · Clips get a storage choice and timing handles — **answered 2026-08-08, queued behind the design-round-3 PR**

Raised when the owner queued the follow-up to Q56's video work: a choice
between referencing footage and storing it in the file ("the former requires
the user to have compositing software... I want to enable other users to also
use this rudimentary"), the imported video visible in the timeline with
handlers for timing, and — perhaps — cutting and rearranging sections of both
audio and video. Asked with the question prompt.

### The answers

- **Two purposes, each with its own storage.** The owner's own words: "2
  paths. One reference 2 small production." A **reference** import may embed
  the extracted contact-sheet frames (reference quality, capped — the same
  240-frame/480px extraction, stored the way image references already store),
  or stay by-path as shipped. A **small-production** import embeds the
  **original video bytes** — full fidelity, re-extractable, for the user whose
  whole pipeline is Lightbox. The cost asymmetry is deliberate: a reference is
  a drawing aid and pays reference prices; production footage is material and
  pays material prices.
- **Audio gets the same reference-or-embed choice** (recommendation taken).
  Same rationale: a self-contained file survives being shared without the WAV
  beside it. Reference stays the default; embedding warns past ~10 MB.
- **Timing handles live in the Timeline docker** (recommendation taken).
  Audio and video each get a clip bar in the track timeline: drag the body to
  slide, drag an end to trim in/out. The X-sheet stays a drawing grid.
- **Slide + trim this round; split-and-rearrange next** (recommendation
  taken). The model is a segment list from day one so "split at playhead" and
  reordering land later without a migration.

### Answered in the same exchange

**Small-production footage exports.** Asked whether embedded production
footage stays draw-against-only, the owner answered "yeah also export for
small production" — so the production path composites into the render
pipeline's output, unlike references, which never reach an exported pixel.
That difference is the line between the two paths: a reference is a drawing
aid, production footage is material.
