# Q131 · Does Lightbox go open core, and where does the paid boundary sit — **answered 2026-08-19: yes, open core with paid add-ons; publishing continues unchanged for now**

**Raised by:** a direct question from the owner — switch to an open source core
with paid extensions, keeping the general application free, with the bone system,
the fluid effects and certain roadmap features as the commercial tier. Q54 settled
*public, and GPL-3.0*; this is the question Q54 explicitly left open when it named
sole copyright as the one thing keeping the commercial option alive.

**What it blocks:** the plugin host, the licence posture, and — indirectly — the
`MainViewModel` decomposition, which is the only place an add-on boundary could
be drawn.

## The measurement that made this answerable

Taken 2026-08-19, before the decision rather than after:

| | |
| --- | --- |
| Bone / rig / IK / constraints / correctives, already published GPL-3.0 | **6,221 lines** |
| Fluid effects and sim, already published GPL-3.0 | **5,899 lines** |
| Repo created | 2026-07-30 |
| Public per Q54 | 2026-08-08 |
| Forks | **0** |
| Stars / watchers | **0 / 0** |
| Dynamic assembly loading anywhere in `src/` | **none** — no `Assembly.Load`, no `AssemblyLoadContext`, no `Activator.CreateInstance` |
| Public interfaces in Core/Ai/Raster/Import | 5 — `IAiArtist`, `IMcpChannel`, `IPixelResampler`, `ISimMasks`, `IVersionHistoryStore` |

Two facts do most of the work here. **Roughly twelve thousand lines of the two
features named as commercial are already published under GPL-3.0 and cannot be
recalled** — anyone may fork from any pushed commit and keep them, free, forever.
And **nobody has**: zero forks, zero stars. A silent `git clone` leaves no trace,
so that is the visible measure rather than proof, but for an unstarred alpha
eleven days old the exposure is as close to nil as it will ever be again.

## The decision

**Open core with paid add-ons.** The core stays free; the bone system, the fluid
effects and named roadmap features become the commercial tier.

**Publishing continues exactly as now** — new bone and fluid work keeps going to
the public repo on `main`, rather than being held on branches or moved to a
private repository.

## What this costs, stated plainly

**This went against the recommendation, which was to defer the model and do only
the no-regrets decomposition work.** The costs of the choice made are therefore
worth writing down rather than discovering:

- **Open core needs three things the codebase does not have.** A licence posture
  that permits a proprietary in-process add-on against a GPL-3.0 core — which
  today it does not, so it needs either a linking exception or an MPL-2.0
  relicense, and that is a solicitor's question rather than an engineering one. A
  plugin host, from zero: there is no dynamic loading anywhere in the tree. And an
  entitlement mechanism, which under GPL cannot live in the core because a
  recipient may remove it.
- **The two answers are in tension, and the tension is the interesting part.**
  Publishing bone and fluid work publicly while intending to sell them means the
  commercial tier can only ever be a *future generation* of those features — every
  line pushed is permanently free. That is coherent, and it is the safe form of
  the rule *what has been given stays given*; it is also a date that has to exist
  and has been deliberately set to "not yet". The longer it is deferred the more
  of the paid feature has already been given away. **Left open on purpose, to be
  revisited rather than drifted past.**
- **The document-portability problem is unsolved and collides with invariant 1.**
  The stroke record *is* the document, so a paid effect that paints writes into
  the record. What a free build shows when it opens that file — baked raster,
  placeholder, or refusal — is a product decision nobody has made, and it decides
  whether `.lbx` stays a format an artist can trust.
- **The rug-pull risk is currently zero and is not fixed there.** With no users,
  nothing can be taken back. That protection expires as adoption grows, which
  means the boundary wants deciding while it is still free to decide.

## What is not in doubt

**Sole copyright still holds and `CONTRIBUTING.md` still declines pull requests**,
so relicensing remains available. The day that changes it needs a CLA first — Q54's
sentence, and it is now load-bearing rather than precautionary.

**The process boundary already has doctrine.** `ROADMAP.md` applies it twice —
Laigter shelled out to rather than linked, because linking a GPL-3.0 tool "would
put Lightbox under GPL-3.0, which is a project-level licensing decision and must
not be made by accident"; and Perforce/UVCS clients run as separate processes to
keep the licences apart. `Lightbox.Mcp` is already a separate process over a pipe
and references no Lightbox assembly. An add-on that speaks the MCP surface raises
no linking question at all, which makes it the cheapest first tier — and it will
not carry a brush or a sim, where invariant 6's frame budget rules IPC out.

**Not legal advice, and the owner has been told so twice** — Q54 said an hour with
an IP solicitor is cheap if the commercial stakes are real. The stakes are now
real, and the linking question above is exactly what that hour is for.
