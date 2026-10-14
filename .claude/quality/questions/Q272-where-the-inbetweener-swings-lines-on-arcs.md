# Q272 · Where the inbetweener swings lines on arcs — **answered 2026-10-14**

**Answered: every inbetween.** ＋ Inbetween swings each line about its joint,
matched gap or not. Asked with `AskUserQuestion`, answered as recommended.

Raised on 2026-10-14 as the next step the owner chose after Match lines (Q271's
order: matching, then arcs, then breakdowns, then AI helpers). The engine moved
every point in a straight line from key A to key B, so a limb swinging between
the keys shrank halfway — the chord of an arc is shorter than the arc — and the
motion read as sliding. The fix is for each line to turn about the end that
moves least (the hip for a thigh, the knee for a shin) while that end travels,
with any change of shape between the keys blended on top: at the keys it is
exactly the artist's drawing, between them the line keeps its length.

What it blocks: inbetweens that keep a limb's length and read as a swing.

## What was asked, and what was answered

**Where arcs apply.** *Every inbetween* (recommended): **chosen**. Better motion
everywhere; existing inbetween frames do not change, but redrawing or
re-inbetweening an old gap gives a slightly different, arced, result. Against:
*only gaps with matches* (safer for existing work, but most inbetweens keep the
straight-line shrink) and *a switch on the inbetween bar* (most control, one
more control to learn and a setting to store per gap).

## What it costs

- A deliberate change of output for ＋ Inbetween on any gap where a line turns;
  a gap where lines only move or bend in place is unchanged.
- The joint is a blend of the line's two ends and its middle, weighted by how
  little each moves (a still hip wins, a still middle wins; ends that move
  alike put it between them). Picking "the end that moves least" outright was
  tried first and failed review: the joint flipped on a pixel, and a line
  turning about its middle drifted 20px.
- Lines that meet at both keys are welded so they meet between them — a
  thigh's and a shin's knee ends travel on two different arcs otherwise and
  parted 10px (the art-director).
- **Known limits, not fixed:** a turn of more than half a circle (a windmilling
  arm, a wheel) takes the short way round, because each pair of keys is judged
  alone; and a line that both turns a lot and changes shape a lot can bulge,
  because the change of shape is blended on top of the turn.
