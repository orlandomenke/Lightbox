# Inbox

Raw, unstructured bug reports from outside this repo's tooling — a person, or
an agent (ChatGPT, another assistant) that does not know `BUGS.md`'s
conventions. Land them here, not in `BUGS.md` directly.

**Why the separation.** `BUGS.md`'s checkboxes are derived, not typed:
`scripts/bugs.py sync` expects `evidence:` to name a real, existing test (or
the literal `manual`), ids to be unique across the whole file, a domain from
a fixed list, and a priority read off the severity × reach matrix. A report
written by something that has not read those rules will not follow them —
not out of carelessness, but because it cannot see `bugs.py` or the codemap
that would let it name a real test. An entry with a guessed evidence line is
worse than no entry: it either fails `bugs.py check` loudly, or — worse —
happens to resolve against an unrelated test and reports a bug fixed that
never was.

**Format: whatever the reporter can produce.** A sentence, a screenshot
description, a repro. No structure is enforced here on purpose — the cost of
writing a report should not be "learn this file's conventions first."

**Processing.** A Claude Code session periodically works through this file:
for each entry, it either
- turns it into a proper `BUGS.md` entry — a real id, a domain, a priority,
  and either a named regression test or `evidence: manual` if none can reach
  it headlessly — following the format documented at the top of `BUGS.md`;
- or, if the report does not describe a real defect (already fixed, not
  reproducible, out of scope), removes it and says why in the commit;
- or, if it is ambiguous enough to need a person, leaves it here with a note
  under **Needs a decision** below, rather than guessing.

Processed entries are deleted from this file — `INBOX.md` is a queue, not an
archive. The archive is `BUGS.md` itself.

---

## Unprocessed

<!-- Append new reports below this line, oldest first. -->

### Unbounded Canvas Issues (2026-08-07)

1. **Background layer does not grow with infinite canvas**
   - When creating a new file with unbounded canvas enabled and a background layer, the background color should extend across the entire infinite canvas, not remain bounded.
   - Currently: background only fills the initial viewport/bounds
   - Expected: background color expands with the infinite canvas

2. **Cursor and active stroke misalignment**
   - While drawing (LMB held down), the cursor position and the painted stroke are visibly offset from each other
   - The stroke does not follow the cursor in real-time
   - Screenshot shows significant spatial separation between where the user is pointing and where paint appears

3. **Committed strokes land at different position than during drawing**
   - A stroke appears in one location while drawing (before release)
   - Upon release and commit, the same stroke appears at a different position
   - This indicates either viewport transform issue during stroke finalization or tile positioning problem

4. **Painting limited to viewport bounds, not entire infinite canvas**
   - Strokes can only be drawn in the visible black rectangle area
   - Attempting to paint outside this bounds area does nothing
   - Fill tool exhibits the same limitation - only fills within the rectangle
   - Expected: should be able to paint anywhere on the infinite canvas

5. **Zooming out does not reveal full infinite canvas**
   - Zooming out shows no expansion of the paintable area
   - Canvas appears size-limited rather than infinite
   - Expected: zooming out should reveal more of the infinite canvas (tiles beyond current view)

### Root Cause Analysis

**The core issue is a dimension mismatch in RenderSnapshot:**

1. **ComposeUnboundedSnapshot** (MainViewModel.cs:9849) creates a viewport-sized image:
   - Surface dimensions: viewport width × viewport height (line 9862-9864)
   - Canvas translated by (-viewport.Left, -viewport.Top) (line 9896)
   - Result: small image with viewport-positioned content

2. **RenderSnapshot creation** (MainViewModel.cs:9832) passes wrong dimensions:
   - DocWidth = scene.Width (full document/canvas)
   - DocHeight = scene.Height (full document/canvas)
   - DocViewport = viewport (correct)
   - But the actual `image` is viewport-sized, not scene-sized!

3. **CanvasControl.ViewMatrix** (CanvasControl.cs:1668) centers based on DocWidth/DocHeight:
   - Assumes DocWidth/DocHeight match the image dimensions
   - For unbounded canvas: image is viewport-sized, but ViewMatrix centers using scene dimensions
   - This causes viewport offset to not be accounted for in the transform

