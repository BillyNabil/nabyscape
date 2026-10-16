# Stage 4: internal notifications and download arrivals

Base: 181e566. Scope: notifications, opt-in download inference, compact file handoff, settings, fixtures. No dependency changes, push, release, install, browser changes, extensions, real Downloads access, real clipboard access, or network fixture calls. Pending `tools/render_showcase.py` and `Assets/showcase/social-preview.png` remain untouched by this stage.

## Implementation

- Shared dispatcher-owned `InternalNotifications` routes Timer, Focus, download readiness, update availability, and existing important failure calls. Notch is the default; Windows, Both and Off plus independent kind switches are available in Application settings. Timer deadline/break logic and completion sound remain intact; the duplicate completion automatic-preview event was removed.
- Queue is ephemeral, capped at ten, with a twelve-second idle timeout. User interaction and volume/brightness indication defer timeout. Consecutive download arrivals group within three seconds, dedupe paths and cap each group at forty. Burst grouping uses its own arrival timestamp, independent of timeout extension during interaction. The active notice is retained when capacity is reached. Delivery errors are swallowed at the notification delivery boundary and never recursively notify.
- Arrival only updates the closed label, or an 18-by-2 DIP mark within the tucked edge. It does not reveal a manually hidden island, activate the window, replace an editor or open a full panel. Notch delivery while manually hidden falls back to Windows. Hover/click or the Notifications command exposes a compact completion card. Mac shoulders and round corners retain padding, including the minimum 300 DIP hover width.
- The completion card uses the existing thumbnail loader and file shelf. Drag payload contains original existing file paths and supports the whole burst, with Copy/Link effects only. Cancelled drag has no file-open or activity action; Show in folder is explicit, and Add to shelf reuses forty-item limit, deduplication and missing-path handling. Originals remain in place.
- `DownloadArrivals` is disabled by default. Native `SHGetKnownFolderPath` resolves Downloads, including relocation; an optional folder picker overrides it. A nonrecursive watcher has a 16 KiB buffer, one worker waking every 500 ms, at most 128 candidates, at most 240 retry observations, and at most 10,000 baseline metadata entries. It ignores partial extensions and hidden/temp prefixes, waits for a final name, two seconds of unchanged size and a readable file handle, without reading file contents. Create/change/rename races reconcile and dedupe. Existing startup/enabling contents form the baseline. Overflow reconciliation compares bounded enumeration against baseline and fails closed above the folder limit. Disable/folder change releases watcher events, cancels and joins the worker and clears candidates. Disconnect releases the watcher and reports one failure; users toggle monitoring off/on after reconnect. App callback epochs prevent stale delivery after a configuration change.
- Application settings include distinct Notifications and Downloads scenes, descriptions, folder-inference wording and keyboard alternative actions. Reset maps include new settings. Layout exports exclude watcher enablement, folder and private arrival content. Existing privacy-sensitive preferences remain excluded.

## Verification

Initial `../tools/dotnet/dotnet.exe build src/NabyScape/NabyScape.csproj -c Release --nologo` found a settings helper local to another method; replaced it with an ordinary named ComboBox. Subsequent build succeeded with 0 warnings and 0 errors.

`./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage4-ready` succeeded. Settings validation: all 47 actual section titles registered. Release self-tests: 315 passed, 0 failed. Includes temp-only partial-to-final, growing readable file, exclusive lock delay, repeated change/final rename, cancelled/missing files, startup baseline, nonrecursive burst, reconciliation, candidate cap, folder change/disconnect and disable cleanup; manager routing/toggles/group/cap/timeout/failing Windows delivery; layout exclusion.

After parent granted the desktop slot and gracefully stopped the daily app, executed:

```powershell
$stage4Process=Start-Process -FilePath (Join-Path $PWD 'artifacts-stage4-ready/NabyScape.exe') -ArgumentList @('--diagnostics',('"'+(Join-Path $PWD 'artifacts-stage4-ready/diagnostics')+'"'),'--stage4-only') -WindowStyle Hidden -PassThru -Wait
```

