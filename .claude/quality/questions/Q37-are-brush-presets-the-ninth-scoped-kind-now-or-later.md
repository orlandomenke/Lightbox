# Q37 · Are brush presets the ninth scoped kind now, or later? — **answered: now**

**Answered 2026-08-07: now, scoping the preset id only.** `BrushPreset.Id` is
already a stable `Ids.NewId("preset")`, and a `ScopedResource` is a kind plus an
id — so `Lightbox.Core` needs no knowledge of `BrushPreset`, which lives in
`Lightbox.App`. It is the palette pattern with a different string.

Scoping the whole `BrushSettings` record was rejected: large, it would bloat
every manifest using it, and it would put two sources of truth behind one brush
plus a new question about which wins when the preset is edited.

**What it delivers:** *"a project could dictate which brush settings need to be
used"* — and it needs no enforcement concept, because the machinery already
separates the verbs. `Resolve` **offers** a set; `Nearest` **selects** one. A
project-level declaration coming back from `Nearest` *is* the dictate.

**Known cost, inherited rather than new:** a document can reference a preset
that was deleted or never shared. The palette path has the same shape and wants
the same answer, not a bespoke one.

**Blocks:** nothing.
