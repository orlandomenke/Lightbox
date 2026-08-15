# Q59 · The audio track: which output backend, and where does the sound live? — **answered, both recommendations taken, 2026-08-08**

Raised when the audio track (Q58's first "adopt next") came up in the queue.
Playback needs a native audio output — .NET has none built in and Avalonia
does not either — and a native dependency is exactly the kind of decision that
goes to the owner before code. Asked with the question prompt.

### The answers

- **Output: OpenAL-soft through Silk.NET.OpenAL.** One small, LGPL,
  ships-everywhere native library, bound by a .NET Foundation-maintained
  wrapper. Decoding stays managed — WAV read by our own code, OGG via NVorbis
  and MP3 via NLayer when they arrive — so the native surface is output-only.
  The alternatives were waveform-without-playback (cheapest, but a silent
  audio track misses the point: you animate to the sound, not the picture of
  it) and SDL2 (battle-tested but a windowing/input/audio kitchen sink linked
  for one function).
- **Storage: reference by path, never embed.** The document stores a relative
  path plus offset/volume/mute; waveform peaks cache separately. Documents
  stay small, the source file stays editable in a DAW, and a missing file
  degrades to a silent badge rather than an error. TVPaint and OpenToonz do
  the same. Embedding would make the file self-contained at the cost of
  megabytes per document and autosave churn on a blob that never changes.

### What did not need deciding

Optionality. Whichever way both questions went, the audio block is nullable
and absent-until-used — a document without audio writes no keys, shows no
audio UI, and pays nothing. That is the same rule the camera already proves.
