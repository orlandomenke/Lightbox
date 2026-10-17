# Q287 · Does a document reference follow an absolute path outside the document's folder? — **answered 2026-10-16: yes, follow it**

Raised by the sensitivity review of the document-reference feature (Q284). A
reference picked from outside this document's folder is stored as an absolute
path, and is re-read with no action from the artist — on open, on a tab switch,
whenever the window comes forward. A received document can therefore name a
local file outside its folder to be read.

| | What it costs |
| --- | --- |
| **Follow it** (recommended, **chosen**) | The artist's own pick is honoured and stays live. A received document can cause a local file to be *read* and its frames copied into the record — never written. The same exposure `VideoPath` already has. |
| **Ask once per path** | Safest: one prompt the first time a document names a new outside path, and a remembered allow-list to build. |
| **Folder only** | Simplest; a reference across project folders keeps its last frames and never refreshes. |

**What is refused regardless:** network shares and device paths (a UNC path
would open a connection to another host and offer it credentials), and a
relative path that climbs out of the folder with `..`. Those are not a
preference — they were fixed in the same branch.
