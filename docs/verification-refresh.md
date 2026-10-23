# Settings, media and website refresh verification

Verified locally on 24 October 2026, Windows x64. Source version 2.21.1. No subagents were used.

## Results

Logic suite: **518 passed, 0 failed**. Settings scene registration: **59 section titles covered**. Latest full UI/integration run: **836 passed, 0 failed**.

| Isolated suite | Passed | Failed |
| --- | ---: | ---: |
| accepted-full | 836 | 0 |
| accepted-recent-ux | 48 | 0 |
| final-voice-layout | 65 | 0 |
| final-speech-storage | 11 | 0 |
| final-ux-completion | 19 | 0 |
| final-welcome | 14 | 0 |
| media-ux | 28 | 0 |
| scroll-lyrics | 17 | 0 |

Evidence lives in `outputs/release-refresh/`, with per-suite `integration-report.txt`, screenshots and a machine-readable `suite-results.json`. Logic evidence is `unit-verified.txt`. Test profiles are isolated under each suite directory; no real provider, microphone, desktop recording or personal conversation is needed for these fixtures.

## Corrections during verification

- Added the Feature usage and Your setup branches to the Settings scene registry.
- Reduced the fixed Settings header on short windows so the scrolling form retains usable space at 480 × 300.
- Preserved appearance/layout/widget Undo when unrelated window-position saves occur. The latest targeted suite confirms both the visible Undo action and persisted restoration.
- Updated old test navigation, expander and preview assumptions to the current visible tabs and preview policy. Storage checks now open Models; audio checks open Check audio. Provider save controls are explicitly revealed before interaction.
- Changed the classic-scroll assertion to target the actual outer widget viewport instead of the first nested ScrollViewer. The corrected full run passes it.
- Fixed focus-induced clipping in the website stage by using a non-scroll clipping container.

Earlier failing runs remain in the evidence directory; they are not represented as passing runs.

## Packaging and personal data

Windows x64 and ARM64 self-contained builds completed, including PE architecture checks. ARM64 was cross-compiled, not executed on an ARM64 device. The verified x64 candidate was installed locally; its SHA256 matches the setup payload. SHA256 comparisons of the existing settings and assistant configuration files are unchanged after installation. A pre-installation backup is retained by the local installer helper.

No GitHub commit, push, public release or new installer upload was performed in this refresh.

## Website and README

README has a new native SVG cover, current feature descriptions, fresh isolated 2.21.1 Settings captures, clear download/source distinctions and explicitly historical v2.10 media. The website uses a warm ivory/ink composition, a black dock stage, three cover-inspired palettes, actual app imagery, current Settings evidence and a matching guide palette.

Static validation passes: two HTML pages, 58 local link/asset/anchor references, JavaScript syntax and gallery assets. Browser checks at the available 449-pixel viewport confirm all three preview states, palette selection, guide filtering, no horizontal overflow, no broken images and no captured console errors. This is not a claim of an exhaustive desktop/mobile device matrix. Reduced-motion support is implemented in CSS; no browser emulation was available for that setting.

Source archive: `outputs/release-refresh/NabyScape-website-refresh.zip`. The existing hosted Site was unavailable in the connected Sites account (both listing and search returned no accessible project). Following the owner's request, a new Vercel project named `nabyscape` was created and deployed on 24 October 2026 at https://nabyscape.vercel.app/ with the guide at `/docs/`. The original Site was not modified. Canonical, social-image, sitemap, robots and README links now use the Vercel origin. Deployment is CLI-based; no Git integration was configured.

## Practical limits

Vercel production verification: deployment `dpl_3UmbBaf6Z1yr8q1QU8zKzkTbb33S` is Ready and aliased to `nabyscape.vercel.app`. Eleven public HTML, script, stylesheet, metadata, image and video responses returned HTTP 200 and matched the local files by SHA-256 (`outputs/release-refresh/vercel-public-check.json`). The public home preview responds to state changes, and the guide opens without login. Screenshot: `outputs/release-refresh/website-vercel-live.png`.

These tests do not establish live lyric coverage in Apple Music or YouTube Music, real API/provider acceptance, local-model inference quality, microphone/speaker quality, physical Windows 10/ARM64 behavior, mixed-DPI hotplug, sleep/recovery hardware behavior or code signing. Those require the relevant services and devices.

The full WPF suite's final idle sample was 0.93% of total-machine CPU and 336.6 MiB working set after many UI fixtures; hidden CPU was 0.18%. This is a warmed diagnostic process, not a fresh everyday-app memory baseline or a RAM promise. WPF timing is not GPU-presented FPS.
