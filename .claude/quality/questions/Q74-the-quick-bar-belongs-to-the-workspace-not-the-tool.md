# Q74 · The quick bar belongs to the workspace, not the tool — **answered 2026-08-13**

Q70 stage 1 shipped the bar's frame — the tool icon, the pinned Size/Opacity
pair, transform out to the docker — but left the contents untouched: the full
per-tool vocabulary, folding into ▾ only when width forces it. On a wide
monitor nothing folds, so the "Quick options bar" read as the old tool-options
bar wearing a new name, and the owner reported exactly that. Asked whether to
curate the per-tool sets now, propose them first, or leave it to stage 2.

**Answer: none of the three — the owner reframed the axis.** Near-verbatim:
*"The quick bar should be determined by workspace options not necessarily tool
options. So the options in the quick bar can be customized by except for size
and opacity are fixed per workspace. For example in animation it could get the
play/pause button or add keyframe button. For illustration it could set the
marquee option etc."* Reading Q70's original answer back, "like a smart bar
per workspace" was already there — it had been read out as a per-tool rule,
and this is the correction.

What landed, same day:

- **`QuickBarCatalog`** — the registry of everything the bar can offer, the
  same reason `ShortcutMap` exists one level up: the customize flyout needs
  something to enumerate. Ten entries: the eight tool groups the bar already
  had, plus **Play/pause** and **Add frame** mirroring the timeline's own
  buttons.
- **`DockLayout.QuickBar`** — the workspace's choice, nullable and absent
  from `workspaces.json` until a choice is made; null resolves to the bar as
  it always was, so a store written before the property existed changes
  nothing. Living on the layout buys the whole existing machinery for free:
  dirty until saved, undone by reset, switching with the workspace.
- **Built-in defaults chosen by the work**: Animation, Game art and
  Storyboard carry the transport and Add frame; Illustration and Comic carry
  the paint kit with the marquee; Asset library is minimal; Default keeps
  the resolve-to-everything null.
- **Tool gating stays**: a workspace decides what the bar *offers*; the tool
  in hand still decides which of those offers is relevant right now, so
  carrying "Fill options" shows them with the fill held rather than as a
  dead strip all day. The two gates AND together in the XAML.
- **The ⋮ flyout beside the workspace picker** is the customization — 
  checkboxes over the catalogue, saved with the workspace. Size and opacity
  are not in the catalogue at all, which is what "fixed" means mechanically
  (`SizeAndOpacityAreNotOnOffer` keeps it true).

Q70's stage 2 (drag-and-drop rearrangement) remains open and unchanged; this
delivers the "which options" half of customization without it.
