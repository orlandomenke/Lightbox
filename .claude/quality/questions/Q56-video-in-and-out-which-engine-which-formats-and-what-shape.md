# Q56 · Video in and out: which engine, which formats, and what shape does footage take? — **answered, all recommendations taken, 2026-08-08**

Raised when the owner asked for a render pipeline ("export our animations to
(professional) video files") and, in the same breath, video to draw against
("like gumball" — drawn characters over live footage). Both need a codec
engine .NET does not have, so the dependency went to the owner before code,
the same way Q59's audio backend did. Asked with the question prompt.

### The answers

- **Engine: a bundled FFmpeg binary, driven as a subprocess.** Frames pipe
  in, the file comes out; an encoder crash cannot take the app down, the
  LGPL boundary stays clean (a separate executable, not linked code), and
  the same binary decodes footage for references. The installer pays ~25 MB.
  The alternatives were system-FFmpeg-on-PATH (every artist pays a setup
  step, support inherits every version) and FFmpeg.AutoGen bindings (fastest,
  and a codec bug crashes the application in-process).
- **Export v1: H.264 MP4, ProRes 422 MOV, and a numbered PNG sequence with a
  WAV.** Review, editorial handoff and comp pipelines respectively, one
  dialog. The scratch track muxes into all of them. DNxHR and WebM are
  argument sets away when asked for.
- **Footage: a reference layer, never a drawing layer.** Imported the way
  references work today — under the drawing layers, mapped to the timeline
  (video time follows scene fps, with an offset), referenced by path like
  audio (Q59), and never exported. Extracting frames onto a raster layer was
  rejected: it bloats the document with footage bytes and the frames would
  export unless remembered and excluded.

### Also decided in the same exchange (not video)

The tool rail rearrange: buttons flow into **1–3 columns adaptively by
window height**, horizontally centred — 2 columns the comfortable default,
1 when the window is tall enough for a single column, 3 when it gets short.
Every tool always visible, never scrolled.
