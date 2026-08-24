# Q163 · How far do the two font paths go in this first branch? — **answered 2026-08-24: both, installed and Google, in one branch**

| | What it costs |
| --- | --- |
| Local now, Google next (recommended) | Installed fonts behind a provider interface sized for a second source; Google in its own branch, because network policy, a cache location, licence display and offline behaviour are a design of their own. |
| **Both in this branch** (**chosen**) | Google-sourced fonts arrive with the tool. One review carries the tool, the shaping, the UI *and* a network-backed library with a disk cache — which is more than one objective by the branch rule. |
| Google first | A catalogue identical on every machine, at the price of nothing working offline until the cache is warm. |

**Chosen against the recommendation, and what it actually cost:** the branch
carries `IFontSource` with two implementations, a catalogue cache, a font-file
cache, a licence map and an offline story. Two things were given up to keep it
one branch rather than two:

- **The MCP surface is not in it.** Placing type from an agent is a document
  capability and belongs there eventually — but a diff touching MCP needs the
  ai-engineer / art-director pair under charter gate G12, which is a second
  review of a different kind. It is a roadmap line, not an omission.
- **Nothing about the Google endpoints could be verified live**, because the
  environment this was built in denies `fonts.google.com`. Everything is tested
  against captured responses, and the code is written so that being wrong about
  the endpoint degrades to "showing what is cached" with a line of text saying
  so, rather than to an error.

The keyless route was a consequence of this choice rather than a separate
decision: the developer API needs an API key, and asking an artist to sign up to
a cloud console before they can set a title is not a text tool. So the catalogue
comes from the endpoint the Google Fonts website itself reads, and the font
files from the documented CSS endpoint — with a user agent old enough to be
served TrueType rather than woff2, which Skia cannot open. That one trick is
written down where it lives.
