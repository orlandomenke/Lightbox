# Cloud and SaaS: what the architecture already fits, and what it does not

Status: **assessed, nothing committed to.** The application is desktop-only and
this document does not change that. It exists because the question "could this
also be a cloud/SaaS product" has a much more specific answer than it looks, and
that answer will otherwise be re-derived from scratch by whoever asks next.

**Cloud has never been considered in this repository, and that is worth stating
plainly rather than discovering.** The word appears three times in the whole
tree and two are dismissals: `ROADMAP.md:742` `[?] Cloud libraries`, `:743`
`[?] Team asset sharing`, and `character-reference-gap-analysis.md:374`, which
lists "Works offline (no cloud dependency)" as a **strength**. So there is no
prior decision to reconcile with and nothing to undo.

## The finding, in one sentence

**Nothing below `Lightbox.App` needs restructuring; the significant change is
that the application's brain lives in a view model.**

That is a better position than it sounds. The parts that are usually hardest to
move — the document model, the pixel path, determinism — are already clean. The
part that is hard is concentrated in one file, and it is
`docs/DESIGN-mainviewmodel-decomposition.md`.

## What is already good

Every item here was verified rather than inferred.

- **`Lightbox.Core` has zero `PackageReference` and zero `ProjectReference`.**
  It is display-free by construction rather than by convention, and a grep for
  Skia or Avalonia across it returns one hit, a comment in
  `Geometry/TileGrid.cs` saying that is why the type lives there. 706 tests in
  838 ms.
- **`Lightbox.Raster` is CPU Skia with no `GRContext` anywhere in `src/`**, and
  already ships `SkiaSharp.NativeAssets.Linux`. **Its 457 tests run green
  headless in a Linux container with no display and no GPU.** Server-side
  rendering is not a port; it is a deployment.
- **`Lightbox.Import` touches the filesystem nowhere at all** — `byte[]` in,
  objects out. It is the shape the rest of the codebase should copy.
- **Invariant 1 makes the document a replayable command log.** A frame is a list
  of strokes and the pixels are derived, so a document is 45.9 KB for 20
  strokes. That is the data model a sync engine wants, arrived at for unrelated
  reasons.
- **Invariants 2 and 7, plus `RuntimeDeterminismTests`, give bit-identical
  renders across runtimes.** This is the single most valuable cloud property in
  the codebase and almost no painting application has it: a client and a server
  rendering the same record agree exactly, so **pixels never have to cross the
  wire**. Every plausible architecture below leans on it.
- **An RPC boundary already exists.** `IpcProtocol` / `IpcServer` /
  `PipeBridge` speak one JSON line per request and one per response, `op` plus
  `payload`, a fresh connection per call and no session state. Pipe to HTTP is a
  transport swap rather than a redesign.
- **`.lbproj` is a folder of plain JSON**, chosen deliberately over an archive
  (`Projects/ProjectIo.cs:7`), which maps onto object storage without a format
  change.
- **`IVersionHistoryStore` and `VersionHistoryManager` already sit in Core**,
  are covered by `VersioningTests`, and are referenced by **nothing** in `src/`
  — the only implementer is a test mock. A storage-agnostic version-history seam
  is sitting there waiting for a backend.

## The obstacles, in descending cost

**1 — The session is a view model.** `ViewModels/MainViewModel.cs` is 10,098
lines and is simultaneously the document API, the tool state machine, the render
scheduler and the binding surface, with no interface between those four roles.
Mitigating, and it changes the estimate considerably: the whole file contains
three Avalonia touchpoints, and its base type is CommunityToolkit
`ObservableObject`, which is UI-framework-agnostic. It is movable. It is not
small. See `DESIGN-mainviewmodel-decomposition.md`.

