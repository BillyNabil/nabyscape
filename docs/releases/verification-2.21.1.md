# 2.21.1 release preparation — 24 October 2026

The owner authorized publishing all accumulated application, installer, documentation and README changes to GitHub. Source is based on public 2.10.1 and advances to 2.21.1.

## Local packaging

`tools/Build-Release.ps1 -Tag v2.21.1` completed with .NET SDK 10.0.402 on Windows 11 x64 build 26200. No tests were skipped for the native architecture.

- 518 logic tests passed, 0 failed.
- All 59 actual Settings section titles have registered render branches.
- x64 and ARM64 self-contained Setup executables and portable ZIPs were built.
- Both Setup/portable pairs have the expected PE architecture and identical executable content within their architecture.
- All five release asset hashes were independently recomputed. Both portable ZIPs and the notices ZIP include all six required notices.
- File version is 2.21.1.0 for both installers.
- Website static integrity passes for two HTML pages and 73 local references; README image/local-document references resolve.

Local artifacts: `release-output/v2.21.1-official-20261024/`. The release workflow independently rebuilds the tagged commit before creating its draft. Public assets are taken from that workflow, not mixed with earlier local candidates.

## Compatibility scope

One package per architecture targets Windows 10 build 19041+ and Windows 11. Windows 10 22H2 is recommended over older Windows 10 builds; OS/.NET support follows Microsoft's lifecycle. x64 has local runtime tests on Windows 11. ARM64 is cross-compiled and structurally validated, not executed on an ARM64 device. Physical Windows 10 execution is not validated here.

Earlier isolated UI acceptance for the same runtime source completed 836 passed, 0 failed; see [the detailed refresh report](../verification-refresh.md). The documentation/visual publication work does not introduce new app behavior. Physical provider/speech/device compatibility and code signing remain subject to the documented limits. Existing daily profiles and installation were not changed by packaging.
