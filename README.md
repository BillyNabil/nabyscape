# NabyScape

A compact, customizable notch for Windows 11. Control music, adjust app volumes, check your PC, and keep everyday tools close.

**Windows 11 x64 · C# / WPF · .NET 10 · English interface · Local storage**

![NabyScape media controls](Assets/screenshots/compact-media.png)

## Features

| Area | What you can do |
| --- | --- |
| Media | Artwork, playback controls, seeking, media-session selection, and an audio-reactive waveform. |
| Audio | Master volume, individual app/output sliders, mute, output selection, and microphone mute. |
| Focus | Countdown timers and configurable work/break sessions with completion notifications. |
| System | CPU history, RAM, uptime, GPU activity/memory, hardware details, network, storage, and processes. |
| Launch | Search installed apps or save applications, files, folders, and websites. Choose icons, labels, or both. |
| Tools | File shelf, notes/tasks, opt-in clipboard history, local calendar, keep awake, and link cleaning. |
| Appearance | Notch/pill shapes, colors, material, size, position, typography, widget order, and live Settings previews. |

## Interaction

- **Hover** to browse a compact preview.
- **Scroll anywhere on the preview** or drag left/right to switch pages.
- **Click** to open the corresponding panel. Scrolling alone stays in preview mode.
- **Escape** goes back or closes; **Ctrl+K** inside the panel opens command search.
- **Ctrl+Alt+I** opens/closes the panel; **Ctrl+Alt+H** hides/shows the island. Both are configurable.
- Automatic hiding above fullscreen/maximized apps leaves a narrow edge at the top. Hover over it to reveal the preview. Manual hiding removes the island completely.

The tray menu provides Settings, visibility recovery, and Quit. Starting with Windows is optional and off by default.

## Get started

Build from source below, or download a successful Windows build from [GitHub Actions](https://github.com/BillyNabil/nabyscape/actions/workflows/windows.yml). Workflow artifacts require a GitHub account to download.

1. Run `NabyScape-Setup.exe` for a per-user installation, or `NabyScape.exe` directly for portable use.
2. Hover or click the island to explore its controls.
3. Open **Settings** from the island or tray. Changes save automatically.

Self-contained builds include the .NET runtime. Installation does not require an SDK. Portable use still stores preferences in your Windows user profile.

## Build from source

Requirements: **Windows 11 x64** and **.NET SDK 10**.

```powershell
git clone https://github.com/BillyNabil/nabyscape.git
cd nabyscape

# Publish a self-contained build and run logic checks.
.\build.ps1

# Include interactive desktop diagnostics and screenshots.
# Quit running instances first; use a fresh output folder for each run.
.\build.ps1 -OutputDirectory '.\artifacts-desktop' -RunDiagnostics
```

Use `-Dotnet 'C:\path\to\dotnet.exe'` to select an SDK. Output defaults to `artifacts/` and includes the app, installer, dependency notices, and `unit-report.txt`. Desktop diagnostics additionally write an integration report and captures under `diagnostics/`.

The Windows workflow builds and runs logic checks for pushes and pull requests. Interactive diagnostics require a local Windows desktop and run separately.

## Media and audio

Controls follow the Windows media sessions exposed by each app. Choose the app in **Media**. Waveforms read matching sessions across active outputs, so a selected Spotify session is not driven by Discord audio.

Choose **Appearance → Closed media icon** for album artwork or a waveform icon. **Closed media content** independently selects the song title or a compact waveform. Countdown and volume feedback keep priority.

Waveforms show amplitude history, not a frequency spectrum. They do not record audio or use the microphone. Optional gentle motion during quiet or unavailable readings is decorative. Unsupported app identities may use this fallback. Muted sessions return to a quiet baseline.

Mixer rows control individual Windows audio sessions; multiple sessions for one app can appear separately. Output selection changes the Windows default output. Apps with explicit routing preferences may keep their selected device.

## Hardware and display limits

- GPU measurements depend on Windows performance counters. Missing readings are labeled unavailable.
- Temperature/fan readings require an already running LibreHardwareMonitor or OpenHardwareMonitor WMI provider. NabyScape installs no sensor driver.
- Brightness feedback requires supported Windows events. There is no brightness slider or DDC/CI support.
- Battery controls hide on PCs without a battery.
- Focus mode manages sessions inside NabyScape; it does not change Windows Do Not Disturb.
- The glass surface uses WPF gradients, reflections, and artwork tint. It does not capture or blur the desktop.
- Animation follows the Windows compositor. Callback cadence does not guarantee GPU-present FPS. Reduce Motion disables continuous animation.

## Privacy and compatibility

No account or cloud service is required. Preferences, timers, notes, tasks, shortcuts, and calendar events stay on this device. Clipboard history is opt-in; disabling it clears stored clipboard history and images. File shelf entries refer to the originals.

Existing installations retain these internal paths and identifiers for compatibility:

```text
%LOCALAPPDATA%\LumaIsland\             Local preferences and content
%LOCALAPPDATA%\Programs\LumaIsland\    Per-user installation
luma.layout.v1                         Existing layout-profile format
```

The executable, Start Menu shortcut, app display name, and enabled startup entry use **NabyScape**. Updates recognize previous app names and preserve settings, local content, and startup state. Internal single-instance and uninstall registration identifiers remain compatible.

Uninstall through **Windows Settings → Apps → Installed apps → NabyScape**. Personal content is retained. The current installer is unsigned.

## Verification

Logic checks cover persistence, profile compatibility, audio fixtures, layout, tools, and services. Desktop diagnostics cover rendered bounds, hover navigation, Settings, media sessions, visibility, timers, and idle activity.

Desktop diagnostics use an isolated profile and a muted publisher. They do not change daily playback, routing, microphone state, or clipboard contents. A keep-awake test briefly prevents automatic sleep. Physical hot-plug, other DPI configurations, cross-app drops, hardware volume changes, clipboard-busy behavior, and battery transitions still need device-specific checks.

## Project structure

| Files | Responsibility |
| --- | --- |
| `OverlayWindow`, `CompactOverlay`, `PeekCarousel` | Overlay states, panels, and hover navigation. |
| `SettingsWindow`, `SettingsCustomization`, `CustomizationProfile` | Settings, previews, and layout profiles. |
| `Services`, `MediaAudioMeter`, `AudioDevices`, `AudioMixer`, `MixerOverlay` | Windows media and audio controls. |
| `SystemMonitor`, `HardwareMonitor`, `SystemMonitorOverlay` | System sampling and presentation. |
| `Productivity`, `UtilityServices`, `UtilityOverlay`, `RoadmapOverlay` | Local content and tools. |
| `Motion`, `LiveActivity`, `IslandGeometry`, `FramePerformance` | Motion, shape, activity, and pacing. |
| `App`, `Models`, `Native`, `Installer` | Lifecycle, storage, Windows integration, and installation. |
| `*Tests`, `Diagnostics`, `MediaFixture` | Logic and desktop verification. |

See [FEATURE-ROADMAP.md](FEATURE-ROADMAP.md) for implemented capabilities and proposed additions. NotchNook and Vorssaint are feature references; their assets and implementations are not bundled.

## License and notices

The project license is pending selection. Dependency notices: [NAudio](NAudio-LICENSE.txt), [Lucide](LUCIDE-LICENSE.txt), [.NET](DOTNET-LICENSE.txt), and [.NET third-party components](DOTNET-ThirdPartyNotices.txt).
