# Q274 · What the AI gives an artist for an inbetween: a pose guide to draw over — **answered 2026-10-14**

**Answered: a construction guide, not a drawing.** Claude reads the pose of the
subject in both keys — the joints of each part it sees, a figure, an animal or
an object — and the app shows, over each inbetween, the pose part of the way
between them with the volume of every part around it. The artist draws the
inbetween over it, in their own hand. Four questions asked with
`AskUserQuestion`, three answered as recommended; on subjects the owner added
that it need not be human.

## How this was reached, the same day

The owner's goal, restated after every route was tried: *"saving the tedium
while keeping the art style and expression"*, and of drawing key from key:
*"frame to frame drawing misses its swoon, the unique flavour an artist
brings."* Measured on the traced running legs (Q270, Q271, Q273):

- Claude drawing the frame lost the anatomy every time; editing the engine's
  frame it found the faults but drew a blob.
- Gemini drew beautiful, on-model lines but did not interpolate: it copied a key
  or added a leg — given Claude's pose as a stick figure, it still took one leg
  straight from key B and gave the other two shoes.
- The engine, given matches, draws the artist's lines, but every route that
  guesses which line is which leaves lines where there should be none.
- **Claude reading the pose was right**: the joints of both legs in both keys,
  the legs told apart, the hidden foot estimated and said so; swung on arcs, the
  halfway pose was a believable running pose. The owner: *"Claude's halfway pose
  is already heaps better and very useable as an overlay. That at least gives
  an artist guides on where to place the strokes. Perhaps we should rethink this
  and only let claude add volume objects around the halfway pose so we know
  approximately how big the part should become?"*

Bending the key's own drawing into that pose (a scratch test) also looked right,
and is not what was chosen: it keeps the key's drawing rather than leaving the
inbetween to the artist.

## What was asked, and what was answered

**1. Where the volumes come from.** *Measured from the keys* (recommended):
**chosen**. Claude gives the joints; the app measures each part's width along
its bones in both keys' ink and blends the two — exact, deterministic, no extra
request. Against: *Claude estimates them* (copes better with a part one key
hides; less exact, a little more per request).

**2. How the guide appears.** *An overlay, never exported* (recommended):
**chosen**. Drawn over the inbetween like onion skin, skeleton and volumes,
toggled, saved with the gap, never in an export; the inbetween stays the
artist's strokes alone. Against: *strokes on a construction layer* (editable
with any tool, a layer per animation to manage).

**3. What Claude reads.** *Whatever it sees* (recommended): **chosen**, with
the owner's note: *"it doesn't always have to be a human. It can be an animal or
inanimate object."* Claude names the parts the drawing has and gives each a
chain of joints. Against: *a fixed human skeleton* (predictable names, human
figures only).

**4. Correcting it.** *Drag joints* (recommended): **chosen**. A joint dragged
in a key or an inbetween corrects Claude's reading, and every inbetween's guide
in the gap follows. Against: *ask again* (a request per correction, which may
not land).

## What it costs

- One small request per key pair (two pictures, a few hundred tokens out), and
  none per inbetween: the inbetween poses are computed.
- The joints are Claude's reading; a misread costs a drag.
- Volume measured from ink needs ink on both sides of a bone; a part hidden in
  one key takes its width from the other.
