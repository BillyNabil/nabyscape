# Connect your Hermes agent

1. Configure your provider/model in Hermes first. In its API server settings, enable the server and set your own long random `API_SERVER_KEY`. Start or restart `hermes gateway` after saving. The [official Hermes API server guide](https://hermes-agent.nousresearch.com/docs/user-guide/features/api-server/) explains installation and gateway options.
2. In Naby chat, choose **Options → Connect Hermes**, or **Settings → Naby AI → Hermes**. Paste the API address, normally `http://127.0.0.1:8642/v1/`, and that server's key. For shared profile routing, enter the profile name or paste its complete `/p/name/v1/` URL. Use that profile's own key.
3. Choose **Check server**, select its advertised agent ID, then **Connect & use Hermes**. This checks address, authentication and model metadata without sending a chat or starting an agent task. An actual chat can still fail if Hermes' underlying model/tools are misconfigured.

For a remote machine, use public HTTPS or a localhost port forward. The wizard does not install, launch or reconfigure Hermes. The key belongs to the Hermes API server, rather than the underlying LLM provider. Enable image input only if the agent's configured model supports it.

The wizard stores the key in Windows Credential Manager and selects Hermes for chat. For speech, choose a local pack in **Options → Voice settings**. Naby supplies chat/voice and chosen screen context; Hermes manages its own tools and permissions. Naby's former Actions/Computer switches and per-action reviews are removed.

Connection errors are displayed in the setup card. Overview distinguishes this metadata check from a confirmed chat reply. The optional chat connection check sends a short message to Hermes, which may process it as an agent turn; it can use provider credits. No real Hermes server was contacted during development.

## Chat attachments and image replies

Naby sends image attachments through standard inline image parts. Because the Hermes API currently rejects `file` / `input_file` and non-image data URLs, Naby converts the first 12 PDF pages and 8 sampled video frames into images, with explicit page/frame limits. Text and Office documents are provided as extracted text. Enable Image input for a vision-capable underlying model; frame sampling does not include audio.

For image replies, configure an image-generation or image-search tool in Hermes and have it return a reachable image URL using Markdown image syntax, a `MEDIA:` URL, or structured image data. Naby displays, enlarges, copies and saves those images. A server-local `/tmp/...` path is not a downloadable image; Naby does not expose an agent's filesystem or invent an upload service. The [official API guide](https://hermes-agent.nousresearch.com/docs/user-guide/features/api-server) documents supported inputs. No live Hermes request was used for this implementation.

## Widget page navigation over MCP

NabyScape 2.17 adds a local MCP connector so Hermes can navigate the running island without restoring the old Computer or Actions features.

1. In NabyScape, enable AI and **Widget navigation · AI tools** under Naby AI → Advanced settings.
2. Expand **Connect Hermes & other MCP clients**. Choose **Copy Hermes setup** for Windows Hermes, or **Copy WSL setup** for Hermes in WSL on the same PC. WSL requires Windows executable interop.
3. Merge the copied `nabyscape` server entry under `mcp_servers` in your existing Hermes `config.yaml`. Preserve other MCP servers. The command points to the installed NabyScape executable with `--widget-mcp`; it is not the chat API address.
4. Restart Hermes or reload its MCP tools. Keep NabyScape running on the same Windows account. Ask Hermes to list NabyScape widget pages, then open Weather or the next page.

The connector exposes `naby_list_widget_pages` and `naby_navigate_widget`. The latter accepts a page ID, `next`, or `previous`. It only changes the selected widget page. It never returns widget contents or provides arbitrary app/computer actions. Turning off AI or widget navigation makes these tools unavailable. A stopped app returns an actionable tool error; the connector does not auto-launch it. Refresh/reconnect the MCP server after changing enabled widget pages or the navigation setting.

**Save MCP config** produces `mcpServers` JSON for local clients that use that format; Hermes uses `mcp_servers` YAML. The bridge is local to this Windows user, with no network listener or copied API key. A remote Hermes host needs a separate explicitly configured local integration; this connector is not a remote-control server. See the [official Hermes MCP guide](https://hermes-agent.nousresearch.com/docs/user-guide/features/mcp). No live Hermes/MCP client requests were run during this implementation.
