# Stage 6 composed-window verification: open

On 2026-10-18, actual Settings captures reproduced disappearing illustration text and accent swatches after material transitions. Stage 6 is **not approved** for release. The ordinary controls remain readable, and the native material changes visibly, but successful DWM calls and WPF bitmap checks do not establish correct composed illustration rendering.

The original final binary was `release-output/publish/NabyScape.exe`, version 2.5.0, source `9a4f112`, SHA-256 `16D10B3ABDF76DACC0BCB8D61EB75BF5F3107BD02C9C3D66DF07B1AE1B06AD78`. It ran with `--diagnostics <absolute isolated output> --stage6-only --stage6-capture`; no daily application was running. Captures used the computer-use Windows capture tool and its exact returned Settings window. Foreground interference captures were discarded, then Settings was activated and captured again.

| Capture | Direct visual inspection |
| --- | --- |
| [Solid](evidence/stage6/solid-original.jpg) | Settings window / Song title / Black glass labels and white accent swatch visible. |
| [Native Mica](evidence/stage6/native-mica-original.jpg) | Labels and swatch retained; body appearance differs from Solid; requested/actual status reports Native Mica. |
| [Native Acrylic failure](evidence/stage6/native-acrylic-failed.jpg) | Native body visibly changes, but Song title / Black glass and accent swatch disappear. A later settled capture also lost the Settings illustration labels. |
| [Simulated glass, no bitmap checks](evidence/stage6/simulated-glass-failed-no-bitmap-checks.jpg) | First characters of Settings window disappear; Black glass and accent swatch disappear. This run performed no intervening RenderTargetBitmap scene checks. |

The second run used `artifacts-stage6-composed-final/NabyScape.exe`, SHA-256 `3BC5943FA3E54E3E8F1FE71E3795599AE76045D19217A99F7138B16C0C2C0FB6`, with unchanged production source and a diagnostic-only change to skip intrusive bitmap renders between composed transitions. This reproduced the issue and excludes those renders as the sole cause. Both incomplete fixtures were stopped; neither has a passing final diagnostic report. A capture acknowledgement establishes only that an external file was supplied, not that its pixels pass visual review.

A scoped static-scene BitmapCache experiment compiled, but its capture was blocked by the tool error `failed to activate captured window`, including after refreshing window selection and retrying. The experiment was reverted and is not shipped. The diagnostic now requests `.jpg`, matching the actual capture payload, and keeps WPF-only renders separate from composed-capture runs. The retained JPEGs are unedited tool captures; earlier scratch `.png` filenames contained JPEG payloads and were corrected when copying durable evidence.

The earlier scratch report's `artifacts-stage6-verified` publication/test claims were not traceable to an existing directory and are withdrawn. The prior composed run ended in one failure; it is not a pass. The parent's later final-binary unit run reported 373 passed / 0 failed, which covers policy and compatibility but does not close this visual gate.

Native eligibility checks Windows 11 build 22621+, high contrast, transparency effects, energy saver and remote sessions. AC power is not required: battery operation with energy saver off is permitted. Physical mixed-DPI relocation, hotplug, hardware sleep and native island geometry remain unverified. No installer, installed app or daily profile was used in these fixtures, and nothing was published.
