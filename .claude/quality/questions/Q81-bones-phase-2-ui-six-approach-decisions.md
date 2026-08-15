# Q81 · Bones phase 2 UI: six approach decisions — **answered 2026-08-14, all six as recommended**

Prompted before building the UI half of phase 2 (`docs/DESIGN-bones.md`),
with the core — weights record, LBS, rest-pose seeding, bake — already
landed and green.

1. **Rigged-ness is record-driven.** A stroke with `Weights` poses; there is
   no layer flag. Cost accepted: render caches must derive pose-dependence
   from content, and invalidation on pose edits is built for that, not read
   from a bit that could go stale.
2. **Auto-key at the playhead.** Dragging a bone at frame N writes or
   updates the pose key at N — posing is the high-frequency act and gets the
   one-step gesture. The camera keeps its explicit keys; the two differ on
   purpose. Accident risk is handled by armature onion-skin and key pips.
3. **One Bone tool, then a rig mode.** The Bone tool lives in the palette
   always (every feature reachable — first drag creates the armature); bone
   list, heat overlay, weight brush and pose/bind toggle appear only once an
   armature exists (unused shows no controls).
4. **Weight painting at rest first.** Heat overlay and brush operate on the
   bind pose; scrub to check, return to correct. Painting under a live pose
   is sequenced later, with the caching that makes per-brush-event re-posing
   affordable — a sequencing call, not a scope cut.
5. **Pose-drag preview: exact, degrading only over budget.** Affected
   strokes re-render exactly per pointer event, region-bounded; strokes over
   the frame budget (already badged by `BrushCostOf`) ghost as their posed
   centreline during the drag and land exactly on release. Invariant 6 is
   the tiebreaker.
6. **X-symmetry mirrors by paired bone names** (`hip.l` ↔ `hip.r`, axis from
   the paired rest placements), Blender's convention — a fixed canvas axis
   breaks on any character not drawn dead-centre. The bone tool's
   auto-naming supplies the discipline.

---
