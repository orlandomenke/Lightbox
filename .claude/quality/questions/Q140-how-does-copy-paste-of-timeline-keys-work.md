# Q140 · How does copy/paste of pose and camera keys work? — **answered 2026-08-21: one cross-kind selection clipboard**

The owner asked to copy "the pose, the camera positions etc." from the
timeline (2026-08-21). Nothing existed: the only clipboard was the cel
clipboard, one row's drawings pasted consecutively.

| | What it costs |
| --- | --- |
| **Cross-kind selection clipboard** (recommended, **chosen**) | Copy takes the dot selection whatever it holds — camera keys, whole pose keys, single bones' entries, cels — keeping relative offsets; paste lands the beat at an aimed frame as one undo step. A second clipboard beside the cel one, and the two must stay distinguishable. |
| Per-kind commands | Predictable, but a beat spanning kinds is three copies and three pastes that drift apart. |
| Pose-snapshot only | Cheapest; covers one act and defers the rest. |

The precedent is the mixed retime drag: selection already means one thing
across kinds, and the clipboard is its "there too" face. Landing is per kind —
replace a camera/whole-pose key, join a bone into the key already there
(seeded from the interpolated pose so neighbours never snap to rest), set a
cel. Cut removes exactly what it copied; delete stays per kind. Deliberately
**not** merged with the cel clipboard: consecutive-holes-closed and
at-their-distances are different pastes, and one Ctrl+V meaning either
depending on what was copied last is a trap. Evidence:
`MainViewModel.TimelineClipboard.cs`, `TimelineUxTests`.
