# Packaging

Today's distribution is the self-contained zip that `.github/workflows/build.yml`
uploads — the app runs from anywhere, no install, no admin. What lives here is
everything an *installer* needs the moment one ships, so that day is a
pipeline change rather than a design task. The brand is already threaded
through all of it: **`docs/design/brand/gen_logo.py` is the one source** for
the mark, the wordmark, the app icon and every file named below.

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
