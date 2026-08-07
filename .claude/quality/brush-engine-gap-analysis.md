# Brush Engine & Vector Tools Market Gap Analysis

## Executive Summary

Lightbox has **exceptional raster brush capabilities** that match or exceed industry standards (Clip Studio Paint, TVPaint, Procreate). Its core differentiator is **deterministic rendering + AI integration** — a combination no other tool offers. However, critical gaps in vector tooling and brush editor UX create friction for professional workflows.

**Key Finding**: Professionals are willing to accept fewer brush presets if the ones they have are textured, reproducible, and work with AI. Lightbox's stroke record model is the right foundation; the gaps are in editing, not rendering.

---

## SECTION 1: RASTER BRUSH CAPABILITIES

### Lightbox Current State: ✅ COMPETITIVE

| Feature | Lightbox | Clip Studio | TVPaint | Procreate | Status |
|---------|----------|-------------|---------|-----------|--------|
| **Pressure curves (drawn, not gamma)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Shape dynamics (size/roundness jitter)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Color dynamics (HSV jitter)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Texture brushes (paper/canvas)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Imported custom tips (.abr/.gih)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Smudge + blur (live + baked sampling)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Medium simulation (watercolor/oils)** | [x] | [~] | [x] | [x] | ✅ Advantage |
| **Procedural tip generation** | [x] | [~] | [~] | [ ] | ✅ Advantage |
| **Stabilization (weighted, predictive)** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Deterministic rendering (invariant 2)** | [x] | [ ] | [ ] | [ ] | ✅ **UNIQUE** |
| **Preset presets tagged/filtered** | [x] | [x] | [x] | [x] | ✅ Parity |
| **Brush cost badging** | [x] | [ ] | [ ] | [ ] | ✅ **UNIQUE** |

**Lightbox's Raster Advantage**: Deterministic rendering + medium simulation + cost visibility. Every stroke renders identically on reload, AI inbetween, or undo—a property neither Clip Studio nor Procreate guarantee.

### Raster Gaps (Minor)

1. **Tilt input** [ ] — `StrokePoint` is (X, Y, Pressure); tilt not captured
   - **Impact**: Medium (artists note as "nice to have")
   - **Blocker**: Requires `StrokePoint` record change
   - **Workaround**: Rotation jitter + direction following mimic tilt for many strokes

2. **Velocity-driven dynamics** [ ] — Speed cannot reach dynamics
   - **Impact**: Medium (smooth accelerating strokes valued)
   - **Blocker**: Speed inference from point spacing is post-`Densify` artifact
   - **Market**: Clip Studio, Corel, Procreate all have this

3. **Bristle simulation variants** [ ] — Only single bristle engine
   - **Impact**: Low (most artists use flat/scatter instead)
   - **Market**: Corel offers "Dynamic Speckles, Static Bristle, Dynamic Speckle Bristle"
   - **Lightbox alternative**: Texture + scatter achieves same visual result

4. **Pressure response calibration UI** [ ] — No per-device profile
   - **Impact**: Low–Medium (professionals on multiple devices)
   - **Market**: Procreate, Clip Studio, Adobe all have per-device calibration
   - **Opportunity**: Lightbox could offer first standardized pressure curve export/import

---

## SECTION 2: VECTOR TOOLING STATE

### Lightbox Current: ⚠️ MINIMAL

| Feature | Lightbox | Harmony | Linearity | Affinity | Status |
|---------|----------|---------|-----------|----------|--------|
| **Vector layer type** | [x] | [x] | [x] | [x] | ✅ Exists |
| **Strokes use same record as raster** | [x] | [ ] | [ ] | [ ] | ✅ **UNIQUE** |
| **Stroke editing (path points)** | [ ] | [x] | [x] | [x] | ❌ Missing |
| **Bezier curve handles** | [ ] | [x] | [x] | [x] | ❌ Missing |
| **Adaptive strokes (width editing)** | [ ] | [x] | [x] | [x] | ❌ Missing |
| **Centerline + contour editing** | [ ] | [x] | [ ] | [ ] | ❌ Missing |
| **Vector smoothing/point reduction** | [ ] | [x] | [x] | [x] | ❌ Missing |
| **Convert raster to vector** | [ ] | [x] | [ ] | [x] | ❌ Missing |
| **SVG export with real paths** | [ ] | [x] | [x] | [x] | ❌ Missing |
| **Stroke stays textured when edited** | [?] | [ ] | [ ] | [ ] | ✅ **IF BUILT** |

