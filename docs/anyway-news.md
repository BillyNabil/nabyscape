# Anyway News summary

An optional news summary in the NabyScape island. The widget is disabled by
default and makes no news requests until you connect it. No account, Telegram
session or bot token is required.

## Service ownership and compatibility

This integration is contributed by the maintainer of Anyway News, [@cyxar4uk](https://github.com/cyxar4uk).
Anyway News is an independently operated service; NabyScape and its maintainer do
not operate it, curate its news, or guarantee its availability.

The Anyway News maintainer commits to keeping the documented public `/v2/digest`
and `/v2/integrations/news/sources` field names and types backward-compatible.
Additive fields may be introduced; incompatible changes will use new versioned
endpoints. Planned retirement of the existing endpoints or service will be
announced at least **30 days in advance** through a public issue or documentation
update in the [NabyScape repository](https://github.com/BillyNabil/nabyscape),
including any replacement endpoint.
This policy does not promise uninterrupted or indefinite service operation.

If the service is unavailable, the widget shows a localized error and retains
the last summary cached by the current process. A restart clears that cache; an
offline first launch shows the connection error without stories. Other NabyScape
features continue working. Users can disconnect, hide the widget, or configure
another API implementing the documented contract. No account or reading history
needs to be migrated. Breaking-change versioning and retirement notices are the
service maintainer's responsibility.

## Connect

1. Open **Settings → Widgets & tools → Your widgets** and enable **Anyway News**.
2. Open its page in the island and choose **Connect**. The default API address is
   `https://news.anyway-community.ru/api`.
3. Use **News connection** to disconnect or set a compatible API address.

The leading story has a cover and a full headline, followed by three compact
stories with circular source avatars. Headlines and source notes remain in the
language supplied by the service; NabyScape controls follow your selected UI
language. With the standard Anyway endpoint, the footer's **by Anyway News**
credit opens the mini app, stories open their cards, and **All news** continues
reading there. When the panel is narrow, the duplicate All news action is hidden;
the credit still opens the app. The story list and connection form scroll inside
the card, with its header and footer outside the scrolling area.

The logo and credit are provider identification, not a required attribution for
compatible APIs. They appear only for the normalized standard address
`https://news.anyway-community.ru/api` (an optional trailing slash or the default
HTTPS port is equivalent). A custom address shows its host as non-clickable text,
with no Anyway logo, credit or mini-app action. Custom stories open only their
explicit HTTP(S) `url`; a missing or unsafe URL leaves the story non-clickable.

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

For the standard Anyway endpoint, numeric `postId` values open
`https://t.me/anyway_news_bot/app?startapp=post_ID`. Other IDs may supply an explicit
HTTP(S) `url`. For a custom endpoint, every clickable story must supply its own
`url`; numeric IDs never imply a Telegram destination. Credentials and unsafe URL
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
The Windows workflow runs these checks after the logic tests, including custom
provider branding and links in its in-memory fixture. A manual check of the
standard live service may add `--news-live`. Live content is not a reproducible
CI fixture.

Icons are Microsoft Fluent System Icons 20 Regular, pinned in
`src/NabyScape/Assets/Icons/Fluent/SOURCE.txt`. Their MIT notice is embedded in the
app and included in installed, portable and notices-only release packages.
