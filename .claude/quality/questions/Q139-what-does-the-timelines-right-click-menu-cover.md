# Q139 · What does the timeline's right-click menu cover? — **answered 2026-08-21: context-aware, keys and empty track alike**

Asked when the owner reported the timeline has no RMB menu at all (2026-08-21).
It had one on exactly one row in three — the pose rows' delete — which read as
broken rather than as restrained.

| | What it costs |
| --- | --- |
| **Context-aware menu** (recommended, **chosen**) | On a key: copy/cut/delete/jump, plus easing for a camera key. On an empty frame: paste here, key the camera/pose here, the playback range. Two menus to maintain, mirroring the X-sheet cel menu's shape. |
| Keys only | Smaller, but paste has no natural home and lands on a toolbar button. |
| One global menu everywhere | Simplest to discover; most items grey most of the time. |

The context split follows what the two targets *are*: a key is a thing with
verbs of its own, an empty frame is a place. Deleting stays per kind (the
manual's standing argument — removing a camera key, unkeying a bone and
clearing a drawing leave different things behind); copy/cut cross kinds for
the clipboard's reason (Q140). `TimelineUxTests` guards the commands the menu
calls; the menu itself is thin view code in `MainWindow.Transform.cs`.
