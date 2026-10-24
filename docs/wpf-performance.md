# WPF runtime and hover performance

NabyScape uses one WPF application process. The experimental Native dock, separate
UI/Core hosts, renderer selector and embedded Native payloads have been removed.
Local speech workers remain available when their ASR/TTS backend needs them.

## Opening a preview

- The first painted frame of the application precedes preview-shell preparation.
  The lightweight shell is prepared at application-idle priority, outside pointer
  input handling. Hover entry starts the configured dwell timer immediately.
- Widget previews are constructed on their first visit. Music hover does not
  construct hidden note editors, clipboard rows, calendar controls or weather
  controls. Visited pages remain retained so drafts, focus and selection survive.
- Every new page receives its current safe dimensions before its first layout.
  Adjacent pages are prepared when a swipe actually needs them.
- Painted contour frames reuse the last placement bounds and DPI. Normal
  placement, settings, display and monitor-follow events refresh that context.
  The contour still updates every accepted animation frame, including side and
  bottom docking positions.

The spring curves, frame policy, typography, artwork, gradients, waveform and
character animation are preserved. There is no production forced GC, periodic
working-set trim or feature disabling to make memory counters appear smaller.

## Character rendering

Allocation profiling identified temporary `Matrix[]` stacks in WPF bounds walks.
The character's eye subtree previously nested ten transforms. Retained transform
groups now compose that subtree with a shallower stack, avoiding its growth from
eight to sixteen matrices. The silhouette keeps its original transform sequence
to preserve raster rounding at fractional DPI. All original clocks, periods,
independent pointer gestures, blink and live-state values remain in use.

`--assistant-transform-only` compares both drawing paths with identical geometry,
brushes and pose values. It covers every character state, five sizes, five DPI
scales and five poses, including blinks, headphones and the live badge. Its report
requires identical pixels, rather than inferring fidelity from frame counts.

An alternating A/B measurement on the same process (180 Hz display, 16 logical
processors, three 10-second samples per path/scenario after 3-second warmups)
recorded the following. CPU percentages are fractions of the whole machine:

| Scenario | Original CPU | Retained eye groups | Managed allocation reduction |
| --- | ---: | ---: | ---: |
| Closed, paused | 1.113% | 0.930% | 26.3% |
| Closed, playing | 1.103% | 1.054% | 16.8% |

Both paths evaluated approximately 180 character-clock updates per second. The
playing scenario had 601 live-render callbacks per 10-second sample on both
paths. The final drawing path passed 875 pixel comparisons with no differences.
These are local measurements, not universal savings or GPU presentation rates.

The accepted change uses two eye groups and leaves the original outer transform
chain in place. A broader grouping variant and a smaller idle HWND were rejected:
their measurements did not establish a consistent CPU benefit. Separate-process
timing proved sensitive to frame cadence, so the final comparison alternates
both drawing paths in one process. `-AlternateCharacter` on
`tools/Measure-Performance.ps1` reproduces that comparison; its original reference
path is diagnostic-only and is never selected by normal application startup.

Settings closes on a later dispatcher turn after an unsaved-provider decision.
This avoids re-entering WPF's `Close` while its first `Closing` event is still
running, which previously left the window and subscriptions alive after Discard.
The island lifecycle diagnostic exercises the actual synthetic Discard button,
then checks that the window closed and its temporary key was cleared.

## Migration

Updating preserves the user's preferences and model storage. The installer
removes only the retired shipped host files. Imported personal model files are
not touched. A legacy `RendererMode` property is ignored on load and removed on
the next normal preference save; other unknown preference fields are retained.

## Verification

Run the ordinary build for logic tests, Settings-scene coverage and uninstall
path checks. Run UI checks against the published executable, whose manifest and
child executable are required for real DPI and window-fixture tests:

```powershell
./build.ps1 -Dotnet <dotnet-path> -OutputDirectory <artifact-directory>
<artifact-directory>/NabyScape.exe --assistant-only --diagnostics <new-report-directory> --hover-responsiveness-only
```

Related UI suites are `--stage1-only`, `--stage5-only`,
`--assistant-character-only`, `--performance-checks-only` and
`--interaction-hotfix-only`, each with its own report directory. They cover
responsive Settings, retained editing state, character motion, hidden-window
sampling and tuck/fullscreen interactions. Diagnostic media playback is isolated
from live Windows media-session changes, including after changing preferences.

Hover diagnostics report opening work, the first render callback, spring settling
and callback intervals separately. Configured pointer dwell is excluded from the
opening measurement. WPF callbacks do not prove GPU presentation time. The
spring's final subpixel tail is observed until completion rather than treating a
fixed sleep as proof that it has finished.

For resource comparisons use `tools/Measure-Performance.ps1`, identical scenarios
and fresh directories. Compare the same memory counter: private working set,
total working set and private commit measure different things. Live ASR/TTS,
screen sharing, imported model sizes, monitor refresh rate and open Settings
windows can materially change resource use. A fixed 100 MB limit is not promised.
