# Q142 · Where are a selected key's values edited? — **answered 2026-08-21: a strip in the timeline docker, present only with one key selected**

The owner asked for the timeline to "split in 2 sections dynamically": nothing
selected shows only the tracks; a selected node shows its adjustable values —
X and Y, rotation, depth (2026-08-21). Camera keys were editable only in the
graph editor; bone keys had no numeric editor anywhere; depth lived in the
Scene panel alone.

| | What it costs |
| --- | --- |
| **Split section in the timeline docker** (recommended, **chosen**) | A strip under the tracks, absent unless exactly one key is selected. A second surface writing camera/pose/depth values, so every write must take the existing door (`EditCameraKey`, the pose keying, `SetLayerDepth`) or the two surfaces fight over clamping. |
| Flyout on the key | No layout change, but you cannot scrub while watching the numbers, and it hides that values are editable at all. |
| Grow the Scene panel | Keeps the timeline pure; puts the numbers a panel away from the key they belong to. |

One key, not an average: a multi-selection shows nothing, because writing one
number into five keys is a different feature wearing this one's clothes. The
armature summary's key is a whole pose and says so rather than pretending a
pose is three numbers. Every getter re-reads the document — the key may have
been retimed or undone since it was selected. Evidence:
`MainViewModel.TimelineInspector.cs`, `TimelineUxTests`.
