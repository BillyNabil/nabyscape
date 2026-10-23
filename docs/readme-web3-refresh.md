# README — web3 art direction

The owner explicitly chose a premium web3 visual direction after rejecting the earlier editorial treatment. This refresh affects the README and its authoring assets. The website is a separate surface and is not restyled by these changes.

## Direction and files

Near-black `#070a08`, off-white `#eff2e9`, lime `#c7ff91`, large Manrope lettering and a polished black/chrome Naby medallion. The only luminous color outside the media artwork is the lime brand accent. No token, wallet, financial or blockchain functionality is implied.

- `Assets/showcase/readme-web3-cover.jpg`: 1440 × 850 rendered cover.
- `Assets/showcase/readme-web3-states.jpg`: 1440 × 850 illustration of Closed, Hover and Open.
- `Assets/showcase/readme-web3-tools.jpg`: 1440 × 760 composition of historical native Focus and Notes captures.
- `Assets/readme/naby-chrome.png`: original transparent 3D brand artwork, 1254 × 1254.
- `tools/readme-studio.html`: editable composition source. Serve the repository locally, open this page, then use `#states` and `#tools` for the additional sheets. It reuses the website's media template and authored SVG icons. Its layout targets the listed raster dimensions rather than a standalone responsive website.
- `Assets/readme/fonts/Manrope.ttf` and `manrope-OFL.txt`: self-hosted font and license from the Google Fonts Manrope directory. This font is used only by documentation authoring and is not embedded in the app.

The first two JPEGs are labeled illustrations. They are browser renders of HTML, not WPF evidence. The Focus/Notes images retain their v2.10 provenance. The latest Settings screenshot remains a separate native 2.21.1 capture. Browser JPEGs are saved directly, without raster editing or re-encoding.

## Generated artwork

Built-in ImageGen output copied from `C:/Users/billy/.codex/generated_images/01a139e5-f648-7b12-9916-cc2b869c112b/exec-78d16c04-0ec1-4d31-813b-2b0249bcee34.png`. Its alpha channel spans 0–255. The exact prompt is also embedded as PNG text metadata; pixels and transparency are preserved.

Exact generation prompt:

> Create a single premium 3D brand object on a genuinely transparent background for NabyScape, a dark Windows desktop companion. The object is the Naby mascot as a thick circular medallion: a completely plain charcoal-black round face, with exactly TWO soft white vertical capsule eyes, evenly spaced, both leaning gently 10 degrees to the left. No mouth, no pupils, no eyebrows, no ears, no antenna, no human features, no other markings. Preserve this extraordinarily minimal face. Front face satin obsidian, perimeter machined liquid-chrome metal with a very restrained acidic lime green light reflection along the lower left rim. The disk has substantial premium metal thickness, subtly bevelled edges and a dark polished sidewall. Three-quarter view turned slightly toward the left, tilted about 12 degrees in space; the two eyes remain entirely visible. Art direction: a highly considered premium web3 product identity campaign, industrial object photography, sculptural metallic material, physically believable studio reflections, crisp black and silver, white emissive eyes, extremely fine finishing. One big object filling the canvas with generous clean transparent margin. Soft directional studio light from upper left, silver highlights on upper right edge, refined tiny green reflection, no colored fog, no purple glow, no rainbow, no scenery, no pedestal, no floor, no cast floor shadow, no coins, no cryptocurrency symbols, no text, no typography, no watermarks, no UI. Square image.

## Verification

Documentation-only checks: visual inspection at each composition's target size, loaded local font/image resources, player containment, valid README image references, image decoding and actual dimensions, and HTML authoring script syntax. No native app behavior is changed. Review is inline at the owner's request; no subagents are used. Changes remain local until a separate GitHub commit/push.

Final checks passed for all four images used by README, all three new JPEG dimensions, local Markdown links and the authoring JavaScript. The local preview reports loaded fonts/images and no captured browser warning/error logs. A clipped historical Weather capture was excluded from the new sheet; the complete Notes capture is used instead.

## Authorized Vercel follow-up

After the owner requested a Vercel deployment, the website was adapted to this visual direction and published on 24 October 2026. Production deployment `dpl_4RhFDovxwszu9Z6mCYUKFyHfwgSb` is Ready at https://nabyscape.vercel.app/. Eight updated public resources returned HTTP 200 and matched local SHA-256 values. Static integrity passes for 2 HTML pages and 73 local references. Viewports 1323, 900, 360 and 320 px were checked without horizontal overflow. Demo state switching, palette/play controls and guide search still work. Evidence: `outputs/release-refresh/vercel-web3-public-check.json` and `website-web3-live.jpg`. No GitHub push was performed.
