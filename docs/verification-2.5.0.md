# Local 2.5.0 integration verification

This verifies the local Windows source candidate and packages. Public downloads and showcase assets remain 2.4.2. Nothing was installed, pushed, signed or published. No daily app was running when the final isolated fixtures started, and the installed app and daily profile were not launched.

Integration adds denser chrome below 340 DIP height so the 512 by 252 DIP Settings case retains a usable form viewport. The Closed/Music live preview follows actual media availability instead of showing a brand fallback when idle. Native Settings use a dark translucent client foundation for secondary-text readability; the island keeps its existing material and frame policy.

| Evidence | Result and scope |
| --- | --- |
| Published self-tests | 373 passed, 0 failed; dynamic updater tests use the final 2.5.0 assembly version. |
| Settings source scene registration | All 49 actual section titles have registered render branches. |
| [Settings matrix](evidence/integration-2.5.0/matrix-report.txt) | 314 passed, 0 failed across 12 synthetic viewports; all eight categories, chooser/preview collapse, named-control reachability, horizontal fit and scroll-to-end. |
| [Full original desktop run](evidence/integration-2.5.0/full-original-report.txt) | 702 passed, 5 failed before diagnostic assumption corrections; preserved rather than relabeled as a clean run. |
| [Focused corrected sequences](evidence/integration-2.5.0/focused-regressions-report.txt) | 12 passed, 0 failed: command scroll after transition settles, deferred HEX persistence, explicit Selected display mode, measured Start-label fit at 514/340 DIP. |
| [Composed material transitions](evidence/integration-2.5.0/material-composed-report.txt) | 42 passed, 0 failed; two cycles through all four materials with Appearance re-entry. Exact eight JPEGs and [pixel retention measurements](evidence/integration-2.5.0/material-pixel-retention.json) retain all three labels and the swatch. |
| [Final published material fixture](evidence/integration-2.5.0/published-material-report.txt) | 50 passed, 0 failed, including WPF label probes corrected for the live visual's layout offset. |
| [100-cycle retention](evidence/integration-2.5.0/retention-initial.json) | No Settings/mini windows or subscriber growth; handles +2, managed heap -36,640 bytes, one weakly retained closed window including warm-up references. |

The five original desktop failures arose from a command-scroll assertion during animation, checking color persistence before its save debounce, setting a monitor without selecting its display mode, and two hardcoded Start-button width thresholds. Focused sequences test the resulting behavior with settled timing, actual persistence, explicit monitor selection and text measurement. These corrections do not change production features.

The matrix divides 1024x600, 1280x720, 1366x768 and 1920x1080 by 1/1.5/2, subtracts a 48-DIP taskbar, and renders WPF at each target DPI. Real-monitor containment is frozen only inside the fixture through the existing private guard. The smallest case is 512x252 DIP with a 70-DIP form viewport. This is synthetic layout coverage, not 12 physical display/DPI configurations. [Method](evidence/integration-2.5.0/matrix-method.txt), [small control](evidence/integration-2.5.0/settings-small-control-simulated.png), [wide Settings](evidence/integration-2.5.0/settings-wide-simulated.png), and [stopwatch](evidence/integration-2.5.0/stopwatch-wpf.png) are actual WPF renders.

Earlier Stage 6 missing-content claims were inconsistent automatic visual interpretations and are withdrawn. Independent numeric checks of the saved files prove label and swatch retention. A cache experiment was removed because the uncached captures already pass. See the [reconciled Stage 6 record](verification-stage6-2.5.0.md). DWM success establishes requested/actual API state; composed pixels establish the illustrated content and visible material. Neither establishes GPU-present FPS or a particular blur strength.

The initial limited performance comparison uses three approximately 20-second runs per scene against a fresh 2.4.2 baseline on DISPLAY2, DPI 1 and 180 Hz. It is not the historical 60-second acceptance benchmark. [Baseline](evidence/integration-2.5.0/performance-baseline-2.4.2.json), [initial candidate](evidence/integration-2.5.0/performance-candidate-initial.json), and [comparison including memory, allocations, handles, callbacks and percentiles](evidence/integration-2.5.0/performance-comparison-initial.json) retain raw measurements.

| Scene | Median total-machine CPU, 2.4.2 to candidate | Audio reads / WPF callbacks, before to after |
| --- | --- | --- |
| Hidden | 0.01450% to 0.02420% | 0/0 to 0/0 |
| Closed paused | 0.04358% to 0.05815% | 0/0 to 0/0 |
| Media panel | 1.28354% to 1.34710% | 598/3572 to 598/3580 |
| Static Settings | 0.02906% to 0.02897% | 0/0 to 0/0 |

Media-panel callback p95/p99 were 5.5559/5.5561 ms before and 5.5560/5.5564 ms after. Median private memory rose by roughly 4-14 MiB across these samples; working set rose by roughly 10-16 MiB. These short process-level samples are noisy, and they do not establish a leak or GPU presentation rate. Hidden, paused and static Settings have no audio reads or render callbacks. Generation collection counts remain in the raw JSON. No frame-rate or waveform-detail reduction was made.

The required final static-Settings rerun completed three 20-second samples on the same DISPLAY2/DPI1/180-Hz display. Its [raw measurements](evidence/integration-2.5.0/performance-settings-final.json) are contaminated by continuous external Windows accessibility queries: median CPU 2.06817%, allocation 22.80 MB/s, private memory 211.32 MiB, working set 351.60 MiB, 848 handles, zero audio/render callbacks, median Gen0/1/2 collections 28/2/0. A temporary five-second diagnostic observed zero layout updates, responsive calls, workspace size changes, preview builds or illustration renders, with five ordinary app heartbeats. [Dispatcher stacks](evidence/integration-2.5.0/performance-observer-stacks.txt) identify `MS.Internal.Automation.ElementUtil.Invoke` and `ElementProxy` provider/bounds requests as the repeated work. All instrumentation was removed.

A [control run of unchanged 2.4.2](evidence/integration-2.5.0/performance-observer-baseline-2.4.2.json), under the same observer conditions, also allocated 12.13 MB/s and used 1.09380% CPU over ten seconds, with zero audio/render callbacks. Resetting the JavaScript capture session did not remove the observer. These final observer-contaminated runs cannot establish a fair idle CPU regression comparison; the earlier matched 12-sample comparison remains the clean limited sample. No accessibility support was disabled to improve measurements. A clean final-binary idle CPU acceptance result remains an explicit limitation.

Local delivery is in `artifacts-integration-delivery-final-2.5.0`: setup executable, portable ZIP, notices ZIP, checksums and generated WinGet manifests. [Package validation](evidence/integration-2.5.0/package-validation.txt) independently recomputed all hashes, checked all five notices in both ZIPs and validated the local manifest with WinGet. [Checksums](evidence/integration-2.5.0/SHA256SUMS) identify the final files. The setup executable was not run.

Physical mixed-DPI movement, hotplug, hardware sleep, external OLE drag behavior, real Downloads ACL failures, actual installer execution/migration, signing acceptance and remote WinGet/CI publication remain unverified. Reset chooser presence and earlier isolated reset behavior are covered; this final pass did not inject physical reset-confirmation keyboard input. The Stage 2 unused migration-constructor marker remains a documented minor cleanup; no storage path or migration contract was changed here.
