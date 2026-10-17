# Interaction hotfix report

Base: `d99fc6c` (main). Version remains 2.5.0. No install, release, push, website or marketing change.

## Implemented

- `HoverPagePolicy.cs` and `PeekCarousel.cs`: explicit HoverViews determine the complete ordered deck independently of main widget visibility. Individual tool switches still exclude their own pages; existing satellite/drag overrides remain. Global hover and detach switches retain their controls. Clipboard history is never enabled by a page toggle.
- `CompactOverlay.cs`, `OverlayWindow.cs`, `UtilityOverlay.cs`, `InteractionFeatures.cs`, `DetachedPreviews.cs`, `SystemMonitorOverlay.cs`: hidden main panels open through a temporary in-memory widget route, including Timer, Focus, Stopwatch, media, System and individual tools in compact and classic layouts. The route is cleared on collapse; saved widget toggles/order are untouched. Detach accepts separately enabled hover views; detached Open main uses the same target routing. System/media hover data remain usable with the corresponding main widget hidden.
- `SettingsCustomization.cs` / `SettingsScene.cs`: actual available-page count, explicit individual-tool-off explanation, independent-main-widget and clipboard wording. The four-card schematic now states both the total available count and the number illustrated.
- `AutoTuck.cs`: existing SurfaceMotion now animates translation from its current value and velocity, alongside surface contraction. Repeated heartbeat requests do not restart motion. Reversal retains the displayed offset. Reduce Motion/manual hide snap and dispose translation motion; cleanup also disposes it. Existing translated contour/visual clip and native hit geometry read the current transform. Live rendering and monitor work suspend on tuck; motion unsubscribes after settling.
- `AudioMixer.cs`: cached per-session process-image lookup through OpenProcess QUERY_LIMITED_INFORMATION + QueryFullProcessImageName, avoiding privileged/cross-bitness MainModule access. Cache is bounded; no shell icon extraction during audio heartbeats.
- `MixerAppIcon.cs` / `MixerOverlay.cs`: stable 20 DIP local extracted app icon with compact neutral monogram fallback, output speaker identity, and 16 DIP mute/unmute glyph within the existing 30 DIP target. Reuses AppIconCache and its local-path restrictions/ShellWorker; no remote URLs, UNC handling, dependencies or audio routing/volume behavior changes. Motion.Icon avoids regenerating unchanged mute visuals.
- Added focused logic checks, enhanced existing mixer layout tests, and `--interaction-hotfix-only` temporary-profile desktop fixture. Diagnostic media is isolated for that fixture.

## Verification

- Initial plain `dotnet build ...`: failed NETSDK1045 because system dotnet is SDK 9. Repository-bundled SDK used for all actual checks afterward.
- `& ../tools/dotnet/dotnet.exe build src/NabyScape/NabyScape.csproj -c Release -o artifacts-interaction-hotfix-sdk --nologo`: PASS, zero warnings/errors. Final rebuild after fixture-label correction and removal of redundant condition also PASS.
- `& ./tools/Test-SettingsScenes.ps1`: PASS, 49 actual section titles registered.
- SDK DLL self-tests (`dotnet .../NabyScape.dll --self-test ...`): 378 passed, 1 failed. The update PE check inspected Environment.ProcessPath = dotnet.exe rather than the app, so its version assertion was invalid for this invocation. Resolved with actual apphost invocation below; no test weakened.
- `$env:DOTNET_ROOT=(Resolve-Path ../tools/dotnet).Path; Start-Process ./artifacts-interaction-hotfix-sdk/NabyScape.exe -ArgumentList '--self-test','--report',<absolute unit-apphost-report.txt> -WindowStyle Hidden -PassThru -Wait`: PASS, exit 0, **379 passed / 0 failed**. Report: `artifacts-interaction-hotfix-sdk/unit-apphost-report.txt`. Includes all-ten hover choices with hidden main widgets, individual-tool exclusion, persisted choices/clipboard preservation, local limited-process lookup, app icon cache reuse/extraction, safe-path regressions and existing mixer editing/accessibility/layout checks.
- Coordinated desktop command: `$env:DOTNET_ROOT=(Resolve-Path ../tools/dotnet).Path; & ../tools/dotnet/dotnet.exe artifacts-interaction-hotfix-sdk/NabyScape.dll --diagnostics C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape/artifacts-interaction-hotfix-fixture --interaction-hotfix-only --background`: PASS, exit 0, **60 checks / 0 failures**. Report: `artifacts-interaction-hotfix-fixture/integration-report.txt`. Parent owned the daily application's graceful exit/restore; implementer never stopped/launched the installed application. Parent confirmed post-exit settings hash identical and startup registry unchanged before restarting daily. Fixture children exited before slot release.
- Desktop checks: all ten ordered pages; correct hidden targets; inactive-page keyboard exclusion; arrows/swipe; all eight supported detachable kinds plus actual Timer detachment; classic targets; no saved toggle/order or clipboard-capture change; intermediate translation and matching clip/hit geometry; no-jump rapid reversal; repeated heartbeat continuation; zero endpoint; 3/6/12 DIP edges; hover reveal; Reduce Motion; manual hide; external fullscreen/maximized rules; settled no-live-frame/no-monitor-read state; callback cleanup; narrow/default mixer containment and truncation.
- Saved pixels reviewed: `mixer-width-193.6.png` and `mixer-width-302.png` show readable speaker/mute icons and the LE fallback; `hover-route-timer.png`, `hover-route-system.png`, `hover-route-notes.png`, `tuck-edge-6.png` retain geometry evidence. The original mixer capture supplied the Explorer executable as a synthetic local-icon source while labeling that test row Discord. This was fixture input only, not an actual Discord-icon assertion. Final fixture source correctly labels that row “Explorer (local icon fixture)”; existing capture is documented rather than misrepresented. No claim that LEDKeeper exposes a real extractable icon; it receives the neutral LE monogram when unavailable.
- `git diff --check`: PASS (line-ending notices only).

