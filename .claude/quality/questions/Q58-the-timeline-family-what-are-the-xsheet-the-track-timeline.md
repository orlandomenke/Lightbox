# Q58 · The timeline family: what are the Xsheet, the track Timeline and the Graph Editor in v1? — **answered, all recommendations taken, 2026-08-08**

Raised when the owner asked for the reference's timeline (its strip reads
*Timeline | Xsheet | Dope Sheet | Graph Editor*) plus "2 more dockers
complimenting each other, xsheet (i presume this is what we have today), and
graph editor", with field research requested (TVPaint, Toon Boom, OpenToonz,
and the general dope-sheet/graph-editor vocabulary). Asked with the question
prompt; all four answers took the recommendation.

### The answers

- **Xsheet = today's horizontal grid, re-hosted.** One row per layer, one cell
  per frame, holds, timing presets — already an exposure sheet laid sideways,
  and the owner presumed as much. A classic vertical sheet is a later
  orientation toggle, not a second implementation. OpenToonz-style cell marks
  and drag-fill cycles join the queue rather than v1.
- **The track Timeline ships editable.** One track per layer, drawings as
  dots, holds as bars, the camera as its own track, per-track colours as the
  reference draws them — and the dots drag to retime from day one, because a
  timeline you can see and not touch reads as broken.
- **Graph editor v1 = camera curves + hold easing + the spacing graph.** The
  conventional half is what the field has (transform curves with handles and
  interpolation presets). The spacing graph is the differentiator no
  competitor has: because the stroke record is the document, Lightbox can
  MEASURE how far the drawings actually move between frames and plot the true
  spacing of the animation — the pencil-era spacing chart, derived from the
  art. The AI inbetweener fills toward it.
- **Adopt next: audio + timing ladders.** An audio track with a waveform and
  scrubbed playback is the single biggest gap against every competitor;
  timing ladders (the chart on an extreme naming where the inbetweens sit)
  are the classic tool nobody ships as a first-class object, and the natural
  input to the inbetweener. Shift-and-trace and cycle drag-fill stay on the
  list, unscheduled.

### What did not need deciding

The dope sheet. The reference names one, but a dope sheet is keyframes by row
with timing and no values — between our Xsheet and the track view there is no
job left for it to do. If one earns its way in later it is a view over the
same records, not a fourth store.
