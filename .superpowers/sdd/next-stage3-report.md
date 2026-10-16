# Stage3 report - official updates and release tooling

Base: d6015b4150d4461fdc4a693d54238511f868e104. Local implementation only; no pushes, public tag/release changes, installs, certificate signup, external PR or payments. Existing public v2.4.2 remains untouched. Pending tools/render_showcase.py and Assets/showcase/social-preview.png remain outside this change.

## Implemented

- OfficialUpdates uses only built-in HttpClient/JSON/SHA-256 and an injectable HttpMessageHandler. Canonical BillyNabil/nabyscape release identity, stable strictly newer versions, exact win-x64 setup/checksum names, HTTPS URL identity, restricted GitHub asset-CDN redirect, bounded metadata/download sizes, exact length/hash, PE x64 and version inspection. No arbitrary update URL or executable target setting.
- Signature handling uses Windows WinVerifyTrust. Unsigned existing releases are accepted without claiming publisher authenticity; present signatures must pass Windows trust/revocation policy. Downloads retain an Internet Zone.Identifier stream before execution. A future signed-only rollout still needs publisher pinning and an explicit signed-release policy; documented rather than fabricated.
- Application > Updates: installed/available version, notes, manual check, enabled-by-default once/day switch, cancellable progress/errors, separate download and install/restart actions, and clear portable path. Static specific Updates example; no render loop. UpdateAvailable event is exposed on the internal service for Stage4 notification routing.
- Last attempt persists in update-state.json, separate from preferences/layout profile. Automatic check only at normal startup, max once/24 hours; manual checks explicit. HTTP timeout 45 seconds; whole operation ten minutes; one two-second retry for 429/server errors. Test/diagnostic runs do not auto-check; Stage3 UI fixture always injects fake transport.
- Backup/Notes/settings/session flush before helper launch or shutdown. Backup failure aborts. Helper uses fixed staging/installed locations, exact PID+start time+path confirmation and ready acknowledgement, waits for that exact process, revalidates hash/architecture/version/signature, invokes only fixed setup with --silent --launch, and logs failure. Duplicate operations/handoffs are excluded. Existing installation root and legacy exe paths remain supported separately from migrated storage. Portable install is disabled. No real installer ran during verification.
- AutoCheckUpdates belongs to Application's explicit reset map and is excluded from layout profiles. Pre-update backups missing only that new preference restore with its default; other completeness checks remain.
- Release tag workflow matches project version, cleans/publishes/tests, packages installer/portable/notices, calculates real hashes and only then creates a draft. Canonical repository guard, no overwrite command. Final WinGet generator uses real setup hash, canonical URL, x64/user scope and --silent, with schema headers. Manual submission workflow and official SignPath eligibility/gaps documented in docs/RELEASING.md.
- Corrected missing Updates and pre-existing Reset preferences example branches. Build-time source check reads actual Section("...") calls, requiring registered scene branches for all 45 titles; logic checks render every registered scene. Parent identified sidebar word wrap in capture; branding now uses 20px NoWrap while category scrolling remains.

## Commands and results

Working directory: C:/Users/billy/Documents/Codex/2026-10-14/ra/work/nabyscape.

