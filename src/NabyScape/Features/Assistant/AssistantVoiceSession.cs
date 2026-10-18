using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NabyScape;

internal interface IAssistantVoiceSocket : IDisposable
{
    Task Connect(Uri endpoint, string? key, bool gemini, CancellationToken token);
    Task Send(string json, CancellationToken token);
    Task<string?> Receive(CancellationToken token);
    void Abort();
}
internal interface IAssistantVoiceAudio : IDisposable
{
    void Start(int inputRate, Action<byte[]> capture, Action<float, bool> level, Action failure);
    bool Append(byte[] pcm);
    int PlayedMilliseconds { get; }
    void Clear();
}

/// <summary>Explicit-session voice only. Events report data; tools are never executed here.</summary>
internal sealed class AssistantVoiceSession : IAssistantVoice
{
    internal static IReadOnlyList<string> Protocols { get; } = Array.AsReadOnly(new[] { "OpenAIRealtime", "GeminiLive" });
    internal static string DefaultUrl(string protocol) => protocol switch
    {
        "OpenAIRealtime" => "wss://api.openai.com/v1/realtime",
        "GeminiLive" => "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent",
        _ => ""
    };
    private readonly object gate = new();
    private readonly Func<IAssistantVoiceSocket> socketFactory;
    private readonly Func<IAssistantVoiceAudio> audioFactory;
    private Session? current;
    private bool disposed;
    private sealed class Session(IAssistantVoiceSocket socket, bool gemini, bool vision, IReadOnlyList<AssistantTool> tools)
    {
        internal readonly IAssistantVoiceSocket Socket = socket;
        internal readonly bool Gemini = gemini, Vision = vision;
        internal readonly CancellationTokenSource Cancel = new();
        internal readonly SemaphoreSlim SendLock = new(1, 1);
        internal readonly Channel<byte[]> Input = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(10) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Dictionary<string, AssistantCall> Pending = new(StringComparer.Ordinal);
        internal readonly HashSet<string> SeenCalls = new(StringComparer.Ordinal);
        internal readonly Queue<AssistantCall> ReadyCalls = new();
        internal readonly ManualResetEventSlim HardwareStopped = new(false);
        internal readonly HashSet<string> ToolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        internal readonly StringBuilder UserText = new(), AssistantText = new();
        internal IAssistantVoiceAudio? Audio;
        internal Task Reader = Task.CompletedTask, Sender = Task.CompletedTask;
        internal CancellationTokenRegistration Registration;
        internal bool Connected, Ended, ResponseActive, IgnoreAudio;
        internal string AudioItem = "";
        internal int AudioIndex, AudioStart, OutputBytes;
        internal int StopThread;
        internal long LastImage;
    }
    internal AssistantVoiceSession() : this(() => new AssistantVoiceSocket(), () => new AssistantVoiceAudio()) { }
    internal AssistantVoiceSession(Func<IAssistantVoiceSocket> socketFactory, Func<IAssistantVoiceAudio> audioFactory)
    { this.socketFactory = socketFactory; this.audioFactory = audioFactory; }
    public bool Connected { get { lock (gate) return current?.Connected == true; } }
    public event Action<string, string>? Transcript;
    public event Action<string>? Status;
    public event Action<AssistantCall>? ToolCall;
    public event Action<float, bool>? Level;

