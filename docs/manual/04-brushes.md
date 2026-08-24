# Brushes

## Brushes

The **Quick options bar** above the canvas carries the controls you reach
for constantly. It opens with the active tool's icon, then your two colours,
then the **brush preset** button with its **⚙**, then **Size** and
**Opacity**. All of those are pinned: they stay put whatever tool you switch
to, and Size and Opacity grey out rather than disappear when the tool in
hand makes no mark to size. Everything to their right is the tool's own
quick options — hardness and stabilizer for the brush, variants for the
selection, kind and spread for the gradient — and folds into the bar's **▾**
menu when the window gets narrow. The pinned section never folds.

The brush button is pinned for the same reason the colours are: which brush
you are holding is something you change from any tool. Picking a preset also
**puts its tool in your hand** — a brush preset hands you the brush, an
eraser preset the eraser — so the picker is the way back to painting from
the selection tool or the fill without visiting the rail.

**⚙**, immediately right of the brush button, opens the **Tool options**
panel with every parameter, grouped: General, Effects, Medium, Pen pressure,
Presets. It is a docker, not a flyout — it stays open while you paint and
test, docks anywhere a panel docks, and is also under **View → Tool
options**. Like the brush button it is pinned for every tool, the eraser
included, whose parameters it has always served.

The panel follows the tool in your hand. With the brush or the eraser it is
the parameter editor above; pick the fill, the selection, a shape or the
gradient and it shows that tool's options instead — the depth behind the
bar's quick reach, laid out vertically with room for labels and sliders. A
tool whose whole vocabulary fits on the bar says so rather than going blank.

