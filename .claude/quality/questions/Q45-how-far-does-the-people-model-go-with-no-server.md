# Q45 · How far does the people model go, with no server? — **answered: a name and an id, forever**

**Answered 2026-08-07: `Person` is a label with a stable id, and it never gains
a role or a rights field.** Recorded as a decision rather than left as a comment
on the type, because the pressure to add one arrives with the first dashboard
filter.

**The reason is that rights inside this application would be theatre.** The
manifest is plain JSON on disk — a stated design commitment, so an agent can
read and write any part of it and so a project diffs in git. A permission a text
editor defeats is not a permission; it is a UI that lies about what it enforces,
which is the same class of defect as a menu entry bound to nothing and worse,
because people plan around it.

An advisory role field was rejected for that exact reason: a role that grants
nothing will be read as granting something, and the first time somebody asks why
a junior could still edit a locked shot, the honest answer is that the field
never meant that — by which point the studio has organised around it.

Designing the client/server split now was rejected as architecture for a product
that does not exist, paid for by the one user who does.

**The two positions this leaves, in order:**

1. **The project file is the shared state and the network is somebody else's** —
   git, a shared drive. The `.lbproj` folder-of-JSON layout was designed for
   this, and assignment and status are fields people edit and merge.
2. **Feed an existing tracker** — ShotGrid, Kitsu, Flow — through an adapter, if
   a studio ever needs one. It needs no new model, because documents already
   have stable ids to match a shot against. Kitsu being open-source is the same
   instinct as bring-your-own-model.

**The accepted cost:** two people editing one manifest can conflict, and nothing
in Lightbox mediates it. The merge is the studio's, the same as for any other
file in their repository.
