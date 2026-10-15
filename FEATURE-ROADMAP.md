# NabyScape feature roadmap

Reviewed 16 October 2026. Reference sites: [NotchNook](https://getnotchnook.com/) and [Vorssaint](https://vorssaint.com/). The first batch was approved and implemented in 1.7. Entries in the proposed sequence remain proposals, not scheduled commitments.

NotchNook groups music, calendar/meeting access, notes, tasks, timers, a camera mirror, and a file tray around the notch. Vorssaint offers modular local tools, including clipboard history, a command bar, clean links, keep-awake controls, audio utilities, and optional Dynamic Island activities. These are feature references; NabyScape keeps its own black-glass presentation and implementation.

## Already available in NabyScape 1.7

| Reference area | NabyScape capability | Current boundary |
|---|---|---|
| Music and live bars | Windows media sessions, transport, seeking, artwork, output-amplitude animation | Depends on the media app's published controls |
| Timer and focused work | Timer, work/break sessions, deadline persistence | No stopwatch yet |
| Notes, tasks, shelf | Local notes/checklist and file/folder staging | Shelf stores references |
| Clipboard | Opt-in local text/image history, pinning, pause, clear | No global paste-as-plain-text shortcut |
| Audio tools | Master volume, output selection, microphone mute | Per-app/output mixer shipped; no automatic device priority |
| Quick access | Ordered app/folder/web shortcuts and configurable hotkey | Searchable NabyScape commands and saved shortcuts now available |
| Live status | CPU/RAM, battery, clock, track/volume/timer previews | No general Windows notification reader |

## First batch completed in 1.7

- Command bar: local actions and saved shortcuts, keyboard search, no arbitrary shell text.
- Keep awake: explicit start/stop, timed or until quit, optional display request, active indicator, automatic release.
- Clean links: preview and explicit copy, preserving functional parameters and recognized signed links.
- Calendar: user-selected local storage, month/day browsing, event add/delete, optional manual meeting access. No online provider is connected.

## Remaining proposed sequence

| Priority | Addition | How it should fit the compact island |
|---|---|---|
| 2 | Stopwatch and timer presets | Add inside Focus rather than adding another top-level tab |
| 2 | Downloads activity | Opt-in folder selection, arrival preview, open/reveal actions; stop watching when disabled |
| 2 | Device priority | App/output mixer shipped in 2.0; automatic device switching remains optional future work |
| 2 | Accessory and battery alerts | Supported device state and configurable low-battery warnings, suppressing duplicate alerts |
| 3 | Camera mirror | Explicit request, camera-use status, release camera when closed |
| 3 | Disk transfer activity | Network rates and disk capacity shipped; disk transfer counters remain proposed |
| 3 | Lyrics and up next | Real provider data only; source selection and licensing need research |
| 3 | Windows notifications | First verify supported APIs, permissions, filtering, and privacy controls |

## Rules for future additions

Keep the progression **closed status → hover glance → selected tool**. Show one main activity at a time. Keep existing panel bounds and put secondary controls inside tool pages or menus. Optional integrations should stop watchers, subscriptions, and background work when disabled. Respect Reduce motion and fullscreen settings; automatic previews must not take keyboard focus.

Windows integrations need Windows-specific feasibility checks. macOS AirDrop, Apple Shortcuts, Dock behavior, and platform-only display/fan features are not assumed to transfer. Broad system utilities, AI-agent tracking, window-content watching, and a companion remain exploratory ideas outside the next proposed batch. No reference code, branding, or assets are bundled.

## Version 2.0

Shipped: app-session and output volume mixer with individual controls, visual Settings previews, expanded layout/hover customization and import/export, and detailed local system monitoring. GPU temperatures, general notification reading and automatic audio-device priority remain outside this release.

## Version 2.3

- Drag-to-island shelf reveal with three-thumbnail staging, multi-file selection, and cancellation that retains open editors.
- Seven detachable previews: Music, Timer, Focus, Calendar, Notes, Shelf and System. Shared data, resizable windows, optional topmost, and manual hide/show recovery.
- Activities in Launch and command search: ordered app/folder/website launches, Focus, and timed Keep awake. Explicit Start and ownership-aware Stop; no automatic execution.
- Smart clipboard actions for text, links, images and HEX/RGB colors. Existing opt-in privacy and clearing rules remain.
- Closed-cover playback gestures and a secondary countdown satellite alongside music.
- New Controls descriptions, interactive Settings examples, and an optional System hover page (off by default).

Detached notes share the main document. Closing a preview never stops its timer or media. Physical mixed-DPI transitions, monitor hot-plug and cross-application drag behavior still require device-specific verification.
