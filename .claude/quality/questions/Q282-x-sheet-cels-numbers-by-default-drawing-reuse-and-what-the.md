# Q282 · X-sheet cels: numbers by default, drawing reuse, and what the sheet adds — **answered 2026-10-16, all recommendations taken**

Raised by the owner, 2026-10-16, with screenshots of Animate's timeline,
Photoshop's and Krita's: *"I am leaning towards the dots instead of thumbnails
so we do not have the performance challenge"*, asking for field research on the
X-sheet and for a sketch in the gallery before anything is built. Asked with the
question prompt after the sketch (`X-sheet · cels`, `· additions`, `· vertical`
in `tools/Lightbox.Gallery`) was rendered and shown.

What it blocks: any change to how the X-sheet draws a cel.

## The answers

1. **A cel shows the drawing's number by default, with dots as a per-sheet
   toggle.** Recommended because the field's dense frame-by-frame tools
   (Harmony, OpenToonz/Tahoma2D, TVPaint 12.1's Timesheet) all write the
   drawing's identity where it lands and a line along its hold, and it is the
   only one of the three styles in which a cycle reads as *1 2 3 4 1 2 3 4*.
   Dots — the Timeline docker's language — were the alternative, and cost
   exactly that: the sheet could not say which drawing is which. Pictures per
   cel stay as they are only until the replacement lands.
2. **One drawing may be exposed in several cels — true cycles and
   substitution — as its own roadmap item.** Today `Cel.Frame` owns its
   drawing and `Cel.Clone` copies it, so a walk cycle exposed three times is
   twelve drawings. Without reuse the numbers in (1) would be a running count,
   and a repeat would get a new number. It is a change to the record, so it is
   costed and questioned on its own branch, under the optional-settings rule (a
   document that reuses nothing writes nothing new).
3. **On the roadmap, all four additions offered:**
   - **One picture, on hover**, in place of a picture per cel, with the sheet
     drawn as one control the way `TrackView` is — not a `Button` and a
     `ContextMenu` per cel in a non-virtualised `StackPanel`, which is today's
     shape and is as much of the cost as the thumbnails are. *Not yet
     measured;* the lab profiles the two before the change is judged.
   - **AI inbetween requests on the sheet**: the slots between two keys an
     artist asks the inbetweener to fill (dashed), and the drafts it returns
     waiting for review. TVPaint's To Do/Done inbetween is the nearest thing
     shipped; nothing makes the request itself a mark on the sheet.
   - **Pipeline stage per drawing** (rough, clean, colour) as a hairline under
     its hold — missing almost everywhere; Harmony's retake marks are the
     nearest.
   - **The paper's dropped columns**: a dialogue lane (the scratch track's
     waveform with its sounds broken down) and the vertical, frames-down
     orientation with Action and Dialogue columns, a rule every 8 frames. Q58
     already settled vertical as an orientation of the same sheet. The onion
     range tinted on the sheet rides along, being cheap.

## What did not need deciding

- **The Timeline docker keeps its dots.** It is the track view; the X-sheet is
  the exposure sheet (Q58). Numbers belong to the sheet.
- **Ripple retime, 1s/2s/3s on a selection, markers, notes, timing ladders,
  the spacing graph** — the research lists these as gaps elsewhere, and
  Lightbox already has them (timing presets and Re-time, `FrameMarker` notes,
  `TimingChartView`, the graph editor). Not re-proposed.
- **An unused-drawings view** waits on (2): without reuse every drawing is
  exposed exactly where it was made.
