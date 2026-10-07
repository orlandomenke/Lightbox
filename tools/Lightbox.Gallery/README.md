# Lightbox.Gallery

Lightbox's real controls, in their named states, under candidate looks. It's a
sandbox for trying out a treatment before it reaches the app (Q203). It isn't
shipped.

It loads the app's own `Styles/AppResources.axaml` and `Styles/AppStyles.axaml`,
so what it shows is what the app would draw, not a copy. `SharedStylesTests`
keeps the two loading the same files.

## Run it

```bash
dotnet run --project tools/Lightbox.Gallery
```

The bar across the top switches the three independent parts of a look:

- **Theme**: `Current` (as shipped), `DarkLit` or `StudioGrey`.
- **Corners**: `Current`, `Square` (0), `Tight` (4 · 6) or `Soft` (8 · 14).
- **Light effects**: drop shadows and glows on or off. The rim light on the
  top edge and the sunken fields stay on either way, because they cost nothing.

The UI scale slider is the app's own interface scale.

### Live editing

Run from the repository, the gallery reads `Looks/*.axaml` straight from the
source tree and watches them. Save a file and the window redraws a quarter of a
second later, with no rebuild. A file that won't parse shows as an orange
"Could not load" line at the top, and the rest of the gallery keeps working.
What lives in code still needs a rebuild: the corner sizes and shadow strengths
in `Look.cs`, and the stories in `Stories/`.

The look files name the app's types as `clr-namespace:…;assembly=Lightbox.App`
rather than `using:…`, because the runtime loader resolves only the
assembly-qualified form.

## The rules the looks follow (Q203)

These were settled in the gallery, one decision at a time. Each one lives in a
single place, and everything else names it.

**Tokens, not numbers.** Spacing and sizes come from `Looks/Tokens.axaml`:
panel padding, header padding, tab padding, row height, row gap, column gap,
value-field width and icon slot. Radii come from the corner set's five roles
(see `Corners.Styles.axaml`). An override is allowed, but it names the token it
bends, in a comment at the site.

**One edge for a docker's content.** A docker's padding insets everything in it:
dropdown, sliders, rows. Nothing inside carries a margin of its own.

**One layout for every setting row.** The control comes first, then a fixed
value column, then an icon column. A row without a value or an icon still keeps
those columns, so every row ends on the same line.

**Flat.** Buttons have no rim and no shadow. "On" is a quiet lighter fill: the
tool in hand, a toggle that is on, the layer being drawn on, the chosen
segment, the canvas bar's active icon.

**Hover is active at half strength, in the same shape.** This holds for tabs,
menus, dropdowns, selectors and the canvas bar.

**Text at rest.** Dropdowns, selectors and the canvas bar's buttons show no
well and no border until you point at them.

**Tabs are v.2.** The active tab is part of the panel, rising into a darker
header strip.

**Edges catch light.** Containers get chrome in the dark theme and prism in the
light one. The brush is picked once, as `LookEdgeBrush`, so a container never
asks which theme it's in. Docker frames have their own switch
(`LookDockerEdgeBrush`), because their edge is still being judged.

## Snapshots

```bash
dotnet run --project tools/Lightbox.Gallery -- --snapshot gallery-snapshots
```

Add `--live` to render the look files as they are on disk instead of as
built. This renders every story under every look to
`gallery-snapshots/<look>/<story>.png`: 13 looks × 14 stories, headless on
Skia, 960 px wide. Use it to put before-and-after images in a pull request.

## How a look is applied, and why it rebuilds

The app names its colours with `StaticResource` in 176 places, and a
`StaticResource` is resolved once, when the control loads. A look is therefore
applied by **reloading** the resources and styles with the look's overrides on
top, and then building the window again. Swapping brushes in place would leave
every existing control on the old colours.

Two traps were found by measuring the snapshots rather than by reading the
code:

- **Values written inline in a `ControlTemplate` beat every style.** The docker
  frame and the canvas bar kept their shipped outline under every look until
  their border moved into style setters (`PART_Frame`, `PART_Bar`).
- **Avalonia draws no box shadow on a border with uneven thickness.** A
  `0,1,0,0` top rim turned every drop shadow off without any error. The rim is
  drawn as a 1 px inset shadow instead, so the rim and the drop shadow are one
  `BoxShadows` value.

## Adding a story

Add a `Story` to `Stories/Catalog.cs`. A story is a component and its named
states, each one a builder (not a control, because of the rebuild above). Use
the app's real controls and style classes. When a part can only be assembled
rather than reused, as the layer rows are, say so in the story's note.
