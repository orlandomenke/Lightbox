# Packaging

Today's distribution is the self-contained zip that `.github/workflows/build.yml`
uploads — the app runs from anywhere, no install, no admin. What lives here is
everything an *installer* needs the moment one ships, so that day is a
pipeline change rather than a design task. The brand is already threaded
through all of it: **`docs/design/brand/gen_logo.py` is the one source** for
the mark, the wordmark, the app icon and every file named below.

## Getting a build to try

Two ways, and they produce deliberately different things.

**A dev build, no Release, no tag.** Actions ▸ **release** ▸ *Run workflow*,
pick the branch, run it. When it finishes, the run page has an **Artifacts**
section at the bottom holding `Lightbox-win-x64-<version>-<branch>-<sha>`.
Download it, extract once, run `Lightbox.exe`. It keeps for 14 days.

The version is derived rather than chosen: `<VersionPrefix>` from
`Directory.Build.props` plus `-alpha.<run number>`, then the branch and the
short commit. That is what makes two builds of the same alpha off different
branches tellable apart in a downloads folder.

**A Release.** Push a `v*` tag. That builds the same bundle, creates a GitHub
Release, and attaches the zip to it. *Run workflow* never does this — a Release
is permanent and public and wants a version behind it, and "build me something
to try" wants neither.

> **Extract once, not twice.** The artifact download is a zip and the app is
> inside it directly. It used to be a zip inside a zip:
> `actions/upload-artifact` always compresses what it is given and offers no way
> to opt out, so handing it a zip we had already made produced two layers. The
> workflow now hands it the publish folder and only builds a zip for the tag
> path, where `gh release upload` attaches a file as-is and one is genuinely
> needed.

## The icon, per platform

| Platform | File | Where it is used |
| --- | --- | --- |
| Windows, in the EXE | `src/Lightbox.App/Assets/lightbox.ico` | Embedded at build via `ApplicationIcon`; Explorer, taskbar, alt-tab, shortcuts all read it from the executable |
| Windows, the installer | `docs/design/brand/lightbox.ico` | `SetupIconFile` in `windows/lightbox.iss` — the setup EXE's own face — and `UninstallDisplayIcon` for Add/Remove Programs |
| macOS | `docs/design/brand/lightbox.icns` | Goes to `Lightbox.app/Contents/Resources/` with `CFBundleIconFile=lightbox` when a bundle ships |
| Linux | `docs/design/brand/appicon-{16,24,32,48,64,128,256,512}.png` | Install each as `/usr/share/icons/hicolor/<size>x<size>/apps/lightbox.png`; `linux/lightbox.desktop` names the icon `lightbox` and the theme resolves it |

All of them are renders of `docs/design/brand/lightbox-appicon.svg` — the
split-gray tile with the mark — so regenerating after a brand change is
`python3 gen_logo.py` plus the render commands in that file's history, not
redrawing anything.

## Windows installer

`windows/lightbox.iss` is a complete Inno Setup definition: per-user install
by default (the app never needs admin), desktop shortcut as an opt-in task,
icon wired at every surface an installer shows one. The build steps are in
the file's header comment; CI does not run it yet.

## Linux

`linux/lightbox.desktop` is the desktop entry. A future .deb/.rpm/AppImage
lays down the publish output, the desktop file, and the hicolor icons —
nothing else is required for the app to look installed.

## FFmpeg (video export and video references — Q56)

`VideoExporter.FindFfmpeg()` looks for the binary in **`ffmpeg/` beside the
executable first**, then falls back to PATH. So packaging means dropping a
static ffmpeg build into `publish/<rid>/ffmpeg/` before zipping or running
Inno Setup — the `[Files]` recursion picks it up with everything else.

Static builds, one file each, no installers of their own:

| Platform | Source |
| --- | --- |
| Windows x64 | <https://www.gyan.dev/ffmpeg/builds/> (`ffmpeg-release-essentials`, take `bin/ffmpeg.exe`) |
| Linux x64 | <https://johnvansickle.com/ffmpeg/> (static `ffmpeg`) |
| macOS | <https://evermeet.cx/ffmpeg/> |

FFmpeg stays a **separate executable, never linked** — that is the LGPL
boundary and the crash boundary in one. A machine without the bundled copy
and without ffmpeg on PATH gets a sentence in the status bar naming the fix,
not an error dialog.
