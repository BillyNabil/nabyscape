# Security, antivirus reports and network use

This page answers the questions raised in [issue #10](https://github.com/BillyNabil/nabyscape/issues/10) and anyone else checking NabyScape before installing it.

## Why a scanner may flag the installer

`NabyScape-win-*-Setup.exe` is a **self-contained .NET single-file app**: the .NET runtime, WPF and the app's own assemblies are bundled inside one executable (`PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`; see [build.ps1](../build.ps1)). Generic rules aimed at packed .NET payloads, such as the YARA rule `INDICATOR_EXE_DotNET_Encrypted`, match that bundle layout. Heuristic "Downloader" labels match the built-in updater, which downloads official releases from GitHub, and the optional speech-pack installer.

- No obfuscator, crypter or custom packer is used. The bundle is the standard .NET single-file format and the full source is in this repository.
- NabyScape does not send ICMP/ping requests. The source contains no `System.Net.NetworkInformation.Ping` use.
- Installers are **not code-signed** yet, so Windows SmartScreen shows a warning. Signing through an open-source signing programme is being looked into; until then, verify the checksum as described below.

If your scanner reports a detection on an official release asset, please open an issue with the file name, SHA-256 and the engine/rule name. False-positive reports can also be sent directly to the vendor.

## How releases are built

Pushing a version tag runs [.github/workflows/release.yml](../.github/workflows/release.yml) on a GitHub-hosted Windows runner. It runs [tools/Build-Release.ps1](../tools/Build-Release.ps1), which publishes x64 and ARM64 from the tagged source, runs the logic tests on the native host, packages the Setup, Portable ZIP and notices, writes `SHA256SUMS`, and creates a **draft** release. The maintainer reviews the draft and publishes it. The run for each tag is visible under the repository's **Actions** tab.

Some older releases (for example the 2.10.x line) were packaged on the maintainer's PC before every release went through this workflow. Current releases are built by the workflow.

To check a download, compare its SHA-256 with the release's `SHA256SUMS` asset:

```powershell
Get-FileHash .\NabyScape-win-x64-Setup.exe -Algorithm SHA256
```

The in-app updater does the same check automatically and only accepts downloads from `github.com` redirected to `release-assets.githubusercontent.com`. A checksum proves the file matches the release; it does not prove publisher identity the way a code signature would. You can also build the app yourself; see [Build & verify](../README.md#build--verify).

## Network connections

NabyScape has no account, telemetry or analytics. Private diagnostics stay on your PC and are never uploaded automatically. Connections happen only for features you use:

| When | Hosts | What is sent |
| --- | --- | --- |
| Update check (once a day at startup if **Check automatically** is on, or when you press **Check for updates**) | `api.github.com` | Request for the latest release; user agent `NabyScape/<version>` |
| Update download (only when you choose it, or turn on automatic downloads) | `github.com`, `release-assets.githubusercontent.com` | Download of the setup file and `SHA256SUMS` |
| Weather widget, after you choose a city | `geocoding-api.open-meteo.com`, `api.open-meteo.com` | City search text; the chosen city's coordinates |
| Automatic lyrics, while a song plays and lyrics are shown | `lrclib.net`, `lyricsplus.binimum.org`, `lyrics-api.binimum.org`, `lyrics-api.boidu.dev`, `unison.boidu.dev` | Song title, artist and duration |
| Manual lyrics sources, only when you pick one in the Lyrics menu | e.g. `itunes.apple.com`, `lyrics.paxsenix.org`, `api.paxsenix.org`, `lrc.red`, `lyrics.kugou.com`, `api.musixmatch.com` | Song title and artist |
| Naby AI, only after you connect a provider | The provider you configure (for example `api.openai.com`, `generativelanguage.googleapis.com`, `api.anthropic.com`, `api.x.ai`), a local server, or your Hermes Agent | Your messages and any audio, screen or files you explicitly share |
| Local speech packs, only when you install one | `huggingface.co`, `github.com` | Model file downloads |
| Links you click (guide, website, donation pages) | Opened in your default browser | Nothing from NabyScape itself |

Notes, tasks, clipboard history, shelf references and settings stay in your Windows profile. API keys are stored in Windows Credential Manager.
