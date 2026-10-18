# NabyScape website

Buildless, self-contained HTML, CSS and JavaScript for the [NabyScape website](https://nabyscape.sigma-bw-0740.chatgpt.site/) and [searchable user guide](https://nabyscape.sigma-bw-0740.chatgpt.site/docs/). No package installation, framework, account, analytics or backend is required.

## Run locally

From the repository root:

```powershell
python -m http.server 8000 --directory website
```

Open `http://localhost:8000/`. Serve this directory at the origin root: asset and guide URLs start with `/`. Opening the HTML through `file://` or mounting it under a subpath is not supported. HTML, styles and interactions work without an external font or script service; GitHub download/source links need internet access.

```powershell
node tools/check-website.mjs
# Check a different static output directory:
node tools/check-website.mjs path/to/site-root
```

The dependency-free check validates local HTML links, asset paths, anchors, duplicate IDs, image alt attributes, document titles/language, JavaScript syntax and data-driven gallery asset references. It does not replace browser accessibility or responsive-layout QA.

## Files & behavior

- `index.html`: product story, three-state island preview, feature tabs, actual app film, settings, privacy, stable downloads and source-preview availability.
- `docs/index.html`: 11 meaningful topics, topic navigation, local full-topic filter, result status, Escape to clear and deep links.
- `styles.css` / `app.js`: shared styles and local interactions. No remote data fetch, audio, OS integration or clipboard access.
- `assets/`: only the necessary optimized actual app captures, icon, preset, social image, poster and silent MP4.
- `robots.txt` / `sitemap.xml`: canonical hosted origin.

The island preview is a browser illustration made from actual WPF state captures. Hover temporarily expands it; click or the Closed/Hover/Open buttons select a state; Escape resets it. Feature tabs support arrows and Home/End. The film uses controls and `preload="none"`. Motion respects `prefers-reduced-motion`.

## Asset provenance

Stable views and the 26-second silent film come from the isolated actual v2.4.2 WPF showcase in `Assets/showcase/`. Artwork, note text, tasks and filenames are original sample content; amplitude input is synthetic. The movie has no recorded music. System readings are from the capture machine. Captions and desktop backgrounds are original presentation artwork.

`tools/render_marketing.py` composes the new `marketing-hero.png` (1600 × 900), `marketing-social.png` (1280 × 640) and `marketing-video-poster.jpg` (1280 × 800). The icon comes from the app's own `src/NabyScape/Assets/island.ico`. Gallery WebPs crop the committed stable posters and remove only the connected presentation background outside the black panel. No controls or content are generated. `settings.webp` is the stable Appearance capture. `settings-source-preview.webp` derives from `docs/evidence/integration-2.5.0/native-mica-wgc.jpg`, explicitly labeled as actual composed v2.5 source Settings evidence. The scene inside Settings is schematic.

Regenerate assets without launching the app:

```powershell
# Pillow is a documentation-tool dependency.
python tools/render_marketing.py website
```

See `docs/showcase.md` and `docs/verification-2.5.0.md` for capture and verification limits. Historical assets and `tools/render_showcase.py` remain available.

## Version truth

Public stable is **v2.6.3**. Its installer, portable ZIP, checksums and release notes are published in the [GitHub release](https://github.com/BillyNabil/nabyscape/releases/tag/v2.6.3). The optional AI companion has no bundled provider key; users provide their own, stored per Windows user in Credential Manager. The site itself does not store or handle API keys.

Hosting identity, credentials and publishing state are managed separately and are not included in this source snapshot. Public hosted URL: https://nabyscape.sigma-bw-0740.chatgpt.site/.
