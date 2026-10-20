# Closed waveform — 2.7.1

The closed dock still used the old 72 × 12 DIP strip and 1.2 DIP bars. Quiet frequency bands could shrink to a few pixels even with the new shared spectrum sampling enabled.

The closed strip now uses up to 104 × 22 DIP, with bars up to 2 DIP wide. A compact-only perceptual curve makes quieter measured bands readable while retaining variation between frequency bands. Zero signal stays at the baseline; pause and reduced motion clear the animation. The panel and hover keep their existing size and response. Sampling, FFT and render pacing are unchanged.

## Checks

- Release build: no warnings or errors.
- Logic checks: 464 passed, including compact-band visibility, contrast, silence and pause.
- Focused WPF checks: six passed, including the user's 327 × 36 DIP dock, minimum 180 × 28 and maximum 400 × 72 sizes, clock/companion spacing and no unexpected audio capture.
- Captures at 100%, 150% and 200% raster scale. These are WPF offscreen renderings, not physical monitor/DPI transitions or presented-frame measurements.
- The animation below uses synthetic frequency-band fixtures. It demonstrates rendering and does not claim to reproduce a particular Spotify track.
- Installer and portable ZIP passed checksum/content validation. Local upgrade to 2.7.1 is running with the exact verified payload; startup preferences, notes/tasks/shelf, assistant configuration and calendar remain intact. A pre-upgrade backup was created.

![Closed waveform animation, synthetic test signal](evidence/closed-waveform-2.7.1/closed-waveform.gif)

[Focused UI report](evidence/closed-waveform-2.7.1/integration-report.txt)

[Logic report](evidence/closed-waveform-2.7.1/logic-report.txt) · [Local upgrade verification](evidence/closed-waveform-2.7.1/upgrade-report.json)