    public async Task Start(AssistantProvider provider, string? apiKey, string instructions, IReadOnlyList<AssistantTool> tools,
        IReadOnlyList<AssistantMessage> history, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider.RealtimeProtocol is not ("OpenAIRealtime" or "GeminiLive"))
            throw new InvalidOperationException("This voice protocol is not supported. Choose OpenAIRealtime or GeminiLive.");
        if (string.IsNullOrWhiteSpace(provider.RealtimeModel) || provider.RealtimeModel.Length > 200 || string.IsNullOrWhiteSpace(provider.Voice) || provider.Voice.Length > 100)
            throw new InvalidOperationException("Configure a realtime model and voice before starting voice.");
        CheckText(instructions); if (tools.Count > 32 || history.Count > 200) throw new InvalidOperationException("Voice session context is too large.");
        bool gemini = provider.RealtimeProtocol == "GeminiLive";
        Uri endpoint = ValidateEndpoint(provider.RealtimeUrl, provider.RealtimeModel, gemini);
        var enabledTools = provider.Tools ? tools : Array.Empty<AssistantTool>();
        Session s;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current != null) throw new InvalidOperationException("Stop the current voice session first.");
            s = new Session(socketFactory(), gemini, provider.Vision, enabledTools); current = s;
        }
        s.Registration = cancellationToken.Register(() => End(s));
        try
        {
            Emit(s, () => Status?.Invoke("Connecting voice…"));
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(s.Cancel.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(20));
            await s.Socket.Connect(endpoint, apiKey, gemini, connect.Token).ConfigureAwait(false);
            await SendBatch(s, new[] { Setup(provider, instructions, enabledTools) }, s.Cancel.Token).ConfigureAwait(false);
            s.Reader = ReceiveLoop(s);
            await s.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20), s.Cancel.Token).ConfigureAwait(false);
            // Seed only bounded text history. Images are sent exclusively by explicit SendImage calls.
            var seed = history.Where(m => m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Text)).TakeLast(80).ToArray();
            foreach (var m in seed) CheckText(m.Text);
            if (seed.Sum(m => (long)m.Text.Length) > 512 * 1024) throw new InvalidOperationException("Voice history is too large.");
            if (gemini && seed.Length > 0)
                await SendBatch(s, new[] { JsonSerializer.Serialize(new { clientContent = new { turns = seed.Select(m => new { role = m.Role == "assistant" ? "model" : "user", parts = new[] { new { text = m.Text } } }), turnComplete = false } }) }, s.Cancel.Token).ConfigureAwait(false);
            else if (!gemini)
                foreach (var m in seed) await SendBatch(s, new[] { TextItem(m.Role, m.Text) }, s.Cancel.Token).ConfigureAwait(false);
            var audio = audioFactory();
            lock (gate)
            {
                if (!Active(s)) { audio.Dispose(); throw new OperationCanceledException(); }
                s.Audio = audio;
            }
            s.Sender = CaptureLoop(s);
            audio.Start(gemini ? 16000 : 24000, bytes => Captured(s, bytes), (value, output) => Emit(s, () => Level?.Invoke(value, output)), () => Fail(s, "Audio device unavailable. Start voice again after checking Windows audio devices."));
            lock (gate) { if (!Active(s)) throw new OperationCanceledException(); s.Connected = true; }
            Emit(s, () => Status?.Invoke("Voice connected"));
        }
        catch (OperationCanceledException) { End(s); throw; }
        catch { Fail(s, "Voice connection failed. Check the endpoint, model, voice and API key, then start again."); throw new InvalidOperationException("Voice connection failed. Check the endpoint, model, voice and API key, then start again."); }
    }

    public Task SendText(string text, CancellationToken cancellationToken)
    {
        CheckText(text); var s = GetSession();
        cancellationToken.ThrowIfCancellationRequested();
        var events = Interrupt(s);
        if (s.Gemini) events.Add(JsonSerializer.Serialize(new { clientContent = new { turns = new[] { new { role = "user", parts = new[] { new { text } } } }, turnComplete = true } }));
        else { events.Add(TextItem("user", text)); events.Add("{\"type\":\"response.create\"}"); }
        return SendBatch(s, events, cancellationToken);
    }
    public Task SendImage(string dataUrl, CancellationToken cancellationToken)
    {
        var s = GetSession(); if (!s.Vision) throw new InvalidOperationException("Enable vision for this voice provider before sharing images.");
        int comma = dataUrl?.IndexOf(',') ?? -1;
        if (dataUrl == null || dataUrl.Length > 3 * 1024 * 1024 || comma < 0 || !(dataUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) || dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)))
            throw new InvalidOperationException("Supply a bounded JPEG or PNG image data URL.");
        try { _ = Convert.FromBase64String(dataUrl[(comma + 1)..]); } catch { throw new InvalidOperationException("The shared image is invalid."); }
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (s.LastImage != 0 && now - s.LastImage < 1000) throw new InvalidOperationException("Share at most one image per second.");
            s.LastImage = now;
        }
        return SendBatch(s, new[] { s.Gemini ? JsonSerializer.Serialize(new { realtimeInput = new { video = new { mimeType = dataUrl[5..dataUrl.IndexOf(';')], data = dataUrl[(comma + 1)..] } } })
            : JsonSerializer.Serialize(new { type = "conversation.item.create", item = new { type = "message", role = "user", content = new[] { new { type = "input_image", image_url = dataUrl } } } }) }, cancellationToken);
    }
    public Task SendToolResult(AssistantCall call, string result, CancellationToken cancellationToken)
    {
        CheckText(result); var s = GetSession();
        bool resume;
        lock (gate)
        {
            if (!s.Pending.TryGetValue(call.Id, out var expected) || expected != call) throw new InvalidOperationException("This voice tool request is no longer pending.");
            s.Pending.Remove(call.Id);
            resume = !s.ResponseActive && s.Pending.Count == 0;
        }
        return SendBatch(s, s.Gemini ? new[] { JsonSerializer.Serialize(new { toolResponse = new { functionResponses = new[] { new { id = call.Id, name = call.Name, response = new { result } } } } }) }
            : resume ? new[] { JsonSerializer.Serialize(new { type = "conversation.item.create", item = new { type = "function_call_output", call_id = call.Id, output = result } }), "{\"type\":\"response.create\"}" }
            : new[] { JsonSerializer.Serialize(new { type = "conversation.item.create", item = new { type = "function_call_output", call_id = call.Id, output = result } }) }, cancellationToken);
    }
    // Hardware and callbacks stop before returning the Task, even when callers do not await it.
    public Task Stop()
    {
        Session? s; lock (gate) s = current;
        if (s == null) return Task.CompletedTask;
        End(s); return Drain(s);
    }
    public ValueTask DisposeAsync()
    {
        lock (gate) disposed = true;
        return new ValueTask(Stop());
    }
    private static async Task Drain(Session s)
    { try { await Task.WhenAll(s.Reader, s.Sender).ConfigureAwait(false); } catch { /* Loops already sanitize and stop. */ } }
    private void End(Session s)
    {
        IAssistantVoiceAudio? audio;
        bool owner;
        lock (gate)
        {
            owner = !s.Ended;
            if (owner)
            {
                s.Ended = true; s.Connected = false;
                s.StopThread = Environment.CurrentManagedThreadId;
                audio = s.Audio; s.Audio = null; s.Pending.Clear();
            }
            else audio = null;
        }
        if (!owner) { if (s.StopThread != Environment.CurrentManagedThreadId) s.HardwareStopped.Wait(); return; }
        s.Registration.Unregister();
        try { audio?.Dispose(); } catch { }
        s.Input.Writer.TryComplete();
        try { s.Cancel.Cancel(); } catch { }
        s.Ready.TrySetCanceled();
        try { s.Socket.Abort(); } catch { }
        try { s.Socket.Dispose(); } catch { }
        lock (gate) if (ReferenceEquals(current, s)) current = null;
        s.HardwareStopped.Set();
    }
    private void Fail(Session s, string message) { Emit(s, () => Status?.Invoke(message)); End(s); }
    private bool Active(Session s) => ReferenceEquals(current, s) && !s.Ended;
    private Session GetSession() { lock (gate) return current is { Connected: true } s ? s : throw new InvalidOperationException("Start voice first."); }
    private void Emit(Session s, Action action)
    { lock (gate) { if (Active(s)) { try { action(); } catch { /* UI subscribers must not escape native or receive threads. */ } } } }
    private static void CheckText(string text)
    { if (text == null || text.Length > 128 * 1024) throw new InvalidOperationException("Voice text is too large."); }
    private void Captured(Session s, byte[] bytes)
    {
        lock (gate)
        {
            if (!Active(s)) return;
            if (bytes.Length == 0 || bytes.Length > 9600 || (bytes.Length & 1) != 0) return;
            if (s.Input.Writer.TryWrite(bytes)) return;
        }
        ThreadPool.QueueUserWorkItem(_ => Fail(s, "Voice upload is too slow. Start voice again after checking the connection."));
    }
    private async Task CaptureLoop(Session s)
    {
        try
        {
            await foreach (var pcm in s.Input.Reader.ReadAllAsync(s.Cancel.Token).ConfigureAwait(false))
            {
                Emit(s, () => Level?.Invoke(AssistantVoiceAudio.Peak(pcm, pcm.Length), false));
                string data = Convert.ToBase64String(pcm);
                await SendBatch(s, new[] { s.Gemini ? JsonSerializer.Serialize(new { realtimeInput = new { audio = new { data, mimeType = "audio/pcm;rate=16000" } } })
                    : JsonSerializer.Serialize(new { type = "input_audio_buffer.append", audio = data }) }, s.Cancel.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds((double)pcm.Length / (s.Gemini ? 32000 : 48000)), s.Cancel.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { } catch { Fail(s, "Voice upload failed. Start voice again to reconnect."); }
    }
    private async Task SendBatch(Session s, IEnumerable<string> payloads, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, s.Cancel.Token);
        bool held = false;
        try
        {
            await s.SendLock.WaitAsync(linked.Token).ConfigureAwait(false); held = true;
            foreach (string payload in payloads)
            {
                linked.Token.ThrowIfCancellationRequested();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await s.Socket.Send(payload, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
        catch { Fail(s, "Voice upload failed. Start voice again to reconnect."); throw new InvalidOperationException("Voice upload failed. Start voice again to reconnect."); }
        finally { if (held) s.SendLock.Release(); }
    }
    private async Task ReceiveLoop(Session s)
    {
        try
        {
            while (!s.Cancel.IsCancellationRequested)
            {
                string? json = await s.Socket.Receive(s.Cancel.Token).ConfigureAwait(false);
                if (json == null) { Fail(s, "Voice disconnected. Start voice again to reconnect."); return; }
                if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException();
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 40 });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                if (s.Gemini) HandleGemini(s, doc.RootElement);
                else await HandleOpenAI(s, doc.RootElement).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { } catch { Fail(s, "Voice received an invalid response or disconnected. Start voice again to reconnect."); }
    }
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    private void Audio(Session s, string encoded, string item = "", int index = 0)
    {
        if (encoded.Length > 512 * 1024) throw new InvalidDataException();
        byte[] pcm = Convert.FromBase64String(encoded); if ((pcm.Length & 1) != 0) throw new InvalidDataException();
        lock (gate)
        {
            if (!Active(s) || s.IgnoreAudio || s.Audio == null) return;
            if (item != "" && s.AudioItem != item) { s.AudioItem = item; s.AudioIndex = index; s.AudioStart = s.OutputBytes / 48; }
            if (!s.Audio.Append(pcm)) throw new InvalidDataException();
            s.OutputBytes = checked(s.OutputBytes + pcm.Length);
        }
    }
    private async Task HandleOpenAI(Session s, JsonElement e)
    {
        string type = Str(e, "type");
        switch (type)
        {
            case "session.updated": s.Ready.TrySetResult(); break;
            case "response.created": lock (gate) { s.ResponseActive = true; s.IgnoreAudio = false; } break;
            case "response.done":
                lock (gate) s.ResponseActive = false;
                if (e.TryGetProperty("response", out var response) && Str(response, "status") == "failed") throw new InvalidDataException();
                AssistantCall[] ready;
                lock (gate) { ready = s.ReadyCalls.ToArray(); s.ReadyCalls.Clear(); }
                if (e.TryGetProperty("response", out var finished) && Str(finished, "status") == "completed")
                    foreach (var call in ready) Emit(s, () => ToolCall?.Invoke(call));
                else lock (gate) foreach (var call in ready) s.Pending.Remove(call.Id);
                break;
            case "response.output_audio.delta":
                Audio(s, Str(e, "delta"), Str(e, "item_id"), e.TryGetProperty("content_index", out var ix) ? ix.GetInt32() : 0); break;
            case "response.output_audio_transcript.done": if (!s.IgnoreAudio) FinalText(s, "assistant", Str(e, "transcript")); break;
            case "response.output_text.done": if (!s.IgnoreAudio) FinalText(s, "assistant", Str(e, "text")); break;
            case "conversation.item.input_audio_transcription.completed": FinalText(s, "user", Str(e, "transcript")); break;
            case "conversation.item.input_audio_transcription.failed": Emit(s, () => Status?.Invoke("Voice transcription unavailable for this turn.")); break;
            case "response.function_call_arguments.done": Call(s, Str(e, "call_id"), Str(e, "name"), Str(e, "arguments")); break;
            case "input_audio_buffer.speech_started":
                var events = Interrupt(s);
                if (events.Count > 0) await SendBatch(s, events, s.Cancel.Token).ConfigureAwait(false);
                break;
            case "error": throw new InvalidDataException();
        }
    }
    private List<string> Interrupt(Session s)
    {
        var events = new List<string>();
        lock (gate)
        {
            int played = Math.Max(0, (s.Audio?.PlayedMilliseconds ?? 0) - s.AudioStart);
            if (!s.Gemini && s.ResponseActive) events.Add("{\"type\":\"response.cancel\"}");
            if (!s.Gemini && s.AudioItem != "") events.Add(JsonSerializer.Serialize(new { type = "conversation.item.truncate", item_id = s.AudioItem, content_index = s.AudioIndex, audio_end_ms = played }));
            s.Audio?.Clear(); s.AudioItem = ""; s.AudioStart = 0; s.OutputBytes = 0; s.IgnoreAudio = !s.Gemini;
            s.AssistantText.Clear(); s.Pending.Clear(); s.ReadyCalls.Clear(); s.ResponseActive = false;
        }
        return events;
    }
    private void HandleGemini(Session s, JsonElement e)
    {
        if (e.TryGetProperty("error", out _)) throw new InvalidDataException();
        if (e.TryGetProperty("setupComplete", out _)) s.Ready.TrySetResult();
        if (e.TryGetProperty("goAway", out _)) { Fail(s, "Voice session is ending. Start voice again to reconnect."); return; }
        if (e.TryGetProperty("toolCallCancellation", out var cancelled) && cancelled.TryGetProperty("ids", out var ids))
            lock (gate) foreach (var id in ids.EnumerateArray()) s.Pending.Remove(id.GetString() ?? "");
        if (e.TryGetProperty("serverContent", out var content))
        {
            if (Flag(content, "interrupted")) lock (gate) { s.Audio?.Clear(); s.OutputBytes = 0; s.AssistantText.Clear(); }
            if (content.TryGetProperty("inputTranscription", out var input)) AppendText(s.UserText, Str(input, "text"));
            if (content.TryGetProperty("outputTranscription", out var output)) AppendText(s.AssistantText, Str(output, "text"));
            if (content.TryGetProperty("modelTurn", out var turn) && turn.TryGetProperty("parts", out var parts))
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("inlineData", out var data))
                    {
                        if (Str(data, "mimeType") != "audio/pcm;rate=24000") throw new InvalidDataException();
                        Audio(s, Str(data, "data"));
                    }
            if (Flag(content, "turnComplete")) { Flush(s, "user", s.UserText); Flush(s, "assistant", s.AssistantText); }
        }
        if (e.TryGetProperty("toolCall", out var calls) && calls.TryGetProperty("functionCalls", out var functions))
        {
            Flush(s, "user", s.UserText);
            foreach (var call in functions.EnumerateArray())
                Call(s, Str(call, "id"), Str(call, "name"), call.TryGetProperty("args", out var args) ? args.GetRawText() : "{}");
        }
    }
    private static void AppendText(StringBuilder builder, string text)
    { if (builder.Length + text.Length > 128 * 1024) throw new InvalidDataException(); builder.Append(text); }
    private void Flush(Session s, string role, StringBuilder builder) { string text = builder.ToString(); builder.Clear(); FinalText(s, role, text); }
    private void FinalText(Session s, string role, string text) { CheckText(text); if (!string.IsNullOrWhiteSpace(text)) Emit(s, () => Transcript?.Invoke(role, text)); }
    private void Call(Session s, string id, string name, string arguments)
    {
        if (id.Length is 0 or > 200 || name.Length is 0 or > 100 || arguments.Length > 128 * 1024) throw new InvalidDataException();
        using var args = JsonDocument.Parse(arguments); if (args.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var call = new AssistantCall(id, name, arguments);
        lock (gate)
        {
            if (!Active(s)) return;
            if (!s.ToolNames.Contains(name)) throw new InvalidDataException();
            if (!s.SeenCalls.Add(id)) return;
            if (s.SeenCalls.Count > 256 || s.Pending.Count >= 32) throw new InvalidDataException();
            s.Pending.Add(id, call);
            if (!s.Gemini) { s.ReadyCalls.Enqueue(call); return; }
        }
        Emit(s, () => ToolCall?.Invoke(call));
    }
    internal static Uri ValidateEndpoint(string value, string model, bool gemini)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
            throw new InvalidOperationException("Configure a secure WSS voice endpoint or loopback WS endpoint.");
        var check = new UriBuilder(uri) { Scheme = uri.Scheme == "wss" ? "https" : "http", Query = "" };
        AssistantTransport.ValidateEndpoint(check.Uri.AbsoluteUri);
        if (uri.Query.Length != 0 && (gemini || uri.Query != "?model=" + Uri.EscapeDataString(model)))
            throw new InvalidOperationException("Voice endpoints must not contain API keys or other query parameters.");
        var result = new UriBuilder(uri) { Query = gemini ? "" : "model=" + Uri.EscapeDataString(model) };
        return result.Uri;
    }
    private static string TextItem(string role, string text) => JsonSerializer.Serialize(new { type = "conversation.item.create", item = new { type = "message", role,
        content = new[] { new { type = role == "assistant" ? "output_text" : "input_text", text } } } });
    private static string Setup(AssistantProvider provider, string instructions, IReadOnlyList<AssistantTool> tools)
    {
        if (provider.RealtimeProtocol == "GeminiLive")
            return JsonSerializer.Serialize(new { setup = new { model = provider.RealtimeModel.StartsWith("models/", StringComparison.Ordinal) ? provider.RealtimeModel : "models/" + provider.RealtimeModel,
                generationConfig = new { responseModalities = new[] { "AUDIO" }, speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = provider.Voice } } } },
                systemInstruction = new { parts = new[] { new { text = instructions } } }, inputAudioTranscription = new { }, outputAudioTranscription = new { },
                realtimeInputConfig = new { automaticActivityDetection = new { disabled = false }, activityHandling = "START_OF_ACTIVITY_INTERRUPTS" },
                tools = tools.Count == 0 ? Array.Empty<object>() : new object[] { new { functionDeclarations = tools.Select(t => new { name = t.Name, description = t.Description, parametersJsonSchema = t.Parameters }) } } } });
        return JsonSerializer.Serialize(new { type = "session.update", session = new { type = "realtime", model = provider.RealtimeModel, instructions,
            output_modalities = new[] { "audio" }, audio = new { input = new { format = new { type = "audio/pcm", rate = 24000 },
                transcription = new { model = "whisper-1" }, turn_detection = new { type = "server_vad", threshold = 0.5, prefix_padding_ms = 300, silence_duration_ms = 500, create_response = true, interrupt_response = false } },
                output = new { format = new { type = "audio/pcm", rate = 24000 }, voice = provider.Voice } },
            tools = tools.Select(t => new { type = "function", name = t.Name, description = t.Description, parameters = t.Parameters }) } });
    }
}

