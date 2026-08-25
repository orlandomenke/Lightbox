---
name: optional-settings
description: The two ways an 'optional' setting stops being optional — a non-nullable block that serializes at its default, and a convenience getter that reintroduces the key under a second name. Read when adding a setting, a brush option, a preset field or any new block to the document model.
---

# "Optional" has two halves, and the second one is easy to miss

Both failures below were found by dumping the JSON for a document with one
default stroke — not by reading the model. That is the check to copy.

A setting is optional when it is **absent unless used** — not merely inert at
its default. Two ways that goes wrong, both found by dumping the JSON for a
document with one default stroke rather than by reading the model:

- **A non-nullable block serializes even when it is untouched.** The medium was
  behaviourally absent and written anyway: twenty-one keys on every stroke of
  every document, a third of the brush record, for a pass nobody switched on.
  A block whose default is "off" wants to be nullable, or to have a shadow
  property that returns null when it is untouched.
- **A convenience getter beside a nullable field is a property.**
  `BlendOrNormal => Blend ?? Normal` had a public getter, so every stroke wrote
  `"blendOrNormal": "normal"` — reintroducing under a second name the exact key
  that making `Blend` nullable existed to remove. These need `[JsonIgnore]`.

So: after adding a setting, **serialize a document that does not use it and
look**. `Assert.DoesNotContain("\"yourKey\"", json)` is the cheap version and
belongs in the same commit.
