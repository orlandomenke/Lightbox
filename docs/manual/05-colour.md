# Colour

The **Color** panel offers a hue ring with the saturation/value square
inside it — hue around the ring, light and strength in the square, no
separate slider to reach for — and HSV, HSL, RGB and CMYK slider sets.

The swatch at the bottom does two things, told apart by whether you move:

- **Click** it for the numbers — hex, HSV and RGB — in a flyout.
- **Drag** it onto the canvas to fill with that colour.

## Foreground and background

Two colours, shown one over the other at the **left end of the Quick options
bar**, just after the active tool's icon, shared by the brush, the fill and
the gradient. **X** swaps them; **D**
resets to black over white. They are global on purpose — reaching for the same
colour in three tools and finding three different answers is what this prevents.

The pair is always there, whichever tool is selected, and always in the same
place: it does not come and go, and the tool's own options begin after it rather
than shifting along. It also never collapses into the bar's **▾** overflow, which
the tool's own controls do when the window is narrow.

The swatch link travels with the swap, so trading to a palette colour and back
leaves your strokes still following that swatch.

Either half does two things:

- **Click** it to open its own picker — the same ring-and-square wheel,
  readouts and palette the Color panel shows, editing that half of the pair.
- **Drag** it onto the canvas to fill with that colour.

The **▾** beside the pair opens the foreground picker directly. It is there
because the swatches themselves are a press-and-maybe-drag gesture, and a hand
that moves on the way down should get a fill rather than a panel.

## Choosing a colour anywhere else

Every other place a colour is set — a palette swatch, a gradient stop, the
brush's secondary colour — is a **swatch you click**, and it opens the same
ring-and-square wheel and the same readouts.

Hex is at the bottom of that flyout, under the wheel, in the same order the
Color panel uses. It is a readout you can also type into, which is the right
rank for it: typing `#c04a2f` is transcribing a colour you already found, not
choosing one.

A checkerboard swatch means **no colour**, which is a different answer from
black. The brush's secondary colour is the one place that matters, and it has
a ✕ to get back to it.

## Keeping a colour you found

Every picker has a **＋** beside the word *Palette*. It puts the colour on the
wheel into the palette the Palette panel has selected, and makes a palette
first if the document has none — finding a colour and keeping it should be one
gesture, not a trip to another panel and back.

The new swatch is then the one you are painting with, so the stroke that
follows *references* it. A colour you went to the trouble of writing down would
otherwise be the one colour in the drawing a later palette edit could not
reach. Adding the background colour, or a gradient stop's colour, leaves the
brush where it was.

**A colour already in the palette is not added twice.** The same colour arriving
twice is almost always a slip — the wheel moved a little and came back — and a
palette full of near-identical entries is a palette nobody can use. What happens
next depends on which wheel you used:

- **Foreground or background** — the swatch already there is *selected* instead.
  That is the useful answer: the point of adding was to paint with a live
  colour, and that swatch already is one.
- **Anywhere else** — nothing is added, and it says which swatch already holds
  the colour.
- **The wheel in the Palette panel** — the copy is made. Somebody working in the
  palette who asks for a second copy wants one.

When you do want two of a colour — the same grey filed under two characters, say
— use **Duplicate** in the Palette panel. It makes an independent swatch with a
new identity, so recolouring the copy leaves art painted with the original
alone. That is the whole reason to have two.

## Palettes

Every document starts with a palette holding **pure black and pure white**,
with black selected.

This is the one place the "absent unless asked for" rule does not apply, and
deliberately. A swatch is not a feature you opt into — it is the difference
between a stroke that carries a colour and one that carries a *reference*, and
only the second can be recoloured later. Starting empty would mean the first
hour of work is painted in literals that can never follow a palette edit.

The palette appears in **every** colour picker, not just the panel. Picking
from it links the swatch, so the recolour still reaches the art.

The **Palette** panel manages named palettes. Import and export **.gpl** (GIMP)
files.

