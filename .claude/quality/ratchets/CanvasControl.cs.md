# src/Lightbox.App/Rendering/CanvasControl.cs

budget: 4867

## Why it has moved

Newest last. Both sides of a merge keep their entry — taking one deletes the
other's reason and leaves a number nobody can account for.

- **Lowered three times**, twice as `CanvasControl.Guides.cs` grew — the
  guide-grab section, then the Guides/DraftGuide/BalanceDots overlay properties
  — and once when `CanvasControl.Pointer.cs` took what the pointer draws for
  itself: the brush ring's record and tip-outline cache, and the eyedropper's
  ring.
- **Lowered again** when the rig and armature overlay surfaces moved to
  `CanvasControl.Overlays.cs`.
- **5,078 → 5,122** on 2026-08-13, re-seeded when this work was re-applied on top
  of 52 commits of `main`, PR222 included. That growth is *main's*, not the
  branch's, and re-seeding is the honest move rather than a bypass: a ratchet
  seeded against a stale baseline reports its own staleness as a violation.
- **Re-measured on the merged tree**, as the rule above says to. The extractions
  on either side of a merge are independent, so the merged file is smaller than
  either branch measured alone, and taking a side keeps a budget with the other
  side's slack spent into it. `ratchets.py remeasure` is what does this now.
- **→ 4,914** (2026-08-14): the guide options came *down* rather than up. The
  `GuideLine` snapshot and `GuideDragEnabled` moved to the partial already named
  for guide chrome — a feature that needed room in a budgeted file buying it by
  extraction rather than by raising a number, which is the mechanism working as
  designed.
- **→ 4,932** (2026-08-15, B217): +18 for the whole-line hover, after extraction
  had already paid for most of it. The hovered-line surface and the B216 point
  snapper both went to `CanvasControl.Overlays.cs` — the partial that exists for
  exactly this — and what is left could not follow them: the hook that fires the
  hover lives inside the pointer-move handler, and the split that lets the
  preview reuse the selection's own outline painter lives inside the nested
  `DrawOp`. Both are gesture and paint code in the places the gesture and the
  paint happen, so extracting them would mean moving the handler rather than the
  feature. The exact eighteen lines, recorded rather than rounded up.
- **→ 4,946** (2026-08-16, B223): +14 for turning the line drag into a transform
  session. The four events it needs went to `CanvasControl.Overlays.cs` with the
  rest of the pushed-in surface; what is left is the press that opens the
  session, the move that reports a position as well as repainting, and the
  release that commits or discards — three edits inside the pointer handlers,
  which is the one place they can be. The gesture is the thing that grew, and a
  gesture cannot be extracted from the handler that receives it.
- **→ 4,932** (2026-08-16): +4 for the motion trail's draw-op wiring — the pass
  parameter, the field and the `MotionTrailPainter.Paint` call, all inside the
  nested `DrawOp` where the painting happens.
- **Worth recording *why* this needed a raise at all, because the interaction is
  the instructive part.** The entry above lowered this to 4,928 by re-measuring
  the merged tree, which was correct: both sides of that merge had extracted
  code, so the budget was carrying slack neither branch had earned. The motion
  trail was in flight at the time, written against the old 4,946, and its four
  lines fitted inside that slack — so it was green on its own branch, green
  against the pre-remeasure `main`, and over the line the moment the two landed
  together. **Nothing was wrong with either change.** A remeasure that removes
  unearned slack can turn a parallel branch red on merge, and the honest
  response is this entry rather than leaving the slack in place to avoid it.
- **→ 4,961** (2026-08-16, B231 and the board projection): +29 for the
  reference stack becoming workable from the canvas — two events
  (`ReferenceAlignPressed`, so a grab in align mode picks the strip under the
  pointer, and `ReferenceMenuRequested`, the right-click stack menu) and the
  press-handler branches that raise them. The branches cannot leave this file:
  they are lines inside the one pointer-pressed decision chain, and the window
  owns everything they mean.
- **→ 4,952** (2026-08-16, B241): +20 for the cursor becoming a single decision.
  Everything that could be extracted was: the whole decision — twelve grabs, the
  angle arithmetic and the hit tests behind them — is a new partial,
  `CanvasControl.Cursors.cs`, and the two `OnPointerPressed`/`OnPointerReleased`
  wrappers went there too, because they exist for the cursor and nothing else
  even though the handlers they wrap do not. What is left in this file is the
  part that cannot move: the hover call inside the pointer-move handler, the
  `_cursorAt = null` in `OnPointerExited`, the re-ask in the `PointerIntent`
  class handler (a static constructor cannot be split from the type it
  initialises), and the two `Core` bodies the wrappers now call. Every one of
  them is a hook in the place the event arrives, and moving a hook means moving
  the handler rather than the feature — the same reason B217 and B223 record.
- **→ 5,002** (2026-08-16, Q104): +21 for Ctrl taking hold of what a marquee
  holds, measured against the merged tree rather than the base this was written
  on. The delegate and its setter went to `CanvasControl.Selection.cs`, which is
  the file that owns what a selection *is* on this control; what is left is the
  press branch itself, and a press branch cannot leave the press handler. Its
  placement is the feature — it is asked before the held eyedropper because it
  is the narrower claim — so it is also the part that most needs to be read
  where the ordering is visible.
- **→ 4,981** (2026-08-16, on merge): neither side's number. The two above
  are +29 and +20 against the same base and landed together, so the merged
  tree is both. Taking either would have banked the other's growth as
  unearned slack, which is the failure the paragraph at the top of this
  section describes — re-measured here rather than chosen.
- **→ 5012** (2026-08-16, B248): +31 for the scale drag holding the opposite
  side rather than the pivot. The anchor *decision* went to a new partial,
  `CanvasControl.TransformAnchor.cs`, following the cursor decision's
  precedent; what is left is the scale case inside `TxDragTo` growing by the
  anchor terms and the offset correction that keeps the anchor still. That
  case is the gesture itself, inside the one switch that receives the drag —
  the same boundary B217, B223 and B241 record above. The exact thirty-one
  lines, recorded rather than rounded up.
- **→ 5,033** (2026-08-16, on merge): neither side's number, again. B248's +31
  and Q104's +21 were measured against the same base and landed together, so
  the merged tree carries both — re-measured with `ratchets.py remeasure`
  rather than chosen, exactly as the 4,981 entry above records for the
  previous pair.
- **5,033 → 4,880** (2026-08-17, Q88): the reference chrome left the file. The
  `ReferenceBox` record, the `ReferenceBoxes` channel, the four grid events and
  the grid gesture went to `CanvasControl.References.cs`; the grid painter went
  to `ReferenceBoxPainter`, beside `GuidePainter` and `ArmatureOverlayPainter`
  for the same reason. −187, then +34 for the press, move and release hooks that
  let the Arrow pick a projected reference up.
  **The extraction is what paid for the feature**, which is the arrangement this
  file's budget is meant to force: a feature does not get to raise it, so the
  work went beside the file first and the number came down on the way past.