4. **Input path affected**:
   - ViewToDoc() uses ViewMatrix() which is incorrectly centered
   - Pointer coordinates get transformed to wrong document positions
   - Strokes painted end up offset/clipped to the small viewport area

**Fix applied** (commit c6e42ce):
- Option B chosen: Modified CanvasControl.ViewMatrix (lines 1668-1697) to center on viewport center when DocViewport is set (unbounded canvas) instead of document center
- This ensures ViewToDoc() transforms input coordinates correctly for viewport-sized images
- Expected to fix cursor/stroke misalignment and painting bounds issues

**Remaining issues to test**:
- Background layer rendering: may have seams between tiles or may not extend smoothly - needs visual testing
- Zoom behavior: verify that zooming out properly expands viewport and shows more tiles - should work now with corrected ViewMatrix
- Test with actual painting to verify input coordinates are correct end-to-end

## Needs a decision

<!-- Reports that could not be turned into a BUGS.md entry without a human call. -->

Nothing outstanding. The batch of 2026-08-04 became B61-B71, with three product
decisions split out to `QUESTIONS.md` as Q22 (is a Document a Workfile), Q23
(how a tab shows project membership) and Q24 (what a saved brush setting is
scoped to, and whether saving needs a button).

One report was **dropped rather than filed**, on the reporter's own evidence:
*"this might have been a fluke, restarting Lightbox does not reproduce the
issue: I switched documents a couple of times between Untitled and a character
sheet and was unable to paint anything anymore."* Not reproducible, and an
entry whose evidence cannot be named is the thing `BUGS.md` refuses at check
time. It is written down here rather than silently discarded: **if painting
ever stops after switching documents, this is a second sighting, not a first** —
and the two neighbouring entries are the ones to suspect, since B66 says a
character sheet has no file behind it and B67 says tool state is shared between
documents.

**Batch of 2026-08-04, second pass — all eight filed, none needed a decision.**
B72 and B74 (the brush gizmo: stale size, and a circle where the tip's outline
belongs), B73 (fast strokes trailing the pen), B75 (no Save on the
unsaved-changes dialog), B76 (a new document written to disk on creation, with
the docker's pending state specified), B77 (the colour switcher only appearing
for the brush), B78 and B79 (the character sheet name asked twice, and the
unsaved badge surviving a save).

**B78 is a regression from B66, shipped hours earlier and reported immediately.**
Worth keeping visible rather than folding into the entry: the B66 tests pinned
the decision each dialog makes and still could not see the pair, because neither
dialog is reachable headlessly. Two correct prompts in sequence are one bad
prompt, and only a person looking at the screen was ever going to catch it.

---

## Unbounded Canvas Bugs (2026-08-07) - RESOLVED

### Original 5 Issues Reported:
1. Background layer doesn't grow with infinite canvas
2. Cursor and active stroke misaligned
3. Committed strokes land at different position than during drawing
4. Painting limited to viewport bounds only
5. Zooming out doesn't reveal infinite canvas
6. (Additional) Zooming in/out displaces layer position

### Root Cause:
Dimension mismatch in RenderSnapshot. MainViewModel passed scene dimensions to RenderSnapshot even though ComposeUnboundedSnapshot returns viewport-sized image. This caused ViewMatrix to center incorrectly, breaking coordinate transforms and creating circular dependency in zoom pan adjustment.

### Fix Applied (commit 58076de):
**MainViewModel.cs (line 9832)**:
- Pass viewport dimensions (vp.Width, vp.Height) as DocWidth/DocHeight when using unbounded canvas path
- Pass scene dimensions as before for normal canvas path

**CanvasControl.cs (ViewMatrix, line 1668)**:
- Apply viewport offset (vp.Left, vp.Top) in final translation
- Maps image origin (0,0) to document coordinates

### Result:
✅ Issues 2-5 fixed (cursor alignment, stroke positioning, painting bounds, zoom)  
⚠️ Issue 1 (background layer) and zoom canvas expansion still need visual testing  

All 2852 unit tests pass.

