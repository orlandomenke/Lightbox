# src/Lightbox.App/Views/MainWindow.axaml

budget: 4428

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
- **→ 4,306** (2026-08-14): the X-sheet's *Delete column* menu entry and its
  tooltip (Q88). A menu item has nowhere else to live — the handler it calls is
  in `MainWindow.Timeline.cs`, and the command it reaches is in the view model.
- **→ 4,306** (2026-08-14): the layer docker's link menu (Q90) — the *Linking*
  and *Follows the rig* flyouts, the same case as the entry above it. The bracket
  that shipped beside them did **not** raise it: that is self-contained chrome, so
  it went to `LinkBracket.axaml` and cost one hosting line instead of fourteen.

- **→ 4,222** (2026-08-14): the guide options, which came *down* from 4,306
  rather than up. The new options went into two `UserControl`s and the Select
  tool's quick-bar group followed them out unchanged; the lines this leaves
  behind are the references to the three. A feature that needed room in a
  budgeted file buying it by extraction is the mechanism working as designed.

**Five raises and lowerings have met on this number now, and it is none of
theirs — it is the merged tree's.** That is the rule this file's header states, and it is the one these
conflicts keep testing. Taking any branch's figure banks the others' slack as
headroom nobody earned; taking one side's *comment* deletes another's reason and
leaves a number nobody can account for. So every reason above stays, and
`ratchets.py remeasure` supplies the figure.
- **→ 4,251** (2026-08-16, B221): one line, and it is the whole cost of a new
  options bar. `LineOptionsBar` is a `UserControl` for the reason
  `BoneOptionsBar` and `GuideOptionsBar` are — the feature's thirty lines live in
  their own file — so what lands here is the single `<views:LineOptionsBar />`
  that puts it in the strip. A budget that refused this would be refusing the
  registration rather than the code, which is the opposite of what it is for.
- **→ 4,391** (2026-08-16): +140, and every line of it is markup shape rather
  than new surface. The icon set replaced the glyph buttons: a button whose
  face was a one-character `Content` attribute now carries a `<Path>` child
  (two for the stateful pairs — play/pause, the collapse chevrons), which
  costs two to five lines per button across some fifty buttons and adds no
  control, no handler and no binding that was not already there. The next
  extraction should still come out of this number; the raise only prices the
  shape the same buttons now have.
- **→ 4,405** (2026-08-16, B230/B231): +14 for the reference board's two
  always-present entry points — the View menu item and the Sheets docker's
  **Board…** button, which is B231's fix and has to be markup — and the
  workspace menu's tooltips saying what save, save-as and reset now actually
  do (B230).
- **4,405 → 4,416** (2026-08-17, Q88): +11 for the two reference locks on the
  canvas shortcut bar and the *Locked* checkbox in the Reference sheets docker.
  The standing note above applies unchanged — a button has no partial to live in
  — and the owner asked for these on the bar by name: locking is what an artist
  reaches for the instant a reference is where they want it, and the pointer is
  already on the canvas.
- **4,416 → 4,428** (2026-08-17, Q110): +12 for the Navigator docker and its
  View-menu entry. The standing note applies — a panel's markup and a menu item
  have no partial to live in — and the panel itself is nine lines because the
  drawing is a control (`NavigatorView`) rather than markup.
