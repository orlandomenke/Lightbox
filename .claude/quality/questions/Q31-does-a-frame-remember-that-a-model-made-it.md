# Q31 · Does a frame remember that a model made it? — **answered (a)**

**Answered 2026-08-07: (a), stored on the frame, absent unless AI touched it.**
A hand-drawn frame writes no key, so a document that never used the AI is
byte-identical to one from before the feature existed — the camera's rule.

**Blocks:** nothing. Phase 0 can proceed.

`docs/DESIGN-ai-correctness.md` puts a verifier and a deterministic fallback
behind every AI inbetween, which means three frames can look alike and have very
different histories: one the model got right, one it got wrong and was repaired,
one that fell back to the deterministic engine entirely.

**(a) Stored on the frame, absent unless AI touched it.** An artist returning
after a month knows which frames to trust and which to look at again. It is a
new key in the document, so hand-drawn frames write nothing — the camera's rule.

**(b) Session-only.** The timeline marks AI frames while the app is open and
forgets on reload. No format change; the information vanishes exactly when it is
most wanted.

**(c) Not tracked.** An inbetween is an inbetween.

**Recommend (a).** The whole feature is a claim about trust, and a claim you
cannot audit a month later is not one. Note the cost honestly: it is a document
format change, and *derived* data in the record is the mistake Q16 avoided for
placement readings — the defence here is that provenance is not derived from
anything, it is a fact about how the frame came to exist.

**Blocks:** phase 0 of the correctness pipeline.
