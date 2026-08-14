# src/Lightbox.App/Views/MainWindow.axaml

budget: 4276

## Why it is here despite being XAML

`HOTSPOTS.md` puts it at the top of the risk table — 4,188 lines, 53 commits,
and no test file that exercises it directly. It is the largest unguarded surface
in the repository, so the least it can do is stop growing.

Buttons and menu items have no partial to live in — XAML cannot be split the way
the code-behind was — and a toolbar that hides tools to satisfy a line count is
the tail wagging the dog. That is why this budget moves more than the others.

## Why it has moved

Newest last. Both sides of a merge keep their entry — taking one deletes the
other's reason and leaves a number nobody can account for.

- **4,188 → 4,273** (2026-08-13), re-seeded on top of 52 commits of `main`. That
  growth is main's, not a branch's.
- **4,273 → 4,306 → 4,314** (2026-08-13): the construction-guide, guide-set and
  volume-check menu entries.
- **→ 4,326** (2026-08-14): the Bone tool's toolbar button.
- **→ 4,270** (2026-08-14), re-measured on the merged tree. Three independent
  changes met here: the onion bar was extracted to `OnionBar.axaml` (−71), the
  `TimelineRuler` element gained one attribute (+1), and the bone tool's options
  bar arrived as `BoneOptionsBar.axaml` leaving one hosting line behind (+1).
  Measured alone the last two wanted 4,327 and 4,271; taking either on the merged
  tree would bank the extraction's slack as permanent headroom, which is the one
  thing a ratchet must not do.
- **→ 4,273** (2026-08-14): the layer docker's drag-and-drop wiring — two pointer
  handlers on the row template and three `DragDrop` attributes on the
  `ItemsControl`. Event attributes live on the element they handle; the handlers
  themselves went to `MainWindow.Workspace.cs`. Measured alone that wanted 4,275,
  and 4,275 would have banked two lines of the other branch's slack as headroom
  nobody had earned. +4 over main, which is the size of the change.
- **→ 4,276** (2026-08-14): the past-the-end cel's hatch (Q89) — one setter and
  its two-line reason. The brush itself is in `Palette.axaml`, where the colour
  system lives; only the style selector has to be here, because a selector cannot
  live anywhere else. Measured alone that wanted 4,272, against a main that had
  not yet taken the drag wiring above.
