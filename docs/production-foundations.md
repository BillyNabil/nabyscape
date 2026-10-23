# Production foundations · 2.20

This local candidate adds recovery and accessibility foundations. Source inspection and compilation do not establish production readiness across PCs. No UI, microphone, provider, model or device acceptance testing was performed for this update.

## Updates and recovery

Settings → General → Updates can check official GitHub releases and download the setup matching the running app architecture. Automatic checks remain optional. Automatic downloads are separately opt-in, follow the startup check schedule and may use a metered connection. An update never installs itself while you work: choose **Install and restart**.

Before installation the helper waits for NabyScape to quit and backs up the installed executable and top-level JSON profiles locally. The downloaded file must match official release metadata, size, SHA-256, version and process architecture. Existing releases may be unsigned; checksums establish integrity, not publisher authentication.

If installation fails, or the new process does not acknowledge initial startup, the helper attempts to restore the saved app and JSON profiles. It never replaces files while an installer or owned app process is known to be running. Backups remain local under `%LOCALAPPDATA%\NabyScape-Updates` and can contain private settings: do not share the recovery folder. The manual **Restore previous version** action warns that JSON profile changes since the saved backup will be replaced. It does not remove local media files. Recovery is bounded and can fail because of permissions, antivirus or disk errors; local error details and preserved backups remain available. A startup acknowledgement does not detect every later crash or behavioral regression.

Portable builds require manual replacement of their files after quitting. Installation and rollback are for the standard per-user installation and profile locations.

## Language and accessibility

Settings → General → Language & accessibility offers English, Español and Bahasa Indonesia. English is the default and the explicit fallback for untranslated technical text. Application captions use a central catalog; conversations, filenames, notes, provider identifiers and model names keep their original content. This is partial interface localization, not a claim that every advanced description is translated.

Save language, text size (100–150%) and forced high contrast, then quit and reopen the app. Keyboard focus outlines, accessible icon names and selected polite status announcements are available. Windows high contrast and animation preferences are respected by shared controls. In high contrast the dock uses an opaque system surface and preserves separate checked/progress colors. Custom artwork and late visual changes may need additional accessibility work; large text can require scrolling in compact surfaces. Reduced motion also follows the existing Reduce motion preference.

## Distribution and speech compatibility

Native Windows x64 and ARM64 app packages are separate. Use the matching setup or portable ZIP. Installed updates follow the running app architecture; switching from emulated x64 to native ARM64 is an explicit installation choice. The app's architecture does not imply every external speech engine supports that architecture. Automatic bundled native speech packs require an x64 process with AVX2/FMA. ARM64 users can configure compatible external local ASR/TTS servers or supported providers. A custom local model must also have an engine compatible with the running process.

## Configuration, diagnostics and device recovery

JSON payloads retain their existing flat roots. A matching `<name>.schema.json` records the schema version and a pending migration barrier. Existing version-0 profiles receive an idempotent migration on save, with an immutable original migration backup and a rolling validated last-good backup. Writes are bounded, serialized and staged atomically. Unknown object fields are preserved where supported. Unreadable documents and versions newer than the app supports stay protected from automatic saves, including after reopening the app; temporary defaults do not unlock them. General settings explain how to preserve the originals and recover a matching document/schema pair.

**Private diagnostics** provides a preview, refresh, copy and explicit JSON export. It includes app/runtime/OS versions, architecture, coarse observed storage/service states and up to 64 controlled in-memory events with bounded durations. It excludes credentials, conversation/media content, endpoints, model identifiers, user/device names and file paths. Nothing is uploaded automatically. The report describes this process's observed state; it is not a complete crash dump or persistent audit trail. Existing `errors.log` is a separate local troubleshooting file and is never included in the private report.


Sleep/session, network, endpoint and display changes use coalesced recovery callbacks. Active voice/wake/requests/sharing are stopped when needed; pending wake rearm is invalidated even if the session is already closing. Restart voice or retry explicitly. Inactive device changes preserve chat status. Resume reconciles timer deadlines, audio, keep-awake and monitor layout without rewriting the saved monitor choice. Media and a nonempty previously requested weather location can refresh on resume/network restoration under existing cache policy. Audio/display changes do not request weather. OS link availability is not proof that a provider is reachable. No microphone restart or AI/tool replay happens automatically.