internal sealed class AssistantVoiceSocket : IAssistantVoiceSocket
{
    private readonly ClientWebSocket socket = new();
    private readonly HttpMessageInvoker invoker;
    internal AssistantVoiceSocket()
    {
        invoker = new HttpMessageInvoker(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15), MaxResponseHeadersLength = 32, ConnectCallback = async (context, token) =>
            {
                string host = context.DnsEndPoint.Host;
                bool local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);
                foreach (var address in await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false))
                {
                    if (local ? !IPAddress.IsLoopback(address) : !AssistantTransport.IsPublicAddress(address)) continue;
                    var tcp = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await tcp.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false); return new NetworkStream(tcp, true); }
                    catch (OperationCanceledException) { tcp.Dispose(); throw; } catch (SocketException) { tcp.Dispose(); }
                }
                throw new HttpRequestException("Voice destination unavailable.");
            } }, true);
    }
    public async Task Connect(Uri endpoint, string? key, bool gemini, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(key)) socket.Options.SetRequestHeader(gemini ? "x-goog-api-key" : "Authorization", gemini ? key : "Bearer " + key);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(endpoint, invoker, token).ConfigureAwait(false);
    }
    public Task Send(string json, CancellationToken token) => socket.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, token).AsTask();
    public async Task<string?> Receive(CancellationToken token)
    {
        using var data = new MemoryStream(); var buffer = new byte[16384];
        while (true)
        {
            var message = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (message.MessageType == WebSocketMessageType.Close) return null;
            if (message.MessageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary) || data.Length + message.Count > 4 * 1024 * 1024) throw new InvalidDataException();
            data.Write(buffer, 0, message.Count);
            if (message.EndOfMessage) return new UTF8Encoding(false, true).GetString(data.GetBuffer(), 0, (int)data.Length);
        }
    }
    public void Abort() => socket.Abort();
    public void Dispose() { socket.Dispose(); invoker.Dispose(); }
}
