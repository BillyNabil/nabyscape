# NabyScape user and developer guide

## Get NabyScape

1. Open the [latest release](https://github.com/BillyNabil/nabyscape/releases/latest). No GitHub account is needed to download.
2. Download **NabyScape-Setup.exe** and run it to install for your Windows user.
3. Prefer portable use? Download the **windows-x64-portable.zip**, extract it, and run **NabyScape.exe**.

Each release includes **SHA256SUMS.txt** for verifying the downloads. For development builds, use the [Windows build workflow](https://github.com/BillyNabil/nabyscape/actions/workflows/windows.yml); downloading its artifacts requires a GitHub account.

**Windows 11 x64 is required.** The self-contained build includes the .NET runtime; you do not need an SDK to run it. The current installer is unsigned. Portable use still stores preferences in your Windows user profile.

Open Settings from the island or tray. Changes save automatically. Starting with Windows is optional and off by default; closing Settings keeps the app running. Choose **Quit** from the tray to exit.

## Shortcuts that stay out of the way

| Shortcut | Action |
| --- | --- |
| `Ctrl+Alt+I` | Open or close the panel. |
| `Ctrl+Alt+H` | Hide or show the entire island. |
| `Ctrl+K` | Search commands while the panel is focused. |
| `Ctrl+Space` | Play/pause when the island header is focused and cover gestures are enabled. |
| `Ctrl+M` | Choose a media session from the focused island header when cover gestures are enabled. |
| `Escape` | Go back or close. |

The two global shortcuts are configurable. Buttons retain their click actions; wheel input on hover previews switches pages. After clicking open a panel, content scrolls normally.

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

Existing installations retain these internal paths and identifiers for compatibility:

```text
%LOCALAPPDATA%\LumaIsland\             Local preferences and content
%LOCALAPPDATA%\Programs\LumaIsland\    Per-user installation
luma.layout.v1                         Existing layout-profile format
```

The executable, Start Menu shortcut, app display name, and enabled startup entry use **NabyScape**. Updates recognize previous app names and preserve settings, local content, and startup state. Internal single-instance and uninstall registration identifiers remain compatible.

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
