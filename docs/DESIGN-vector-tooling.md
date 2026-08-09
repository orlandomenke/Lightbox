# Vector tooling: making the lines you already drew editable

Status: **agreed design; phase 0 landed except rotate and scale, phases 1 and 2
landed 2026-08-08, phase 3 and phase 4a (pinch) landed 2026-08-09; phase 4's
other three parts not started.** Decisions Q47–Q53, answered 2026-08-07.
Unblocked by Q26, which has been answered since the same day and which two other
documents still describe as open — see *Corrections* at the end.

Two things phase 1 learned that the design did not predict, both worth having
before phase 2 builds on them:

- **The fit's tolerance is not the flatten's tolerance, and they pull opposite
  ways.** Flattening is a rendering step and wants to be invisible (0.25 px);
  fitting is an *authoring* step and wants a handful of nodes a hand can work
  with (1.5 px). A fit tight enough to be invisible puts a node on every wobble,
  which is a path nobody can edit — the tool would appear to work and be useless.
- **Reshaping loses the line's weight unless something carries it, and phase 2
  had to add that.** A drawn stroke has a pressure at every one of its hundreds
  of points; a fit keeps only the pressures at the handful of places its nodes
  landed, so re-flattening turns a confident taper into three straight ramps.
  Measured on an ordinary tapered arc: the peak drops from 1.00 to **0.89** on
  the first node drag. `PressureProfile` re-applies the original weight by
  normalised arc length, so it stretches with the edit instead of being
  resampled away. This is not a nicety — the roadmap item is worded *"a drawn
  line can be re-shaped **and keeps the mark it was drawn with**"*, and pressure
  is the part of the mark an animator notices first.
- **Flatten had to be uniform rather than recursive-adaptive, and pressure is
  why.** De Casteljau subdivision is the textbook answer and it loses the curve
  parameter as it goes, so pressure would have to be carried through the
  recursion or interpolated along the wrong variable. Uniform steps keep `t` in
  hand at every sample; the cost is a few extra points on an S-curve, in a record
  that already holds hundreds of drawn points per stroke.

Two things happened alongside phase 0 that are not phases and are worth finding
here rather than only in the ledgers: **B132** (a symbol could not be placed on a
vector layer) and **Q52**'s UI removal were both settled by collapsing
`PaintedFrame` and `VectorFrame` into one `Frame` on 2026-08-08. That touched the
record and the serialized format, so it is named in the phase table below even
though it belongs to no phase.

## The thing that reframes the job

Lightbox calls itself *"a raster + vector desktop application"*, and the vector
half is one true sentence and one missing feature.

**A Lightbox stroke is a centreline with a width at every point.** In Toon Boom
Harmony that is exactly a **pencil line** — *"vector information about their
center line and the width of the line"* — as against a brush line, which is
stored as an outline and can only be edited on its contour. Harmony had to build
a separate **Centerline Editor** to give brush strokes what Lightbox's strokes
have by construction. Disney's **Meander**, the Sci-Tech Award-winning hybrid
vector/raster system behind *Paperman* and *Feast*, is the same idea: strokes
recorded as geometry, so they stay editable and can be interpolated.

So the architecture is already right. What is missing is stated plainly by the
roadmap itself:

> `VectorFrame` holds `List<Stroke>` and **nothing reaches into one after it is
> drawn.** — `ROADMAP.md:160`

**The whole task is tools that reach into the strokes that already exist.** Not a
vector layer, not a shape model, not SVG.

## Why not do what Krita did

Krita bolted an SVG shape editor onto a paint program, so its vector layers are a
second world. Two consequences, both fatal for line art and both quotable:

- *"SVG layers can contain filled areas and even text, but they don't actually
  contain brush strokes, which makes them useless for most line art."*
- **You cannot use the brush tool at all while a vector layer is selected.**

Clip Studio Paint did the same idea properly — brush strokes on vector layers,
control points, a vector eraser that erases up to an intersection — and still
pays for the split: vector layers are widely reported as sluggish and cannot hold
fills.

