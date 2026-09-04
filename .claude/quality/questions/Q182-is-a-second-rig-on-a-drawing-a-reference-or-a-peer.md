# Q182 · Is a second rig on a drawing a reference, or a peer? — **answered by the owner, 2026-09-04: a peer**

Raised out of [[Q181]], whose decision 2 this **supersedes**. That decision gave
a saved rig two landings — "use as armature" and "place as a proportion guide",
the second explicitly *not* posable. The owner's correction, on being shown the
plan:

> *"the rigs might still want to be animated. So I can draw over them without
> losing the reference"*

A posable mannequin you rough out across frames and draw on top of is not a
ghost. Q181's ghost was a drawing aid; this is a second character.

The same message closed a smaller thing: *"the 3 defined where just examples and
i might want to create endless rigs"* — the library was always open-ended and
"human, dog, goblin" was only shorthand in the plan's prose, but the plan read
as though those three were a fixture. Nothing in the design changes; the wording
did.

## What it blocks

Whether `Doc.Armature` stays singular. Everything else here follows from that
one answer.

## The finding that made the question cheap to ask

**The animation half needs no new record at all.** `PoseKey.Bones` is a
`Dictionary<string, BonePose>` keyed by **bone id**, and it is deliberately
sparse — a bone absent from a key is at rest on that key, so a walk cycle can
key legs frame by frame while the skull is never mentioned.

One `PoseTrack` therefore animates any number of rigs already, with no
ambiguity, provided bone ids are unique across them. That is a property the
record has had since phase 1 and nobody had reason to notice.

Which leaves the cost entirely in the singular `Doc.Armature`:

| | |
| --- | --- |
| `.Armature` call sites | **90** |
| …of them in `MainViewModel.Armature.cs` | **35** |
| `PoseTrack` sites | 51 |

The concentration is the good news. Most of those thirty-five say *the rig* and
come to say *the rig I am editing*, which is one new concept rather than
thirty-five decisions.

## The answer, and what was given up

**A peer.** A document holds any number of rigs, all equal: all posable on the
shared track, any layer may follow any of them, any of them may have art bound
to it.

The rejected option was one working rig plus N animatable references — cost M
against L, and its migration was free where this one needs a real one. It was
refused because the asymmetry is not true of the work: two characters
interacting in one shot are two rigs with art bound to both, and a model that
allows only one to own art would have to be undone the first time that came up.
Paying M and then L is worse than paying L.

## What follows, and is therefore not a further question

- **The file gains an `armatures` list**, read alongside the legacy `armature`
  key and written in its place. `Doc.Version` exists for exactly this. A
  document with no rig still writes neither key — absent stays absent, which is
  the rule that does not bend.
- **`Doc.Armature` survives as a `[JsonIgnore]` derived accessor** meaning *the
  selected rig*, so most of the ninety sites keep compiling and keep meaning
  something true. The `[JsonIgnore]` is not optional: a public getter beside a
  nullable field is a property to `System.Text.Json`, which is how
  `blendOrNormal` once got written on every stroke.
- **`Armature` gains an `Id` and a `Name`.** It has neither today, because there
  was only ever one.
- **Each placement gets fresh bone ids**, exactly as a pulled guide gets a fresh
  guide id. Pulling the goblin twice must be two goblins, or one pose track
  cannot tell them apart — which is the whole reason the sparse id-keyed
  dictionary works.
- **Unselected rigs draw dimmer.** They are construction either way; which one
  you are editing has to be legible without clicking.

## The order it lands in

Four branches, each one objective, because "a document holds many rigs" is one
sentence and four pieces of work:

1. **The record** — `Armature.Id`/`Name`, `Doc.Armatures`, the derived accessor,
   the load migration. Behaviour identical with one rig.
2. **The rig you are editing** — selection, the bone tool acting on it, the
   dimmer drawing for the rest.
3. **Bindings across rigs** — a layer's `BoneId` and a stroke's `Weights`
   resolve against whichever rig owns that bone.
4. **Placing a library rig as an additional rig** — fresh bone ids, sizing
   already done by `ArmatureFit`. Q181's refusal rules get revisited here: they
   guard *replacing* a bound skeleton, and adding one is not replacing.