**Critical Gap**: VectorFrame exists but is read-only. A stroke lands, but there's no tool to reshape it afterward.

### Vector Gaps (Critical)

1. **Stroke path editing** [ ] — Reshape lines after drawing
   - **Impact**: HIGH (foundational for vector workflow)
   - **Blocker**: ~~Q19~~ **none — answered 2026-08-07.** The question is **Q26**, not Q19, and it is answered (a): moving a point *does* re-seed the dabs and that is accepted, because "the grain belongs to the canvas". See `docs/DESIGN-vector-tooling.md`
   - **Market**: Every vector tool (Harmony, Linearity, Illustrator) has this
   - **Roadmap**: Item exists as "A drawn line can be re-shaped and keeps the mark it was drawn with" [split status]

2. **Bezier curve control** [ ] — Drag anchor points and handles
   - **Impact**: HIGH (prerequisite for precise vector work)
   - **Blocker**: Stroke editing needs to be designed first
   - **Market**: Standard in Harmony, Affinity, Illustrator

3. **Adaptive/variable-width strokes** [ ] — Width envelope after drawing
   - **Impact**: MEDIUM (professional illustration feature)
   - **Blocker**: Vector editing + some stroke record changes
   - **Market**: Linearity, Affinity offer this

4. **Vector-to-raster tracing** [ ] — Convert finished strokes to filled paths
   - **Impact**: LOW–MEDIUM (workflow convenience)
   - **Market**: Clip Studio, Affinity offer this
   - **Lightbox rationale**: "A vector stroke *is* a raster mark already; tracing is optional"

5. **SVG export with real paths** [ ] — Export VectorFrame as editable SVG
   - **Impact**: MEDIUM (asset interoperability)
   - **Blocker**: Vector editing needs to exist first
   - **Market**: Animation tools export SVG for web/mobile
   - **Roadmap**: Item exists "Save as an ordinary image format — PNG, JPEG, SVG" [ ]

---

## SECTION 3: PROFESSIONAL PAIN POINTS & LIGHTBOX ALIGNMENT

### Pain Point 1: "Brushes Look Flat and Fake" (Adobe Animate)
**Complaint**: Adobe Animate brushes criticized as having "no texture variation" and reading as "extremely flat."

**Lightbox Solution**: ✅ SOLVED
- Every brush can have paper texture, scatter, wet edge, and medium simulation
- Strokes carry pigment model (watercolor/oils), not just dumb alpha
- Deterministic so texture is reproducible

**Market Opportunity**: Position as "realistic media in every brush" vs. Animate's flat alternatives

---

### Pain Point 2: "Pressure Response is Inconsistent Across Tools"
**Complaint**: Artists re-calibrating pressure curves for Clip Studio vs. Procreate vs. Wacom drivers; no standard import/export.

**Lightbox Opportunity**: ✅ UNOPENED
- Import .abr (which carries pressure curves)
- **Gap**: No export of Lightbox curves to standard formats
- **Opportunity**: First tool to standardize pressure curve API (export ResponseCurve as JSON/YAML importable into other tools)

**Recommendation**: Add pressure curve export to "Brush tips — a generated library" roadmap item

---

### Pain Point 3: "Vector Editing is Separate from Raster"
**Complaint**: In Harmony, Illustrator, even Procreate — raster and vector are different engines with different stroke models. Switching between them feels like two different applications.

