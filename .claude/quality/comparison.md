# Feature Request Analysis vs. Current Roadmap

## Summary
Both feature request sets align well with Lightbox's design philosophy. Most items are either **already planned, partially built, or dependent on other pillars**. Few items are entirely absent from strategic thinking.

---

## REQUEST SET 1: Production/Studio Features

### ✅ Already Built or Designed
- **Asset Library & Linking**: Pillar 3 is complete (Symbols, pose/expression/hand/face/prop/FX/background/animation libraries) [x]
- **Game Engine Exports**: Pillar 5 extensively built (Unity, Godot, GameMaker, Unreal) [x]
- **JSON Export**: Generic JSON exporter [x], with metadata [x]
- **Scene Management**: [x] with multi-scene support
- **Project Types & Defaults**: [x] with workspace layouts [x]
- **Status Tracking**: Asset status system [x] with auto-export on "Ready" [x]
- **Timeline & Exposure**: Multi-layer timeline [x], X-sheets [x]

### 🟡 Partially Built or In Progress
- **Version Snapshots**: Roadmap has "Version snapshots" [?] under Project plumbing — not yet built
- **Undo History**: "Undo history browser" [?] planned but not implemented
- **Comments/Markup**: "Comments on frames" [?] and "Comments on layers" [?] are planned but unbuilt
- **Caching**: Large canvas optimized [x]; tiled playback compositing built (the infinite canvas that motivated it was removed 2026-08-12)
- **Project Packaging**: "Package projects" [?] planned under Project plumbing

### ❌ Missing or Out of Scope
- **Non-Destructive Versioning Pipeline**: No incremental auto-save history with visual timeline (auto-save exists but no version browser)
- **Storyboard Mode**: No dedicated storyboard view with beat-to-timeline conversion
- **Shot Grid/Matrix**: No global project dashboard showing all shots at a glance
- **Per-Scene Override Settings**: Global vs. local settings not distinguished
- **XRef/External Linking**: No external file references without embedding
- **Review & Annotation Layer**: No dedicated non-rendering markup layer
- **ShotGrid/ftrack Integration**: Status exists but no production tracker integration
- **Studio-Wide Preference Deployment**: No centralized template/configuration distribution
- **PSD Import/Export**: [?] marked as unverified

---

## REQUEST SET 2: Technical/Animation Architecture

### ✅ Already Built
- **Shared Symbols/Linked Assets**: Pillar 3 (edit once, update everywhere) [x]
- **Symbol Editing**: [x]
- **Texture Atlas Packing**: Automatic rect/skyline packing [x]
- **Sprite Sheet Generation**: [x]
- **JSON Metadata Export**: [x]
- **Render at Any Output Scale**: [x] (invariant 7)
- **Collision Data Export**: [x] (for game engines)
- **Frame-by-Frame to Rigging Hybrid**: Layers + symbols + camera [x]

### 🟡 Partially Built
- **Background Pre-Rendering**: ComposeRing [x] and FrameBitmapCache [x] exist, but silent background pre-render while artist works is not automatic
- **High-Performance Caching**: Tiling and culling exist [x], but RAM/disk allocation toggle is not user-facing
- **Multi-Resolution Compositing**: Output scale works [x], but dynamic scaling on vector cameras is not explicitly built

### ❌ Missing or Speculative
- **Sub-Frame Keyframing**: Mentioned in roadmap prose as "essential for vector motion graphics" but not listed in the feature checklist — likely blocked on vector keyframing [?]
- **Rig Isolation & Versioning**: Symbols handle asset versioning [x], but "rig as self-contained versioned file" is not explicitly designed
- **Shared Parametric Styles**: Live palettes exist [x], but broader "global style sheet" for vectors + rigs + fills simultaneously is not built
- **Skeletal Data Serialization**: Collision/anchor data exports [x], but explicit skeletal animation format (Spine-compatible, etc.) is not mentioned
- **User-Configurable RAM/Disk Caching**: Caching is automatic; no manual allocation controls

---

## Key Observations

