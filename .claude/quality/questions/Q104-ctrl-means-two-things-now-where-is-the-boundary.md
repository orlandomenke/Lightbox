# Q104 · Ctrl means two things now — where is the boundary? — **answered 2026-08-16: place decides, and the narrower claim wins**

Asked when the owner asked for *"when selecting and pressing ctrl and hovering
on a selected area (and during) enable moving"*. The obstacle is that Ctrl is
already spoken for: it is the **held eyedropper** for the brush, the eraser and
the fill, on the canvas's own stated grounds that *"the colour you want is
almost always already on the canvas, and reaching for a tool to fetch it breaks
the stroke you were about to make."*

So something had to give.

| | What it costs |
| --- | --- |
| **Move wins inside the marquee, pick everywhere else** (recommended, **chosen**) | An artist reaching for a colour that happens to lie inside their own selection gets a move instead. |
| **Pick always wins; another key moves** | Nothing an artist knows changes — and it is not what was asked for, and Photoshop trains Ctrl-to-move. |
| **Move wins inside, but only for tools that cannot pick** | No gesture is ever displaced, and the feature is absent exactly when it is most useful: mid-stroke, wanting to nudge what you just selected. |

**The narrower claim wins, and that is the whole rule.** Ctrl-to-move needs a
selection to exist *and* the pointer to be inside it; the eyedropper needs
neither. So the move is asked first and keeps a small, deliberate region, and the
picker keeps everything else — which is nearly all of the canvas, nearly all of
the time, since most documents have no marquee up at all.

**This is only defensible because the pointer says which one it is.** A modifier
that quietly means two things in two places is a trap; the same modifier, with
the cursor changing to the four-way arrow the moment it crosses into the
selection, is a discovery. B228 landed one commit earlier and is what makes it
so — including *during* the drag, and including the case where the hand is still
and only the key moves.

## What moves, and why it is not the pixels

**The strokes the marquee contains**, through the ordinary transform session:
`BeginSelectionMove` is three lines and every one of them is `BeginLineMove`'s,
because a second way to drag artwork about would be a second set of bugs — its
own undo step, its own snapping, its own idea of what Shift means. The filter is
`DerivedTransformFilter`, which already owns the answer to "what does this
marquee contain".

Moving the **pixels** instead — Photoshop's floating selection — was the other
candidate and is a much larger piece of work: the marquee would cut, and what
travels would have to become strokes again on release or invariant 1 stops
holding. Worth doing one day; not a modifier's worth of work.

## Two things that came free, and are the reason for reusing rather than adding

- **The marching ants follow the move**, because `SetSelectionPreviewTransform`
  already exists and the ants already apply the session's preview matrix.
- **The press, the drag and the release ride the line drag's own channel** — the
  same commit, and the same discard of a press that went nowhere so a Ctrl-click
  that selected nothing leaves no identity move in the history.

Neither was built for this. Finding them is the answer to the question that
started this whole run: *"sometimes tools have their own systems instead of
reusing existing ones."*

## What this did not decide

**Nothing about Alt**, which duplicates-on-drag in most applications an artist
will have come from. It is free over the marquee tools and it is a separate ask.

**Nothing about a selection with no strokes in it** — a marquee over bare canvas
Ctrl-drags nothing and says "Nothing to transform in this scope", which is the
existing session's answer rather than a new one. Whether it should instead
refuse before opening is a question about that message, not about this modifier.
