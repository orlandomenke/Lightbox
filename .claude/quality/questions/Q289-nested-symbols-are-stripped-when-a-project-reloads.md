# Q289 · Nested symbols are stripped when a project reloads — **answered 2026-10-18: keep nesting**

Raised by the adversary reviewing B487 (#781). `ProjectIo` drops every
placement inside a project symbol when the project loads, with a comment saying
nesting is refused until "a cycle check, a depth limit and a dependency graph"
exist. The application does not refuse it, though. A symbol can be placed inside
another symbol's tab, it renders, B486 keeps it current when the inner symbol is
edited, and then it silently disappears when the project is reopened.

What it blocks: any promise that a symbol can hold other symbols, including the
manual's *What a symbol can hold*.

| | What it costs |
| --- | --- |
| **Keep nesting** (recommended, **chosen**) | Stop stripping on load. B487 provides the cycle check: placing refuses a loop, and the renderer draws nothing for one that arrives in a file. Still to build is a depth limit, plus a round-trip test showing that a nested placement survives save and reload. One small branch. |
| **Refuse nesting everywhere** | `PlaceSymbol` refuses any placement inside a symbol tab, matching the load. An artist loses an ability they have today, and B486's nested test describes something unreachable. |
| **Leave it for now** | Nested work keeps vanishing on reopen until somebody decides. |

**Not a preference:** whichever way it went, the two halves had to agree. Allowed
in a session and stripped on reload is data loss that looks like a feature.
