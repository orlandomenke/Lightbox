# Q276 · A docker's header menu: what picking a panel open elsewhere does — **answered 2026-10-15: it moves here**

Raised by the owner, 2026-10-15: *"I also want to be able to rmb next to a tab
to select a docker to open as a new tab."* Both Photoshop and Krita have a menu
on a panel's tab strip. Photoshop's offers Close, Close Tab Group, Minimize and
the icon modes. Krita's is Qt's: every docker, with a tick. Lightbox had none:
a right-click on a header did nothing.

What it blocks: the menu itself. Most of what it lists is not a preference:
a closed panel opens as a tab here, a panel already in this group comes
forward, and the timeline (which never leaves the bottom) is not offered. The
one real choice is a panel that is **open in some other group**, because
"no panel is ever open twice" rules out a copy.

| | What it costs |
| --- | --- |
| **Move it here** (recommended, **chosen**) | The quickest way there is to build a group: one right-click per member instead of one drag. A misclick pulls a panel out from under you, but it is undone the same way it was done, and the menu says where the panel currently is, so the move is never a surprise. |
| List it, disabled | Nothing ever moves unexpectedly. But then regrouping still needs a drag, which is the gesture the owner called not fully intuitive. |
| Bring it forward where it is | Safe, but the item does not do what the menu says it does ("open here"). |

## What was decided

- **Picking a panel open elsewhere moves it into this group and brings it
  forward.** Its old group closes up behind it, exactly as a drag would leave it.
- The menu is offered on the **whole header**: a tab, and the empty space
  beside the tabs. Right-clicking a tab also makes that tab the subject of the
  menu's Close and Float items.
