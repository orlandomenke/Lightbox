# Q73 · Docking: the slot cap, the colour family, and where a reopened panel lands — **answered 2026-08-12**

The owner asked for three rules — at most four stacked dockers per side,
default stack groups ("for example Color, Palette and channel — latter not
implemented, go ahead though"), and closed tabs reopening into their tab
group unless the session or the saved workspace placed them elsewhere — and
asked to be prompted for edge cases. Four were prompted, each with a
recommendation, and all four recommendations were taken:

- **A fifth slot never opens.** A drop (or programmatic show) that would
  exceed the cap tabs into the nearest slot instead — nothing is refused,
  and the panel lands where the artist can see it. `DockLayout.MaxSlotsPerSide`.
- **Channels ships minimal but real**: red, green, blue and alpha of the
  composited frame as grayscale thumbnails, click to solo one on the canvas,
  click again for all. The alternative — registering an empty panel marked
  *Planned* — puts dead weight in the default group, and the manual rule
  about documenting what nobody can use applies to panels too. The solo is
  view-only (invariant 5): an `SKColorFilter` on the artwork draw, the
  record untouched.
- **All four colour panels in one group** — Color | Palette | Gradient |
  Channels — rather than the literal "Color, Palette and channel" with
  Gradient evicted. Four tabs still cost one strip, and Gradient keeps the
  home it had just been given.
- **An orphan reopens alone.** A panel whose whole family is closed opens in
  its own slot; the family finds it as members reopen (each closed member
  remembers its slot-mates in `DockPlacement.LastGroupedWith`). The
  alternative — reopening the whole family group — opens three panels
  nobody asked for.

One rule sharpened during the work: the family default applies only to a
panel that has **never been placed** (`HomeSide == Hidden`). A panel the
artist parked somewhere on purpose — solo included — goes back exactly
there, which is what keeps "unless in current session grouped with other
dockers, or if workspace is saved like that" true rather than approximate.
