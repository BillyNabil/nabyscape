# Dark website refresh — 24 October 2026

Requested changes: dark mode throughout the website and replacement of the weak placeholder media/demo presentation. Implementation stays in the existing dependency-free static site and is published to the linked Vercel project.

The two featured media presentations now use a shared HTML template rather than enlarging placeholder screenshots. The opening shows the full player immediately; Closed and Hover are available through labeled controls. The artwork is original and the preview is explicitly illustrative. Play/pause changes the demo waveform, track controls choose sample names and seeking updates the visible time. The demo does not produce audio or connect to a device.

## Artwork provenance

Asset: `website/assets/afterglow-coast.png`.

Generated with the built-in ImageGen tool. Original output: `C:/Users/billy/.codex/generated_images/01a139e5-f648-7b12-9916-cc2b869c112b/exec-074a5826-91e0-4169-a54a-3879c27e3700.png`. The project asset preserves the generated pixels; its exact prompt is embedded as PNG text metadata. No third-party song or artist is implied by the fictional demo titles.

Exact generation prompt:

> Create an original cinematic photographic landscape artwork for NabyScape desktop ambience and a fictional music demo cover. Landscape 3:2 composition. A quiet black volcanic coastline at blue hour, long exposure dark teal ocean water with a thin silver surf line sweeping toward the foreground, distant rugged headland in silhouette on the left, small warm amber crescent of last sunset light near the horizon on the right, deep smoky indigo clouds fading to charcoal at the top. Analog medium-format film photography aesthetic, rich true blacks, restrained warm copper highlights, misty depth, tactile fine film grain, believable natural light, a contemplative alternative music record sleeve mood. The horizon in the upper third, plenty of dark negative space in the upper sky. Exquisite real scenery, not a vector illustration, not geometric mountains, not a 3D render, no UI, no lettering, no logos, no watermarks, no frames. This is a finished bitmap asset for a dark-mode product website.

## Verification scope

Website-only verification; no native app changes or app test reruns are needed for this refresh. Review is performed inline because the user requested no subagents. Results and captures are recorded under `outputs/release-refresh/`.

- Static integrity: 2 HTML pages, 70 local links/assets/anchors, JavaScript syntax and gallery assets pass.
- Browser viewport checks at 1323, 900, 360 and 320 pixels: no horizontal overflow; both player illustrations fit their containers.
- Closed, Hover and Open states show the intended content. Palette changes update the selected state. Play/pause updates its accessible label and pressed state; next track changes the title; keyboard seeking updates time and range value.
- Guide search for lyrics returns three topics, with the dark theme retained and no overflow at 360 px.
- Offscreen motion pauses through visibility observation. Reduced-motion CSS disables continuous effects; this is a source check, not a measurement of physical display frame rate.
- Captured browser warning/error logs are empty during these interactions. Mobile and desktop captures are `website-dark-mobile.png` and `website-dark-desktop.png`.

Production deployment `dpl_6CKrQU3Ya2WZfmZj7zV8DU4Dthfe` is Ready at https://nabyscape.vercel.app/. Six updated public responses returned HTTP 200 and matched local SHA-256 values (`vercel-dark-public-check.json`). The live browser reports dark color-scheme, a charcoal background, two populated players, the Open initial state and no captured warning/error logs. Live proof: `website-dark-live.png`.
