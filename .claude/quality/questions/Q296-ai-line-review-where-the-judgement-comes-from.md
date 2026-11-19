# Q296 · AI line review: where does the judgement come from? — **answered 2026-11-19: an MCP agent and an in-app AI button**

Raised by: the owner, after the deterministic line cleaner (Q292–Q295): on a
matched inbetween of the sketched legs, the knee-front line and the shin line
both run past the joint, side by side for about 70 px. Nothing deterministic
can call that wrong — within the drawing it is two lines near each other — but
neither key draws it. "Perhaps an agent can … It should be fully optional."

What it blocks: whether the review needs a prompt and provider code in the app
(gate G12 in full) or only MCP tools.

| | What it costs |
| --- | --- |
| MCP agent first (recommended) | Two tools in the render_match_pieces / set_line_matches pattern: render the inbetween beside its keys with numbered line ends; take the agent's named fixes and apply them as a preview. No prompt in the app. |
| **In-app AI button too** (**chosen**) | As above, plus a "Review with AI" step that calls the configured provider through `Lightbox.Ai`. A prompt, payload work and the full ai-engineer + art-director review; reachable without an outside agent. |
| Inside the inbetweener | Every insert costs and waits on a request. |

The model names, the engine draws: the same division as line matching (Q270),
because a model can judge which line is wrong and cannot place coordinates.
