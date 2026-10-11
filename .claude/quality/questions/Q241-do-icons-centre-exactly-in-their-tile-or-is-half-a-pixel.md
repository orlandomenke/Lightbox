# Q241 · Do icons centre exactly in their tile, or is half a pixel accepted?

Raised by: the icon placement census (`IconCensus`, `IconPlacementTests`, the lab's
`icon-buttons` scenario), 2026-10-10. On the real main window at 100% scaling, 96 of
the 111 icons that sit alone in a button were half a pixel off centre: the styles gave
13 px to an icon and 15 px to a stateful pair, and the tile is 26, so the exact centre
is a half pixel and layout rounding has to pick a side. It picked both — six 13 px icons
landed up and to the left, five down and to the right — so identical buttons could
disagree by a pixel. No written rule said whether that was accepted; the only thing
deciding it was a test tolerance.

What it blocks: nothing was broken. It decided whether the tolerance in
`IconPlacementTests` is the rule or a debt.

**Recommendation:** even glyph sizes — 13 → 12 and 15 → 16, both already tokens — so
every icon at a class size centres exactly at 100%, with row heights unchanged because
the tile stays 26. The alternatives: accept the half pixel and write that down (nothing
changes on screen, and neighbours stay able to disagree by a pixel); or an odd tile,
25 or 27, which moves `--tile`, every docker row with it, and breaks the 24 px rows.

**Answered 2026-10-10 (owner, prompted): even glyph sizes.**

What it costs, written down because the recommendation won: every button icon is about
8% smaller and every eye, lock and onion glyph about 7% larger, which is a visible change
to the whole chrome made on the strength of a measurement rather than a look — the owner
has not yet seen it side by side. It is exact only where a tile is a whole, even number
of device pixels: 100% and 200%. At 150% a 26 tile is 39 device pixels and the half pixel
comes back. And it is the two class sizes only: the inline one-offs (9 and 11 px in
several rows, the 8 px reorder chevrons in their 11 px halves) keep theirs, 25 icons on
the default window, because each sits in a space somebody sized by eye.