**2 — Six process-global registries in `Lightbox.Raster`.** `BrushTipRegistry`,
`ClipRegionRegistry`, `PaletteRegistry`, `SymbolRegistry`, `TextureRegistry` and
`ReferenceStripRegistry` are static dictionaries keyed by asset id with no
document, session or tenant dimension and no eviction —
`TextureRegistry.cs:105` says so outright: *"Tests only — the app registers and
never unregisters."* The severity differs by registry and the difference
matters:

- Clip regions are genuinely content-hashed — `MainViewModel.cs:4047` builds the
  id from a SHA-256 of the serialized region — so equal ids mean equal content
  and sharing them is harmless. Invariant 3 holds.
- **`PaletteRegistry` and `SymbolRegistry` overwrite on register**, by design,
  because a swatch is meant to be edited and the new value must win. Under one
  user that is the feature. Under two tenants in one process, a shared id
  repaints somebody else's art.

Also `Media/FluidLattice.cs:274` is a `[ThreadStatic]` cache — fine pinned to a
UI thread, a lattice retained per pool thread on a server.

**3 — Ids are not globally unique.** `Documents/Ids.cs` mints
`prefix_unixMillisHex_counterHex` from a process-local counter that starts at
zero, so `swatch_<ts>_1` is the first swatch of *every* process. This is already
a mild hazard when opening another artist's document; it is not safe as a
multi-tenant key.

**4 — Around ten user-scoped stores are static and anchored to one directory.**
`AiSettings`, `ExportPresetStore`, `TimingPresetStore`, `SymbolLibrary`,
`TipStore`, `BrushPresets`, `ShortcutMap`, `AppSettings`, `WorkspaceStore` and
`AutosaveService` all resolve under `%APPDATA%/Lightbox`, most of them off
`ApiKeyProvider.SettingsPath`. Static means one user per process. The
encouraging half: every one already has a `PathOverride` test seam, and
`Docking/WorkspaceStore.cs:184` already did it per-instance **and wrote down
why** — *"writing to a global path anyway is how one test's saved workspace
became the next test's starting layout."* That comment is the template for the
whole migration.

**5 — Core reads the disk directly.** `DocJson.Save` and `DocJson.Load` take a
path, and no `Stream` overload exists anywhere in Core; `ProjectIo` has roughly
thirty filesystem call sites. Cheap to fix, because `DocJson.Serialize` and
`Deserialize` are already string-pure — the seam is additive.

**6 — Undo is lambdas.** `DocumentEditor` keeps a `SnapshotStep` holding a whole
`Doc` clone and a `DeltaStep` holding a pair of `Action<Doc>` delegates. A
closure has no data form, so the stroke commit — deliberately a delta, because
snapshotting per pen lift caused a visible pause — is precisely the step that
cannot be written down, sent, or made a collaborative operation. This is already
`ROADMAP.md:696` and is justified on local grounds alone: a 64-deep stack is
2.8 MB for a 45.9 KB document.

**7 — Latency.** The charter budgets a pointer event during a 4K stroke at
20 ms and a whole stroke plus commit at 400 ms. No network hop fits inside the
first number. Determinism is what rescues this — keep rendering local and sync
the record — and it is why the browser-editor shape below is the expensive one.

**8 — Some features are desktop-bound and do not travel.** Pillar 5 writes into
the artist's own Unity/Godot/Unreal project directory; the planned version
control shells out to a locally installed `p4`/`cm`/`git` and explicitly must
not own the workspace root; `FileReveal`, the `StorageProvider` pickers and
`ProjectWatcher`'s `FileSystemWatcher` all assume a local disk.

**9 — The security posture inverts.** `Services/IpcServer.cs:10` states the
current model: *"The pipe is per-user by OS default — nothing is exposed on the
network."* Separately, and worth fixing regardless: `ProjectIo.ResolveInProject`
performs a proper root-containment check *because a manifest is plain JSON a
person or an agent can edit*, but `Project.PathOf` (`Projects/Project.cs:118`),
`LoadDocument` and `Save` join root and manifest path unchecked. A hostile
`project.json` escapes the root. Locally that needs a hand-edited file; the
moment a manifest is tenant-supplied it is a vulnerability.

