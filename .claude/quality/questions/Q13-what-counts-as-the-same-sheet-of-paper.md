# Q13 · What counts as the same sheet of paper — **answered (c)**

**Answered 2026-08-02: (c).** The wet window is per frame and per layer, and
**generated strokes never carry wetness** — the inbetweener and the MCP surface
write `WetStrokes = 0` whatever the source stroke said.

Per frame and layer because a cel is a separate drawing and a layer is not
paper; it is the same answer Q6 gave for what a smudge samples, and it keeps
the replay trivially bounded. The extra clause is a determinism one: an
inbetween whose appearance depended on how many strokes the generator happened
to emit before it would diverge between runs, which is invariant 2 broken by a
side door.
