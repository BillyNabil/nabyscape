# README visual refresh — 24 October 2026

Historical iteration. The owner subsequently chose a web3 direction; the active README visual sources are documented in [readme-web3-refresh.md](readme-web3-refresh.md).

The README now shares the website's dark charcoal, warm off-white and copper palette. Its light header and geometric sample-cover screenshot have been replaced. Existing app behavior, feature descriptions and native screenshots retain their scope and provenance.

## Assets

- `Assets/showcase/readme-dark-hero.jpg`: 1440 × 780 browser-rendered composition using the website's actual HTML player template, dark styles and original coastal artwork.
- `Assets/showcase/readme-dark-music.jpg`: 1120 × 650 browser-rendered composition of the same illustrative player, with the original coastal image behind it.
- `Assets/showcase/readme-motion.svg`: editable native vector poster linking to the existing 29-second v2.10 video. It is a title card, not a new app recording.

The first two assets are deliberately labeled illustrations, not WPF captures. No new image generation was needed. The coastal artwork reuses the built-in ImageGen asset documented with its exact prompt in [the website refresh](website-dark-refresh.md). JPEG bytes returned by the browser are saved with the correct `.jpg` extension, without raster editing or re-encoding. The capture sources are retained in the ignored `outputs/readme-dark/` folder.

Current Settings screenshots and historical Focus/Weather screenshots remain native app evidence. The film itself is unchanged and retains its v2.10 label. The GitHub page background follows the reader's GitHub theme; embedded artwork uses the dark theme in either mode.

## Verification

Header and music compositions were rendered at their actual asset dimensions, checked visually, and checked for loaded imagery and clipped content. README local image references, image formats and vector XML are validated after editing. No native app tests are needed for these documentation-only changes. Review was performed inline in accordance with the user's no-subagent request.

This refresh changes local repository files. It does not create a GitHub commit or push the repository.