## Limits / handoff

Parent performs fresh review. No full-app performance sweep, browser automation, live volume writes, clipboard capture, installer execution, release metadata bump or daily profile mutation was added. Native screenshots cover synthetic row data at 193.6 and 302 DIP; they do not claim every device DPI or protected process exposes an extractable app icon. Existing cache handling rejects web/UNC paths. Clips and Tasks retain the existing lack of detachable windows.

Unrelated pending `tools/render_showcase.py` and `Assets/showcase/social-preview.png` are preserved and excluded from this commit.

## Review correction

Fresh review found one lifecycle gap: during animated downward reveal, the unchanged-zero-destination optimization returned before manual hide could dispose tuck motion. `AutoTuck.cs` now handles manual hide before that optimization. Added shared regression checks to the full fixture and focused `--interaction-tuck-hide-only` diagnostic path, with isolated media.

- Correction SDK build: PASS, 0 warnings/errors. `git diff --check`: PASS.
- Parent granted a second isolated slot and owned daily exit/restore. Command: `$env:DOTNET_ROOT=(Resolve-Path ../tools/dotnet).Path; & ../tools/dotnet/dotnet.exe artifacts-interaction-hotfix-sdk/NabyScape.dll --diagnostics C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape/artifacts-interaction-tuck-hide-fixture --interaction-tuck-hide-only --background`. PASS, exit 0, **3 checks / 0 failures**, in 1.9 seconds. Verified the starting state is an active downward animation, manual hide immediately cancels its unchanged destination, and hidden state retains no callback. Report: `artifacts-interaction-tuck-hide-fixture/integration-report.txt`.
- Before this correction, authorized `./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-interaction-hotfix-final` passed all 49 scene registrations and **379 published apphost self-tests / 0 failures**, file version 2.5.0.0. This earlier binary is superseded; the same final packaging command is being rerun after correction. Neither setup copy is executed.

## Final classic-mixer review correction

Broader review found classic transient panels rendered before the mixer, so a hidden media route could obscure an otherwise active mixer. Reordered the existing branches so classic mixer takes precedence, retained the temporary route and restored its correct ActivePage on Back. Opening a hover/detached target explicitly exits classic mixer, including reopening the same target. No persisted preference changes.

The first focused rerun passed route and mixer visibility, then failed with a shared timelinePanel logical-parent exception on Back. The timeline was detached only in BuildCompactBody, which classic temporary routes bypass. Added timelinePanel to the existing shared-control detach loop in BuildBodyContent. No other reparent architecture was introduced; detached previews retain their own controls.

- Final SDK build: PASS, zero warnings/errors. `git diff --check`: PASS.
- Parent coordinated both short slots and preserved the daily settings hash. Final command uses the same SDK DLL with `--diagnostics C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape/artifacts-interaction-classic-mixer-fixture-final --interaction-classic-mixer-only --background`. PASS, exit 0, **6 checks / 0 failures**, in 3.11 seconds. Report: `artifacts-interaction-classic-mixer-fixture-final/integration-report.txt`.
- Checks: classic hidden Media with Volume enabled; visible mixer; Back restores Media; reopening the same hidden Media target exits mixer; another hidden Timer target exits mixer; Timer → mixer → Back retains Timer.
- Same six checks are shared with the full interaction fixture. No unrelated suite/fixture repetition. The immediately preceding self-contained build passed, but is superseded again by this source correction; final package rebuild follows this commit.

## Final verified package

Combined implementation and review corrections through `3aa3402ced0a7a68b221aff846c7d5824e11e1ed` were approved by fresh review. Final authorized `./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-interaction-hotfix-final` completed exit 0: **49 registered scene sections; 379 published apphost self-tests passed / 0 failed**. No installer execution or installed-file swap by implementer.

- Executable: `artifacts-interaction-hotfix-final/NabyScape.exe`, 82,613,173 bytes, file version **2.5.0.0**.
- SHA256: `D1A49ACCDB11653FF97BB126960E43DD089C511DECC43BFE15D31C9AD5B9D998`.
- Executable LastWriteTimeUtc: **2026-10-17 23:38:35**.
- Fresh `artifacts-interaction-hotfix-final/unit-report.txt` LastWriteTimeUtc: **2026-10-17 23:39:04**, `RESULT: 379 passed, 0 failed`.
- Parent confirmed the daily settings hash unchanged after each coordinated fixture and restored the process it stopped. Parent owns any subsequent local EXE swap and rollback backup.

This final record is documentation only; no implementation changed after the verified package build.
