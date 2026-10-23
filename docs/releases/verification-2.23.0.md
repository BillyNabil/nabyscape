# 2.23.0 verification — 24 October 2026

Verified on Windows 11 x64, .NET SDK 10.0.402. No subagents were used.

## Preferences and onboarding

- 524 native published-x64 logic tests passed. The complete embedded preferences cover every writable Settings field except personal shortcuts, which keep the standard Explorer/Settings defaults.
- All 133 portable, non-personal preferences were compared with the owner's current local snapshot and matched. Monitor identity, display selection, location, private paths, recent color history and transient snoozing are cleared for another device.
- Fresh profile loading uses that complete snapshot; existing saved choices remain unchanged. An unreadable/future existing profile still disables background clipboard/download capture while protected from overwrite; its targeted regression test passed.
- Naby is available with no provider credentials or wake listening enabled by default.
- 58 isolated UI checks passed, including visible installer startup choice, recommended onboarding selection/restoration, microphone settings and the General uninstall entry. Installer and welcome captures were inspected.

## Uninstall

`tools/Test-UninstallPlan.ps1` passed under both bundled PowerShell and Windows PowerShell 5. It exercises real deletion only inside a checked absolute fixture under the repository's ignored outputs directory:

- Managed profiles, ASR/TTS model/engine files and update caches are removed.
- External imported-model sentinel files remain unchanged.
- Paths outside the dedicated root are rejected.
- A real NTFS junction is rejected before any planned deletion. Fixture junction cleanup uses nonrecursive Directory.Delete, avoiding a Windows PowerShell 5 Remove-Item junction error.
- Exact interrupted migration/speech-job names and application-specific runtime cache roots are selected; similarly named unrelated directories are excluded.
- The full PowerShell script parses, its credential interop compiles, and the dialog defaults/layout bounds were checked without opening uninstall or reading/removing credentials.

The tests dot-source helper functions and never invoke the live Registry, credential or process cleanup. The user's actual installation, models and personal profile remain intact. Full system uninstall/reinstall, live credential deletion, native Windows 10 and ARM64 execution are not claimed.

Native builds run the cleanup fixture as a required build check. Release packaging independently validates both architectures, checksums, Setup/portable identity and notices before creating a draft. Published assets are verified again after downloading from that draft.

Evidence: `outputs/defaults-uninstall-2230-final/unit-report.txt`, `outputs/defaults-ui-2230/integration-report.txt`, `outputs/defaults-ui-2230/future-profile-final.txt` and screenshots in the same ignored output directories.
