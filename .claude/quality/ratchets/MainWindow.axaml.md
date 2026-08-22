# src/Lightbox.App/Views/MainWindow.axaml

budget: 4771

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
- **4428 → 4434** (2026-08-18), the X-sheet's *Drawing from pose* item. Six
  lines: the item, its visibility binding, a tooltip and the two-line comment
  saying why it is absent rather than disabled without a rig. The command
  already existed in the bone options; this is the second surface, and the
  owner asked for it on the sheet because that is where an artist working a
  cycle is looking. There is nowhere else for a menu item to live.
- **4434 → 4440** (2026-08-19), the effects window's menu item. Six lines: the
  item, its tooltip and the four-line comment saying why it is a window rather
  than a docker. It is the *whole* markup cost of the feature — the window is a
  file of its own, the view model is two more, and `MainWindow` gains a menu
  item, a shortcut case and one field. A budget that refused this would be
  refusing the registration rather than the code, which is the opposite of what
  it is for.
- **4440 → 4450** (2026-08-19), the effects menu. Ten lines and a net +4 over the
  entry it replaces: the owner asked for `Effects ▸ Fluid effects…` rather than
  a line under View, so the item moved into a top level of its own with the
  eight-line comment saying why. It is a top level rather than a View entry
  because View is where you say what you want to *look* at and everything here
  changes what is *in* the document — and because more of these are coming
  (goo, water, style inference), each of which would otherwise be another orphan
  among the dockers. A menu has nowhere else to live; the standing note above
  applies unchanged.
- **4434 → 4514** (2026-08-19, Q128): +80 for three menu surfaces, none of
  which has anywhere else to live. Undo and redo at the top of **Edit** (6
  lines plus the note on why the gesture text is literal); the two crops on
  **Image** (11); and a new top-level **Select** menu (44) gathering the seven
  marquee commands that until now existed only on keys — All, Deselect,
  Invert, Grow, Shrink, Delete contents, Fill with background. The commands
  themselves already existed in the view model and the crop's own logic went
  to `MainViewModel.Crop.cs`; what lands here is registration, which is the
  one thing a budget must not refuse. The comments are the larger half of the
  raise and are the part that says which entries are deliberately *not*
  disabled and why — a reader who deletes that reasoning re-derives B168 and
  B173 the hard way.
- **4514 → 4540** (2026-08-19): +26 for the undo-history docker's row density,
  asked for alongside the menu above. Two setters and the comment saying why
  they have to exist: a `ListBoxItem` takes the Fluent theme's own padding
  unless something overrides it, which made these rows half again as tall as a
  layer row for no reason anybody chose. The Project docker already carries the
  identical pair — the third copy of a two-line style is the point at which it
  should become a class in `Density.axaml`, and the next panel that needs it
  should do that rather than paste it again.
- **4540 → 4554** (2026-08-19, Q128): +14 for the Crop tool's rail button and
  the one line that hosts its options bar. The button is the tool's only
  permanent surface and a `ToggleButton` has no partial to live in; the bar
  itself is `CropOptionsBar.axaml`, the trade `BoneOptionsBar`,
  `GuideOptionsBar` and `LineOptionsBar` each made, so a bar with a readout and
  two buttons costs one line here instead of fifteen. The overlay went to
  `CropOverlayPainter`, the drag math to `CropSession`, and the pointer wiring
  to `CanvasControl.Crop.cs` — what is left in this file is registration, which
  is the one thing a budget must not refuse.
- **→ 4,570, remeasured on the merged tree** (2026-08-19). Two branches raised this
  number from 4,434 at once and neither figure is right for the tree they now
  share: the effects menu wanted 4,450, the crop and menu work wanted 4,554, and
  taking either would bank the other's growth as headroom nobody earned. Every
  reason above stays — deleting one leaves a number nobody can account for —
  and `ratchets.py remeasure` supplies the figure, which is the one moment that
  script exists for.
- **4434 → 4440** (2026-08-18), the timeline's *Bones* toggle. Six lines: the
  checkbox, its visibility binding, a tooltip and the three-line comment saying
  why per-bone rows are off by default. The armature's summary row costs no
  XAML at all — it is a `TrackRow` the view model projects — so this is the
  whole UI cost of the pose track.
- **→ remeasured again on the merged tree** (2026-08-19). Same situation one
  merge later: the *Bones* toggle above was raised to 4,440 on its own branch
  from the same 4,434, so it is a third claim on that number and its six lines
  are additional to everything the remeasure before it counted. Its reason
  stays, the figure comes from `ratchets.py remeasure`.
- **4,576 → 4,712** (2026-08-20): +136 for menu mirroring — the Layer and
  Animation top-level menus, Edit ▸ Transform, and the View menu's six view
  transform entries. Every command already existed in a docker, a bar or a
  context menu; what lands here is registration, which is the one thing a
  budget must not refuse (Q128's precedent). Photoshop and Krita both mirror
  their panels' verbs in the menu bar, and Krita's one exception — animation
  ops living only in the timeline docker — is its most-cited papercut; an
  animation-first application should not repeat it. The comments carry the
  which-half-stays-in-the-docker reasoning and are the larger part of the
  raise.
- **4,576 → 4,586** (2026-08-20, Q84): +10 for the Scene panel's two entry
  points — its Docker host (the panel itself is `ScenePanel.axaml`, the
  `BoneOptionsBar` trade again) and its View ▸ Dockers checkbox. Registration,
  which is the one thing a budget must not refuse.
- **→ 4,722, remeasured on the merged tree** (2026-08-20). The two raises
  above left the same 4,576 on parallel branches — the menu mirroring wanted
  4,712, the Scene panel wanted 4,586 — and taking either would bank the
  other's growth as headroom nobody earned. Both reasons stay, and
  `ratchets.py remeasure` supplies the figure, which is the one moment that
  script exists for.
- **→ 4,726** (2026-08-20): the character library's picker button on the
  project panel (Q138). One button, four lines — the flyout it opens is built
  in `MainWindow.ProjectFiling.cs` where it can live in a partial, which is
  why the entry point costs four lines and not forty. Registration again: an
  import nobody can reach is not a feature.
- **4,726 → 4,771** (2026-08-21, Q143): +45 for the variant surfaces on the
  project docker's row — the Variant submenu (the Glyph menu's entry pattern,
  which needs its template and style inline), the two override gestures
  beside Duplicate, and the viewed-variant badge in the row's right column.
  All of it is the row template's own flyout and markup, which has no partial
  to live in, and the comments carry the why (view state, the Duplicate
  parallel, the badge's reason to exist). Registration: a variant nobody
  could view was the whole defect this lands to fix.
