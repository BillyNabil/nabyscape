# AI companion · v2.6.0 candidate

An optional companion inside NabyScape: text chat, live voice, a reactive character, selected-display context, and actions you review before they run. The island, audio controls and local tools continue to work without AI.

This is a local development candidate. The public installer remains v2.5.1 until the candidate is released. No API key or AI subscription is bundled.

## Get started

1. Open **Settings → Application → AI companion → Manage AI providers**, or **AI companion** from the tray or command bar.
2. Choose a provider preset and **Add provider**. Enter its model ID and, if required, API key. Save the provider, then enable the companion.
3. Open **Companion**, choose the chat provider and send a message. **Enter** sends; **Shift+Enter** adds a line. **Escape** or **Stop** cancels the session.

**Check configuration** validates the fields locally. It does not test your key, model access or endpoint. The first chat or voice request checks those with your provider.

## Connect your provider

| Preset / protocol | Text | Images and actions | Live voice |
| --- | --- | --- | --- |
| OpenAI · Responses | Yes | If your model supports them | OpenAI Realtime, configured separately |
| Anthropic · Messages | Yes | If your model supports them | No native adapter in this version |
| Google Gemini · generateContent | Yes | If your model supports them | Gemini Live, configured separately |
| xAI · Chat Completions | Yes | If your model supports them | Custom compatible endpoint only |
| Ollama / LM Studio · Chat Completions | Yes | Depends on the local server and model | Custom compatible endpoint only |
| Custom · Chat Completions, Responses, Anthropic or Gemini | Compatible API required | Explicit model capability switches | OpenAI Realtime or Gemini Live protocol required |

Enter the model IDs from your provider. Image input and function tools start off, so enable them only for a supported model. Chat uses bounded complete replies; voice uses a live WebSocket connection. API compatibility does not imply identical model features.

Public servers require HTTPS/WSS. Local servers can use localhost HTTP/WS. Redirects, embedded URL credentials, query-string API keys, private-network destinations and automatic proxy forwarding are blocked. Configure the API base URL, not the final chat/completions route.

API keys are stored in **Windows Credential Manager**, separately from `assistant.json`. They are never part of a layout export. Changing either server origin clears the saved key unless you enter a replacement. **Forget key** removes it from the vault.

## Talk live

Expand **Live voice configuration** in Providers. Choose **OpenAIRealtime** or **GeminiLive**, enter the realtime endpoint, model and provider voice name, then save. **Provider used for voice** can differ from the chat provider.

Press **Start voice** when ready. The microphone starts only after the provider acknowledges the connection. The character responds to listening, thinking, speaking and action review. Interrupting a reply clears pending playback. Audio uses the Windows default input and output devices.

**Stop**, **End voice**, closing or minimizing Companion, or manually hiding NabyScape ends the live session and turns microphone, sharing and computer control off. Reduce Motion keeps the character static; inactive or invisible characters have no continuous render callback.

## Share a display

In **Session controls**, select a display, then press **Share screen**. A preview confirms the selected display. The app keeps a scaled JPEG in memory; it does not save screenshots to disk.

The selected display is sent with chat turns and refreshed after reviewed action batches. During live voice it is refreshed about every five seconds. Stop sharing ends the session. The Companion window is excluded from Windows screen capture where the OS supports it; other windows on the selected display can be visible to the provider.

## Let it help

Turn **Actions on** for the current session. A supported model can request:

- Timer, Focus, Stopwatch and timed Keep awake controls.
- Add a task, read or append to your note, or read current NabyScape status.
- Open Settings, a saved Launch target, or an HTTP(S) website.

For other applications, share a display and turn **Computer on**. The initial computer tool supports clicks, double-clicks, scrolling, key combinations and typing. Coordinates refer to the latest shared image, including monitors to the left of the primary display. It cannot run arbitrary shell commands or bypass protected/elevated applications.

Every requested action appears in a review card. **Run once** approves that one request; **Decline** skips it. Review expires after two minutes. Stopping, changing provider or turning actions off cancels pending work. The app never treats screen contents or a model's words as user permission.

## Data and limits

AI is off by default. Chat history lives in memory until **Clear** or application exit. Provider metadata and instructions are saved locally in `%LOCALAPPDATA%\NabyScape\assistant.json`; keys live in the Windows vault. Mic, screen and computer permissions are session-only and are not exported in layout profiles.

When used, your messages, enabled audio/screen context and approved tool results go to the configured provider. Reading a note therefore shares its contents with that provider after approval. Provider pricing, retention and model availability are governed by that provider. A local model can keep these requests on your PC.

Automated checks use fake HTTP/WebSocket/audio/desktop backends and a separate WPF profile. They do not prove real-provider model access, microphone/speaker behavior, protected-window capture or every mixed-DPI configuration. See the [candidate verification report](verification-assistant-2.6.0.md).