**Lightbox has no second world to pay for.** One `Stroke` record, one
`BrushEngine`, one render path. That is the asset this design exists to spend
carefully, and the rule that follows is the first entry under *What this must not
become*.

## What makes a tool feel safe

The research is one-sided. Every tool that feels safe makes geometry editing a
**mode you enter deliberately**:

| Tool | How you get in | How you get out |
| --- | --- | --- |
| Illustrator | double-click (isolation mode *"automatically locks all other objects"*) | Esc |
| Figma | Enter, or double-click | Esc |
| Grease Pencil | switch to Edit mode | switch back |

Every tool that feels mushy uses **a modifier you have to remember**. Krita's own
vector-tool wiki: *"Alt+drag allows you to start a rubber band without
accidentally selecting and moving a shape."* Inkscape's node tool: *"the drag
must not begin on a path unless Shift is used."*

**Modes are safe by default. Modifiers are unsafe by default and ask you to
remember the antidote.** That is the whole of what Q53 buys.

## The record — one optional field, and `Points` stays the truth

Q47 chose Bezier handles on every node. The obvious implementation — widening
`StrokePoint(X, Y, Pressure)` — is a migration and a second curve type in the
renderer, and it is **avoidable**, because a drawn stroke and an authored path are
different things. A drawn stroke has hundreds of sampled points and wants no
handles; a pen path has a dozen authored nodes and wants nothing else.

```csharp
// Stroke.cs — one nullable property, absent unless used
public StrokePath? Path { get; set; }

public sealed class StrokePath
{
    public List<PathNode> Nodes { get; set; } = [];
    public bool Closed { get; set; }
}

public readonly record struct PathNode(
    double X, double Y, double Pressure,
    double InX, double InY,      // handle offsets, relative to the node
    double OutX, double OutY,
    bool Corner);                // handles independent rather than mirrored
```

| | |
| --- | --- |
| **`Points` remains what renders** | `BrushEngine`, `FrameRasterizer`, `StrokeIndex`, `ContourTracer`, `TransformOps` and the AI wire format (`StrokeWire.PointDto`, `MaxWirePoints = 32`) are unchanged. Invariant 1 holds without an argument |
| **Absent unless used, both ways** | A hand-drawn stroke writes no `path` key; clearing a path removes it. `Assert.DoesNotContain("\"path\"", json)` ships in the same commit |
| **No second renderer, no migration** | Editing a node re-flattens `Path → Points` and the engine stamps the new polyline exactly as before |
| **Every existing drawing is editable** | On demand, by fitting a path (Q50) |

### The invariant this creates

> **A stroke's `Path` and `Points` must never disagree.** Any operation that maps
> points maps the path's nodes and handles too, or drops the path.

`TransformOps.TransformStroke` is the first caller that must obey it and
`StrokeInterpolator` the second. Asserted by a test, not by a comment: transform
a stroke that has a path, and flattening the transformed path must reproduce the
transformed points.

## The tools

**Four things, and what makes them safe is that each does exactly one.**

| | Key | What it touches |
| --- | --- | --- |
| **Select** — black arrow | `V` | Whole strokes. Click, shift-click, drag a box. Then move, rotate, scale, delete, recolour. Double-click enters isolation |
| **Direct select** — white arrow | `A` | Individual nodes and handles, on the isolated stroke |
| **Pen** | `P` | Places nodes. Click = corner; click-and-drag = smooth node with handles |
| **Isolation** | double-click in, `Esc` out | Not a tool, a state. Everything else greys and stops responding |

Pen modifiers follow Illustrator so the muscle memory transfers: `Alt` on a
handle breaks the pair, `Ctrl` temporarily gives Direct select, `Shift`
constrains to 45°, clicking the first node closes the path.

Then the reshaping set — **CSP's `Correct line` list, which is the proven minimum
for stroke-level editing**, and notably contains no booleans and no shape
primitives:

- **Pinch a segment** — drag the line between two nodes, no node selection. The
  one artists reach for most.
- **Width along the line** — Illustrator's Width tool over the `Pressure` array.
  Its "width points" are Lightbox's per-point pressure under another name.
- **Simplify** — fewer nodes, with a live count.
- **Cut** and **join**.

