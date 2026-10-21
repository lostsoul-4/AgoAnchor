# AgoAnchor

**A 3D motion-sickness relief overlay for Windows — 163 KB, single process, zero injection.**

Draws a screen-proportional dot frame plus a split center line on top of your game, so your eyes have a stable reference to anchor on when the camera moves. Built for people who get motion sick from fast FPS camera motion.

![build](https://img.shields.io/badge/build-csc.exe%20only-brightgreen)
![size](https://img.shields.io/badge/exe-163%20KB-blue)
![license](https://img.shields.io/badge/license-MIT-blue)
![platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-lightgrey)

---

## Why another overlay?

Most crosshair / overlay tools either inject into the game process, hook `Present`, or capture the screen and re-composite it. All three add frame cost and all three look suspicious to kernel anti-cheats.

AgoAnchor takes the boring path: **it's just a transparent, click-through, always-on-top window**. It never touches the game process.

```
No DLL injection         No OpenProcess
No memory reads/writes   No SetWindowsHookEx
No API hooks             No SendInput
No screen capture        No drivers
```

The game and the anti-cheat see a floating window, exactly like Discord's or Steam's overlay does — except there is no overlay runtime attached to anything.

## What it draws

| Element | Description |
|---|---|
| **Dot frame** | A rectangle whose aspect ratio always matches your monitor (4:3 gets 4:3, 16:9 gets 16:9, 21:9 gets 21:9). Spacing is rounded to integers so the ratio never drifts. |
| **Center line** | A horizontal line split in the middle so it never covers your crosshair. The gap is measured in pixels, independent of resolution. |
| **X mode** | Two diagonals along the screen's own diagonal direction (29.4° on 16:9, 36.9° on 4:3 — not a hardcoded 45°), split at the center. |
| **Double border** | Each dot and each line segment can carry an inner ring (black) and an outer ring (light grey), mimicking the look of a COD crosshair. Colors, opacity and thickness are independent. |
| **Line over dot** | Optional: any dot that intersects a line segment is not drawn at all — no layering tricks, it's simply skipped. |

All colors share one main palette; the dot frame, center line and X mode stay consistent.

## Measured footprint

Measured on a 2560×1440 dual-monitor setup, Windows 11:

| Metric | Value |
|---|---|
| Executable size | **163 KB** (single file, no runtime bundled) |
| Private memory | **~31 MB** (Task Manager) |
| Idle CPU | **0.0 ms over 5 seconds** (truly zero) |
| Processes | **1** |
| Handles | ~370 |

Idle CPU is zero because there is no render loop. The overlay renders once when a setting changes and then sits blocked on the message queue.

For comparison, an Electron-based overlay of the same complexity typically costs 150–300 MB private memory and 1–3 % idle CPU.

## Requirements

- Windows 10 or 11 (x64)
- .NET Framework 4.x — **already included with Windows**, nothing to install
- The game must run in **borderless windowed** mode. Exclusive fullscreen bypasses DWM composition, and no ordinary desktop window can appear on top of it.

## Build

No .NET SDK, no MSBuild, no Visual Studio. The C# compiler ships with Windows itself:

```bat
build.bat
```

This produces:

```
build\AgoAnchor.exe        main app (winexe, no console window)
build\AgoAnchor.diag.exe   console build for self-tests
```

If a rebuild fails with `CS0016: could not write to output file`, a previous instance is still running — close it from the tray and build again.

## Usage

1. Run `build\AgoAnchor.exe`. There is no console window; it goes straight to the tray and opens the settings panel.
2. Adjust the frame size, spacing, dot size, line gap and colors. Everything applies live.
3. Close the panel. The overlay keeps running. Reopen it from the tray icon (or run the exe again — the second launch just raises the existing panel).
4. Set the game to **borderless windowed** and pick a frame size that sits comfortably inside your field of view.

### Controls

- **Pause** — the pause button at the bottom of the settings panel and the tray menu item share one state. Pausing from either place keeps them in sync.
- **Profiles** — three built-in slots, renameable. Useful for keeping one setup per game.
- **Displays** — tick the monitors you want the overlay on. **Unticking every monitor means nothing is drawn** (there is no implicit "all screens" fallback once configured).
- **Refresh** — after switching resolution or changing display scaling, press this so the overlay re-reads the current mode. Some games change resolution without notifying the desktop.

## Tests

The renderer is deliberately decoupled from the window layer: `Renderer.Render(width, height, profile, opacity)` returns a bitmap and touches no handles. That makes it testable off-screen.

```bat
build\AgoAnchor.diag.exe --selftest
```

Covers:

- Aspect-ratio preservation across 9 resolutions (16:9, 16:10, 4:3, 21:9, 32:9, square) × 4 frame percentages
- Even segment counts so opposite edges share dots
- Pixel gap invariance across resolutions
- Crosshair area stays transparent
- Circle roundness (corners of the bounding square must be empty)
- Profile isolation
- Double-border geometry
- X-mode diagonal angle matches the screen's diagonal

Other console commands:

```bat
build\AgoAnchor.diag.exe --gallery out\    # render reference PNGs for every parameter
build\AgoAnchor.diag.exe --headless 3       # create real overlay windows for 3 seconds
build\AgoAnchor.diag.exe --savetest         # config round-trip check
```

## Anti-cheat

AgoAnchor does not guarantee you will never be flagged — no third-party overlay can promise that. What it does guarantee is that the attack surface is as small as it can be for a tool that draws on your screen:

| Behaviour | Common overlays | AgoAnchor |
|---|---|---|
| Inject DLL into the game | sometimes | **never** |
| Read/write game memory | sometimes | **never** |
| Install API hooks | often | **never** |
| Simulate input | occasionally | **never** |
| Keyboard hook / global hotkeys | common | **never** |
| Screen capture | some | **never** |
| Kernel driver | some | **never** |

There are no hotkeys at all — settings apply the moment you change them. That removes an entire class of kernel-visible behaviour.

It has been used in Call of Duty with Ricochet active, but treat that as one data point, not a certification.

## Configuration

Config lives next to the executable, so the folder can be copied and moved freely:

```
build\data\config.json
```

The app probes a few candidate locations and uses the first writable one, recording the choice in `build\.datapath`. If the folder is not writable (e.g. copied into Program Files), it falls back to your desktop or `%LOCALAPPDATA%`.

## Repository layout

```
AgoAnchor/
├── src/                    all sources
│   ├── Program.cs          entry point, single instance, tray
│   ├── Config.cs           profiles, JSON, autostart
│   ├── Renderer.cs         pure rendering — no window handles
│   ├── OverlayWindow.cs    layered window + UpdateLayeredWindow
│   ├── OverlayEngine.cs    one window per selected monitor
│   ├── SettingsForm.cs     settings UI
│   ├── Strings.cs          four-language string table
│   ├── DpiHelper.cs        per-monitor DPI
│   ├── Paths.cs            writable config directory resolution
│   ├── ColorPicker.cs      color + alpha picker
│   ├── SelfTest.cs         off-screen regression tests
│   ├── Gallery.cs          reference image generator
│   ├── Headless.cs         create overlays without a UI
│   ├── SaveTest.cs         config round-trip
│   └── res/app.ico         icon, embedded via /win32icon
├── build.bat               one-click build
├── AgoAnchor.csproj        for people with MSBuild
├── LICENSE                 MIT
└── .gitignore
```

## How it works, briefly

The overlay is a top-level window with:

```csharp
cp.ExStyle |= WS_EX_LAYERED       // per-pixel alpha
            | WS_EX_TRANSPARENT   // mouse events pass straight through
            | WS_EX_NOACTIVATE    // never steals focus
            | WS_EX_TOPMOST;      // stays above the game
```

Content is rendered into a 32bpp premultiplied bitmap and pushed with `UpdateLayeredWindow`, which hands the bitmap to DWM. DWM composites it over the game at the next vsync.

Because nothing is redrawn unless a setting changes, the window costs nothing while you play.

One non-obvious requirement: the window handle must be created explicitly before the first `UpdateLayeredWindow` call, otherwise there is nowhere to push the bitmap and the overlay appears blank.

## Why not Direct2D / DXGI / Electron?

- `UpdateLayeredWindow` does not accept a DXGI swap chain. Switching to Direct2D means rewriting the window layer with DirectComposition.
- The overlay redraws only when you change a setting — a few times per session. GPU acceleration buys nothing.
- Electron would multiply the footprint by roughly 1000× for a settings panel that is opened twice.

The whole renderer is ~600 lines of GDI+, and the entire program is smaller than a single average screenshot.

## License

MIT — see [LICENSE](LICENSE).

## Contributing

Issues and pull requests are welcome. If you are reporting a rendering problem, please include:

- Monitor resolution and scaling (Settings → Display)
- The relevant `build\data\overlay.log`
- A screenshot of the settings panel

Before opening a PR, run `build\AgoAnchor.diag.exe --selftest` and make sure it still prints `ALL PASS`.


Everything is Vibe coded so I'm just leaving it here for backup purposes or if anyone actually want to take it and use. While it's supposedly anticheat safe, use it at your own risk, I'm not responsible if your get banned for using it by any chance.
