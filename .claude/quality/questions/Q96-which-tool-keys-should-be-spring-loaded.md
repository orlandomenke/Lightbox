# Q96 · Which tool keys should be spring-loaded? — **answered 2026-08-16: add B, F and V**

Asked while fixing **B221**, the registry half of the tool-surface audit. The
momentary machinery landed with B176 and is entirely tool-agnostic —
`BeginMomentaryTool(ToolId)` takes any tool, and `ShortcutMap` carries the
binding as `momentaryTool` on a shortcut. Two of thirteen tool keys used it: `E`
for the eraser and `I` for the eyedropper.

That is what made the question worth asking rather than just filling in. Two out
of thirteen does not read as *"these two tools happen to suit it"*; it reads as
*"the eraser key is a bit odd"*. A rule an artist can rely on has to cover
enough of the keyboard to be a rule.

| | What it costs |
| --- | --- |
| **Add B, F and V** (recommended, **chosen**) | Three more keys to know. Brush, fill and move are where "borrow briefly, snap back" is a real gesture, and none of them carries modal state to strand on release. |
| **Every tool key** | One rule, no exceptions to learn — and holding `P`, `N`, `A` or `W` would park a pen path or an isolation session on release, because a borrow deliberately skips the side effects a chosen switch runs. |
| **Leave it at E and I** | Costs nothing and closes the finding as "not a defect". But it leaves the mechanism at 15% use and the rule looking like a quirk. |

**Holding `V` to shift something and letting go back into the brush is the one
artists reach for most**, and it is the case that makes the feature legible: the
eraser borrow can be read as a drawing convenience, while a move borrow is
obviously "the keyboard works this way".

## The two exclusions, which are the part that needed stating

**`S` is already taken.** Pressing it again cycles the selection variants —
Freehand, Polygon, Box, Ellipse, Wand — so a hold would be a third meaning on a
key that has two. That is exactly the ambiguity Q53 refuses one level up, and
`CycleSelectVariant` would fight the borrow for the same keystroke.

**The pen, both arrows and the Width tool carry a session.** A borrow goes
through `SetToolWithoutSideEffects`, which is what makes it safe — an artist
holding a key has not *chosen* to leave their tool, so `LeaveToolStateBehind`
must not run. The consequence is that the tool being borrowed *to* never gets
its own leaving handled either: hold `P`, place two nodes, let go, and the pen
session survives parked with no pen in hand. `MainViewModel.Momentary`'s table
already refuses to borrow *from* these four for the mirror-image reason, and
this is that argument pointed the other way.

`NoSpringLoadedKeyBorrowsAToolThatCarriesASession` is the general form of that,
so a row added later cannot quietly make a modal tool holdable — the table of
five is asserted, and so is the rule behind it.

## What this did not change

The gradient (`G`) and the shape tool (`U`) are stateless and could have joined.
They are left out because nobody has asked for them and the value is speculative:
"borrow the gradient for one drag" is not a gesture anybody described wanting,
where the move borrow is. Adding them later is one line each and no design.
