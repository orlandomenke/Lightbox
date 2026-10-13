# Q259 · How does Lightbox reach the Claude CLI headlessly? — **answered 2026-10-13**

**Answered: a direct provider. Lightbox launches `claude -p` itself, once per request, with its own system prompt and schema and every tool off. No MCP in the loop.** All eight as recommended, in two rounds — the second after the review pair and the sensitivity guardian had read the first build.

Raised by the owner, 2026-10-13: *"Via a headless runner I should be able to send requests from Lightbox to claude CLI via the MCP. But I am unsure if we then can keep the agents/prompt requirements in the MCP. That would mean after setting up the MCP we can use Lightbox instead of switching to Claude to perform the AI actions."*

What it blocks: using AI assistance from inside Lightbox on a Claude subscription, with no API key, and the first run of the inbetweener on real drawings.

## What was found first

The worry in the question turned out not to depend on the answer. The inbetweening rules — the system prompt, the reply schema, the verify-and-repair loop — belong to Lightbox and are sent with every request by every provider, so no transport can drop them. The rules that *do* live in the MCP server (tool descriptions, read budgets, the folder guards, provenance on inserted frames) govern a different direction, an agent driving the document from outside, and are untouched.

What does differ by route is whether the reply is checked: the verifier runs on the in-app artist path and not on frames inserted over MCP.

## What was asked, and what was answered

**1. Which route?** *A direct CLI provider* (recommended) — a catalogue entry and an artist class that spawns the command line. Against: a bridge MCP server behind the existing **Custom agent (MCP)** provider, which needs no change to the application but is a second program to ship and configure, and whose contract carries no images, so character-sheet references are dropped. Against: the agentic route, a headless Claude working the document through Lightbox's MCP server — the most flexible, and the one whose results arrive unverified, with the requirements reduced to prose in a prompt.

**2. What are the three test files?** Lightbox documents with keys. Not to hand on the day; the run on them follows.

**3. Do real drawings feed the golden set's empty Organic category?** *Look first* (recommended). Nothing committed; the drawings stay out of the public repository until that is decided on its own.

**4. May the new launcher be added to the process allowlist in `SENSITIVITY.md`?** *Yes, the one line* (recommended). Against: folding the launch into `StdioMcpChannel.cs`, which is already allowed — no baseline change, and exactly the invisibility the list exists to prevent.

## Second round, after review

**5. What environment does the launched program get?** *Scrub the key, quiet the traffic* (recommended): `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and `ANTHROPIC_BASE_URL` are removed and `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1` is set, so the provider always means the signed-in account and an AI action sends only the request. Against: inheriting and documenting that a key wins, which makes the provider's own name conditionally untrue; or quieting traffic only, which leaves the billing surprise. Cost of the choice: someone routing Claude Code through a gateway by environment variable cannot do it from here.

**6. What is it called?** *Claude Code (your account)* (recommended). "On this machine" sat beside Ollama, the one provider where nothing leaves, and read as local.

**7. Does the egress inventory get its own row?** *Yes* (recommended) — a launched third-party program with its own credential and traffic is a new kind of path even though the `*Artist.cs` glob matched it, and the MCP launcher had set the precedent.

**8. The look-then-draw loop** (render, draw, look, correct once, still through the verifier): *roadmap it, not now* (recommended). Both reviewers called it the real gain in expression the stroke-only routes cannot reach; it waits for measurements on real drawings.

## What it costs

- **A new place the application starts a program**, and a new route by which a drawing reaches Anthropic — through the account Claude Code is signed in to rather than a key Lightbox holds. The provider's summary and the manual say so.
- **A few seconds per request** for starting the program; the two-request connection test measured 22 s.
- **It depends on a command line Lightbox does not ship.** The flags it relies on (`--json-schema`, `--safe-mode`, stream-json input) were checked against Claude Code 2.1.283; an older install fails at Test connection with the CLI's own message rather than silently.
- **The agentic route is still not reachable from inside Lightbox.** An artist who wants Claude to look around the document still switches windows.
