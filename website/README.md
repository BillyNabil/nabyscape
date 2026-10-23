# NabyScape website

A buildless, dependency-free product site. Near-black surfaces, self-hosted Manrope, lime accents, a chrome Naby hero and a black dock stage with Terracotta, Lagoon and Plum ambience. The home page includes crisp HTML Closed/Hover/Open preview states with original coastal artwork, actual app captures, local AI/voice information, recent Settings captures and a published-release link that does not pretend local source is already released.

## Preview

```powershell
python -m http.server 8124 --bind 127.0.0.1 --directory website
node tools/check-website.mjs
```

Home: `http://127.0.0.1:8124/` · Guide: `/docs/`

- `web3.css` adapts the owner-selected README direction to the responsive website. The chrome artwork and social image share the README sources; Manrope includes its OFL license.
- `home.css` scopes the underlying home composition; `styles.css` retains the guide layout primitives.
- `guide-refresh.css` gives the guide the same dark palette.
- `app.js` owns dock state/palette choices, demo play/pause, track switching, seek controls and guide filtering. The player is a labeled illustration, not live playback. Reduced motion is respected; ambience and waveform motion pause outside their own viewport and when the browser is hidden.
- Latest Settings screenshots are actual 2.21.1 WPF captures from an isolated fixture. Older music/tool imagery and the silent demo remain labeled as earlier captures.
- No analytics, account, audio capture, provider calls or API keys exist in this website.

## Hosting

Live website: https://nabyscape.vercel.app/ · Guide: https://nabyscape.vercel.app/docs/

Deployed as the new `nabyscape` Vercel project on 24 October 2026, following the owner's request. The earlier Sites project was unavailable in the connected account and was not modified.

`vercel.json` serves this directory directly with no build or framework. From `website/`, use `vercel deploy --prod` to publish later changes to the linked project. Project linkage stays in the ignored `.vercel/` directory. No Git integration is configured; a local edit alone does not publish an update.
