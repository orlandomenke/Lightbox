# Q275 · The reference's second slider, for opacity and flow — **answered 2026-10-15: the gallery's effect slider**

**Answered:** the gallery's *Effect sliders* prototype, made real, on **every
brush opacity and flow** — the tool bar's Opacity, its Flow (brush and eraser)
and the brush editor's Opacity and Flow pages — with the track fading in **the
paint colour in hand**. Size and every other value keep the line and the dot.

Raised by the owner, 2026-10-15: *"Based on our new UI we have different
sliders; the line with handle, but also the rectangle box for opacity and flow.
We haven't implemented that in the current application."*

**The first answer was to the wrong question, and it is kept because the
mistake is the useful part.** Asked from `docs/design/ui-reference.png` alone,
the "rectangle box" read as the image's *Opacity 100%* fields in the bar and
the Layers header — a box holding its label and value, filled to the value —
and a whole `BoxSlider` control was built for it, Krita's drag included, with
the owner choosing those answers as recommended. The owner then pointed at the
component that already existed: *"We have the Avalonia Library, there is a
component already build. That looks different to the one you created"* — the
gallery's `Slider.effect` (`tools/Lightbox.Gallery`, Q203), built there and
never promoted. The box slider was discarded unmerged. **The lesson: before
building a look from the reference image, search the gallery for a prototype of
it** — the gallery is where a treatment waits to be promoted.

The second round, all as recommended:

- **The box slider is discarded**, not kept for the bar: one treatment for one
  value, the one already built in the gallery.
- **Every brush opacity and flow**, bar and brush editor. Layer opacity was
  offered and not taken: its track would have no paint to show.
- **The paint colour in hand**, as the prototype's note asked, so the track
  previews the next stroke. A fixed accent was the cheaper alternative and
  stops the slider saying anything about the mark.

**What promoting it found:** the prototype's track had no buttons either side
of its thumb, and a Slider moves to a press only through them — so in the
gallery it could be dragged by the ring and nowhere else. And `Density.axaml`'s
line-slider styles (a 10 px dot, a track centred to it) outrank a template's
own values, so the pill has to restate both. Each has a test.
