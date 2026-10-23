# Naby AI · local 2.20.0 candidate

Naby provides text chat, voice, a reactive character and context from a window or display you choose. AI is optional; island widgets continue working without a provider. No key, subscription or model weights are bundled.

## Connect and chat

Open **Settings → Naby AI → Overview** for the setup checklist. **Chat** contains provider connections, **Voice** contains listening/speaking setup, **Hermes** contains its connection guide, and **Advanced** contains instructions, widget navigation and character preferences. In Chat, add a provider, enter its endpoint and key if needed, then choose a model from discovery or paste its ID manually. Save the provider before checking the saved connection. **Test connection** sends one short independent chat request; it does not send your conversation, microphone or screen. Charges may apply. Model discovery reads metadata and does not prove voice or image capability.

The overview distinguishes saved configuration from a checked chat response and installed local speech files. Checks can be cancelled. Results stay in memory for this app session and are invalidated when the provider is saved or its key is forgotten. A successful check of unsaved draft fields does not verify a saved connection. Normal successful chat or local-voice replies also confirm the chat connection without an extra request. The overview never claims that a chat check verifies speech, tools or model quality.

Select the chat provider and enable AI. Open Naby from the island character. Enter sends; Shift+Enter adds a line. Ctrl+V and the composer Paste menu accept image-only clipboard contents, PNG streams, Windows bitmap images, copied files and ordinary text. Image preparation runs outside the UI thread, with a bounded clipboard-busy retry. Copy image returns both PNG and bitmap clipboard formats. Chat supports Markdown paragraphs, headings, emphasis, lists and code. Clear removes the in-memory conversation.

Supported protocol presets include OpenAI Responses, Anthropic Messages, Google Gemini and compatible Chat Completions servers including Ollama, LM Studio and Hermes. Public endpoints require HTTPS; localhost HTTP is supported. Private-network endpoints, automatic redirects, embedded URL credentials and automatic proxy forwarding are blocked. API compatibility does not guarantee every model feature.

Keys live in Windows Credential Manager, separately from provider metadata in `%LOCALAPPDATA%\NabyScape\assistant.json`. Layout exports exclude keys. Replacing a server origin requires a replacement key. The Hermes wizard additionally matches the exact profile URL when reusing a key.

## Attach files and receive images

Click the paperclip, drop files onto chat, or paste an image or copied files into the composer. Preview and remove attachments before Send. You can send attachments without a typed prompt. Draft files remain in memory when switching Chat and Voice; end an active voice conversation before sending chat files. Failed sends retain attachments for review.

- Images: Windows decodes supported PNG, JPEG, GIF, BMP, TIFF and installed-codec formats, then sends a bounded JPEG. GIFs use their first frame; unsupported WebP codecs show an attachment error.
- PDF: Responses, Chat Completions and Anthropic receive an inline PDF; Gemini receives inline PDF data. The model/server must support PDFs. Hermes currently rejects non-image files, so Naby sends the first **12 pages** as images. The preview and model context state the page limit.
- Video: Gemini receives the original inline video. Other providers, including Hermes, receive **8 evenly sampled frames**, without audio or full motion. Windows must support the video's codec to create those frames. The attachment states this limitation; this is not full video understanding.
- Documents: TXT, Markdown, CSV, JSON, code and common text files are sent as text. DOCX, PPTX, XLSX and ODT are read locally for text and cached spreadsheet values. Embedded images, document layout and spreadsheet recalculation are not included. Password-protected documents and unsupported binary formats are rejected.
- Audio: MP3, WAV, M4A, OGG and FLAC can be sent to Gemini models that accept the MIME type. Other protocols ask for a transcript.

Enable **Image input** in provider settings for images, PDF or visual video input. **Image replies** opts compatible Responses endpoints into their hosted image-generation tool, Gemini image models into text/image output, and compatible Chat Completions endpoints into image modalities. This cannot add image-generation capability to a text-only model. Hermes uses its own configured image tools. No image provider is called by merely enabling the option; a user-sent chat may incur provider charges.

Image replies appear as native cards: click to enlarge, **Copy image**, or **Save**. Naby recognizes Markdown images, structured image parts, Chat Completions image arrays, Gemini inline images and Responses image-generation results. Agent `MEDIA:` URLs are also supported. An agent must return actual image data or a reachable image URL; a server-local path cannot be displayed as a remote image. Naby explains that case rather than reading arbitrary local files. Image output depends on the model and tools configured at the provider.

Limits: **6 files**, **16 MB per original file**, **24 MB of prepared attachment content**, **60,000 characters per document** and **100,000 attached text characters per message**. Encoded request/response bodies are limited to 32 MB. Downsampling, PDF previews and base64 encoding can affect the prepared-content limit. Oversized conversation context resets the in-memory chat before the next turn. Attachments are not added to settings or exported layouts. They are uploaded only on Send, with local conversion off the UI thread. Returned remote images are fetched for display; provider credentials are sent only to that provider's unchanged API origin and base path. Media redirects, private-network destinations and unrequested local-file reads are blocked.

