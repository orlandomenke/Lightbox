# Q46 · What colour does the theme's accent take, and how does a tab say it is the one showing? — **answered 2026-08-07: violet, and an underline**

Three questions in one exchange, because they were three faces of the same
finding: **the palette had only ever covered half the application.**

Stage 1 tokenised every view, and every test passed, and the application still
wore two colour systems. Tokenising a view reaches the surfaces somebody aimed
at a token; every stock control — toggle buttons, slider thumbs, checkboxes,
radios, focus rings, list selection — paints from the *theme's* palette, and
Fluent's accent is Windows blue. The proof was one control wearing both at
once: the opacity slider had our coral track and Fluent's `#0078D7` thumb.

Nothing could have caught it from inside. It took a screenshot, which is the
part worth keeping: a colour system is only as wide as the surfaces that
resolve through it, and no assertion about the tokens can tell you which
surfaces those are.

**(a) The interactive accent is violet `#7B61FF`.** Every "this is on" state —
toggles, slider thumbs, checkboxes, selection, focus. Violet rather than coral
because it is *already* the selection colour in the layers list and the cel
vocabulary, so the selected row and the switched-on toggle become one colour
instead of two. It also leaves coral meaning "the primary action" without
competition, which is the rule the button ranks depend on: a screen where every
"on" state is as loud as the one button you want pressed has ranked nothing.

The cost, taken knowingly: the primary button's gradient no longer shares a
colour with any control state, so the loudest thing on screen is deliberately
unrelated to everything around it. That is the point, and it is also the thing
that will look wrong to somebody wanting the app to be "coral".

**(b) The active tab carries a 2 px accent underline.** The first version had no
mark at all, reasoning that the header is already a distinct surface and a
filled tab inside it makes two boxes where the artist needed one word. **The
boxes part still holds; the conclusion did not.** Three words at slightly
different brightnesses read as a row of labels rather than as a control — and a
tab strip that is not legible *as* a tab strip has hidden two panels instead of
offering them, which is the opposite of what tabbing is for.

An underline is the affordance that adds no box and costs no height. A filled
pill and full boxed tabs were both rejected for the reason the original
no-mark version was chosen: they put a second box inside the header, and boxed
tabs would want a row of their own, spending exactly the height tabbing exists
to save.

**(c) Dialogs sit on `SurfaceElevated`, one step above the panels.** They were
painting pure black, which is Fluent's window ground showing through because
nothing had told the theme otherwise. Elevated rather than the panel surface so
a dialog reads as floating over the app rather than as a hole cut in it — the
"anything raised goes one step up" rule the four surfaces already encode.

**What none of this needed deciding about**, so it did not hold the question up:
the theme's palette is written as hex literals in `App.axaml` and cannot be
otherwise. A `ColorPaletteResources` is built before the merged dictionaries it
would look into, so `{StaticResource}` there does not resolve. That is a fact
about Avalonia rather than a preference, and it is guarded by
`TheThemePaletteIsWrittenInHexOnPurpose` asserting the literals equal the tokens
they stand in for.

---
