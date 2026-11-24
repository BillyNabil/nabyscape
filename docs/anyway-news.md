# Anyway News summary

An optional news summary in the NabyScape island. The widget is disabled by
default and makes no news requests until you connect it. No account, Telegram
session or bot token is required.

## Connect

1. Open **Settings → Widgets & tools → Your widgets** and enable **Anyway News**.
2. Open its page in the island and choose **Connect**. The default API address is
   `https://news.anyway-community.ru/api`.
3. Use **News connection** to disconnect or set a compatible API address.

The leading story has a cover and a full headline, followed by three compact
stories with circular source avatars. Headlines and source notes remain in the
language supplied by the service; NabyScape controls follow your selected UI
language. The footer's **by Anyway News** credit opens the mini app. Clicking a
story opens that story's card, and **All news** continues reading in the mini app.
When the panel is narrow, the duplicate All news action is hidden; the credit
still opens the app. The story list and connection form scroll inside the card,
with its header and footer outside the scrolling area.

This integration does not provide a separate in-island full news feed or a plugin
loader. The service and its editorial selection are maintained by Anyway News.
Source counts indicate coverage, not independent verification of a story.

## Network and caching

Only HTTPS is accepted, except HTTP on loopback for local development. API
addresses containing credentials, a query or a fragment are rejected. Redirects
and cookies are disabled. NabyScape does not send Telegram initData, account
identifiers, API keys or reading history.

While the page is visible, a timer checks the in-memory cache once a minute; a
summary is fetched at most once every five minutes unless you refresh manually.
Attempts are separated by at least 15 seconds. Hiding or unloading the page stops
its timer and cancels pending requests. Summary requests have a 15-second timeout
and a 1 MiB response limit. A failed refresh leaves the previous summary visible
with an error message. The displayed time is when NabyScape fetched the summary.

Source metadata is fetched in batches of up to 20 IDs and cached for ten minutes,
with a five-second request timeout and a 256 KiB limit. Avatars and covers must
share the API's origin. Image responses require an image MIME type, have a 2 MiB
limit and a 15-second timeout, and up to 32 are cached in memory. An unavailable
source method or picture keeps the news readable using initials or neutral
circles. No local source catalog is required. All caches last for this process.

Normal HTTP requests reveal the client's network address to the configured
server, whose operational logging is controlled by its operator. See also
[NabyScape network connections](security.md#network-connections).

## API contract

`GET {base}/v2/digest` returns up to five stories. Unknown extra fields are ignored;
empty titles and duplicate story IDs are skipped. Source notes are retained.

```json
{"title":"Morning summary","time":"08:00","items":[
  {"postId":"123","head":"Example headline","nsrc":3,"srcs":["42","-7"],
   "cover":"/media/example.jpg","notes":["Example source note"]}
]}
```

`GET {base}/v2/integrations/news/sources?ids=42,-7` returns only public source labels
and avatar paths. Unknown or inactive IDs may be omitted. A missing avatar is an
empty string; failures of this optional method do not prevent the summary.

```json
{"version":1,"sources":[
  {"id":"42","name":"Example source","avatar":"/media/avatars/example.jpg"}
]}
```

Numeric `postId` values open `https://t.me/anyway_news_bot/app?startapp=post_ID`.
Other story IDs may supply an explicit HTTP(S) `url`; credentials and unsafe URL
schemes are rejected. Content is displayed as plain text, not executable HTML.

## Verification

Build on Windows with the .NET 10 SDK:

```powershell
./build.ps1 -OutputDirectory artifacts
./artifacts/NabyScape.exe --diagnostics ./outputs/news --isolated-diagnostics --news-only
```

The isolated WPF diagnostics use generated sample stories and an in-memory HTTP
handler, with no external network or credentials. They check cover decoding,
automatic source metadata, links without launching browsers, containment at narrow
widths and 125% panel scale, a small connection form, six UI languages, 150% text,
high contrast and Light theme. Screenshots and `integration-report.txt` are saved under `outputs/news`.
The Windows workflow runs these checks after the logic tests. A manual live API
check may add `--news-live`; an optional `NABYSCAPE_NEWS_API` environment variable
selects a compatible endpoint. Live content is not a reproducible CI fixture.

Icons are Microsoft Fluent System Icons 20 Regular, pinned in
`src/NabyScape/Assets/Icons/Fluent/SOURCE.txt`. Their MIT notice is embedded in the
app and included in installed, portable and notices-only release packages.