Palettes are **live**, Toon Boom style. Paint with a swatch and the stroke
remembers *the swatch*, not the colour. Edit the swatch and every stroke that
used it repaints — across every layer and every frame at once. A run of edits
collapses into one undo step.

Choosing a colour any other way breaks the link, which is what you want: a
colour picked off the canvas is a colour, not a palette entry.

In a project, palettes belong to the project, so all of a character's animations
paint from the same one.

## Filing palettes

The Palette panel's top half is a tree. **🗀** makes a folder, **＋** makes a
palette, and both land inside whatever is selected — where you were looking,
not at the bottom of the list. **✕** deletes whichever is selected.

**Drag the divider** between the tree and the swatches to give either half more
room — organising wants the tree, painting wants the swatches. A tree deeper
than its half scrolls; neither half can be dragged away entirely.

Move things by **dragging** a row onto a folder, or by **right-clicking** it and
choosing *Assign to*. The two do the same thing; the menu lists every folder by
its full path, which is what tells two folders called "Knight" apart. Right-click
also has *Rename* and *Delete*.

Deleting a folder keeps the palettes in it — they come back one level up. A
folder can hold folders, and can sit empty: filing before there is anything to
file is the normal way round.

With a project open the tree has two headings, **Document** and **Project**, and
nothing moves between them. A document palette travels with its file and a
project palette is shared by every animation in the project, so dragging one
into the other is not filing — it is a change of ownership, and it would leave
the strokes that reference the palette pointing at nothing. Without a project
there are no headings at all, only the document's palettes.

A project's hierarchy is saved with the project, so a project you filed last
week opens filed. A document that has never had a folder carries no filing
system in its file.

## Gradients

Pick the gradient tool and its options appear in the bar, with the ramp itself
as the preview. **Click the ramp** to edit it.

The editor has two rows of markers, and they are independent:

- **Above the ramp: opacity.** Click to add a stop, drag to move it, select one
  to set its value.
- **Below the ramp: colour.** Same, and selecting one gives you the colour
  picker.

Middle-click a marker to remove it. A colour ramp always keeps two stops; an
opacity track keeps two or none, because one stop holds its value everywhere
and that is a flat opacity wearing the costume of a gradient.

The two rows exist because opacity genuinely changes in different places from
colour. A sky fading out at the top while going orange in the middle needs two
stops in one place and one in another, and tying them together would force you
to author a colour you did not want in order to place an opacity you did.

A gradient with no separate opacity track is the ordinary case and writes
nothing extra to the file.

Drag on the canvas to lay one down; the drag sets the axis, or the centre and
radius for a radial. If you have no gradient yet, picking the tool makes a
black-to-white one.

Gradients are live in the same way palettes are: edit the definition and the
art follows.

The **Gradient** panel shows the same editor, for when you want it open
permanently rather than behind a click.

## Channels

The **Channels** panel shows what the canvas shows, one channel at a time:
red, green and blue as grayscale, and **alpha** as coverage — transparent is
black, solid is white. It ships as the last tab of the colour family.

Click a channel to view it alone on the canvas; click it again to get all of
them back. The solo is **viewing only** — like zoom and mirror it never
touches the drawing, so nothing you do while soloed records any differently.
Painting while a channel is soloed paints with your actual colour; the canvas
just shows you one channel of the result.

The thumbnails follow the current frame and redraw as you work, so the panel
doubles as a running answer to "where is my ink actually going" — line work
that should be pure black shows up identically in all three colour channels,
and a stray tint shows up as a difference between them.

The fifth tile is **Silhouette** — the classic pose-reading check: your ink as
solid black on white, with the paper, the references and the onion ghosts left
out. If the pose still reads from the shape alone, it will read at speed; if
two arms merge into one mass here, they will merge on screen too. It works
while flipping and during playback, and like the channel solos it is viewing
only. No key out of the box; **Configure → Shortcuts → "Silhouette view"**
binds one. There is deliberately no score attached — the judge is your eye,
and a number for "does this read" would be a guess wearing one.

---