### Strong Alignment
1. **Game Engine Export**: Both requests emphasize game-ready pipelines. Lightbox has extensively built Pillar 5 (Unity, Godot, GameMaker, Unreal). Request 2 notes "we already have this in part" — accurate.
2. **Asset Management**: Pillar 3 (symbols, libraries) covers most of Request 2's rig/anatomy isolation and linked asset updates.
3. **File Format**: JSON/YAML folder structure [x] already chosen as the on-disk model.

### Strategic Gaps
1. **Production Review & Collaboration**: Request 1 emphasizes studio workflows (review, comments, ShotGrid integration). The roadmap has these [?] but unbuilt.
   - **Impact**: This is where Lightbox *differs from* single-artist tools. Pillar 6 is incomplete here.

2. **Version Management**: Request 1 wants incremental auto-save + visual version history. Roadmap has "Version snapshots" [?] and "Undo history browser" [?] but neither is built.
   - **Impact**: For long projects and team hand-offs, this is important.

3. **Sub-Frame Keyframing & Motion Graphics**: Request 2 mentions this as essential; the roadmap acknowledges it but doesn't list it as a feature (likely depends on richer vector editing).

4. **Storyboard Pipeline**: Request 1 wants rough storyboards → scenes conversion. No roadmap item for this.

### Design Philosophy Alignment
Both requests respect Lightbox's invariants:
- ✅ Strokes are the record (Request 2 understands deterministic rendering)
- ✅ Optional means absent (Request 1 understands symbol/asset scoping)
- ✅ Game-ready exports are first-class (both emphasize this)

---

## Recommendations for Feature Prioritization

### High-Value, Medium-Effort
1. **Undo History Browser** — Pillar 6 project plumbing. Would close Request 1's "version snapshots" partially.
2. **Frame Comments** — Pillar 6 collaboration. Unblocks review workflows in Request 1.
3. **Version Snapshots** — Incremental bookmarks of the document state (lighter than full version history).

### High-Value, High-Effort
1. **Storyboard Mode** — Dedicated UI for beat layout + timeline conversion. Request 1 asks this explicitly.
2. **ShotGrid/ftrack Integration** — Via API; status tracking already [x], just needs connector.
3. **Sub-Frame Keyframing** — Blocks smooth vector motion graphics (Request 2). Depends on vector editing depth.

### Lower-Priority or Dependent
1. **XRef/External Linking** — Nice-to-have; most assets are embedded today.
2. **ShotGrid Integration** — Lower for single-user alpha; critical for studios.
3. **Parametric Styles across Mediums** — Request 2; would require cross-domain theme system (ambitious).

---

## Where Requests Differ from Roadmap Direction

| Request 1 Focus | Roadmap Focus |
|---|---|
| Production review & collaboration (reviews, markup, comments) | Asset pipeline & game export (Pillar 5) |
| Non-destructive versioning & history | Determinism & replay correctness |
| Studio tool integration (ShotGrid, Perforce) | Self-contained document model |
| Storyboard → timeline workflow | Scene management (already in) |

**Interpretation**: Request 1 assumes a studio with supervisors/directors. The roadmap prioritized shipping game-ready exports first (Pillar 5). Request 1's features are Pillar 6 items (production workflow, collaboration), which are mostly [?] (unbuilt).

---

## Actionable Next Steps

### For the Roadmap
1. Upgrade "Undo history browser" [?] → [ ] with anchors if it is truly next.
2. Add "Storyboard view with beat-to-timeline conversion" [ ] to Pillar 6 if Request 1 represents user demand.
3. Clarify "Sub-Frame Keyframing" — is it blocked on vector editing, or is it a separate item?

### For Bug Reporting
- Already done: Project structure bugs (B83-B87) address asset organization.
- Next: Consider filing project-scope review workflows as features, not bugs.

### For Feature Requests
- Both sets complement the existing roadmap; they don't contradict it.
- Request 1 is about *who uses Lightbox* (studios); Request 2 is about *how it renders* (hybrid media).
- Neither requires abandoning Lightbox's design (invariants, JSON layout, determinism).
