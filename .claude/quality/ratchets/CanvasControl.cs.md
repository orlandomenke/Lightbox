# src/Lightbox.App/Rendering/CanvasControl.cs

budget: 4956

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