**10 — Credentials are plaintext.** `%APPDATA%/Lightbox/ai.json` holds the API
key verbatim — no DPAPI, no keychain.

## The three shapes, and what each costs

| Shape | Fit | The work it needs |
| --- | --- | --- |
| **Headless render / export / AI service** beside the desktop app | **Good, essentially today** | Obstacles 3 and 2 — or sidestep both with one process per job, which is a legitimate answer at low volume |
| **Local-first with cloud sync** — desktop stays the editor, the server holds projects, versions, comments, review, shared libraries | **Good, moderate** | 6 as the keystone, then 5, 4, 3, 2, plus a sync and conflict model that does not exist yet |
| **Browser-based editor** | **Poor** | 1, plus an entire new front end, against 7 and against per-tenant memory — one 8K frame is 380 MB against a 512 MB cache |

The third is a second product rather than a port, and should be costed as one.

**Where this lands: do not build cloud yet.** Build obstacles 2 through 6. Every
one of them pays for itself on the desktop — 6 removes a 64× memory multiplier,
4 removes a class of test-isolation bug the codebase has already been bitten by,
3 removes an id collision when opening someone else's document, 2 removes a
cross-document overwrite that exists today with two tabs open. Doing them leaves
the codebase one transport swap away from the second shape, without having bet
on it.

## Decisions

**Settled — AI billing: both, and the user's key wins.** Bring-your-own-key
stays exactly as it is, and server-held keys become the fallback for hosted
users. The reason is that `AiConnection.Value` already resolves stored value,
then environment variable, then default — the precedence chain has the right
shape to extend with one more tier, and it preserves the local-Ollama and
custom-MCP-agent stories that a server-keys-only model would delete outright.

**Settled — assess all three shapes, commit to none.** This document is the
assessment; the choice is deferred deliberately.

**Open — does "every feature is reachable" survive plan tiers?** The rule at
`CLAUDE.md:41` and `ROADMAP.md:909` forbids gating capability behind a value in
a manifest. A SaaS plan tier gates capability behind a value in an account,
which is the same move wearing a different noun. Three ways out: hold the rule
and monetise hosting, collaboration and AI credits rather than features; make it
a desktop-only invariant and amend the charter explicitly rather than by quiet
exception; or keep everything reachable and meter the expensive operations. Note
before choosing that the unbuilt `FeatureDefaults` registry (`ROADMAP.md:952`)
is the mechanism all three answers would use, and is currently designed pointing
the opposite way.

## What the roadmap says, read for this question

- The **Collaboration** subsection is entirely unbuilt, and it is exactly the
  set of things a server would serve: comments, review mode, version comparison,
  change history, asset locking, cloud libraries, team asset sharing.
- `ROADMAP.md:1014` names *"Real-time collaboration (Figma has set expectations;
  animation tools lag)"* as a competitive gap, with no follow-up item anywhere.
- **The existing multi-user answer is deliberately not cloud.** Version control
  is planned as file locks through a local Perforce/UVCS/git client, on the
  reasoning at `ROADMAP.md:542` that *"locking is the feature, not history"*.
  That answer is not invalidated by anything here, and for a studio on a LAN it
  remains the cheaper one.
- `DESIGN-ai-payload.md:72` rejects GraphQL partly because *"there is no schema
  of theirs to query and no server of ours in the path"* — the nearest the
  repository comes to contemplating a server, and it dismisses one.
- One number in that document is conditional on staying desktop, and should not
  be carried across unexamined: *"768 KB on a 20 Mbit link is about 0.3 s
  against 30–120 s of generation"* (`:59`). That holds because the only thing
  crossing the wire is a model request. Put the canvas on a server and the same
  bytes land inside a 20 ms budget instead of a 30-second one, and every
  conclusion in that document needs re-deriving.
