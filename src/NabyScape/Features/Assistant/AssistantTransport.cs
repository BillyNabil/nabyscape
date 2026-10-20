using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

/// <summary>Bounded, non-streaming adapters. The caller owns tool execution and consent.</summary>
internal sealed class AssistantTransport : IAssistantTransport, IDisposable
{
    internal const int MaximumBodyBytes = 4 * 1024 * 1024;
    internal const int MaximumTextCharacters = 128 * 1024;
    private const int MaximumCalls = 16;
    private readonly HttpClient client;
    private readonly TimeSpan timeout;

    internal AssistantTransport() : this(CreateHandler()) { }

    // Injection is for trusted fixture handlers; production must use the default handler.
    internal AssistantTransport(HttpMessageHandler handler, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler is HttpClientHandler http) { http.AllowAutoRedirect = false; http.UseCookies = false; }
        if (handler is SocketsHttpHandler sockets) { sockets.AllowAutoRedirect = false; sockets.UseCookies = false; }
        timeout = requestTimeout ?? TimeSpan.FromSeconds(90);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(3)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        client = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => client.Dispose();

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(15), MaxResponseHeadersLength = 32,
        ConnectCallback = async (context, token) =>
        {
            string host = context.DnsEndPoint.Host;
            bool local = IsLocalHost(host);
            var addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            foreach (var address in addresses)
            {
                if (local ? !IPAddress.IsLoopback(address) : !IsPublicAddress(address)) continue;
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                    return new NetworkStream(socket, true);
                }
                catch (OperationCanceledException) { socket.Dispose(); throw; }
                catch (SocketException) { socket.Dispose(); }
            }
            throw new HttpRequestException("The provider destination is unavailable or disallowed.");
        }
    };

    internal static Uri ValidateEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && IsLocalHost(uri.Host)))
            || uri.Host.Length == 0 || uri.Host.EndsWith('.') || uri.Host.Contains('%'))
            throw new InvalidOperationException("Use a public HTTPS provider base URL or localhost HTTP URL without credentials, query, or fragment.");
        string host = uri.IdnHost.Trim('[', ']');
        if (!IsLocalHost(host) && (IPAddress.TryParse(host, out var ip) ? !IsPublicAddress(ip)
            : uri.HostNameType != UriHostNameType.Dns || !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
              || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The provider destination must be public or localhost.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static bool IsLocalHost(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] is not (0 or 10 or 127) && b[0] < 224
                && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2))
                && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100))
                && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        return address.AddressFamily == AddressFamily.InterNetworkV6 && b[0] is >= 0x20 and <= 0x3f
            && !(b[0] == 0x20 && b[1] == 1 && b[2] == 0x0d && b[3] == 0xb8)
            && !(b[0] == 0x20 && b[1] == 1 && b[2] == 0 && b[3] == 0);
    }

    public async Task<AssistantReply> Complete(AssistantProvider provider, string? apiKey,
        IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantTool> tools, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(provider);
        var endpoint = ValidateEndpoint(provider.BaseUrl);
        ValidateInput(provider, messages, tools);
        if (apiKey is { Length: > 4096 } || apiKey?.Any(char.IsControl) == true) throw new InvalidOperationException("The API key is invalid.");
        var activeTools = provider.Tools && provider.Backend!="Hermes" ? tools : Array.Empty<AssistantTool>();
        var body = Build(provider, messages, activeTools);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        if (bytes.Length > MaximumBodyBytes) throw new InvalidOperationException("The assistant request is too large. Reduce the conversation or image size.");
        string route = provider.Protocol switch
        {
            "ChatCompletions" => "chat/completions", "Responses" => "responses", "Anthropic" => "messages",
            "Gemini" => "models/" + Uri.EscapeDataString(provider.Model) + ":generateContent",
            _ => throw new InvalidOperationException("Select a supported assistant protocol.")
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, route));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if(provider.Backend=="Hermes")
        {
            if(provider.Protocol!="ChatCompletions")throw new InvalidOperationException("Hermes Agent uses Chat Completions. Select that protocol.");
            if(provider.HermesSessionId.Length>0)
            {
                if(!Guid.TryParseExact(provider.HermesSessionId,"N",out _))throw new InvalidOperationException("The Hermes session is invalid. Clear chat and try again.");
                request.Headers.Add("X-Hermes-Session-Id","nabyscape-"+provider.HermesSessionId);
            }
            request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
        }
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (provider.Protocol == "Anthropic") request.Headers.Add("x-api-key", apiKey);
            else if (provider.Protocol == "Gemini") request.Headers.Add("x-goog-api-key", apiKey);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        if (provider.Protocol == "Anthropic") request.Headers.Add("anthropic-version", "2023-06-01");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(provider.Backend=="Hermes" && timeout==TimeSpan.FromSeconds(90)?TimeSpan.FromMinutes(3):timeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new IOException((int)response.StatusCode is >= 300 and < 400 ? "Provider redirects are blocked. Check the configured base URL."
                    : $"The provider rejected the request (HTTP {(int)response.StatusCode}). Check its settings, access, and model support.");
            if (response.Content.Headers.ContentLength > MaximumBodyBytes) throw new IOException("The provider response exceeded the size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > MaximumBodyBytes) throw new IOException("The provider response exceeded the size limit.");
                buffer.Write(chunk, 0, count);
            }
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
                return Parse(provider.Protocol, document.RootElement, activeTools);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { throw new IOException("The provider returned an invalid or unsupported response."); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The assistant request timed out. Try again or check the provider."); }
        catch (HttpRequestException) { throw new IOException("The assistant could not connect securely to the configured provider."); }
    }

    private static void ValidateInput(AssistantProvider provider, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantTool> tools)
    {
        ArgumentNullException.ThrowIfNull(messages); ArgumentNullException.ThrowIfNull(tools);
        if (string.IsNullOrWhiteSpace(provider.Model) || provider.Model.Length > 256 || provider.Model.Any(char.IsControl))
            throw new InvalidOperationException("Enter the model identifier supplied by your provider.");
        if (provider.Protocol is not ("ChatCompletions" or "Responses" or "Anthropic" or "Gemini")) throw new InvalidOperationException("Select a supported assistant protocol.");
        if (messages.Count is < 1 or > 100 || tools.Count > 32) throw new InvalidOperationException("The assistant conversation or tool list is too large.");
        int textLength = 0, callCount = 0;
        long payloadCharacters = 0;
        var known = new HashSet<string>(StringComparer.Ordinal);
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool == null || !ValidName(tool.Name) || !names.Add(tool.Name) || tool.Description == null || tool.Description.Length > 4096
                || tool.Parameters.ValueKind != JsonValueKind.Object || tool.Parameters.GetRawText().Length > 32768)
                throw new InvalidOperationException("The assistant tool definition is invalid.");
            payloadCharacters += tool.Description.Length + tool.Parameters.GetRawText().Length;
        }
        foreach (var message in messages)
        {
            if (message == null || message.Text == null || message.Role is not ("system" or "user" or "assistant" or "tool")) throw new InvalidOperationException("The assistant message is invalid.");
            textLength += message.Text.Length;
            payloadCharacters += message.Text.Length;
            if (textLength > MaximumTextCharacters) throw new InvalidOperationException("The conversation text is too large. Start a new chat.");
            if (message.ImageDataUrl != null)
            {
                if (!provider.Vision || message.Role != "user") throw new InvalidOperationException("Enable vision only for a model that supports user images.");
                Image(message.ImageDataUrl);
                payloadCharacters += message.ImageDataUrl.Length;
            }
            if (message.Calls is { Count: > 0 })
            {
                if (!provider.Tools || message.Role != "assistant" || message.Calls.Count > MaximumCalls) throw new InvalidOperationException("Tool calls are not enabled or valid for this message.");
                foreach (var call in message.Calls)
                {
                    ValidateCall(call);
                    payloadCharacters += call.Arguments.Length + (call.ThoughtSignature?.Length ?? 0);
                    if (++callCount > 128 || !known.Add(call.Id)) throw new InvalidOperationException("Duplicate or excessive tool calls in conversation.");
                    pending.Add(call.Id);
                }
            }
            if (message.Role == "tool" && (message.CallId == null || !pending.Remove(message.CallId))) throw new InvalidOperationException("A tool result must match an earlier assistant call exactly once.");
            if (message.ProtocolItems != null)
            {
                if (message.Role != "assistant" || message.ProtocolItems.Count > 128) throw new InvalidOperationException("Invalid provider continuation items.");
                foreach (var item in message.ProtocolItems)
                {
                    if (item.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Invalid provider continuation item.");
                    payloadCharacters += item.GetRawText().Length;
                }
            }
            if (payloadCharacters > MaximumBodyBytes) throw new InvalidOperationException("The assistant request is too large. Start a new chat or reduce the image size.");
        }
    }

    private static bool ValidName(string? name) => name is { Length: > 0 and <= 64 } && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static void ValidateCall(AssistantCall call)
    {
        if (call == null || call.Id is not { Length: > 0 and <= 256 } || call.Id.Any(char.IsControl) || !ValidName(call.Name)
            || call.Arguments is not { Length: > 0 and <= 32768 } || call.ThoughtSignature?.Length > MaximumBodyBytes) throw new InvalidOperationException("The provider returned an invalid tool call.");
        using var args = JsonDocument.Parse(call.Arguments, new JsonDocumentOptions { MaxDepth = 32 });
        if (args.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Tool arguments must be a JSON object.");
    }

    private static (string Mime, string Data) Image(string value)
    {
        int split = value.IndexOf(',');
        if (split < 0 || value.Length > 3 * 1024 * 1024) throw new InvalidOperationException("The screenshot is invalid or too large.");
        string mime = value[..split] switch { "data:image/jpeg;base64" => "image/jpeg", "data:image/png;base64" => "image/png", _ => "" };
        string data = value[(split + 1)..];
        if (mime.Length == 0 || data.Length == 0 || data.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))) throw new InvalidOperationException("Use an inline PNG or JPEG screenshot.");
        try { Convert.FromBase64String(data); } catch (FormatException) { throw new InvalidOperationException("The screenshot encoding is invalid."); }
        return (mime, data);
    }

    private static JsonNode Node(JsonElement element) => JsonNode.Parse(element.GetRawText())!;
    private static JsonNode Args(AssistantCall call) => JsonNode.Parse(call.Arguments)!;
    private static JsonObject Text(string type, string text) => new() { ["type"] = type, ["text"] = text };

    private static JsonObject Build(AssistantProvider provider, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantTool> tools)
    {
        var body = new JsonObject();
        var items = new JsonArray(); var definitions = new JsonArray();
        if (provider.Protocol != "Gemini") body["model"] = provider.Model;
        var calls = messages.Where(m => m.Calls != null).SelectMany(m => m.Calls!).ToDictionary(c => c.Id, StringComparer.Ordinal);
        string system = string.Join("\n\n", messages.Where(m => m.Role == "system").Select(m => m.Text));
        foreach (var message in messages)
        {
            if (provider.Protocol is "Anthropic" or "Gemini" && message.Role == "system") continue;
            if (provider.Protocol == "ChatCompletions")
            {
                var entry = new JsonObject { ["role"] = message.Role, ["content"] = message.Text };
                if (message.ImageDataUrl != null) entry["content"] = new JsonArray(Text("text", message.Text), new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = message.ImageDataUrl } });
                if (message.Role == "tool") entry["tool_call_id"] = message.CallId;
                if (message.Calls is { Count: > 0 }) entry["tool_calls"] = new JsonArray(message.Calls.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments } }).ToArray());
                items.Add(entry);
            }
            else if (provider.Protocol == "Responses")
            {
                if (message.Role == "tool") { items.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = message.CallId, ["output"] = message.Text }); continue; }
                if (message.ProtocolItems is { Count: > 0 }) { foreach (var item in message.ProtocolItems) items.Add(Node(item)); continue; }
                if (message.Text.Length > 0 || message.ImageDataUrl != null)
                {
                    var content = new JsonArray(Text(message.Role == "assistant" ? "output_text" : "input_text", message.Text));
                    if (message.ImageDataUrl != null) content.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = message.ImageDataUrl });
                    items.Add(new JsonObject { ["role"] = message.Role, ["content"] = content });
                }
                if (message.Calls != null) foreach (var call in message.Calls) items.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = call.Id, ["name"] = call.Name, ["arguments"] = call.Arguments });
            }
            else
            {
                bool anthropic = provider.Protocol == "Anthropic";
                var parts = new JsonArray();
                if (message.ProtocolItems is { Count: > 0 }) { foreach (var part in message.ProtocolItems) parts.Add(Node(part)); }
                else if (message.Role == "tool")
                {
                    var call = calls[message.CallId!];
                    if (anthropic) parts.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = call.Id, ["content"] = message.Text });
                    else
                    {
                        var result = new JsonObject { ["name"] = call.Name, ["response"] = new JsonObject { ["result"] = message.Text } };
                        if (!call.Id.StartsWith("gemini_local_", StringComparison.Ordinal)) result["id"] = call.Id;
                        parts.Add(new JsonObject { ["functionResponse"] = result });
                    }
                }
                else
                {
                    if (message.Text.Length > 0) parts.Add(anthropic ? Text("text", message.Text) : new JsonObject { ["text"] = message.Text });
                    if (message.ImageDataUrl != null)
                    {
                        var image = Image(message.ImageDataUrl);
                        parts.Add(anthropic ? new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.Mime, ["data"] = image.Data } }
                            : new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = image.Mime, ["data"] = image.Data } });
                    }
                    if (message.Calls != null) foreach (var call in message.Calls)
                    {
                        if (anthropic) parts.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = Args(call) });
                        else
                        {
                            var function = new JsonObject { ["name"] = call.Name, ["args"] = Args(call) };
                            if (!call.Id.StartsWith("gemini_local_", StringComparison.Ordinal)) function["id"] = call.Id;
                            var part = new JsonObject { ["functionCall"] = function };
                            if (call.ThoughtSignature != null) part["thoughtSignature"] = call.ThoughtSignature;
                            parts.Add(part);
                        }
                    }
                }
                if (parts.Count == 0) parts.Add(anthropic ? Text("text", " ") : new JsonObject { ["text"] = " " });
                string role = message.Role == "assistant" ? (anthropic ? "assistant" : "model") : "user";
                string field = anthropic ? "content" : "parts";
                // Parallel tool results must form a single user turn in both protocols.
                if (items.LastOrDefault() is JsonObject previous && previous["role"]?.GetValue<string>() == role)
                    foreach (var part in parts) ((JsonArray)previous[field]!).Add(part!.DeepClone());
                else items.Add(new JsonObject { ["role"] = role, [field] = parts });
            }
        }
        foreach (var tool in tools)
        {
            if (provider.Protocol == "ChatCompletions") definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = Node(tool.Parameters) } });
            else definitions.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, [provider.Protocol switch { "Anthropic" => "input_schema", "Gemini" => "parametersJsonSchema", _ => "parameters" }] = Node(tool.Parameters) });
            if (provider.Protocol == "Responses") { definitions.Last()!["type"] = "function"; definitions.Last()!["strict"] = false; }
        }
        switch (provider.Protocol)
        {
            case "ChatCompletions": body["messages"] = items; body["stream"] = false; break;
            case "Responses": body["input"] = items; body["store"] = false; body["include"] = new JsonArray("reasoning.encrypted_content"); body["max_output_tokens"] = 4096; break;
            case "Anthropic": body["messages"] = items; body["max_tokens"] = 4096; if (system.Length > 0) body["system"] = system; break;
            case "Gemini": body["contents"] = items; body["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 4096 }; if (system.Length > 0) body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) }; break;
        }
        if (definitions.Count > 0) body["tools"] = provider.Protocol == "Gemini" ? new JsonArray(new JsonObject { ["functionDeclarations"] = definitions }) : definitions;
        return body;
    }

    private static string Get(JsonElement element, string name) => element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
    private static JsonElement ReadArray(JsonElement element, string name) => element.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Array ? item : throw new JsonException();

    private static AssistantReply Parse(string protocol, JsonElement root, IReadOnlyList<AssistantTool> tools)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)) throw new JsonException();
        var text = new StringBuilder(); var calls = new List<AssistantCall>(); var continuation = new List<JsonElement>();
        void AddText(string value) { if (text.Length + value.Length > MaximumTextCharacters) throw new JsonException(); text.Append(value); }
        void AddCall(string id, string name, string arguments, string? signature = null)
        {
            var call = new AssistantCall(id, name, arguments, signature); ValidateCall(call);
            if (calls.Count >= MaximumCalls || calls.Any(c => c.Id == id) || !tools.Any(t => t.Name == name)) throw new JsonException();
            calls.Add(call);
        }
        if (protocol == "ChatCompletions")
        {
            var choices = ReadArray(root, "choices"); if (choices.GetArrayLength() == 0) throw new JsonException();
            var choice = choices[0]; if (Get(choice, "finish_reason") is "length" or "content_filter") throw new JsonException();
            var message = choice.GetProperty("message");
            if (message.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.String) AddText(content.GetString()!);
                else if (content.ValueKind == JsonValueKind.Array) foreach (var part in content.EnumerateArray()) if (Get(part, "type") == "text") AddText(Get(part, "text"));
            }
            AddText(Get(message, "refusal"));
            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind != JsonValueKind.Null)
                foreach (var call in toolCalls.EnumerateArray()) { if (Get(call, "type") != "function") throw new JsonException(); var function = call.GetProperty("function"); AddCall(Get(call, "id"), Get(function, "name"), Get(function, "arguments")); }
        }
        else if (protocol == "Responses")
        {
            if (Get(root, "status") is "failed" or "incomplete" or "cancelled" or "queued" or "in_progress") throw new JsonException();
            foreach (var item in ReadArray(root, "output").EnumerateArray())
            {
                string type = Get(item, "type");
                if (type is "message" or "reasoning" or "function_call") continuation.Add(item.Clone());
                if (type == "message") foreach (var part in ReadArray(item, "content").EnumerateArray())
                { if (Get(part, "type") == "output_text") AddText(Get(part, "text")); else if (Get(part, "type") == "refusal") AddText(Get(part, "refusal")); }
                else if (type == "function_call") AddCall(Get(item, "call_id"), Get(item, "name"), Get(item, "arguments"));
                else if (type != "reasoning") throw new JsonException();
            }
        }
        else if (protocol == "Anthropic")
        {
            if (Get(root, "stop_reason") is "max_tokens" or "pause_turn") throw new JsonException();
            foreach (var item in ReadArray(root, "content").EnumerateArray())
            {
                string type = Get(item, "type");
                if (type is not ("text" or "tool_use" or "thinking" or "redacted_thinking")) throw new JsonException();
                continuation.Add(item.Clone());
                if (type == "text") AddText(Get(item, "text"));
                else if (type == "tool_use") AddCall(Get(item, "id"), Get(item, "name"), item.GetProperty("input").GetRawText());
            }
        }
        else
        {
            var candidates = ReadArray(root, "candidates"); if (candidates.GetArrayLength() == 0) throw new JsonException();
            var candidate = candidates[0]; string finish = Get(candidate, "finishReason"); if (finish.Length > 0 && finish != "STOP") throw new JsonException();
            foreach (var part in ReadArray(candidate.GetProperty("content"), "parts").EnumerateArray())
            {
                continuation.Add(part.Clone());
                if (!part.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True) AddText(Get(part, "text"));
                if (part.TryGetProperty("functionCall", out var function))
                {
                    string id = Get(function, "id"); if (id.Length == 0) id = "gemini_local_" + Guid.NewGuid().ToString("N");
                    AddCall(id, Get(function, "name"), function.TryGetProperty("args", out var args) ? args.GetRawText() : "{}", Get(part, "thoughtSignature") is { Length: > 0 } signature ? signature : null);
                }
                else if (!part.TryGetProperty("text", out _)) throw new JsonException();
            }
        }
        if (text.Length == 0 && calls.Count == 0 || continuation.Count > 128) throw new JsonException();
        return new AssistantReply(text.ToString(), calls, continuation.Count == 0 ? null : continuation);
    }
}
