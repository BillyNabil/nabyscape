# NabyScape 2.7.9 performance verification

This is a local measurement report, not a guarantee for every desktop. The comparison uses the published 2.7.8 executable and self-contained 2.7.9 builds on the same 16-logical-core Windows machine, WPF tier 2, 180 Hz monitor and 100% DPI. The fixture uses the same Auto frame policy, dimensions, animation settings and deterministic media signal on both builds. The user's saved performance profile is preserved during installation.

## Measurement method

Each timed scenario warms for three seconds and runs for 15 seconds, repeated twice. Private working set is sampled once per second through `K32GetProcessMemoryInfo` with `PROCESS_MEMORY_COUNTERS_EX2`; it is reported separately from total working set and private commit. Memory tables use the median of timed samples. CPU uses process CPU time divided by elapsed time and all 16 logical cores. Allocations are managed allocated bytes per second, not retained memory.

The paired memory fixture runs Hidden → Closed/paused → Music panel in exactly that order for both versions. Broader candidate experiments also covered closed playback, static/moving Settings and detached Notes/Music/System windows; those experimental files are labeled separately because the retained-wave renderer was subsequently rejected. Captures occur before timing and can leave native rendering caches resident. These fixtures use synthetic media and isolated data; no real provider call, microphone session or screen capture is started.

## Final paired measurements

| Scenario | 2.7.8 private working set (MiB) | 2.7.9 private working set (MiB) | Reduction |
| --- | ---: | ---: | ---: |
| Hidden | 142.18 | 56.98 | 59.9% |
| Closed, paused | 144.00 | 58.57 | 59.3% |
| Music panel | 150.55 | 66.00 | 56.2% |

These are medians across 30 timed samples per case, except 29 for the new music panel. The full observed 2.7.9 ranges were 38.86–69.60 MiB hidden, 50.50–63.21 MiB closed/paused and 61.89–68.42 MiB for music. Sequential UI/cache history affects these values; the minimum hidden reading is not a general-use memory claim.

| Scenario | 2.7.8 CPU (% whole machine) | 2.7.9 CPU | 2.7.8 allocations (KiB/s) | 2.7.9 allocations |
| --- | ---: | ---: | ---: | ---: |
| Hidden | 0.165 | 0.097 | 114.9 | 97.1 |
| Closed, paused | 1.035 | 1.002 | 896.7 | 709.7 |
| Music panel | 2.434 | 2.401 | 1508.6 | 1459.2 |

Active CPU is broadly comparable within measurement variation; the substantial improvement is resident private memory. Closed/paused managed allocations fall about 21%. The frame cap remains 180 in the music case; recorded WPF callbacks were 2661/2624 before and 2512/2626 after over approximately 15 seconds. This is not a GPU presentation or smoothness guarantee. [Raw paired results](evidence/optimization-2.7.9/paired-after.json), [baseline](evidence/optimization-2.7.9/paired-before.json), and [working-set/commit summary](evidence/optimization-2.7.9/memory-summary.json).

The final EXE/installer is approximately 192.2 MiB and portable ZIP 75.6 MiB. Build/package checks independently verified hashes, archive contents, third-party notices and the generated WinGet fixture. The final build passes 488 logic checks, all 55 Settings scene registrations and 203 UI checks. [Logic report](evidence/optimization-2.7.9/logic-report.txt), [performance invariants](evidence/optimization-2.7.9/performance-checks-report.txt), [checksums](evidence/optimization-2.7.9/SHA256SUMS).

After 100 isolated open/close cycles, there were zero remaining Settings/mini windows and zero growth in Updated/SettingsChanged subscribers. Managed heap changed by −1,923,544 bytes; handle count increased by 14. One closed-window weak reference remained alive, as it also did in the baseline. This is a bounded diagnostic observation, not proof of zero native retention. [Final retention result](evidence/optimization-2.7.9/retention-after.json), [baseline](evidence/optimization-2.7.9/retention-before.json).

The corrected 2.7.9 installer was installed locally after a clean exit and backup. Payload hash and startup, preferences, Notes/Tasks/Shelf/Clipboard, calendar and AI configuration preservation checks all passed. Provider credentials were neither exported nor bundled.

## Reproduce

Use Windows 11 with support for `PROCESS_MEMORY_COUNTERS_EX2` (September 2023 cumulative update or newer). Run each immutable executable separately, with a fresh output directory, and keep display/settings/workload consistent:

```powershell
./tools/Measure-Performance.ps1 -Executable PATH_TO_NabyScape.exe -Directory NEW_OUTPUT_DIRECTORY -Scenarios 'hidden,closed-paused,panel-media' -Runs 2 -Seconds 15
```

Add `-Cycles` for the isolated retention diagnostic. It intentionally forces GC only after warmup and after the 100 cycles. Ordinary timed measurements and application runtime do not force GC. Synthetic captures and timings do not exercise live provider billing or microphone/screen sessions.

## Preserved rendering and data behavior

- The waveform renderer and equations are unchanged. An experimental retained geometry renderer matched pixels at 100/150/200% scales, but was rejected after adding CPU and allocation cost near 180 updates/second. Its render changes and its comparison-only test were removed.
- The companion's composed matrix matches the original four sine channels over 500 times in each mode, including transformed corner/center positions. The clock remains retained WPF animation, without a new managed render loop or a lower frame cap.
- PCM tests exercise wrapped reads, growth, overflow, two-minute bursts, generation clears, zero filling and release. Portrait/landscape/square image tests check decoded bounds and original image preservation.
- Cache tests cover the combined budget, LRU behavior and idle expiry. The existing application heartbeat owns expiry; no new polling loop is added.
- UI regression suites cover media popovers, widgets, ambient, scrolling/lyrics, countdowns, companion interactions, tuck/hide, editor identity/selection and shared audio demand.

The old performance fixture had three failures on both 2.7.8 and the initial candidate. It assumed that no waveform meant no audio consumer despite reactive ambient, and expanded the responsive preview while testing that it was hidden. The fixture now disables ambient for its waveform-only checks and keeps the preview expander collapsed. All 16 performance invariants then pass.

## Interpretation and limits

The largest measured RAM saving comes from avoiding internally compressed runtime assemblies. [Microsoft documents that compressed single-file assemblies are decompressed into memory](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps). The EXE is larger on disk; compression of the portable archive does not impose that runtime assembly cost.

RAM can rise with high DPI, many detached windows, clipboard/shelf images, long conversations, live voice and screen sharing. Forty MiB is not a supported general-use guarantee. The private working set field describes resident private pages; [Microsoft documents that counter separately from commit and total working set](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2).

Frame statistics are WPF callback intervals, not GPU-presented frames. No GPU utilization, physical mixed-DPI hot-plug session or long live-provider session was benchmarked. The existing native window reservation and animation frame policy are retained. Runtime code does not force GC or trim the working set to manufacture lower readings; full GC is used only in the isolated retention diagnostic.
