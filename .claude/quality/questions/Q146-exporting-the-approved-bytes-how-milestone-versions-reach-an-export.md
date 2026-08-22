# Q146 · Exporting the approved bytes: how milestone versions reach an export — **answered 2026-08-22**

Q75's milestone capture exists so "which bytes were the Ready ones" survives
somebody continuing to draw — the roadmap calls it the export-filter story's
missing half. The half is still missing: export always takes the current file,
so the kept Ready bytes are paid for and never delivered. While wiring the
project window to *show* version state (the branch this was asked on), the
question of how the bytes should reach an export was prompted and answered
ahead of building it, so its own branch starts from a decision rather than a
guess.

**Answer: opt-in per export preset.** A flag on the preset — *use the Ready
version where one is kept, current bytes otherwise* — because:

- It is explicit and survives reuse: the studio that wants approved-only
  exports wants them on every run of that preset, which is exactly the
  situation presets exist for. A per-run checkbox (the alternative) is cheaper
  but forgets the choice in the situation studios automate.
- The Export tab can show what the flag changes before anything is written —
  which rows would ship kept bytes rather than current ones — the same
  standing-still honesty the tab already has for status filters.
- Visibility-only (show the milestones, always export current bytes) was
  rejected as a half-promise: a column that names the Ready version beside an
  export that ignores it misleads more than it informs.

Costs accepted with the answer: a preset field (serialized, so a preset that
never sets it must write no key — the optional-means-absent test applies), and
a visible divergence between what the canvas shows and what the export wrote,
which the plan view must therefore surface per row rather than bury in a
footnote.

Not built yet — it is its own branch with its own design pass. The pieces that
exist today: `VersionEntry.MilestoneStatus` and `ProjectVersions.ContentPathOf`
(Q75), and `VersionFacts.ChangedSinceMilestone` (this branch), which is the
per-row fact the plan view will lean on.