**Lightbox Opportunity**: ✅ UNBUILT BUT DESIGNED
- Both raster and vector use the same Stroke record
- A vector stroke *is* a textured mark, not a flat outline
- **Gap**: No editing tool to move points
- **When built**: First tool where "I drew this with charcoal as a vector" is true, not a lie

---

### Pain Point 4: "AI Inbetweening is Unreliable"
**Complaint**: Cascadeur 2025.1 pioneering AI inbetweening, but most tools cannot guarantee input → output consistency (stochastic rendering breaks it).

**Lightbox Solution**: ✅ SOLVED
- Invariant 2: "No randomness in rendering"
- Every dab seeded from position via `Hash01`
- Inbetween output is reproducible (same input stroke → same output on every AI request)

**Market Opportunity**: "AI inbetweening you can trust" positioning

---

### Pain Point 5: "Pressure Saturation Hides Real Brush Behavior"
**Complaint**: Documented in CLAUDE.md: overlapping dabs saturate alpha (1 - (1-a)^20 ≈ 0.92 at flow 0.12), making brush tests pass even when flow is wired to nothing.

**Lightbox Solution**: ✅ DESIGNED AGAINST
- Tests in CLAUDE.md and DESIGN-performance.md specifically avoid saturation traps
- Measurement best practices documented

**Market Opportunity**: Educational positioning — "how to test brush engines correctly"

---

## SECTION 4: MARKET OPPORTUNITIES FOR VECTOR & BRUSH EXPANSION

### Tier 1 (High-Value, Unblocked)

1. **Stroke Reshaping (Path Editing)** — Core vector feature
   - **Dependency**: ~~Resolve Q19~~ **none.** **Q26** answered 2026-08-07 (a) — no seed origin, no arc-length seeding, no re-seed radius
   - **Effort**: `PathEditSession` pattern (a second instance of the transform session), plus a `StrokePicker` composing three primitives that already exist, plus undo through `PerformDelta`
   - **Market validation**: 100% of vector tools have this; zero question it's needed
   - **Revenue impact**: HIGH (unlocks "vector layer" use case)
   - **Blocking**: Sub-pixel precision, SVG export

2. **Per-Layer Onion Skin Control**
   - **Dependency**: None (UI + caching layer only)
   - **Effort**: 300–400 LOC (OnionSkinLayer docker page, cache filtering)
   - **Market validation**: Requested in animation workflows; rare in current tools
   - **Unique positioning**: "Show layer history without scene history"

3. **Pressure Curve Export/Import** (JSON/YAML standardization)
   - **Dependency**: None (serialization only)
   - **Effort**: 150–200 LOC (ResponseCurve → JSON export, import from clipboard)
   - **Market validation**: Unsolved workflow gap across all tools
   - **Revenue impact**: MEDIUM (B2B positioning: "first standardized curves")

### Tier 2 (Medium-Value, Medium Effort)

4. **Tilt & Velocity Recording**
   - **Dependency**: StrokePoint record change (migration required)
   - **Effort**: 600–800 LOC (StrokePoint → (X, Y, Pressure, Tilt, Time); all readers updated)
   - **Market validation**: Medium (professionals on high-end devices want this)
   - **Blocking**: Many downstream (dynamics using tilt, AI payload re-serialization)

5. **Bezier Curve Editing (after stroke reshape)**
   - **Dependency**: Stroke reshaping UI (Tier 1)
   - **Effort**: 400–600 LOC (BezierHandle serialization, drag interaction, render curve previews)
   - **Market validation**: Standard in professional tools
   - **Unique twist**: Edited vector strokes retain texture (no other tool does this)

6. **SVG Export with Real Paths**
   - **Dependency**: Stroke reshaping + Bezier editing (Tier 1–2)
   - **Effort**: 300–500 LOC (VectorFrame → SVG serializer, path → SVG polygon)
   - **Market validation**: HIGH (web export, asset interop)
   - **Roadmap alignment**: Already item in Pillar 4

### Tier 3 (Nice-to-Have, High Effort)