### Isolation mode is cheap because the pattern is already here

The transform tool is a modal session with exactly this shape: state on the view
model (`TransformActive`, `BeginTransform`, `CancelTransform`), a gizmo owning
interactive state on the canvas, a zoom-invariant hit test
(`tol = 10.0 / (FitScale() * _zoom)`), an overlay drawn inside the document
transform with every dimension divided by `view.Scale`, `Enter`/`Escape` handled
**above** the shortcut switch, and `ToolMode` saved and restored around the
session.

`PathEditSession` is a second instance of that, not a new mechanism. The roadmap
reserved the name.

## The one genuinely new primitive

There is no stroke-under-point query anywhere in the codebase. **All three pieces
exist and are tested; nothing composes them.**

| Piece | Where | Note |
| --- | --- | --- |
| `StrokeIndex.Intersecting(SKRectI)` | `src/Lightbox.Raster/StrokeIndex.cs` | Visible only to `TiledRasterizer` today |
| `GeometryOps.DistToSegment` | `src/Lightbox.Core/Geometry/GeometryOps.cs` | |
| `BrushEngine.CommitBounds` | `src/Lightbox.Raster/BrushEngine.cs` | Already widened by dab reach, blur and clip feather |

`StrokePicker` queries the index with a tolerance box, refines with
`DistToSegment`, and returns **topmost-first**. `StrokeIndex`'s own contract is
*ascending record position, not speed* — because a renderer that returns strokes
in the wrong order draws a different image. The picker reverses it, and says so
where it does, because getting that backwards picks the line underneath and looks
like a tolerance bug.

`CanvasToolMode.Select` and the whole `SelectionManager` picking path already
exist and are **orphaned** — `SyncCanvasToolMode` never assigns that mode, so
none of it is reachable. Phase 0 revives it rather than writing a parallel one.

## What phase 3 learned

The pen is the first tool here that **creates** a path, which breaks this
document's own title — the rest of it is about lines you already drew. Two
things fell out of that, neither predicted:

- **A live preview a pen can afford is not the preview a shape tool uses.** The
  shape tool stamps the real brush into the scratch surface on every pointer
  move, which is right for a gesture that lasts one drag and is exactly wrong for
  one that lasts a dozen clicks: a full-canvas clear and re-stamp per move, for
  as long as the artist is thinking. So the pen traces the flattened path as
  chrome and stamps the brush once, at the commit. **The general form is worth
  keeping: the cost of a preview is set by how long the gesture lives, not by how
  much work one frame of it is.**
- **Escape had to mean the opposite of what the neighbouring tool does.** The
  polygon selection cancels on Escape, and copying that would have thrown away a
  minute of placed nodes on the key everybody presses to mean "I'm done". The
  line is whether the thing in progress is *artwork*: a selection is not, a path
  of twelve nodes is. Both Enter and Escape finish and keep, and losing it is
  `Ctrl+Z` — which works because the whole path is one undo step.

A third thing was found rather than learned, and it belongs to phase 2: the node
overlay is drawn whatever the tool is, so leaving isolation for the brush left
glyphs on screen over a line nothing could reshape any more. B147's exact shape,
one tool along. Both arrows keep the session now and everything else ends it.

## Phases

One branch, one objective.

