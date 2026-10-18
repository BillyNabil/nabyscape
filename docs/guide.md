# NabyScape user and developer guide

[Website](https://nabyscape.sigma-bw-0740.chatgpt.site/) · [Searchable user guide](https://nabyscape.sigma-bw-0740.chatgpt.site/docs/) · [Interactive island preview](https://nabyscape.sigma-bw-0740.chatgpt.site/#experience)

**Current release: v2.5.1.** [Download and release notes](https://github.com/BillyNabil/nabyscape/releases/tag/v2.5.1). The original showcase film remains labeled v2.4.2.

New installations use the **Daily Notch** layout: 327 × 36 closed island, 526 px panel, 447 × 207 hover, 420 ms motion and icon navigation. [Default layout](presets/default-layout.json). Existing settings are never replaced on upgrade; explicit preference reset uses this default while preserving personal content, startup and clipboard consent.

## Get NabyScape

1. Open the [latest release](https://github.com/BillyNabil/nabyscape/releases/latest). No GitHub account is needed to download.
2. Download **NabyScape-win-x64-Setup.exe** and run it to install for your Windows user.
3. Prefer portable use? Download the **NabyScape-win-x64-Portable.zip**, extract it, and run **NabyScape.exe**.

Each release includes **SHA256SUMS** for verifying the downloads. For development builds, use the [Windows build workflow](https://github.com/BillyNabil/nabyscape/actions/workflows/windows.yml); downloading its artifacts requires a GitHub account.

**Windows 11 x64 is required.** The self-contained build includes the .NET runtime; you do not need an SDK to run it. The current installer is unsigned. Portable use still stores preferences in your Windows user profile.

Open Settings from the island or tray. Changes save automatically. Starting with Windows is optional and off by default; closing Settings keeps the app running. Choose **Quit** from the tray to exit.

## New in v2.5.0

The release includes the features below. [Release notes](releases/v2.5.0.md) describe the interaction fixes and verification limits. The [integration report](verification-2.5.0.md) preserves the original test record; the installer remains unsigned.

- Settings keeps its custom caption and resize controls, section index, responsive category navigation, schematic examples and independent island preview. Appearance offers **Settings material**: Solid (the original default), Simulated glass (a WPF gradient), Native Mica or Native Acrylic. Mica is an opaque wallpaper/theme-based foundation; Acrylic is a translucent system blur. Native support requires Windows 11 build 22621 or later. High contrast, disabled Windows transparency, energy saver, remote sessions, unsupported APIs or a failed DWM call produce a Solid fallback with requested/actual status. Windows controls the final native appearance. The scene is schematic; examine the Settings window itself for the material. The layered island contour keeps its existing simulated glass; native island hit testing, shaped transparency, animated resize, physical DPI and performance remain unverified. Layout exports and Appearance reset include the independent Settings material; older `luma.layout.v1` profiles still import.
- Preference reset and local-data migration follow the preservation rules below. Automatic tuck has an adjustable edge, an Always tucked option and a separate manual latch. Manual Hide takes priority over previews and tucking.
- **Application → Updates** checks official stable releases, verifies compatible downloads and offers a separate explicit install/restart action. See [release and signing limits](RELEASING.md); publishing a release does not remove SmartScreen warnings.
- **Application → Notifications** routes NabyScape's own Timer, Focus, Downloads, Updates and failure notices to Notch, Windows, Both or Off. Notices expire without a saved history. This does not read other apps' Windows notifications. **Downloads** is off by default and infers arrivals from readable files with stable size in one selected folder; it cannot guarantee that a browser has finished. Originals stay in place; folder paths are excluded from layout exports.
- **Focus → Stopwatch** provides Start, Pause, Resume and Reset, shares its persisted session with hover and detached views, and counts elapsed sleep time. **Layout → Display** offers Primary, Selected or Follow cursor; Follow waits 250 ms on another display and defers during island interaction or manual hide. Detached previews remain where placed. Physical mixed-DPI relocation, hotplug and hardware sleep require device testing.

The targeted native fixture checks API/client setup and captures the composed Settings HWND using Windows.Graphics.Capture. WPF-only scene renders cannot prove a native backdrop, and API success alone does not measure blur strength, GPU-present FPS or physical-DPI behavior.

Hover pages are selected in **Settings → Content** independently of which main panels are visible. Tool-specific pages still require their tool switch. There is no four-slide limit. Clicking a preview opens its target without changing your saved panel order. Clipboard capture remains a separate opt-in preference.

## Shortcuts that stay out of the way

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+I` | Open or close the panel. |
| `Ctrl+Alt+H` | Hide or show the entire island. |
| `Ctrl+Alt+T` | Toggle manual tuck. |
| `Ctrl+K` | Search commands while the panel is focused. |
| `Ctrl+Space` | Play/pause when the island header is focused and cover gestures are enabled. |
| `Ctrl+M` | Choose a media session from the focused island header when cover gestures are enabled. |
| `Escape` | Go back or close. |

The panel, visibility and tuck global shortcuts are configurable. Buttons retain their click actions; wheel input on hover previews switches pages. After clicking open a panel, content scrolls normally.

## Compatibility and privacy

<details>
<summary><strong>Media sessions, audio routing, and waveforms</strong></summary>

Controls follow the Windows media sessions exposed by each app. Choose the app in **Media**. Waveforms read matching sessions across active outputs, so a selected Spotify session is not driven by Discord audio.

Choose **Appearance → Closed media icon** for album artwork or a waveform icon. **Closed media content** independently selects the song title or a compact waveform. Countdown and volume feedback keep priority.

Waveforms show amplitude history, not a frequency spectrum. They do not record audio or use the microphone. Optional gentle motion during quiet or unavailable readings is decorative. Unsupported app identities may use this fallback. Muted sessions return to a quiet baseline.

Each waveform bar follows a differently delayed level envelope with quick attack and smooth release, giving the original centered waveform more varied motion. Reduce Motion keeps it still.

The visual range adapts to small peak changes in mastered music, so a narrow audio range does not flatten the bars. **Settings → Audio → Live signal** shows whether the selected app has an audible meter, is muted, or is using the optional quiet fallback. No audio or volume is changed by this display.

Mixer rows control individual Windows audio sessions; multiple sessions for one app can appear separately. Output selection changes the Windows default output. Apps with explicit routing preferences may keep their selected device.

</details>

<details>
<summary><strong>Hardware readings, glass, and motion</strong></summary>

- GPU measurements depend on Windows performance counters. Missing readings are labeled unavailable.
- Temperature/fan readings require an already running LibreHardwareMonitor or OpenHardwareMonitor WMI provider. NabyScape installs no sensor driver.
- Brightness feedback requires supported Windows events. There is no brightness slider or DDC/CI support.
- Battery controls hide on PCs without a battery.
- Focus mode manages sessions inside NabyScape; it does not change Windows Do Not Disturb.
- The glass surface uses WPF gradients, reflections, and artwork tint. It does not capture or blur the desktop.
- Animation follows the Windows compositor. Callback cadence does not guarantee GPU-present FPS. Reduce Motion disables continuous animation.

</details>

<details>
<summary><strong>Local data, clipboard privacy, and upgrading</strong></summary>

No account or cloud service is required. Preferences, timers, notes, tasks, shortcuts, and calendar events stay on this device. Clipboard history is opt-in; disabling it clears stored clipboard history and images. File shelf entries refer to the originals.

The v2.6.2 development [AI companion](assistant.md) lives in the top island. Hover its character for a greeting and **Chat here / Talk**, or click it to chat inside the island. AI connections start off; simply hovering never starts a microphone or provider request. If enabled, your chosen messages and explicitly enabled audio/screen context go to your configured provider; approved note-reading can send note contents. Provider keys live in Windows Credential Manager, chat history stays in memory, and screen/control permissions last only for the session. **Back to island** keeps a live conversation running; **Stop**, **Esc**, manual hide or Alt+F4 ends it. The public v2.5.1 download does not yet include this candidate feature.

**v2.5.0** stores local content and preferences in `%LOCALAPPDATA%\NabyScape`, migrating legacy LumaIsland data as described below. Existing installations retain their install path and internal identifiers for compatibility:

```text
%LOCALAPPDATA%\NabyScape\              v2.5 local preferences and content
%LOCALAPPDATA%\LumaIsland\             v2.4.2 data; preserved legacy copy after v2.5 migration
%LOCALAPPDATA%\Programs\LumaIsland\    Per-user installation
luma.layout.v1                         Existing layout-profile format
```

The executable, Start Menu shortcut, app display name, and enabled startup entry use **NabyScape**. Updates recognize previous app names and preserve settings, local content, and startup state. Internal single-instance and uninstall registration identifiers remain compatible.

**v2.5.0:** on first launch, migration copies legacy data into a sibling staging folder, validates JSON and clipboard-image references, then activates the complete copy with a directory rename. External shelf, shortcut and activity targets stay unchanged. If both data folders exist, NabyScape uses its own folder and warns you to review the legacy copy. If migration fails, it uses the legacy folder with a warning; fix the reported permissions or invalid file and restart to retry. Failed staging folders are kept for review and are never loaded as active data.

**v2.5.0 — Application → Reset preferences** offers a category chooser, Reset this category, Reset all preferences and Restore last preference backup. A confirmation describes the affected values. Reset writes a preference backup before changing settings; backup failures abort the reset. Reset and restore preserve startup, clipboard capture/history, shortcuts, Notes, Tasks, shelf, calendar and activity targets. Restore applies preference categories only, so it cannot re-enable clipboard capture or startup from an old backup.

**v2.5.0 — Controls → Automatic tucking** offers Always tucked and an edge height from 3 to 12 DIP (default 6 DIP). Fullscreen and maximized rules keep their previous defaults. The configurable `Ctrl+Alt+T` shortcut, tray action and command action toggle a manual tuck latch; the second toggle releases it to automatic rules. Hover reveals a preview without taking focus. Manual Hide stays fully hidden, including when the tuck shortcut is used. Settings reports conflicts between the panel, visibility and tuck shortcuts and failures to register a shortcut already used by another app.

Uninstall through **Windows Settings → Apps → Installed apps → NabyScape**. Personal content is retained. The current installer is unsigned.

</details>

## Build from source

You need **Windows 11 x64** and **.NET SDK 10**.

Open [`NabyScape.slnx`](../NabyScape.slnx) in your IDE, or run `dotnet build NabyScape.slnx -c Release` from the repository root. The app project is [`src/NabyScape/NabyScape.csproj`](../src/NabyScape/NabyScape.csproj). The root build script publishes the standalone distribution and runs checks as before.

```powershell
git clone https://github.com/BillyNabil/nabyscape.git
cd nabyscape

# Publish the app and installer, then run logic checks.
.\build.ps1

# Also capture interactive desktop diagnostics.
# Quit running instances first and use a fresh output folder for each run.
.\build.ps1 -OutputDirectory '.\artifacts-desktop' -RunDiagnostics
```

Use `-Dotnet 'C:\path\to\dotnet.exe'` to select a specific SDK. Output defaults to `artifacts/`, including the app, installer, licenses, and `unit-report.txt`. The Windows workflow builds and tests pushes and pull requests; interactive desktop diagnostics run separately on a local Windows desktop.

<details>
<summary><strong>Verification and project structure</strong></summary>

Logic checks cover persistence, profile compatibility, audio fixtures, layout, tools, and services. Desktop diagnostics cover rendered bounds, hover navigation, Settings, media sessions, visibility, timers, and idle activity.

Desktop diagnostics use an isolated profile and a muted publisher. They do not change daily playback, routing, microphone state, or clipboard contents. A keep-awake test briefly prevents automatic sleep. Physical hot-plug, other DPI configurations, cross-app drops, hardware volume changes, clipboard-busy behavior, and battery transitions still need device-specific checks.

Performance workloads use a deterministic local media fixture and the same animation and frame-rate settings as the app. Quit other NabyScape instances first:

```powershell
# Demand-driven sampling, editor retention, and window lifecycle checks.
.\artifacts\NabyScape.exe --diagnostics "$PWD\artifacts\performance-checks" --performance-checks-only

# Fourteen scenes, three repetitions, 60 seconds per scene, then 100 window cycles.
.\artifacts\NabyScape.exe --diagnostics "$PWD\artifacts\performance" --performance-benchmark
```

See the [local v2.4.2 performance verification](performance-2.4.2.md) for measured comparisons, app captures, raw runs, and limitations.

The benchmark writes `performance.json`, `retention.json`, progress, and screenshots. CPU is normalized across all logical processors. WPF callback intervals describe callback pacing, not frames presented by the GPU; GPU utilization is left unavailable when it cannot be measured reliably. Synthetic media isolates UI work and does not replace device-specific Spotify, audio-routing, or hardware checks.

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

</details>

## Source layout

The repository root contains the solution, build entry point and project documentation. Application code lives under `src/NabyScape/`:

| Directory | Responsibility |
| --- | --- |
| `Application/` | App lifecycle, storage/models, native integration, profiles and installer. |
| `Features/Media/` | Media sessions, waveform input, audio devices, mixer and feedback. |
| `Features/System/` | System/hardware sampling and monitor views. |
| `Features/Productivity/` | Notes, tasks, clipboard, calendar and utility views/services. |
| `Features/Activities/` | Activity runner, editor and live activity visuals. |
| `Features/Interaction/` | Drop shelf, detached previews and shared gesture behavior. |
| `Features/Launch/` | App catalog, icons, target picker and launcher presentation. |
| `UI/Island/` | Closed/hover/expanded surfaces, carousel, panel navigation and feedback. |
| `UI/Settings/` | Settings window, categories, controls and contextual previews. |
| `UI/Shared/` | Common controls, icons, color picker, scrolling and thumbnails. |
| `Motion/` | Animation, frame pacing and island geometry. |
| `Tests/` | Logic checks run by the existing `--self-test` command. |
| `Diagnostics/` | Isolated desktop, performance and showcase checks/fixtures. |
| `Assets/` | Runtime app icon. `Theme.xaml` remains at project level for its stable WPF URI. |

`Assets/` at the repository root holds documentation imagery; runtime assets belong to the app project. Third-party notices live in `licenses/` and are embedded under their existing resource names. The published filenames, namespaces, storage paths and installer behavior are unchanged. CI uses the root `build.ps1`, which resolves the app project from its own location.

## Contribute

Found a bug or have a feature idea? [Open an issue](https://github.com/BillyNabil/nabyscape/issues) with your Windows version, app version, and steps to reproduce it. Screenshots are helpful; remove personal content before sharing them.

See [the roadmap](roadmap.md) for implemented capabilities and proposed additions. NotchNook and Vorssaint are feature references; their assets and implementations are not bundled.

## License

NabyScape is available under the [MIT License](../LICENSE). Copyright (c) 2026 BillyNabil.

Dependency notices: [NAudio](../licenses/NAudio-LICENSE.txt), [Lucide](../licenses/LUCIDE-LICENSE.txt), [.NET](../licenses/DOTNET-LICENSE.txt), and [.NET third-party components](../licenses/DOTNET-ThirdPartyNotices.txt).
