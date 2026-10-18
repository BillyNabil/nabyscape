# Assistant live voice implementation

Implemented `AssistantVoiceSession : IAssistantVoice` plus `AssistantVoiceAudio`, and `AssistantVoiceTests.Run(Action<string,Action>)` registration entrypoint. No other app/runtime/model/UI files changed.

## Integration

- Supported protocol strings: `OpenAIRealtime`, `GeminiLive`. Static `Protocols` and `DefaultUrl(protocol)` are available for provider management. User must configure realtime model and voice; no realtime model guessed. xAI/Anthropic voice is not claimed. Compatible custom OpenAI Realtime WSS servers can be configured explicitly.
- OpenAI input/output is PCM16 LE mono 24kHz. Gemini input is PCM16 LE mono 16kHz, output 24kHz. NAudio opens Windows default WaveIn/WaveOut devices only after the server acknowledges `session.updated`/`setupComplete`. Constructor has no device access.
- Typed `SendText` does not echo a user Transcript; only final microphone transcription emits user text. Gemini fragments are accumulated until completed turns. OpenAI uses the officially supported stable `whisper-1` transcription helper, separate from the configured realtime response model.
- Explicit SendImage accepts only bounded JPEG/PNG data URLs and enabled vision, with a one-image/second cap. Session history replays only bounded text, never previous images. No screen capture exists in this service.
- ToolCall emits data for root approvals/execution only. Advertised names, bounded JSON arguments, call IDs, deduplication and pending outputs are enforced. OpenAI calls are delivered after response completion and the final correlated result requests one continuation. Gemini uses matching functionResponses IDs and drops canceled pending IDs.
- `Level(value, speaking)` uses real PCM peaks: false denotes microphone, true denotes output playback. Native playback-thread callbacks are queued off that thread to allow synchronous disposal.
- Stop synchronously marks inactive, stops/disposes hardware, cancels capture/send work and aborts/disposes the socket before returning its Task. Session generation gating suppresses all callbacks after Stop. External cancellation uses the same shutdown. No automatic reconnect/recording.
- OpenAI server VAD with client-managed response cancel and playback truncation handles speech/typed interruption. Native playback is cleared; conservative played duration accounts for WaveOut pending buffers. Gemini activity interruption clears playback.
- Capture queue is bounded to ten 50ms frames, paced by frame duration; playback buffer is four seconds. Slow upload/overflow stops the session with a fixed safe error. Incoming messages are size/depth bounded. Endpoint validation and pinned public/loopback DNS mirror AssistantTransport. HTTP redirects/cookies/proxies are disabled; API keys are headers only, never URL parameters or displayed errors.

## Verification

16/16 fixtures passed using isolated temporary net10.0-windows console harness with the actual AssistantModels, AssistantTransport, voice files and NAudio 2.2.1. No warnings. Tests cover both setup shapes, safe key/endpoint handling, handshake gating, rates, replay/text/images, PCM upload/output, final/fragment transcripts, tool correlation/deduplication/multiple-result continuation, interruption/truncation, malformed responses/provider errors, socket closure, overflow, blocked connection/send cancellation, synchronous Stop, restart and stale callback suppression, and PCM peak calculation.

Command: `../tools/dotnet/dotnet.exe run --project "$env:TEMP/nabyscape-voice-fixtures/VoiceFixtures.csproj" --verbosity quiet`.

No application launch, actual microphone/speaker, screen/desktop access, credentials or live API request was used. Native hardware and real-provider interoperability remain untested; final integration/build belongs to root while app files are in progress.

## Official protocol sources fetched

- [OpenAI Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations): current session audio format, final output audio events, function outputs/continuation, client playback interruption/truncation.
- [OpenAI Realtime client events](https://developers.openai.com/api/reference/resources/realtime/client-events): GA session.update schema, PCM 24kHz, supported whisper-1 input transcription, message/image/function output item shapes.
- [OpenAI Realtime server events](https://developers.openai.com/api/reference/resources/realtime/server-events): final transcription/function arguments and server VAD lifecycle.
- [OpenAI WebSocket guide](https://developers.openai.com/api/docs/guides/realtime-websocket): direct WebSocket transport and bearer authentication.
- [Gemini Live WebSocket reference](https://ai.google.dev/api/live): setup/ack sequencing, generationConfig, audio transcriptions, serverContent, toolCall/toolResponse/cancellation.
- [Gemini WebSocket quickstart](https://ai.google.dev/gemini-api/docs/live-api/get-started-websocket): realtimeInput audio/video/text and function response shapes.
- [Gemini capabilities](https://ai.google.dev/gemini-api/docs/live-api/capabilities): PCM16 input16k/output24k, incremental text context and interruption.
- [Official googleapis python-genai live.py](https://raw.githubusercontent.com/googleapis/python-genai/main/google/genai/live.py) and [official _api_client.py](https://raw.githubusercontent.com/googleapis/python-genai/main/google/genai/_api_client.py): header authentication verified in actual SDK. `_api_client.py` sets x-goog-api-key in client headers; live.py passes these as WebSocket additional_headers with a key-free endpoint. This avoids the URL-key example still shown in the raw-WebSocket quickstart.