Provider schemas: [OpenAI file inputs](https://developers.openai.com/api/docs/guides/file-inputs), [OpenAI image generation](https://developers.openai.com/api/docs/guides/tools-image-generation), [Gemini video input](https://ai.google.dev/gemini-api/docs/video-understanding), [Gemini image output](https://ai.google.dev/gemini-api/docs/generate-content/image-generation), [Claude PDF input](https://platform.claude.com/docs/en/build-with-claude/pdf-support), [Hermes API limits](https://hermes-agent.nousresearch.com/docs/user-guide/features/api-server).

## Connect Hermes

Choose **Options → Connect Hermes** in the bottom toolbar, or open **Settings → Naby AI → Hermes → Connect Hermes · guided setup**. The wizard explains gateway setup, address/profile/key entry, advertised agent discovery and connection errors. See the [Hermes guide](hermes.md).

Naby's former **Actions** and **Computer** features remain removed, including mouse/keyboard injection and widget mutations. The limited widget-navigation tools described below can only list and open enabled pages. A connected Hermes agent manages its own tools and permissions. Naby does not show a local Run once approval for Hermes agent work; configure those permissions in Hermes.

## Voice mode

The bottom toolbar's **Chat / Voice** control switches between text and voice. The duplicate composer microphone has been removed. Opening the widget never starts capture. Start conversation is explicit unless the user has enabled wake listening; that setting enables a local phrase listener and starts voice after a matching wake phrase. Choose **Options → Voice settings** for automatic speech packs, your own model files or existing external speech services. Your selected chat provider, including Hermes, generates replies for local speech. See [hardware recommendations, supported models and installation](local-voice.md).

Realtime voice remains available through a separately configured OpenAI Realtime or Gemini Live provider. Local speech works turn by turn: listen, transcribe after a pause, then generate and speak the reply. Compatible Chat Completions endpoints can stream text into sentence-sized speech segments before the full answer finishes. The microphone waits during processing and playback. Voice uses a normal conversation without style presets. Fading captions share the full chat history.

**Stream local voice replies when supported** is on by default in Voice settings. **Save streaming preference** ends the active session and saves the choice. Disable it for an endpoint that rejects streaming. JSON returned to the original streaming request is consumed directly; Naby never automatically resends agent work to switch formats. Other protocols and image-output requests use the existing complete-response path. Turns offering local navigation tools buffer speech until a final text-only response is confirmed; reasoning and Hermes tool-progress frames are not read aloud.

During managed local voice, Supertonic/Kokoro/Kitten use one isolated native worker and keep the model loaded until End conversation. The private worker uses the pinned Sherpa 1.13.8 C API over a current-user pipe, with no HTTP port. Closing the dock keeps the session and model active. Windows speech and external services keep their own engine paths; the legacy bundled Piper CLI synthesizes one complete reply to avoid repeated model loads per sentence. Native ASR packs other than Whisper retain their existing per-utterance loading behavior. Model processes use additional RAM/VRAM beyond the dock process.

Voice shows the current stage and in-memory **ASR / Reply / TTS** durations. The tooltip explains first-text, first-segment synthesis and first-queued-audio timing; it excludes the end-of-speech pause and hardware output latency. These are observations from the user's own turn, not a benchmark or a promise of response speed. No audio, transcripts or timing telemetry are written to a log by this feature. Audio/text queues are bounded, playback stalls have a timeout, and a failed local voice turn stops capture with an actionable status. An interrupted stream is recorded as partial text, without claiming agent work completed. Hermes may continue backend work after a disconnected request; check its session before retrying.

**Stop**, Escape in chat, hiding NabyScape or quitting ends microphone capture, playback, sharing and managed speech children, and pauses wake listening. **End conversation** ends the active voice session and can return to the armed local wake listener after the old voice device closes. Closing the chat panel to the island can preserve an active voice session; its indicator remains visible. Reduced motion preserves static state feedback.

When a new reply arrives with the dock closed, a compact panel shows a reactive Naby on the left and reply text on the right, without taking keyboard focus. **Open chat** shows the full answer. **Dismiss reply** closes the panel while voice keeps running. The panel stays during speech, then leaves reading time based on reply length; hover or keyboard focus pauses dismissal. Other notices, hover controls and file drags take priority, and hidden/tucked docks are respected. Replies already shown in an expanded dock are not announced again.

## Wake word · optional local listening

Open **Options → Voice settings → Wake word · hands-free voice**. Configure and save a voice connection first. Check **Enable wake word on this PC**, keep **Hey Naby** or enter a phrase, choose an installed Windows recognizer and confidence threshold, then **Save & apply wake word**. This is the explicit microphone opt-in. The setting is off by default; ASR/TTS model installation alone does not enable it. A saved opt-in resumes at normal app startup while AI and voice configuration remain enabled. Diagnostic runs never start it.

Windows must have a matching SAPI speech-recognition language installed. **Refresh recognizers** only lists installed metadata; **Windows speech settings** opens the language setup page. Installing a local Whisper/Qwen/Moonshine model does not install a Windows recognizer. If no recognizer, microphone or voice configuration is available, the UI explains the issue. The default English phrase accepts a few close pronunciation variants; a custom phrase uses its own grammar. This is phrase recognition, not a trained speaker-verification system.

The listener evaluates a small local grammar; it does not upload or save wake audio. A matching phrase releases the wake microphone, opens the voice widget and starts the selected local or realtime voice connection. Voice audio then follows that selected connection. It does not submit the wake phrase as a chat message. Wait for voice to be ready before speaking the request.

The island character and tray tooltip indicate microphone listening, and Voice shows the current wake status. **Options → Pause/Resume wake word** and the tray offer quick controls. End conversation can re-arm wake listening after voice closes and a short cooldown; the voice privacy label explains this. **Stop all microphones**, Stop/Escape in chat, Hide, quitting, screen lock and sleep pause both listeners. After lock or sleep, resume manually. Saving provider configuration also pauses wake listening. Default-device or Windows recognition failures require checking settings and explicitly resuming; there is no retry loop that repeatedly opens the microphone. Wake and voice startup/teardown are serialized to avoid concurrent capture or stale activation after Stop.

Wake uses Microsoft's [System.Speech recognition API](https://learn.microsoft.com/en-us/dotnet/api/system.speech.recognition.speechrecognitionengine) and its [MIT-licensed package](https://www.nuget.org/packages/System.Speech/10.0.13). Enabled recognition uses additional Windows resources; no hardware recognition or resource benchmarks were run for this candidate.

## Navigate widget pages with AI tools

**Settings → Naby AI → Widget navigation · AI tools** controls the capability. It is enabled for the requested navigation feature, while AI itself still has its existing enable switch. Direct chat, local speech replies and supported realtime voice models receive exactly two local tools:

- `naby_list_widget_pages`: list enabled page IDs, titles and the currently selected page. No widget contents are returned.
- `naby_navigate_widget`: open an enabled page using `{"page":"weather"}`, or use `next` / `previous` to cycle through enabled pages in settings order. Widgets that share a panel are grouped into that page. For example, timer/focus share Focus, and clock/system/battery share Status.

Try “Open Weather”, “Show my tasks”, or “Next widget page”. The provider/model must support function calls. Navigation changes the view only; it cannot enable a disabled widget, read notes or clipboard contents, edit tasks, start a timer, change playback, open apps, record a microphone or operate the computer. Opening the voice page through a navigation tool does not start voice. The tool reports the actual page result. Function calls are validated by name, bounded JSON schema, current enabled pages and cancellation checks before dispatch. Changing the capability resets local chat so old tool calls do not remain in its continuation.

Hermes manages its own server-side tools, so Naby does not send local function definitions to its chat endpoint. Use **Widget navigation · AI tools → Connect Hermes & other MCP clients** instead. **Copy Hermes setup** produces a `mcp_servers` YAML entry for Hermes on this Windows PC; **Copy WSL setup** uses `/mnt/<drive>/…/NabyScape.exe` and requires Windows executable interop in WSL. Merge the entry into the existing Hermes `config.yaml`, keep other server entries, restart/reload Hermes and keep NabyScape running. **Save MCP config** exports a standard `mcpServers` JSON entry for other local MCP clients. See [Hermes MCP instructions](hermes.md#widget-page-navigation-over-mcp).

The executable's `--widget-mcp` mode handles bounded newline-delimited JSON-RPC over stdio and forwards only the two widget tools through a named pipe restricted to the same Windows account. It does not launch the UI, expose a network port, read API credentials, or expose general computer-control actions. AI and widget navigation must be enabled in the running app. Remote Hermes machines cannot use this local bridge directly. [MCP stdio specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports), [Hermes MCP setup](https://hermes-agent.nousresearch.com/docs/user-guide/features/mcp).

## Share a window or display

Enable **Image input** for a compatible provider/model, then click the monitor icon in the composer. Windows opens its native sharing picker: choose an application window or an entire monitor. Cancel keeps the previous source. A preview and source name confirm what is shared. Click the sharing icon again to stop sharing without ending voice.

Naby captures the chosen source into a scaled JPEG in memory, without mouse/keyboard control or saved screenshots. Chat turns refresh the image when sent. Realtime voice refreshes it approximately every five seconds while connected; local voice refreshes on each turn. Closing the selected window stops sharing. Minimized, protected or unsupported sources can fail; restore the source or choose another. An entire monitor can include other applications visible on it.

## Data and verification limits

Chat history stays in memory until Clear or exit. Messages and explicitly shared screen/audio context go to your configured provider. Hermes can retain history or perform tools according to its own configuration. Local speech does not make a remote chat provider local.

Whisper/Piper audio stays in memory. Moonshine/Qwen3-ASR and native TTS CLI adapters use temporary WAV files and delete them on normal completion, errors and cancellation; an application/Windows crash can leave files under `%TEMP%\NabyScape-speech-*`. Speech weights stay outside the installer under the local speech folder.

This candidate was compiled and packaged without automated tests, UI automation, microphone/speaker trials, provider/agent requests or model inference, as requested. Source interfaces and official package metadata were inspected. Real-device capture, agent execution and model performance remain unverified.

## Runtime and control consistency · 2.19

Chat, voice and dock state updates are coalesced on the UI dispatcher, and immutable history snapshots are reused between message changes. Hidden chat views release transcript visuals while retaining the conversation and scroll position for reopening. Local voice shutdown cancels the active turn with its session, stops capture/playback/managed engines and waits for work to unwind with a bounded eight-second drain. Stale replies and attachment preparation do not replace the state after Stop or a provider/conversation change. Recognition timeouts display a recovery message. No resource or latency reduction has been benchmarked.

AI and Settings share button geometry, control colors, keyboard focus, input styling and expanders. Voice mode offers Set up voice when configuration is incomplete, and Try again after an error. Runtime behavior and visual alignment have not been tested in this candidate; verification was source/build/package only.


## Interface language and accessibility · 2.20

Application captions have English as the default, with Spanish and Indonesian resources and an English fallback for untranslated technical text. Chat messages, files, provider IDs and model names are preserved. Language, text size and forced high contrast are saved in General settings and applied after quitting and reopening. Shared controls have focus outlines, accessible icon labels and selected polite status announcements. Voice caption height follows the configured text size. See [production foundations and limits](production-foundations.md).


## Setup, readiness and recovery

Start in **Settings → Naby AI → Overview**. Choose **Chat with AI**, **Talk to Naby**, or **Connect my Hermes**. The overview names the provider that generates replies, the listening model (ASR), and the speaking voice (TTS). Protocol and realtime endpoint fields are under Advanced in the provider editor.

A saved configuration is not a successful capability test. **Test connection** checks chat only. **Test image support** sends a generated red sample to the saved vision-enabled connection; no personal image is used. It may consume provider credits, and Hermes may process it as an agent task. A successful image check does not verify image generation or every document format. No check runs just because Settings opens.

Provider connection edits remain drafts until **Save provider**. Switching the edited provider, adding/removing a provider, connecting Hermes or closing Settings offers **Save**, **Discard**, and **Keep editing** when needed. Moving between Settings categories keeps the editor draft. Dock preferences continue to save automatically. Storage failures expose **Retry save**.

### Test voice independently

In **Voice → Test microphone & speech**, **Test microphone** captures four seconds and checks for audible input. **Test transcription** records a short sample and runs the saved local/loopback ASR setup without sending the result to the chat provider. **Play sample** speaks a fixed sentence without starting the microphone. Realtime providers are checked by starting their voice conversation. Tests can load the saved engines, use extra memory and take time; they do not install weights automatically. Use **Cancel test** to stop; leaving the test surface or Stop all microphones cancels it too. Wake listening pauses during these explicit tests.

Chat and voice distinguish wake listening, microphone activity and screen sharing. End conversation returns to an enabled wake word; Stop all microphones pauses it. Error controls lead to connection, voice or Windows microphone settings. Hermes tasks are never automatically resent because a request may already have started work.

### Models and attachments

**Voice → Installed models** shows managed ASR/TTS weights and cached/incomplete downloads. Disk size is measured on demand, including the matching cache. Uninstall confirmation shows estimated space reclaimed; shared engines, Windows voices and externally imported files are kept. After removal, use **Reinstall / choose another model**. Selected IDs remain available, but missing local weights cannot silently fall back to cloud voice. Use your own model setup's file picker to locate moved files.

Before sending attachments, their cards explain what is supplied to the selected provider. Hermes PDFs use the prepared first pages as images; other PDF endpoints receive the original document. Non-Gemini video uses sampled frames without audio; Gemini receives the original video. Capability and file support still depend on the chosen model. Multiple draft attachment cards wrap within the chat composer.

Search Settings for **wake word**, **uninstall model**, **image input**, or **test microphone** to open the corresponding nested controls.
