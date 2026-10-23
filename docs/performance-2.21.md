# Memory optimization and local acceptance · 2.21

This candidate preserves animation timing, visual effects, waveform behavior and frame policy. It does not force garbage collection, trim the working set, impose a process memory cap, or lower animation quality in normal operation.

## Measurement method

Compare the immutable 2.20 executable with a self-contained 2.21 Release build on the same machine, with identical deterministic media fixtures and settings. Each paired scenario has two 12-second timed runs following warm-up. Report median private working set, total working set and private commit separately. No forced GC occurs during timed scenarios. WPF render callback intervals indicate scheduling; they are not compositor/GPU presentation measurements. GPU/native codec memory is not separately measured.

The local evidence is kept under `outputs/optimization-2.21/`. Invalid empty runs, Setup-name launches, framework-host launch failures and superseded failing test attempts are excluded from acceptance results.

| Scenario | Private working set 2.20 → candidate (MiB) | Total working set (MiB) | Private commit (MiB) |
|---|---:|---:|---:|
| Hidden | 57.07 → 54.66 | 174.49 → 166.56 | 104.24 → 89.83 |
| Closed, paused | 59.57 → 56.70 | 178.79 → 171.93 | 106.84 → 92.09 |
| Closed, playing | 60.77 → 62.79 | 180.76 → 183.40 | 107.52 → 106.25 |
| Open media | 67.78 → 73.36 | 190.21 → 196.27 | 129.04 → 124.70 |

These short runs do not establish a universal RAM reduction: idle private residency improved, while active media residency increased. Playback callback p95 remained 16.67 ms and open-media callback p95 remained 5.56 ms under the unchanged 60/180 frame policy. CPU medians were 0.06%, 0.85%, 1.10%, and 2.57% of this machine respectively. Baseline had 96 and candidate 94 one-second samples. Allocation improvements in capture and image-heavy chat should not be generalized to media-only scenarios. Final source differs from this benchmark candidate in Settings high-contrast fixes and test-only selectors; a final whole-app benchmark remains pending.

## Targeted allocation evidence

### Stability follow-up

After profiling the common caption path, normal-size/non-high-contrast labels no longer allocate a captured Loaded handler per element. Accessibility labels use one shared handler; their scale/contrast behavior remains covered by the dedicated UI diagnostic. A fresh self-contained Release comparison (`stability-paired`, 96 samples, two 12-second runs per scenario) measured:

| Scenario | Private WS (MiB) | Total WS (MiB) | Private commit (MiB) |
|---|---:|---:|---:|
| Hidden | 57.14 | 173.52 | 105.45 |
| Closed, paused | 58.49 | 176.40 | 106.91 |
| Closed, playing | 60.77 | 179.09 | 108.97 |
| Open media | 67.36 | 188.38 | 130.32 |

Open-media residency returned from the earlier candidate's 73.36 MiB to 67.36 MiB, close to the 67.78 MiB baseline. Its managed allocation rate returned from 1,600,697 to 1,568,237 bytes/second, versus 1,566,975 baseline. Frame caps remained 60/180 and callback p95 remained 16.67/5.56 ms. Whole-machine CPU for open media was 2.65% in this run versus 2.57% earlier and 2.48% baseline; short runs do not establish a CPU improvement. Commit did not decrease. These results correct the earlier active-residency regression; they do not establish a new universal low-RAM target.

Three warm synthetic 3840×2160 opaque frame encodes reduced managed allocation from approximately 99.9 MB to 0.4 MB, while matching decoded legacy JPEG output in opaque and mixed-alpha regression fixtures. Translucent frames use a bounded pooled scan and native bitmap view rather than a full managed frame copy. This is an allocation reduction for that operation, not a process-wide RAM percentage.

A 48-image transcript realized two nearby previews: 903,168 decoded pixel bytes versus a 24,883,200-byte reference decoding every preview at the previous size. The bounded cache retained at most 12 entries; hidden visuals release their pixels and cancel pending work. Actual process savings depend on attachments, originals retained in conversation, active widgets and runtime state.

## Acceptance boundary

Automated tests use temporary profiles, synthetic media/attachments, fake provider HTTP responses and fixture speech/desktop backends. They do not send paid provider requests, capture a user's microphone/desktop, or change the user's clipboard. Scaling exports and WPF layout tests do not replace physical mixed-DPI, screen-reader, Windows 10, ARM64 or GPU presentation testing. Those remain device acceptance work before a broad production-readiness claim.