Which quick options the bar carries is the **workspace's** choice — the ⋮
beside the workspace picker ticks them on and off (see *Getting started →
The quick bar is the workspace's*). The pinned section — colours, the brush
button, ⚙, Size, Opacity — is not on offer there and cannot be removed.

#### Finding a brush

The brush button opens a flyout, not a dropdown: once you have forty brushes,
scrolling is the wrong verb.

Every brush is a **tile with a picture of its own mark on it** — one swash,
drawn by the real engine with that brush's tip, edge, texture, scatter and
wetness. A collection you imported arrived with names somebody else chose, and
"Wet bristle 04" and "Wet bristle 07" are genuinely different brushes with
nothing in the words to say how. The picture is what you choose by.

Two things the tile deliberately does not promise:

- **It is not to scale.** A 300 px brush at true size fills the tile with the
  flat middle of a mark, which is the least useful part of it, so size is
  mapped onto a range the tile can draw — a heavier brush always reads heavier,
  but the width is not the number. The number is in the corner of the tile.
- **Smudge, blur and the blender are drawn over a test pattern**, because they
  move what is already there rather than laying anything down. On clean paper
  their tiles would be blank.

Everything else is the brush's own, because the tile goes through the same
engine your canvas does.

- **Search** matches names and tags.
- **Tag chips** across the top narrow the list. Pick several and you get all of
  them — "inking *or* roughs" — because asking for a brush that is both is
  almost always an empty list.
- The chips only appear once you have tagged something.

Tag a brush on the **Presets** page: a comma-separated list, whatever you would
look for it under. There is no fixed vocabulary, because the categories worth
having are the ones your work has.

#### Changing a brush and keeping the change

An **●** next to the brush name means the settings have drifted from the brush
they came from. It compares values, so putting a setting back clears it.

The **Presets** page then gives you three moves:

| | |
| --- | --- |
| **Update** | Writes your changes back over the brush you started from. |
| **Save as new** | Keeps both — the original untouched, your version under a new name. |
| **Delete** | Removes a brush you made. |

**You can update the brushes that ship with Lightbox.** Tweak Pencil, press
Update, and it stays tweaked across restarts. Nothing is lost doing it:
**Revert** gives you the original back whenever you want it, and on a shipped
brush the Delete button *is* Revert — it is not yours to delete, and "delete"
on one plainly means "give me back the one that came with the app".

Effect brushes (**Smudge**, **Blur**) swap the bar for their own controls —
strength, radius, and for smudge how much of its own colour it adds. A smudge
has no opacity in the usual sense, so showing you one would be a lie.

**Smearing or dulling**, and **length**, are on the **⚙ → Effects** page rather
than on the bar. Smearing drags a sample along the stroke so detail streaks;
dulling lays down the colour under the dab so detail dissolves, which is what a
blender is. Length is how far colour travels before the sample refreshes. They
live with the brush because they are what make a brush *that* brush — the three
values on the bar are the ones you adjust mid-drawing.

**Strength on an effect brush is flow, and flow there is not flow on a paint
brush.** On a paint brush flow is how much pigment a dab lays; on a smudge or a
blender it is how hard each dab *pulls*, and because dabs overlap roughly ten
deep the pulls compound along the stroke. A value that looks like a gentle nudge
on one dab is a shove by the time ten have landed — which is why these ship an
order of magnitude lower: Smudge 0.08, Blender 0.06. The bar steps in hundredths
for exactly that reason. If you want a stronger effect, prefer a slower hand or a
second pass over raising strength; that is what gives a smudge somewhere to go.

**Blur is the exception, and it works the other way round.** Its flow is the
softening radius — sigma, in pixels, is roughly flow × size ÷ 4 — and it does
*not* compound: a blur takes one reading of the layer before the stroke and every
dab replaces its own circle with a single pass of that. So the softness you get
is the softness of one dab, however long you draw and however many times you go
back over it. It ships at **0.35**, about two pixels at the default size. It was
0.10 for a while, which is well under a pixel, and the brush appeared to do
nothing at all.

**These defaults are deliberately conservative and you may well want to raise
them.** They were chosen while an effect brush still stacked opacity with every
dab, which turned a pale wash opaque black. That is fixed: flow no longer touches
opacity at all — it only decides how much colour moves — and it now responds
evenly across its range rather than saturating. Measured on a wash, carried
colour runs 0, 17, 34, 47, 64 as flow goes 0.08 → 0.85, with the wash's own
opacity unchanged at every setting. Raise it until the tool feels right; it can
no longer run away from you.

Every numeric field can be **dragged sideways** to scrub its value. Hold
**Shift** for fine, **Ctrl** for coarse. **Click without dragging and the
whole value is selected**, ready to type over; click a second time to place a
caret between digits instead.

Every numeric field also does **arithmetic**: type `50+10`, `128/2` or
`12 * 4` — spaces or not — and commit with Enter or by clicking away. The
four operators, parentheses and decimals all work, and precedence is the
usual one (`2+3*4` is 14). Anything the field cannot evaluate — an emptied
field included — simply keeps the value it had, the same way a typo does.

Brush fractions — opacity, hardness, flow and their kin — **read and write as
0–100**, the scale every other painting tool uses, so half strength is `50`
rather than `0.5`. Only the display changed: presets, saved documents and
existing strokes are untouched.

**Shift + drag** on the canvas resizes the brush.

#### Spacing

**Spacing** is how far the brush travels between dabs, as a fraction of its own
width. It is a texture control, not a quality one: a line drawn at an ordinary
spacing comes out as a line, and you do not have to wind the number down to get
one.

Below about a quarter of the brush's width, the stroke is solid — Lightbox lays
however many dabs it takes to make it so, and thins each one to match, so
tightening the spacing does not darken the mark or cost you anything you were
not already paying. Past that, the dabs come apart on purpose: that is the
dotted trail, the stamped repeat, the row of leaves along a path. Wind it up
when that is what you want.

The one thing spacing still changes is a brush that has **scatter or jitter**
turned on. There the dabs *are* the texture, so spacing sets how dense the
spray is, and Lightbox leaves the walk exactly as you set it.

#### Stabiliser

The **Per brush** box beside the stabiliser decides what those controls belong
to. Off, they set one value for the whole application — how it has always
worked. On, this brush keeps its own and takes it along in its preset.

That is what the setting is actually for: an inking brush wants heavy
lazy-mouse so a long confident line comes out clean, and a pencil wants none,
because the shake *is* the texture and smoothing it makes roughs look dead.
Ticking the box copies whatever is already in effect, so it never changes how
the brush draws — only what the controls are pointed at.

#### Blend mode

On the **General** page. It decides how the finished stroke lands on the layer
— Multiply to shade, Screen to glow, and every other mode the layer docker
offers, because they are the same operation.

It is applied **once, where the stroke meets the layer**, not to each dab. So a
Multiply brush that crosses itself does not go black at the crossing, which is
almost never what you meant. The eraser ignores it: erasing takes paint away,
and no blend mode does that.

#### Choosing a tip

Also on **General**, as a grid of thumbnails rather than a list of names —
nobody knows what a "Cut nib" looks like until they have seen one. **Round** at
the top is the brush's own dab and the default. **Brush tips…** at the bottom
opens the workshop.

Painting with a tip copies it into the drawing, so the file keeps rendering
even if you later delete the tip from your library.

A tip shapes **smudge, blur and the blender** too, not only the brushes that
deposit colour — a chisel smudges in a band and a bristle drags in strands, where
before all three pushed paint around in a circle whatever tip you picked. **Angle
follows direction** works on them as well, so a chisel turns with the stroke
rather than staying at one angle.

#### Paper texture

On the **Effects** page. Pick one of the built-in surfaces, or **Paper image…**
to use a scan or photograph of the real thing — an imported paper takes over
and the surface list goes quiet.

Two things worth knowing:

- **The image goes into the drawing, not a path on disk.** A file pointing at
  your scans folder would paint differently on somebody else's machine.
- **The grain is anchored to the canvas, not to the stroke.** Two marks
  crossing the same patch sit on the same tooth, which is what makes it read as
  paper rather than as an effect applied per stroke.

**Grain size** is how many document pixels one bit of the paper covers, and
**Depth** is how hard it bites. Depth starts at zero, so importing a paper
opens it for you — a texture you cannot see looks like a broken import.

## How the brush answers the pen

The **Pen pressure** page gives each thing pressure can drive its own curve.
Pressure runs left to right, the effect bottom to top, and the dashed diagonal
is "straight through" so you can see what you have changed.

- **Drag** a point to shape the response.
- **Click** empty space for a new point.
- **Middle-click** a point to remove it. The two ends stay — you can still drag
  them up and down.
- **Reset** puts it back to a straight line.

Seven things can be driven: **size**, **transparency**, **hardness**,
The three wet-medium brushes ship with a tip and a heading rather than a bare
circle: **Oil** uses the bristle tip turned to the stroke, **Gouache** a chisel
turned the same way, and **Watercolor** an irregular wash edge with a little
size and roundness variation and no heading at all — a wash edge is not
directional. All of that variation is seeded from where each dab lands, so a
mark is varied and still replays identically.

**Bristle drag and pickup are not on the medium page**, and that is deliberate
rather than missing. Both need the paint to be pushed along the stroke's own
direction, which the medium pass does not yet do, so the sliders that used to be
there moved and changed nothing. A control that does nothing is worse than one
that is absent: it teaches you the panel cannot be trusted. For the *look* of a
dragged bristle, use the bristle tip with **Angle follows direction** — which is
what Oil already does.

**scatter**, **roundness**, and for a smudge, **colour rate** and **smudge
length**. Untick one and pressure stops touching it entirely.

A curve does what no single number can. An exponent can only make the response
gentler or fiercer; it can never rise and then fall, which is what an ink brush
that spreads and then floods actually does. Draw that shape and you have it.

A brush you made before curves existed opens showing the response it already
had, not a straight line — so touching the page never quietly flattens a brush
you had tuned.

**Use pen pressure** at the top is the master switch. Off, the tablet is
ignored entirely and every curve on the page with it.

## Physical media

Watercolour, gouache, oil and ink are simulated, not imitated with a texture:
wetness, viscosity, absorbency, edge pull, pigment density, granulation, paper
grain. The simulation is **deterministic** — the same stroke always produces
the same mark, on reload, after undo, and when the inbetweener replays it.

That determinism is not a detail. An effect that varies subtly between similar
strokes looks fine on one image and *boils* at 12 fps.

**A simulated wash is transparent, and it glazes.** Pigment darkens the way
pigment does rather than the way a slider does: laying the same watercolour
stroke down a second time deepens it, and a third deepens it again, approaching
solid without ever arriving. That is what makes Watercolour behave like
watercolour rather than like ink at a low opacity — build a tone in passes and
let each one dry into the last. **Pigment density** is the concentration in the
wash: raise it and every pass bites harder, and the steps get smaller as it
approaches saturation, exactly as adding more pigment to the same water does.
Gouache and oil use the same model and are simply strong enough to cover in one
pass.

**Body** and **relief** give thick paint its height. Body is how much the paint
stands up off the paper; relief is how hard the light rakes across it. Together
they are impasto — a raised edge on a gouache or oil stroke catches the light
from the upper left and shadows on the other side. The light is fixed, and
deliberately so: two strokes on one canvas must not disagree about where it is
coming from.

Each stroke is modelled from its own paint, so crossing two of them does not
yet build a ridge where they meet.

**Paint load** is how much paint the brush starts with. At 1 it never runs
out. Below that the mark begins full and fades as you draw, and at low values
it is gone within a short scrape — that is dry-brush, and it works whether or
not a medium is switched on. The length scale follows the brush size, so
resizing a brush does not change how far its paint goes.

**Wetness** is how far the paint travels. A wet mark spreads past where the
brush went, more so the longer the flow runs — a 40-pixel stroke reaches nearly
60 at full wetness. The extra room costs a little to paint, so a dry medium
does not pay for it.

**Edge pull** is the wet edge: pigment carried out to the rim of the wash as it
dries, so the mark ends up darker at its border than in the middle. At 0 the
wash dries flat. Turn it up and the border darkens and the middle pales — the
paint is being moved, not added, so a strong wet edge is paid for out of the
centre. That is what a real one costs too.

**Flow steps** decide how far the paint travels, not how much of it there is.
Turn them down for a mark that stays where you put it, up for one that spreads
and pools; the stroke carries the same pigment either way, and at zero it is
simply the mark you drew. How strong the paint is comes from **pigment
density** — a watercolour is meant to be transparent, so raise that rather than
the flow if you want a darker wash.

## Brush tips

**Edit ▸ Brush tips…** opens the tip workshop — its own window, like Configure,
because making a brush is not something you do mid-stroke.

Three pages. **Library** is what you have: your own tips, the project's above
them when a project is open, and eight built-ins below. **Generate** bakes a
shape, with only the controls that shape actually reads. **From a scan** turns a
photographed or scanned stamp into a tip: set the black and white points once
and they apply to every image in the batch, which matters because a series that
will be blended has to match exactly.

A tip is baked once and then only looked up. Nothing in this window is
recomputed while you draw.

#### The eight that are already there

| Tip | What it is |
| --- | --- |
| **Soft round** | The default. A disc with a long shoulder. |
| **Hard round** | Full to the edge, one pixel of feather. A pen. |
| **Paintbrush** | A flat brush seen head-on. Turn on *angle follows direction* and it reads as a loaded brush rather than a nib. |
| **Bristle round** | A round brush whose hairs have parted — fine scratches through a solid middle. Dry-brush without a simulation. |
| **Marker nib** | Squarish with rounded corners, the shape a chisel marker lays down. |
| **Cut nib** | Six flats and six corners. The only tip here with a point. |
| **Spatter** | Grains with a size, not fog: a sponge, a stipple, a rough charcoal edge. |
| **Wet edge** | Pale in the middle, dark at the rim — the mark a puddle leaves when it dries, stamped rather than simulated. |

Built-ins cannot be deleted or renamed, because drawings refer to them by name
under the hood. **Edit a copy** puts one back on the Generate page so you can
change it and bake your own.

#### Shapes the generator can bake

Hard circle, soft circle, ring, chisel, hatch, bristle, superellipse, polygon,
spatter and halo. Three controls do different jobs depending on the shape and
are relabelled to say which: *Count* is bristles, polygon sides or grains
across; *Sharpness* is channel depth, squareness, corner sharpness, coverage or
rim strength; *Flatness* squashes a chisel or a superellipse across its short
axis.

Two things worth knowing:

- **Painting with a tip copies it into the drawing.** Deleting it from the
  library afterwards cannot change a picture you have already made.
- **A scan whose mark runs off the crop is refused, not fixed.** A tip like
  that stamps a faint box down every stroke. Re-crop with clear paper all the
  way round.

## Fast brushes, textured ones, and expressive ones

Brushes come in three kinds, and the picker tells them apart:

- **Fast** — stamps dabs and stops. Predictable cost at any canvas size, and
  what almost every brush is. Ink, soft round, airbrush.
- **Textured ◇** — stamps dabs and then finishes the mark: a wet edge or
  granulation pass runs over the whole stroke when the pen lifts. Drawing
  stays light; the finish is a beat at pen-lift, longer on a big stroke.
  Pencil and the paper-grain brushes live here.
- **Expressive ◈** — reads the canvas back, simulates a medium, or blends the
  layers underneath. The mark behaves like a material instead of like paint
  being placed. Slower, particularly on a large canvas.

The glyphs mark the two paid kinds, and the list is grouped so they sit apart
from the fast ones. Hover a brush and the tooltip names what it is paying for —
"reads the canvas back as it goes", "settles pigment into the grain at
pen-lift" — because that is the thing you can turn off if you want the speed
back.

It is a price tag, not a warning. These brushes exist because the coupling is
what makes a mark expressive, and an artist reaching for one has decided the
trade is worth it. The badge is so that decision is made knowingly rather than
discovered at frame 180.

Nothing about a brush *declares* which kind it is — it is worked out from the
brush's own settings, so turning the medium off moves it to the fast group and
turning it on moves it back. Every simulated medium also has a **(flat)**
counterpart that gets close to the look without running the simulation — but
the flat pair lean on the wet edge and grain passes to fake it, so today they
are textured rather than free, and a flat brush with both turned up can cost as
much at pen-lift as the medium it stands in for. The tooltip tells the truth
per brush.

## The brush library

**Edit → Brush library…**, or the button at the bottom of the brush picker. It is
everything you have — the brushes that ship, the ones you made, and the ones you
imported — and it is where you import, rename and remove them.

Select several with Ctrl or Shift and **Remove** takes them all in one go, which
is the point: a collection you decided against is fifty-something brushes, not
one. The brushes that ship with Lightbox are left alone by that button, and the
window says so before you click rather than after — those are reverted from the
brush options instead, not deleted.

**Rename appears only with one brush selected.** Renaming twelve at once has no
sensible meaning, and an imported pack's names are usually the reason you want
this: whoever made it called them what suited them.

## Brush importers

**.abr** (Photoshop), **.gbr** / **.gih** (GIMP) and **.kpp** (Krita) import
directly. What comes across is what those formats actually carry.

**A big collection takes real time, and the bar tells you how much.** The cost is
the tip: decoding it and re-encoding it, per brush, and it grows with the tip's
area — measured at roughly a quarter-second per brush at a 300 px tip, so a
fifty-six brush pack is around fifteen seconds. That work now happens in the
background with a progress bar naming the file it is on, and **Stop** keeps
whatever it has already read. It used to happen on the drawing thread, which is
why the window looked like it was about to crash.

A file it cannot read is **named, not counted** — a pack usually fails in a
pattern, and "3 files could not be read" out of fifty-six tells you nothing you
can act on.

## The ring on the canvas

The outline that follows the pointer is the brush's own footprint, not a stand-in
for it:

- **The size is the size**, taken from the same call the engine makes for each
  dab. With a pen it tracks live pressure while you draw and shows the maximum
  while you hover, so it stays useful for aiming; **Configure ▸ Canvas** turns
  the pressure tracking off if you prefer a fixed target.
- **The shape is the tip's shape.** A chisel is previewed as a chisel, a bristle
  comb keeps its gaps, and an imported `.abr` or `.gbr` tip is outlined from the
  same image the brush stamps — so the ring cannot disagree with the mark. A
  brush with no tip shows the ellipse the round dab genuinely is, flattened by
  **Roundness** and turned by **Tip rotation**.
- **It is an outline, never a fill**, so nothing is hidden at the moment you are
  deciding where to put a mark.

Two things it deliberately does not show, because they belong to the mark rather
than to the brush: the per-dab jitter on roundness and rotation, which would make
the ring wobble as you moved; and the angle a direction-following tip takes, since
a hovering pointer has no direction yet.

The ring updates the moment you change a setting — you do not have to move the
pointer to see the new size.

## Where the brush lives

**Edit → Configure → Drawing** also decides whether the brush belongs to the
tool or to the work:

| | |
| --- | --- |
| **Follow the project** | The default. Illustration, comic, game art and asset libraries keep the brush with the project; animation and storyboards keep one brush for the tool. |
| **Global** | One brush, carried between projects and sessions. What Photoshop and Krita do. |
| **Per project** | The project remembers the brush you paint with and gives it to every document in it. |

The point of **per project** is the break between sessions. Come back to a
comic or a set of game assets after a fortnight and the tool bar says whatever
you last used on something else — but the work was drawn with something
particular, and where the character of the stroke is part of the style that
matters.

The project rather than the file, because the answer has to reach the pages
that do not exist yet: page one remembering its own brush would leave page
eleven starting from scratch. It is the same reasoning as the shared palette —
a character's work has one set of colours and one set of marks.

It is recorded when you make a mark, not when you save, so a session that ended
without saving still remembers. A project with nothing recorded — an older one,
or one worked on under **Global** — leaves your brush alone rather than
resetting it. With no project open there is nowhere to keep a brush, so the
setting reads as **Global** whatever it says. Strokes an AI or an agent adds
never change it: they are not what *you* were painting with.

## What smudge and blur read

Smudge and blur move pixels that are already there rather than laying down
colour.

**How far a smudge carries is set by two things, and they multiply.** *Strength*
on the bar is how hard each dab pulls; *Length* on **⚙ → Effects** is how much of
what it picked up survives into the next dab. Length is the one that decides the
trail: at the default 0.5 a 20 px smudge carries colour about 15 px past the edge
of a mark, at 0.75 about 26, and at 1.0 about 53 — at 1.0 the sample never fades,
so colour travels as far as you drag it. Raise strength as well and both grow.
If a smear dies sooner than you want, reach for Length first.

Dragging outward *does* thin the edge you dragged across, over roughly half a
brush width. That is the tool working — it is what happens when you pull a finger
through wet paint — and it stops at the edge: the body of the mark keeps its
coverage however long you work over it.

**Edit → Configure → Drawing** decides which pixels a smudge or blur reads:

| | |
| --- | --- |
| **This layer** | Only the layer you are painting on. The default. |
| **All layers (baked)** | Everything you can see, frozen as it was when you made the mark. |
| **All layers (live)** | Everything you can see, and it keeps following. |

The setting applies to the *next* mark. Every stroke remembers what it was made
with, so changing this never alters something already drawn.

The difference between the two shows up later. Repaint the background under a
**live** smudge and the smudge re-blends against the new background; a **baked**
one keeps the colours it picked up when you made it. Baked is what you want once
a mark is finished and you would rather nothing touched it again; live is what
you want while a painting is still moving underneath you.

Changing brush does not reset this. It is a setting, not part of a brush.

A live smudge on the bottom layer has nothing to follow, so it reads its own
layer, exactly as **This layer** would.