Process exit 0; 20 checks passed, 0 failures. Screenshot inspection caught two real defects despite passing behavioral checks: Mac shoulder padding and a duplicated Music label under Timer finished in the Notifications settings scene. Both corrected. Parent restored the daily app during corrected build. Downloads scene and narrow Application layout inspected without overlap.

The second published fixture (`artifacts-stage4-verified`, same launch command with that directory) exited 1: twenty passed and two geometry/hover assertions failed. Screenshot inspection confirmed the product padding/labels were correct. Investigation found fixture assumptions: the fixed 350 ms delay sampled an active 420 ms-configured critically damped spring, and the added minimum-width stage shifted the environment poll while no simulated hover pointer was present. Corrected the fixture, without weakening containment: wait is bounded to 50 observations at 50 ms while `IsAnimating`; actual transformed card bounds, all corners, and twelve-DIP clearance sample points must be within the painted contour. The tucked-hover fixture marks its diagnostic pointer present for the hover.

`../tools/dotnet/dotnet.exe build src/NabyScape/NabyScape.csproj -c Release --nologo` then succeeded: 0 warnings, 0 errors. Parent granted another slot; executed:

```powershell
$stage4Process=Start-Process -FilePath (Join-Path $PWD '../tools/dotnet/dotnet.exe') -ArgumentList @(('"'+(Join-Path $PWD 'src/NabyScape/bin/Release/net10.0-windows10.0.19041.0/NabyScape.dll')+'"'),'--diagnostics',('"'+(Join-Path $PWD 'artifacts-stage4-sdk-final')+'"'),'--stage4-only') -WindowStyle Hidden -PassThru -Wait
```

SDK fixture exit 0: **22 passed, 0 failures**. Report: `artifacts-stage4-sdk-final/integration-report.txt`. Inspected `stage4-download-burst.png`, `stage4-download-minimum.png`, `stage4-tucked.png`, `stage4-settings-notifications.png`, `stage4-settings-downloads.png`, and `stage4-settings-narrow.png`. Both card widths have interior contour padding and contained actions, long names ellipsize, tucked state only has its edge mark, settings examples are distinct and no longer overdraw labels, and narrow controls stay contained. Signalled DIAGNOSTICS EXITED immediately; parent owns daily restoration. No further desktop launches planned.

Final delivery command: `./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage4-delivery`. Result: **exit 0; Release publish succeeded, 47/47 actual Settings sections registered, 315 self-tests passed, 0 failed.** Published package: `artifacts-stage4-delivery/NabyScape.exe` (and the copied setup artifact, never installed). No compiler warnings/errors. `git diff --cached --check` passed.

## Limits

Folder arrival inference cannot prove browser completion, including pauses longer than two seconds in a readable final-name file. Existing baseline files are ignored; a completed file renamed by a user is not another arrival. Overflow reconciliation cannot reconstruct lost rename identity perfectly. Monitoring limits fail closed or bound candidate work; users can choose a smaller folder. Windows delivery remains the app's native tray notification mechanism and follows OS notification preferences. Tests exercise routing with fake delivery and an invisible diagnostic tray; they do not assert the OS displays a physical balloon. Original-file OLE drag and Explorer launch are implemented but not executed against a real external drop target during fixtures. Actual ACL changes and the real relocated Downloads directory are not touched; failure paths and known-folder P/Invoke are reviewed, with missing/disconnected temp folders exercised.

## Completion

DONE. No failing final checks or implementation blockers. Self-review covered queue bounds, callback recursion, stale watcher callbacks, baseline handling, final rename deduplication, worker cancellation/join and actual contour padding. The two visual defects were corrected, and the two later fixture failures were traced and fixed with stronger geometry and state checks. All final checks pass.

Owned commit subject: `Add internal notifications and opt-in download arrivals` (commit hash returned in the task completion response). Only the 21 owned source paths and this report are included. Pending social banner changes remain outside the commit. Parent controls daily app stop/restart; no further diagnostic launches, push, installation or release are planned.
