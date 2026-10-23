# 2.22.1 verification — 24 October 2026

Verified on Windows 11 x64 with .NET SDK 10.0.402. No subagents were used.

- Native published x64 application: 521 logic checks passed; additional targeted Windows Speech adapter and rollback fixtures verifies hash rejection, preservation of the current executable on failure, atomic replacement on success and temporary-file cleanup.
- 54 recent UX checks passed, including visible microphone selection, persistence in an isolated profile, refresh behavior, test cancellation controls and the installer folder field/Browse button.
- 65 voice layout checks passed, including narrow layouts, legacy icon preference migration and enlarged text.
- 34 visual polish checks passed, including dot-only navigation and the current sixteen-category Settings order.
- Release build registers all 59 actual Settings scene titles. Builds complete without compiler warnings or errors.
- Generated uninstall script parses successfully in PowerShell; registered-location and leaf-file guards are present, with no recursive folder deletion.

Evidence is under ignored `outputs/installer-input-2220/`, `outputs/input-ui-2220/`, `outputs/voice-layout-2220-serial/` and `outputs/visual-polish-2220-fixed/`. Native screenshots were inspected for the microphone workspace and installer folder picker. Diagnostics use isolated profiles and do not record microphone audio or call providers.

The tag workflow rebuilds both architectures, runs the complete native-host logic suite and validates the release payloads before creating a draft. Publication follows separate validation of downloaded workflow assets.

Full install/uninstall integration was not executed against the user's Windows profile. An attempted fixture with temporary Registry changes was rejected by automatic approval review; it was not run. Physical microphone quality, reconnect behavior, native Windows 10 and ARM64 execution remain hardware checks. Installers are unsigned.

Silent updates preserve registered legacy locations. Interactive setup offers migration to NabyScape. Restoring a pre-2.22 binary into a new/custom folder preserves its verified bytes in place; that old version needs a manual latest setup for future updates because it cannot discover the new installation path.


The initial v2.22.0 packaging run was cancelled before any draft or public release after finding a missing live-stream contract in the new selected-device wake path. Version 2.22.1 includes an in-memory test against the actual Windows Speech adapter (construction, PCM reads, position queries and EOF on disposal) without opening a microphone. Adapter requirements were checked against the [.NET source](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Speech/src/Internal/SapiInterop/SpStreamWrapper.cs).
