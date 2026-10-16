# Stage 5 — Stopwatch and display follow

## Result

Implemented the Stage 5 brief using existing session persistence, heartbeat, widget/category routing, settings scenes and detached-preview infrastructure. No dependencies, install, push, remote publication or daily-profile writes. Pending `tools/render_showcase.py` and `Assets/showcase/social-preview.png` remain untouched and excluded from the commit.

Stopwatch has Start/Pause/Resume/Reset, one shared persisted session, optional widget/hover enablement, and the same compact control in Focus, classic widgets, hover, detached windows and Settings. New and migrated profiles leave the new widget disabled until chosen. It uses Windows monotonic uptime (which includes suspend) in process, a UTC anchor once for restart recovery, nonnegative recovery deltas, and mm:ss / h:mm:ss formatting. View updates subscribe only while visible and use the existing heartbeat; stopwatch does not request compositor frames. Existing editor controls are not rebuilt by time updates.

Display mode provides Primary / Selected / Follow cursor. A missing/invalid mode maps a nonempty old Monitor to Selected, otherwise Primary; legacy `luma.layout.v1` import also infers that mapping. Follow stores only a runtime monitor identity, routes all island consumers through Native.ChooseScreen, waits 250 ms on a new monitor, cancels pending movement when the pointer returns, and defers for file drag, carousel interaction, island pointer/keyboard focus, editor focus, mouse capture/buttons and audio menus. Hidden islands stop the bounded 100 ms check and resolve when shown. Detached windows retain their own placement and shared sources. Display/DPI events reflow existing content and recompute geometry without opening a panel. Reset mapping covers DisplayMode once; layout export retains format compatibility.

The shared cursor cache has a 50 ms TTL and serves PointerInside/follow polling. Compositor reflection still uses fresh Mouse.GetPosition; frame pacing, waveform rendering and motion demand are unchanged. No pointer timer runs while manually hidden. Incoming file drag and notification priority code were preserved.

Settings includes a registered Stopwatch render branch, an exact shared interactive Stopwatch preview, descriptions and keyboard/button actions. Display has a mode-specific diagram (cursor and 250 ms indicator for Follow), saved/disconnected status, interaction/manual-hide explanation and an example. Three enabled time widgets stack to avoid cramped columns; existing two-column Timer/Focus remains when Stopwatch is off.

## Verification

Commands ran from `C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape`:

- `& tools/Test-SettingsScenes.ps1` — PASS, all 48 actual Settings section titles have specific registered render branches.
- `& ../tools/dotnet/dotnet.exe build src/NabyScape/NabyScape.csproj -c Release --no-restore --nologo -o artifacts-stage5-sdk` — PASS, 0 warnings / 0 errors. An initial fixture compile called a nonexistent PreviewActivity overload; corrected to existing PreviewActivity + SelectPeek before running the fixture.
- `& ../tools/dotnet/dotnet.exe artifacts-stage5-sdk/NabyScape.dll --self-test --report artifacts-stage5-sdk/unit-report.txt` — initial SDK-hosted run: 342 passed / 2 failed. One was a stale expected hover-page count (9, now 10), corrected. The other is the known updater native PE/version test inspecting dotnet.exe as host; it is an SDK-host mismatch, not a product executable failure.
- `& ../tools/dotnet/dotnet.exe artifacts-stage5-sdk/NabyScape.dll --diagnostics C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape/artifacts-stage5-fixture --stage5-only` — PASS, exit 0, 9 desktop checks / 0 failures, isolated fixture profile.
- `& ./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage5-final` — PASS, single-file self-contained Release, 348 passed / 0 failed at that intermediate point.
- `& ./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage5-delivery` — PASS, single-file self-contained Release, 375 passed / 0 failed; includes the updater native PE/version test, all registered scenes rendered, fake stopwatch persistence/pause/reset/sleep/backwards-clock checks, identity/hysteresis/blocked-follow/manual-hide/show tests and 27 production placement checks for negative, portrait, offset bounds at 100/150/200% scales.

- `& artifacts-stage5-delivery/NabyScape.exe --diagnostics C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape/artifacts-stage5-final-fixture --stage5-only` � PASS, final published executable, 10 desktop checks / 0 failures. GUI invocation returned immediately; completion was verified by the fresh integration report and no remaining NabyScape process before signaling the parent to restore the daily app.
- The final delivery build was repeated once to confirm the unchanged final source: 375 passed / 0 failed again. No additional source changes followed.
- `git diff --check` � PASS (only repository line-ending advisory warnings).

## Desktop evidence and limits

The first isolated desktop fixture passed these exact nine checks: readable Focus Stopwatch at 384; readable Focus Stopwatch at 340; shared main/detached updates without editor rebuild; persisted running state; shared stable pause; detached placement unchanged by source policy; shared hover controls; manual hide stops view updates and frames; Settings contains the exact shared control. Captures: `artifacts-stage5-fixture/stage5-focus-384.png`, `stage5-focus-340.png`, `stage5-detached-stopwatch.png`, `stage5-hover-stopwatch.png`, `stage5-settings-content.png`. Narrow Focus and hover captures were visually reviewed; parent also inspected narrow/detached captures.

The final published fixture repeated all nine desktop checks and added the tenth: Layout explains Follow delay and preserves explicit Follow selection. `artifacts-stage5-final-fixture/stage5-settings-follow-default.png` and `stage5-settings-follow-small.png` were visually reviewed: both show the distinct Follow diagram, current selection, saved preference status, manual-hide/interaction explanation and concrete example without clipping. Parent was notified immediately after fixture exit to restore the daily app.

The fake policy tests explicitly cover each interaction blocker, exact 249/250 ms boundary, changed candidate identity, return to source, hidden cancellation, immediate show resolution and blocked show. The mixed-bounds tests exercise the production placement calculation; they do not emulate physical monitor enumeration or actual Windows DPI transitions. Physical monitor hotplug, sleep hardware and mixed-DPI relocation were not validated on real multiple monitors. Follow is input-isolated during desktop fixture execution; runtime wiring was self-reviewed and policy/placement checked with injected identities, clocks and bounds. No full legacy desktop diagnostic suite was run in this stage; final published logic/selftests and the targeted fixture were the proportionate checks.

Parent owns graceful daily-app stop/restore. The first fixture exited before parent restored the unchanged daily executable. No daily data was mutated by this task.