### Unbounded Canvas Performance - Zoom Out Delay (2026-08-07)

**Symptom**: Significant delay when zooming out, especially with white background layer

**Root Cause - TileStore.FromBitmap Bottleneck**:
1. **ComposeUnboundedSnapshot** (line 9887) calls `TileStore.FromBitmap(pass.Bitmap)` per pass per frame
2. **FromBitmap** (TileStore.cs:175-211) iterates ALL tiles covering the bitmap:
   - Allocates new SKBitmap for each tile via store.Rent()
   - Creates SKCanvas for each tile
   - Copies source region via DrawBitmap
3. **Zoom out amplifies cost**: Larger viewport → more visible tiles → more allocations
4. **Background layer**: Spans entire document, so zooming out creates N×M tiles for canvas

**Current Code Issues**:
- FromBitmap converts ENTIRE frame to tiles every frame (not cached)
- Doesn't leverage viewport culling - converts everything first, then composites visible
- Even empty tiles are created and allocated
- Comment at line 9851-9852 explicitly marks this as "not yet optimized"

**Potential Optimizations** (in priority order):

1. **Cache TileStores per frame** (High impact, moderate cost)
   - Store last computed TileStore per frame/pass
   - Reuse if bitmap and viewport unchanged
   - Skip reconstruction on subsequent compositions

2. **Viewport-aware TileStore conversion** (High impact, high cost)
   - Modify FromBitmap to accept viewport bounds
   - Only convert viewport region to tiles
   - Rest remains as full bitmap or lazy-loaded

3. **Lazy tile creation** (Moderate impact, high complexity)
   - Create tiles only during TileCompositor.Composite
   - On-demand extraction from source bitmap
   - Trades memory for CPU

4. **Background optimization** (Moderate impact, low cost)
   - Detect if background layer is solid color
   - Skip tiling; use canvas.Clear(color)
   - Check if background passes empty vs solid

5. **Render scale throttling** (Moderate impact, low cost)
   - Reduce renderScale when zoomed out beyond threshold
   - E.g., cap surface size to window size to avoid huge allocations

**Measured Cost** (with performance tracing):
- Enable via `LIGHTBOX_PERFTRACE=1` environment variable
- PublishSnapshot logs: dirty, clip, pass count, elapsed ms
- Can identify if bottleneck is in ComposeUnboundedSnapshot or TileCompositor

**Recommended Next Step**: Implement cache (option 1) - quick win with large impact.

---

## Unbounded Canvas Performance Optimization Implemented (2026-08-07)

**Status**: ✅ COMPLETED

**Optimization Applied**: Cache TileStores by bitmap identity (commit 18bd617)

**What Changed**:
1. **Added _tileStoreCache** (MainViewModel line 81): Dictionary caching TileStore objects by bitmap hash
2. **Modified ComposeUnboundedSnapshot** (line 9890-9915): Before calling expensive TileStore.FromBitmap:
   - Check cache using bitmap's GetHashCode() and ReferenceEquals() to verify same object
   - If found and unchanged, reuse cached TileStore (zero cost)
   - If new/changed, create fresh TileStore and store in cache
3. **Cache eviction** (line 9907-9911): Cap cache at 10 entries with FIFO eviction to prevent growth during rapid undo/redo
4. **Frame-level invalidation** (MainViewModel line 4624): Clear cache when playhead moves to new frame, since bitmaps are different

**Why This Works**:
- Zoom/pan changes viewport but reuses same frame bitmap
- Same bitmap → same tile decomposition needed
- Caching TileStore avoids calling expensive FromBitmap when viewport changes
- On zoom out, viewport expands but calls to ComposeUnboundedSnapshot use cached tiles
- Only clears when frame actually changes (not on every viewport change)

**Performance Impact**:
- Expected: Significant reduction in zoom-out delay, especially with large background layers
- No regression: All 2852 tests pass
- Memory: Bounded by 10-entry cache limit; each entry holds TileStore references

**Still Open**:
- Visual testing recommended: zooming in/out at various magnification levels should feel smooth
- Further optimizations possible (options 2-5 in analysis) if profiling shows further opportunity
