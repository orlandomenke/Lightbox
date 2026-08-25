# Input traces, kept as fixtures

Every `.txt` in this folder is an input trace written by **F9 ▸ hover ▸ F9** in
the application (`InputTrace.WriteReport`). `InputTraceReplayTests` reads all of
them and replays each through a real canvas, so a capture that was once used to
diagnose a pen problem keeps being checked for as long as it sits here.

**Why this folder exists at all.** B126, B254 and B255 live on hardware this
repository has not got — a Huion pen on Windows, whose driver posts a phantom
mouse stream beside the pen. Diagnosing one of those costs a round-trip to the
reporter's machine, and until now the capture that came back was read once and
then only existed inside a bug entry as a number. A capture dropped in here is
the same evidence turned into a test.

## Adding a real capture

1. Drop the file in unedited, named for the machine and the symptom —
   `huion-kamvas-hover-flicker.txt`, not `trace3.txt`. The prose at the top is
   the provenance; deleting it to save bytes throws away which build, which OS
   and which minute this was.
2. Add an assertion for it in `InputTraceReplayTests` if it has a *specific*
   claim to make — "this capture used to tear the ring down 39 times a second
   and must now do it never". Without one it is still checked by
   `EveryCheckedInCaptureReplaysCleanly`, which is the floor rather than the
   point.
3. Say in the bug entry that the capture is here. A trace named in a ledger and
   findable nowhere is the state this folder replaces.

**Do not edit a capture to make a test pass.** A capture is evidence; the moment
it is tuned it is a fixture that agrees with the code by construction, which is
the failure mode `BUGS.md` calls a green checkbox over a guess. Trim it — the
format is line-oriented and dropping whole events from the end is honest — or
replace it with a fresh capture, and say which in the file's own prose.

## What a replay can and cannot show

It drives the events the canvas received, so it holds the application's
**reaction**: the leave grace, the ring, the crossing counters. It does not
reach anything above those handlers — pointer-over is recomputed inside
Avalonia's input manager, which is where B255's `PenEchoFilter` attempt died,
and no replay from here touches it. A green run is not a fixed pen.

## The two formats

`replay v1` captures carry position, pressure, tilt and modifiers. `replay v2`
adds the three things the *painting* path reads and v1 could not describe:

- **the coalesced batch.** `CanvasControl` calls `GetIntermediatePoints` and
  appends a sample per point, so a v1 capture describes a hover faithfully and a
  stroke as something sparser than the artist drew.
- **contact, per sample.** The paint path drops any sample not in contact, and
  coalesced history reaches back past the press into hover positions — letting
  those into a stroke is B185. Inferring this from the surrounding press and
  release, which is all a v1 replay can do, infers the thing under test.
- **the device's own clock**, which is not the trace's. The gap between them is
  driver and dispatcher latency (B189), and the speed axis is computed from it.

**v1 files still load**, and the replay says which it got rather than pretending.
A v1 capture is still evidence about hover; it is not evidence about a stroke.

## The two synthetic files

Neither is a capture, and both say so in their own headers.
`synthetic-huion-echo.txt` is a hover in v1 — the reader's back-compatibility and
the leave grace. `synthetic-coalesced-stroke.txt` is a stroke in v2, and carries
one sample from before the press so that B185 has something to fail on.