| | Branch | What |
| --- | --- | --- |
| **0** | `feat/canvas/stroke-selection` | `StrokePicker`; `SelectedStrokeIds`; the black arrow; move/rotate/scale/delete/recolour selected strokes through the existing transform session with a stroke-id filter. **No record change** |
| **0** | *landed, partly* | Picker, selection, arrow, move, delete and recolour shipped (PRs #74, #75). **Rotate and scale did not**, and neither did the route this row specifies: no `TransformScope` can mean *"these strokes inside this cel"*, so move/delete/recolour went through `DocumentEditor.PerformDelta` instead of the transform session. Finishing phase 0 means adding that scope, and it is a separate objective |
| **—** | `fix/project/B132-one-frame-class` | Not a phase. `PaintedFrame` + `VectorFrame` → one `Frame` with a nullable baseline and nullable placements; the Raster/Vector picker and the R/V badge removed. **A record and format change**: closes B132, completes Q52's UI half, and drops `kind` and empty `pngBase64` from the file |
| **1** | *landed* | `StrokePath`, `PathNode`, `Stroke.Path`, `PathFlattener`, `CurveFitter` (Schneider), the agreement invariant obeyed at all three callers that map points. **No UI**, as specified. A 121-point arc fits to 4 nodes and flattens back within 1.2 px |
| **2** | *landed* | `PathEditSession`, isolation, the white arrow, the node overlay — plus `PressureProfile`, which the design did not predict and the roadmap item's own wording requires. The white arrow is `N`, not `A`: `A` is this application's black arrow and has been documented as such |
| **3** | *landed* | The pen and its four modifiers, plus its icon and `P`. `PenSession` authors a `StrokePath` and the view model writes one ordinary stroke from it — the preview is a traced overlay rather than a stamped one, because a pen session outlasts a drag |
| **4** | *splitting; pinch landed 2026-08-09 as* `feat/canvas/pinch-a-segment` | Pinch, width, simplify, cut, join — **four branches rather than one**, see below |

**Phase 4 is four objectives wearing one number.** The row above was written as
a set because CSP presents it as one — *Correct line* — and that is a fair
description of what an artist reaches for, not of what gets built. Pinching a
segment is a solve over two control points; width is the pressure array and
nothing else; simplify is the fitter with its tolerance turned up; cut and join
change how many strokes exist. They share a session and share nothing else, and
`CLAUDE.md`'s test applies unchanged — *if the sentence describing the branch
needs an "and", it is two branches*. So:

| | Branch | What |
| --- | --- | --- |
| **4a** | *landed* `feat/canvas/pinch-a-segment` | Drag the curve between two nodes. `SegmentDrag` in Core, because the interesting part is arithmetic |
| **4b** | `feat/canvas/line-width` | Illustrator's Width tool over the `Pressure` array — which means editing `PressureProfile`, not the flattened points, or the invariant re-flattens the edit away |
| **4c** | `feat/canvas/simplify-a-line` | `CurveFitter.Fit` at a larger tolerance, with the node count shown live. The fitter already takes the parameter |
| **4d** | `feat/canvas/cut-and-join` | The only one that changes how many strokes a frame holds, which is why it is last and alone |

**Named so scope cannot drift into them:** cross-frame reshaping (needs the
correspondence work the inbetweener's verifier depends on); SVG export
(`ROADMAP.md:209` — *"should not be faked"*, honest only once the vector side is
richer); the app drawing its own icons (`ROADMAP.md:211`, which the roadmap
already makes depend on exactly this); boolean operations
(`DESIGN-performance.md:101` reserves the perf-sweep row and nothing else claims
them).

## What this must not become

- **Not a second geometry world.** Krita's vector layer cannot take a brush; that
  is the whole failure. Every tool here operates on the one `Stroke` record.
- **Not a second renderer.** `Points` renders; a path flattens to points. There is
  no path-stroking code path, and `SKPaintStyle.Stroke` still never touches
  artwork.
- **Not retained shapes.** Q49. A rectangle is a line you can now reshape.
- **Not required.** An artist who never touches these tools gets no new keys in
  their files and no change in their renders.

## Verification

1. **Picking is exact and topmost-first.** Two overlapping strokes; a click in
   the overlap picks the one drawn later. A click one pixel outside a thin
   stroke's reach picks nothing.
2. **Absence holds both ways.** A hand-drawn stroke serializes with no `path`
   key; a path created and then cleared leaves the JSON byte-identical.
3. **Path and points cannot disagree.** The invariant above, asserted.
4. **Reshaping keeps the mark.** `ReshapingALineKeepsItsBrush`.
5. **The grain shift is asserted, not hidden.** A test pins that moving a
   textured stroke *does* change its dab pattern, naming Q26 — so the accepted
   behaviour is written down rather than discovered later as a bug.
6. **Fitting is reported and reversible.** The node count is stated; one undo
   restores every original point exactly.
7. **The inbetween rule is visible.** Both the carried and the not-carried status
   messages asserted — Q51's mitigation is part of the decision, not decoration.
8. **Isolation actually isolates.** With a stroke isolated, a click on any other
   stroke changes nothing.

## Corrections this design has to make

Found while auditing. Each currently misleads a reader about this exact work.

1. **`ROADMAP.md:1098` and `brush-engine-gap-analysis.md` (six places) cite
   "Q19" as the blocking seed-origin question.** Q19 is *"Are Linux and macOS
   shipping targets"*, answered (a). The real question is **Q26, answered**. Both
   documents therefore report vector work as blocked when it is not — and the gap
   analysis recommends **arc-length seeding, which is one of the options Q26
   explicitly rejected.** Root cause is already filed as **B81**.
2. **`ROADMAP.md:161`** still says re-shaping "needs a decision in `QUESTIONS.md`
   before it needs code". Q26 closed it.
3. **`ROADMAP.md:80`** still says symmetry's record question must be answered
   first. Q15 closed it.
4. **`docs/manual/13-keyboard-and-troubleshooting.md`** lists shape tools, vector
   guides, perspective rulers and grid snapping as *Planned*; all four are built,
   green with resolving anchors, and documented in sections 3 and 6.
5. **Q26's manual line does not exist yet** — nothing tells an artist that moving
   a textured line changes its grain.
6. **The appendices file vector under Pillar 4** while the roadmap body files it
   under Pillar 0, and `ROADMAP.md:1199` writes "Pillar 4 (Drawing floor)",
   conflating the two.

## Sources

- Toon Boom — [About the Pencil Tool](https://docs.toonboom.com/help/harmony-22/advanced/drawing/about-pencil-tool.html), [Centerline Editor](https://learn.toonboom.com/modules/traditional-animation-drawing-tools/topic/centerline-editor)
- Disney Animation — [Meander](https://disneyanimation.com/technology/meander-1/); [Computer-assisted animation of line and paint in *Paperman*](https://www.researchgate.net/publication/254463426_Computer-assisted_animation_of_line_and_paint_in_Disney's_Paperman)
- Krita — [Vector Graphics manual](https://docs.krita.org/en/user_manual/vector_graphics.html), [Vector Tool Reported Bugs](https://phabricator.kde.org/w/krita/vector_tool_reported_bugs/), [Overview of Krita's vector tools](https://www.virtualcuriosities.com/articles/1407/overview-vector-tools-in-krita)
- Clip Studio Paint — [Vector layers](https://help.clip-studio.com/en-us/manual_en/180_layers/Vector_layers.htm), [Correct line tool](https://www.clip-studio.com/site/gd_en/csp/userguide/csp_userguide/500_menu/500_menu_layer_new_vector_edit_senshusei.htm)
- Adobe Illustrator — [Isolate objects](https://helpx.adobe.com/illustrator/desktop/manage-objects/select-objects/isolate-objects.html), [Width tool](https://helpx.adobe.com/illustrator/using/tool-techniques/width-tool.html), [Curvature tool](https://helpx.adobe.com/illustrator/using/tool-techniques/curvature-tool.html)
- [Figma — Edit vector layers](https://help.figma.com/hc/en-us/articles/360039957634-Edit-vector-layers)
- [Inkscape — Editing paths with the node tool](https://inkscape-manuals.readthedocs.io/en/latest/editing-paths.html)
- [Blender — Grease Pencil sculpting](https://docs.blender.org/manual/en/latest/grease_pencil/modes/sculpting/introduction.html)
- [Adobe Animate — merge versus object drawing](https://helpx.adobe.com/animate/using/drawing.html)
- [Joint Stroke Tracing and Correspondence for 2D Animation, TOG/SIGGRAPH 2024](https://markmohr.github.io/JoSTC/); [LayerInbetween, SIGGRAPH 2026](https://dl.acm.org/doi/10.1145/3811364)
- [Topology-Driven Vectorization of Clean Line Drawings, Disney Research](https://media.disneyanimation.com/uploads/production/publication_asset/2/asset/Topology-Driven_Vectorization_of_Clean_Line_Drawings.pdf)
