# Q295 · Line cleaner: deterministic, or does a model decide? — **answered 2026-11-19: deterministic, with an MCP tool**

Raised by: the same request as Q292.

What it blocks: whether the cleaner lives in `Lightbox.Core` as geometry or in
`Lightbox.Ai`, and whether gate G12 applies.

| | What it costs |
| --- | --- |
| **Deterministic, plus an MCP tool** (recommended, **chosen**) | Pure geometry, the same result every run. An agent can call it after inbetweening; the MCP diff takes the ai-engineer + art-director review. |
| Deterministic only, no MCP yet | Smaller first change; agents get it later. |
| A model picks what to fix | Better on odd cases; costs tokens and a full G12 review. |

No model decides anything: the cleaner is a pure function of the strokes (and,
in guided mode, of the hand-drawn frames around them).