1. `& ../tools/dotnet/dotnet.exe build src/NabyScape/NabyScape.csproj -c Release --nologo`: exit 0, 0 warnings, 0 errors (initial implementation).
2. `& ./tools/Build-Release.ps1 -Tag v2.4.2 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage3-release`: exit 0; initial 305 self-tests passed, 0 failed; installer/portable/notices/checksums/WinGet generated locally.
3. `winget validate --manifest artifacts-stage3-final/winget --disable-interactivity`: initially schema-header warnings only. Added official schema headers; repeated exact command passed with no warnings.
4. `& ./tools/Test-SettingsScenes.ps1`: exit 0, all 45 actual section titles have registered render branches.
5. Isolated first desktop launch (parent provided singleton slot): `Start-Process -FilePath (Join-Path (Get-Location) 'artifacts-stage3-fixture/NabyScape.exe') -ArgumentList @('--diagnostics',('"'+(Join-Path (Get-Location) 'artifacts-stage3-fixture/diagnostics')+'"'),'--stage3-only') -WindowStyle Hidden -PassThru -Wait`. App exit 1: first nine state/actions checks passed; screenshot render found `Missing Settings example: Updates`. Fixed both Updates and Reset preferences scenes, added source registration and render-all-scene regression checks. Parent restored daily immediately after fixture exit.
6. Reviewed fixture publish: `& ./build.ps1 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage3-reviewed`: exit 0, 308 passed, 0 failed.
7. Second isolated launch: `Start-Process -FilePath (Join-Path (Get-Location) 'artifacts-stage3-reviewed/NabyScape.exe') -ArgumentList @('--diagnostics',('"'+(Join-Path (Get-Location) 'artifacts-stage3-reviewed/diagnostics')+'"'),'--stage3-only') -WindowStyle Hidden -PassThru -Wait`. App exit 0: all 13 UI checks passed, zero failures. Checks cover initial gates, five focusable/tabbable actions, static specific example, actual Check keyboard focus, fake available version/download state, portable install gate, narrow containment, failed checksum preserving app, and pending Notes persisted. Screenshots stage3-updates-wide.png and stage3-updates-narrow.png visually inspected under artifacts-stage3-reviewed/diagnostics. Parent restored daily afterward. No further desktop launches.
8. Final complete source package: `& ./tools/Build-Release.ps1 -Tag v2.4.2 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage3-complete`: exit 0. Clean Release: 0 warnings, 0 errors; source scene check passes; **312 self-tests passed, 0 failed**. Final source includes nonwrapping brand, worker-thread handoff signature inspection, stricter helper input version validation and old-backup compatibility; these final changes were logic/publish checked after the desktop fixture, not relaunched on desktop.
9. `& ./tools/Test-ReleasePackage.ps1 -OutputDirectory artifacts-stage3-complete -ValidateWinGet`: exit 0. All three asset hashes independently recomputed; portable contains exe and all five notices; notices ZIP contains all five; WinGet real setup hash matches; `Manifest validation succeeded.` with no warnings. This reusable verifier is also called by future Build-Release runs (the completed run's packaging was verified explicitly with the same script after the call was added).
10. Tag rejection fixture: `try { & ./tools/Build-Release.ps1 -Tag v99.0.0 -Dotnet ../tools/dotnet/dotnet.exe -OutputDirectory artifacts-stage3-invalid } catch { $_.Exception.Message }` rejects before publishing with `Tag must match project version.` Existing output reuse is also explicitly rejected in Build-Release.
11. `git diff --check`: exit 0; only Git CRLF-to-LF informational notices before normalization, no whitespace failures.

Final local package SHA256SUMS:

```
9edcbcba094196622aad324ad8c5f4fe792acdcead5d45f72aac99be91582aeb  NabyScape-win-x64-Setup.exe
b0cc2d29145b1003ca14d99b667e0c245ac2ff034c695c783658f6c1f343011f  NabyScape-win-x64-Portable.zip
533393f42ec138b1547adf3ab3f9a99a7391c45091c76d26d6793fd4a418d583  NabyScape-Notices.zip
```

Fake logic fixtures cover offline, timeout, 429/backoff, hostile redirect/host/schema/prerelease/wrong architecture asset, stable newer/equal/older, checksum malformed/missing/duplicate/bad, truncation, successful fake download and duplicate download/check, persisted throttle/profile exclusion, exact PID/start/path, portable gate, backup abort, fake installer wait/verify/run ordering and nonzero failure, duplicate install gate, actual published PE/version/signature inspection without execution, invalid/downgrade handoff inputs, and backward-compatible preference backup restoration.

## Limits and human follow-up

- Real GitHub release download, SmartScreen prompts, signed certificate/revocation scenarios, and real installer replacement/restart were intentionally not executed against the daily app. Pure fake installer/network fixtures exercise boundaries; native unsigned published inspection passed. A disposable Windows account remains appropriate for end-to-end installation after explicit authorization.
- GitHub Actions was implemented and its packaging commands run locally; remote Actions/draft publication was not triggered. Local v2.4.2 output is test material, not a replacement for public v2.4.2. Future official releases must use the new exact asset names and a new matching project version/tag.
- WinGet schemas validate locally; no external submission/install or repository PR happened. Regenerate hashes after signing the final binary.
- SignPath acceptance, MFA/role/build-origin policy setup and publisher/signature enforcement rollout require human approval and external acceptance. No signing claim or identity was fabricated. SHA-256 from the same release is integrity only.
- Parent owns daily stop/restore and verified the daily preference hash. This worker never stopped daily, wrote its storage or invoked an actual installer. Stage3 screenshot branding predates the final NoWrap correction; final correction compiled and logic checks passed.