7. **Vector-to-Raster Tracing** — Trace vector paths into filled raster regions
   - **Dependency**: Stroke reshaping
   - **Effort**: 500–700 LOC (VectorToRaster converter, outline → contour, fill)
   - **Market validation**: LOW (artists usually rasterize in image editor)
   - **Lightbox rationale**: "We don't need this because vector strokes are already drawn marks"

8. **Symmetry & Mirrored Painting**
   - **Dependency**: None
   - **Effort**: 400–600 LOC (SymmetryAxis, mirrored stroke generation, cache invalidation)
   - **Market validation**: HIGH (character design essential)
   - **Roadmap alignment**: Already item [ ] "Symmetry and mirrored painting"

---

## SECTION 5: UNIQUE COMPETITIVE POSITIONING

### Where Lightbox Wins

| Dimension | Lightbox | Clip Studio | Harmony | Procreate | Affinity |
|-----------|----------|-------------|---------|-----------|----------|
| **Deterministic rendering** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **AI inbetweening (1st-class)** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Raster + vector same model** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Medium simulation** | ✅ | [~] | ✅ | ✅ | ❌ |
| **Procedural tips** | ✅ | [~] | [~] | ❌ | ❌ |
| **Game asset pipeline focus** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Pressure curve badging** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Performance transparency** | ✅ | ❌ | ❌ | ❌ | ❌ |

### Where Lightbox Is Behind

| Feature | Lightbox | Market Leader | Impact |
|---------|----------|----------------|--------|
| **Stroke reshaping** | [ ] | Harmony, Linearity | HIGH (vector workflows) |
| **Bezier handles** | [ ] | Harmony, Affinity | HIGH (precision work) |
| **Variable-width strokes** | [ ] | Linearity, Affinity | MEDIUM (illustration) |
| **Brush preset library size** | ~40 | 500+ (Clip Studio) | LOW (quality > quantity) |
| **UI polish (brush categories)** | [x] | Clip Studio | LOW (Lightbox searchable) |
| **Mobile support** | ❌ | Procreate, Clip Studio | N/A (desktop-first) |

---

## SECTION 6: ROADMAP IMPACT & PRIORITIZATION

### Items to Add/Upgrade to ROADMAP.md

**Pillar 4 (Drawing floor) — Raster Brushes**

1. **Upgrade**: "Tilt and speed reach the stroke record" from [ ] to [ ] with market validation
   - Reasoning: Medium-market request; high-end tablet users need this
   - Evidence: Clip Studio, Procreate, Corel all have tilt support
   - Add: TiltTests, SpeedTests, StrokePointDensityTests

2. **New Item**: "Pressure curve export/import (JSON standard)" [ ]
   - Reasoning: Unsolved workflow gap across industry; competitive differentiator
   - Effort: 150–200 LOC
   - Evidence: PressureCurveExportTests, InteropTests with Clip Studio/Procreate formats

**Pillar 0 (The drawing floor) — Vector Tools.** *Pillar 0, not 4 — the earlier
heading here said "Pillar 4 (Drawing floor)", which names two different pillars
in four words. The roadmap body files vector under Pillar 0.*

3. **Upgrade**: "A drawn line can be re-shaped and keeps the mark it was drawn with" [ ]
   - Current status: **unblocked.** The design question was **Q26**, not Q19, and it is answered
   - Action: build it — `docs/DESIGN-vector-tooling.md`, phased pick → path record → isolation mode → pen
   - Evidence: PathEditSession, StrokeReshapeTests, TextureConsistencyTests
   - Blocking: SVG export, Bezier editing

4. **New Item**: "Stroke shapes — Bezier curve handles for precision editing" [ ]
   - Dependency: Stroke reshaping (above)
   - Effort: 400–600 LOC
   - Evidence: BezierHandleTests, CurveEditorIntegrationTests

5. **Upgrade**: "Save as an ordinary image format — SVG" [ ]
   - Current status: [ ] (design says "honest for vector layers only")
   - Action: Implement VectorFrame → SVG exporter
   - Blocker: Stroke reshaping needs to exist first
   - Evidence: SvgExportTests, PathSerializationTests

