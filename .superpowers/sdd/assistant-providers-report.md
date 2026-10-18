# Assistant provider adapters

Implemented `AssistantTransport`, `AssistantProviderCatalog`, and `AssistantProviderTests` only. Root owns model contracts, orchestration, credentials, UI, and test registration. No unrelated showcase files changed.

Adapters: OpenAI-compatible Chat Completions, OpenAI Responses, Anthropic Messages, Gemini generateContent. All map text, explicitly enabled inline PNG/JPEG user images, explicitly enabled function definitions, assistant calls, and matching tool results. Responses preserves/replays output items including encrypted reasoning (`store=false`, `include=reasoning.encrypted_content`); Anthropic preserves content blocks; Gemini preserves raw parts and thought signatures. Gemini calls without native IDs receive local correlation IDs that are omitted on the provider wire. Parallel Anthropic/Gemini tool results are combined into one user turn. Tools are returned for root consent/execution; adapters execute nothing.

Presets: OpenAI, Anthropic, Google Gemini, Ollama, LM Studio, xAI, plus four custom protocol variants. Models are empty and Vision/Tools false until explicitly configured. Custom base URLs are empty. No API credentials supplied, read, inspected, persisted, or used. No live provider requests or desktop/audio/screen actions ran.

Bounds: 90-second default deadline (injectable fixture timeout, maximum 3 minutes), 15-second connection timeout; cancellation covers HTTP headers/body; 4 MiB request/response limit including unknown-length responses; 128 Ki characters visible input/output text; 100 messages; 32 definitions; 16 calls per reply; 128 calls per history; 32 Ki characters per call arguments; 3 Mi characters inline image URL. Invalid or incomplete tool outputs reject the whole reply. Provider bodies, keys, and connection exception details are never echoed in errors. No retries or text streaming. No network/background work until `Complete` is called.

The production handler blocks redirects/cookies/proxies and accepts only public HTTPS or loopback HTTP/HTTPS. URLs reject userinfo/query/fragment, private IP literals, obvious local DNS names, and unknown schemes. DNS resolution is checked again in the connection callback and connects only to accepted public IPs or loopback for localhost, preventing private-address DNS redirection. API keys are request headers on the configured destination, never query strings. Trusted injected handlers exist for fixtures and must not be substituted into production.

Verified primary official sources (2026-10-18):

- OpenAI [function calling](https://developers.openai.com/api/docs/guides/function-calling) and [Responses create reference](https://developers.openai.com/api/reference/resources/responses/methods/create) (reference indexed by official search; direct reference page returned a fetch error, function guide fetched successfully). OpenAI Docs skill applied; API-key tool unavailable and fake-only tests require no key.
- Anthropic [Messages create](https://platform.claude.com/docs/en/api/messages/create), [tool result lifecycle](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls): api.anthropic.com/v1/messages, x-api-key, anthropic-version 2023-06-01, image/source, tool_use/tool_result.
- Google [API reference](https://ai.google.dev/api), [generateContent schema](https://ai.google.dev/api/generate-content): generativelanguage.googleapis.com/v1beta/models/{model}:generateContent, x-goog-api-key, functionCall/functionResponse IDs, inlineData, thoughtSignature. The newer function-calling guide emphasizes Interactions; this adapter intentionally implements the separately documented generateContent schema.
- Ollama [OpenAI compatibility](https://docs.ollama.com/api/openai-compatibility): localhost:11434/v1/.
- LM Studio [OpenAI compatibility](https://lmstudio.ai/docs/developer/openai-compat): localhost:1234/v1/.
- xAI [Chat Completions](https://docs.x.ai/developers/model-capabilities/legacy/chat-completions): api.x.ai/v1/chat/completions.

Verification: isolated net10.0 harness links only root `AssistantModels.cs` and these three files, outside the repository at `$env:TEMP/nabyscape-provider-verify`. Build succeeded with 0 warnings/errors. All 14 registered fixture tests passed after final changes. Tests cover protocol payloads/parsers and tool round trips; opaque reasoning/signature replay; empty/null/malformed/error bodies; declared and streaming oversized bodies; capability, text/tool-count and ID gates; safe headers and redirect handling; localhost metadata; pre-request and in-flight cancellation; timeout. No full app launch/suite, publish, or API integration test performed (root coordinates those).

Commands run from repository root:

```powershell
& '..\tools\dotnet\dotnet.exe' build (Join-Path $env:TEMP 'nabyscape-provider-verify\Verify.csproj') --nologo
& '..\tools\dotnet\dotnet.exe' run --project (Join-Path $env:TEMP 'nabyscape-provider-verify\Verify.csproj') --no-restore
git diff --check -- src/NabyScape/Features/Assistant/AssistantTransport.cs src/NabyScape/Features/Assistant/AssistantProviderCatalog.cs src/NabyScape/Tests/AssistantProviderTests.cs
```

Limitations: protocol compatibility does not establish access, pricing, model availability, vision/tool quality, or live voice support. This version has no provider discovery, Azure-specific deployment/auth, OAuth, native built-in tools, files, audio generation, streaming text, Gemini Interactions, or automatic model selection. System proxies and private LAN provider addresses are intentionally unsupported. The injected HTTP fixtures validate mappings and defenses, not provider account compatibility or production DNS/TLS connectivity. Generic custom providers must implement the selected documented protocol/schema; provider errors require the user to check model/settings without exposing their raw response body.

Review correction: Gemini now emits shared tool schemas through `parametersJsonSchema`, rather than the restricted OpenAPI `parameters` field. The [official FunctionDeclaration schema](https://ai.google.dev/api/generate-content#FunctionDeclaration), fetched on 2026-10-18, documents this JSON Schema field as mutually exclusive with `parameters`. A focused fixture verifies full preservation of nested strict `additionalProperties:false`, `$defs`, and `$ref` and the absence of `parameters`. The isolated harness passed all 15 provider tests after this correction, with zero build warnings/errors. No whole-app or live API run was performed.
