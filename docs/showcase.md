# NabyScape · release 2.10 visual tour

[Latest silent film](https://github.com/BillyNabil/nabyscape/releases/download/v2.10.0/NabyScape-demo.mp4) · [Repository movie](../Assets/showcase/nabyscape-demo.mp4) · [README](../README.md)

![Actual NabyScape 2.10 music and compact dock](../Assets/showcase/release-hero.png)

The 29-second film was captured from actual WPF controls using the refreshed Daily Notch default. It includes closed, hover, music, compact Weather, System, Notes, Tasks, Shelf and Calendar views. Settings images are actual 2.10 windows. Browser state buttons switch these captures and do not connect to Windows.

`tools/render_release_media.py` composes a 1280 × 800, 30 fps silent film from the capture frames. Backgrounds and captions are presentation artwork. Album art, note text, task names and filenames are original samples. Audio amplitude is synthetic, no song is recorded, and the weather forecast is illustrative. System readings are from the capture computer. No personal clipboard, notes, provider keys or city are captured.

The capture-only showcase uses an isolated temporary data profile and its own single-instance name. It does not invoke the test suite. Images and film are documentation, not a benchmark of presented GPU frames or RAM use.

## Historical 2.4.2 gallery

The older images and GIF below remain historical references. Their version labels apply only to those historical captures; the current MP4 has been replaced by the 2.10 film.

## The moving preview

[![Actual app animation: compact, hover, Music, System, Notes, Tasks, Shelf and Calendar](../Assets/showcase/demo.gif)](https://github.com/BillyNabil/nabyscape/releases/download/v2.4.2/NabyScape-demo.mp4)

**[26-second MP4, 1280 × 800, silent](https://github.com/BillyNabil/nabyscape/releases/download/v2.4.2/NabyScape-demo.mp4)** · [Historical release copy](https://github.com/BillyNabil/nabyscape/releases/download/v2.4.2/NabyScape-demo.mp4)

| Time | View |
| --- | --- |
| 0:00 | Closed artwork, waveform and bold clock. |
| 0:02.5 | Music hover preview and its real expansion transition. |
| 0:06 | Full media panel. |
| 0:09.5 | System Overview. |
| 0:12.5 | Notes. |
| 0:15.5 | Tasks. |
| 0:18 | File shelf. |
| 0:21 | Local calendar. |
| 0:23.5 | Collapse to the closed island. |

The UI is rendered from the actual WPF controls, with the author's current visual preferences. Capture frames are sampled at 30 fps; this is documentation media, not a GPU-presented-frame benchmark. Artwork, note text, tasks and filenames are original sample content; waveform input is a synthetic sample signal. There is no recorded music. System readings come from the capture machine. The backgrounds and captions are presentation artwork rather than app features.

## A closer look

### Music

The compact island can show artwork alongside a title or an amplitude waveform. Hover for playback and volume; open the panel to seek or choose an available Windows media session. Unsupported actions stay disabled. Audio meters follow the selected session across active outputs. Select Spotify in Media to keep Discord out of that waveform.

![Closed, hover and expanded Music side by side](../Assets/showcase/interaction.png)

### System

Overview puts the common readings together. Hardware, Network, Storage and Processes separate the longer details. Sampling follows visible consumers; a CPU/RAM mini window does not need a processes scan. Availability depends on Windows and the hardware provider.

![Compact System overview](../Assets/showcase/system.png)

### Notes and tasks

Notes remains one local document across views. Tasks has its own tab; enable Tasks in Content → Hover pages if you want to check items from a preview. Text editors retain ordinary selection and scrolling rather than treating your typing as a carousel gesture.

| Notes | Tasks |
| --- | --- |
| ![The shared local note](../Assets/showcase/notes.png) | ![The Tasks tab with three sample items](../Assets/showcase/tasks.png) |

### File shelf

Drop to stage references, select multiple items, and drag them out together. Originals are not moved or copied when staging. The shelf holds up to 40 items, deduplicates paths and handles missing sources. Manual hide stays respected.

![File shelf and its selection controls](../Assets/showcase/shelf.png)

### Detachable previews

Drag the preview grip to pull it onto the desktop, or use Detach as the keyboard alternative. A view can be moved, resized and optionally kept on top. Its close button closes the view, leaving playback or countdowns running. Positions are remembered and clamped back onto available displays; windows do not automatically reopen after a restart.

![Borderless Notes mini window](../Assets/showcase/detached-notes.png)

### Calendar and countdowns

Calendar stores the events you add locally. Timer and Focus share their countdown with all their views and retain accurate deadlines across sleep/resume. Focus sessions do not change Windows Do Not Disturb.

| Calendar | Timer and Focus |
| --- | --- |
| ![The local month calendar](../Assets/showcase/calendar.png) | ![Timer and Focus controls](../Assets/showcase/focus.png) |

## Settings gallery

These are eight different, full-size captures of the actual Settings window. Click any image to examine its controls. The visual preferences match Midnight Notch. Personal content is not included; the Launch page in this gallery uses the isolated profile's defaults.

### Appearance

Pick a notch or pill. Set surface, accent, control fill, outlines and selection colors separately. Bold media titles and panel headings are independent. Motion duration, Reduce Motion and performance profiles remain available. The live preview shows the current state and content.

[![Appearance: shape examples, color/material controls and current closed preview](../Assets/showcase/settings-appearance.png)](../Assets/showcase/settings-appearance.png)

### Layout

Choose display and placement, then size the closed island, hover and panel independently. Panel dimensions include padding, content scale and section spacing. Layout import/export is here, together with the geometry it changes.

[![Layout: display selection, placement and size controls](../Assets/showcase/settings-layout.png)](../Assets/showcase/settings-layout.png)

### Content

Keep panel order, enabled widgets, individual tools and hover pages together. A disabled tool retains its saved content. Clipboard capture is a separate opt-in; enabling a Clipboard hover page does not grant capture permission.

[![Content: enabled widgets, order and dedicated visual examples](../Assets/showcase/settings-content.png)](../Assets/showcase/settings-content.png)

### Controls

Set hover timing, swipe behavior, drag reveal, detachable previews, cover click/hold gestures, the companion, keyboard shortcuts and automatic tucking. Fullscreen and maximized tucking have separate preferences; hover the remaining edge to reveal the island.

[![Controls: hover and interaction preferences with visual examples](../Assets/showcase/settings-controls.png)](../Assets/showcase/settings-controls.png)

### Audio

Choose the closed media icon and title/waveform content. Inspect the selected app's live signal, choose quiet fallback motion and configure feedback. Mixer access is here too. Signal bars display amplitude history rather than frequency bands.

[![Audio: closed media appearance and waveform preferences](../Assets/showcase/settings-audio.png)](../Assets/showcase/settings-audio.png)

### System

Choose clock format, seconds and date display, and which monitoring details to show. Hardware counters and sensors may be unavailable; missing values remain labeled instead of being invented.

[![System: clock preferences and monitor controls](../Assets/showcase/settings-system.png)](../Assets/showcase/settings-system.png)

### Launch

Add app, folder or website targets with clear names. Choose shortcut presentation. Enable or disable Activities, whose editor lives under Launch in the island. Activities run only after Start; there are no automatic triggers or arbitrary shell steps.

[![Launch: shortcut editor, display preferences and Activities](../Assets/showcase/settings-launch.png)](../Assets/showcase/settings-launch.png)

### Application

Choose whether to start with Windows. Open the local storage folder, check version information or quit the app. Startup and personal data stay independent of exported visual layouts.

[![Application: startup, local data and version information](../Assets/showcase/settings-application.png)](../Assets/showcase/settings-application.png)

## Use Midnight Notch

Download [Midnight-Notch.json](https://github.com/BillyNabil/nabyscape/releases/download/v2.4.2/Midnight-Notch.json) and choose **Settings → Layout → Layout profile → Import layout**. Export your current layout first if you want an easy way back.

| Preference | Showcase value |
| --- | --- |
| Shape and closed size | Notch · 324 × 38 px |
| Hover | 500 × 202 px |
| Panel | 546 px wide · 504 px maximum height |
| Surface / accent | `#000000` / `#FFFFFF` · 100% opacity |
| Control fill / outlines | `#16171B` / `#303238` |
| Selection fill | `#34373E` |
| Media | Artwork icon + waveform content |
| Typography | Bold media titles and headings |
| Motion | 420 ms · Reduce Motion off · Maximum profile |
| Companion | Off |

Dimensions are logical pixels and adapt to available screen space. The profile retains the `luma.layout.v1` format. It includes visual and interaction preferences, tool visibility and order. It excludes personal content, clipboard history preference, activity targets, app shortcuts, startup and mini-window positions. Imported profiles never opt you into clipboard capture.

## Reproduce the captures

Build the app on an interactive Windows desktop and quit any running NabyScape instance first. Use a fresh capture directory. Export your own visual profile from Layout if you want different preferences.

```powershell
.\build.ps1

.\artifacts\NabyScape.exe --diagnostics "$PWD\artifacts\showcase" --showcase --showcase-layout "$PWD\docs\presets\midnight-notch.json" --background

# Wait for integration-report.txt to report zero failures.
# Documentation tooling only: Python + Pillow and FFmpeg on PATH.
python -m pip install Pillow
python tools/render_showcase.py artifacts/showcase
```

The opt-in `--showcase` branch works inside diagnostics, using its separate `test-profile`. It imports only layout preferences, seeds sample content and a synthetic audio meter, captures the actual island/Settings views, then exits. Native clipboard capture and writes remain disabled. It does not import the daily profile, play a song, change volume/routing or change startup preference. Fullscreen/maximized tucking is disabled only in the isolated capture so background windows cannot hide the subject.

`frames/` contains the 780 PNG capture frames. `chapters.json` records scene timing. The render script adds original backgrounds/captions and writes PNG posters, the GIF and silent H.264 MP4 to `Assets/showcase/`. The script uses Segoe UI from Windows; FFmpeg and Pillow are documentation tools, not application dependencies. The `settings-live.png` asset is a separate capture of the running daily Settings window; the reproducible gallery uses the isolated session.

For profiling, use the separate [performance workloads and report](performance-2.4.2.md). Recording and image encoding are excluded from those measurements.

## Recreate the marketing assets

`python tools/render_marketing.py` composes `marketing-hero.png`, `marketing-social.png` and `marketing-video-poster.jpg` from the committed actual WPF cuts and original app icon. No app is launched. Pass a static-site output folder to copy the necessary captures and film, create optimized WebP gallery crops and include the separately labeled v2.5 Settings evidence. Pillow is required. The historical showcase assets and the existing `render_showcase.py --social-only` workflow remain available.