**Pillar 4 (Drawing floor) — Advanced Brushing**

6. **New Item**: "Per-layer onion skin configuration" [ ]
   - Dependency: None
   - Effort: 300–400 LOC
   - Unique: No other tool offers per-layer ghosting
   - Evidence: OnionSkinLayerTests, CacheFilteringTests

7. **Upgrade**: "Symmetry and mirrored painting" [ ]
   - Current status: [ ] (design noted as hard, seed re-seeding issue)
   - Market validation: Essential for character animation
   - Blocker: **none.** The seed-origin question is Q26, answered; the record question specific to symmetry is Q15, also answered (c) — `Mirror` on the stroke
   - Evidence: SymmetryTests, MirroredStrokeTests

---

## SECTION 7: RISK & MITIGATION

### Risk 1: Vector Editing Breaks Invariant 2
**Scenario**: Reshaping a stroke re-seeds dabs from new position, changing texture
**This risk was assessed backwards, and the mitigation it proposed is the option
that was rejected.** Reshaping re-seeding the dabs does **not** break invariant 2
— it *is* invariant 2, working. What it breaks is an expectation.

**Mitigation, as actually decided (Q26, answered (a)):**
- **No arc-length seeding.** It was rejected explicitly: an edit near the *start*
  of a line re-seeds everything after it, which is the same failure the other way
  round and arguably worse. A per-stroke seed origin was rejected too — two
  strokes of the same shape in different places would then share a texture, which
  is the flicker invariant 2 exists to prevent, in a new costume. A blended
  re-seed radius was rejected as a tunable in the render path
- **Test: the opposite of `ReshapingPreservesTexture`.** A test pins that the dab
  pattern *does* change, naming Q26, so the accepted behaviour is written down
  rather than found later and filed as a bug
- **Manual, not design doc:** a line saying the grain shifts and why — the same
  fact as a pencil meeting the paper's tooth — with "move the layer rather than
  the line" as the answer for an artist who needs the mark preserved exactly

### Risk 2: Tilt/Speed Changes Serialization Contract
**Scenario**: Adding Tilt to StrokePoint requires migration; old files load without tilt
**Mitigation**:
- StrokePoint stays (X, Y, Pressure) in serialized form; Tilt is optional field
- Migration: Deserializer fills Tilt = 0 for old files
- Test: OldFileWithoutTiltStillLoads must verify bit-identical rendering

### Risk 3: Vector Tools Cannibalize Raster Usage
**Scenario**: Artists switch to vector for everything, hitting performance cliffs
**Mitigation**:
- Raster is always faster; position as "use vector for final polish, raster for performance"
- Benchmark: VectorRasterPerformanceComparison must show raster 2–4× faster
- Documentation: Update MANUAL.md with hybrid workflow guidance

---

## CONCLUSION & RECOMMENDATIONS

### Immediate Actions (Next Sprint)

1. ~~**Resolve Q19**~~ — **done, and it was Q26.** Answered 2026-08-07 (a); nothing is blocked. Build the vector tooling instead: `docs/DESIGN-vector-tooling.md`
2. **Implement per-layer onion skin** — quick win, high-value
3. **Export pressure curves** — unique market positioning, low effort

### Medium-Term (1–2 Sprints)

4. **Stroke reshaping + Bezier editing** — core vector capability
5. **Tilt & velocity recording** — high-end tablet support
6. **SVG export** — asset interoperability

### Market Messaging

"**Lightbox**: Deterministic animation with real media texture. Raster brushes that work with AI. Vector tools that stay textured when you edit them. First animation software where the camera is optional, not the constraint."

**Tagline for Tier 1 audiences**:
- **Character animators**: "Deterministic inbetweening + textured vectors"
- **Game devs**: "Sprite pipeline with professional brush control"
- **Illustrators**: "Vector editing that keeps your brush feel"
- **Studios**: "Reproducible marks across sessions and AI"
