using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NabyScape;

internal static class AssistantVoiceTests
{
    private sealed class Socket : IAssistantVoiceSocket
    {
        internal readonly Channel<string?> Incoming = Channel.CreateUnbounded<string?>();
        internal readonly List<string> Sent = new();
        internal bool Ack = true, Aborted, Disposed, HoldConnect, HoldSend, ThrowSend;
        internal int SendDelayMs, EnteredSends;
        internal TaskCompletionSource? SendGate;
        internal Uri? Endpoint;
        internal string? Key;
        internal bool Gemini;
        public async Task Connect(Uri endpoint, string? key, bool gemini, CancellationToken token)
        { Endpoint = endpoint; Key = key; Gemini = gemini; if (HoldConnect) await Task.Delay(Timeout.Infinite, token); }
        public async Task Send(string json, CancellationToken token)
        {
            if (ThrowSend) throw new System.IO.IOException("fixture upload failure");
            if (Aborted) throw new InvalidOperationException("The fixture socket has been aborted.");
            // Model native ManagedWebSocket: cancellation of an in-flight frame
            // aborts the whole socket, not just this individual write operation.
            using var abortOnCancellation = token.Register(Abort);
            Interlocked.Increment(ref EnteredSends);
            if (HoldSend) await Task.Delay(Timeout.Infinite, token);
            if (SendGate != null) await SendGate.Task.WaitAsync(token);
            if (SendDelayMs > 0) await Task.Delay(SendDelayMs, token);
            token.ThrowIfCancellationRequested(); lock (Sent) Sent.Add(json);
            if (Ack && (json.Contains("\"session.update\"") || json.Contains("\"setup\""))) Push(Gemini ? "{\"setupComplete\":{}}" : "{\"type\":\"session.updated\"}");
        }
        public async Task<string?> Receive(CancellationToken token) => await Incoming.Reader.ReadAsync(token);
        internal void Push(string? json) => Incoming.Writer.TryWrite(json);
        internal string[] Payloads() { lock (Sent) return Sent.ToArray(); }
        public void Abort() { Aborted = true; Incoming.Writer.TryWrite(null); }
        public void Dispose() => Disposed = true;
    }
    private sealed class NoTransport : IAssistantTransport
    {
        public Task<AssistantReply> Complete(AssistantProvider p,string? key,IReadOnlyList<AssistantMessage> messages,IReadOnlyList<AssistantTool> tools,CancellationToken token)
            => throw new Exception("Local outcome fixtures never request a provider.");
    }
    private sealed class Audio : IAssistantVoiceAudio
    {
        internal bool Started, Disposed, Overflow;
        internal int Rate, Cleared, Added;
        internal Action<byte[]>? Capture;
        internal Action<float, bool>? Meter;
        public int PlayedMilliseconds => 240;
        public void Start(int inputRate, Action<byte[]> capture, Action<float, bool> level, Action failure)
        { if (Disposed) throw new ObjectDisposedException(nameof(Audio)); Rate = inputRate; Capture = capture; Meter = level; Started = true; }
        public bool Append(byte[] pcm) { Added += pcm.Length; return !Overflow; }
        public void Clear() => Cleared++;
        public void Dispose() => Disposed = true;
    }
    private static AssistantProvider Provider(string protocol = "OpenAIRealtime") => new() { RealtimeProtocol = protocol, RealtimeUrl = "wss://provider.example/realtime", RealtimeModel = "configured-model", Voice = "configured-voice", Tools = true, Vision = true };
    private static readonly AssistantTool Tool = new("inspect_app", "Inspect selected app", JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone());
    private static void Assert(bool condition) { if (!condition) throw new Exception("Voice fixture failed."); }
    private static async Task Until(Func<bool> predicate)
    { using var timeout = new CancellationTokenSource(3000); while (!predicate()) await Task.Delay(5, timeout.Token); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected voice rejection: " + typeof(T).Name); }
    private static Task Start(AssistantVoiceSession voice, AssistantProvider? provider = null, CancellationToken token = default) => voice.Start(provider ?? Provider(), "fixture-key", "Speak Indonesian", new[] { Tool }, new[] { new AssistantMessage("user", "hello"), new AssistantMessage("assistant", "hi") }, token);
    private static async Task<AssistantCall> ReceiveCall(AssistantVoiceSession voice, Socket socket, string id)
    {
        var ready = new TaskCompletionSource<AssistantCall>(TaskCreationOptions.RunContinuationsAsynchronously);
        void GotCall(AssistantCall call) { if (call.Id == id) ready.TrySetResult(call); }
        voice.ToolCall += GotCall;
        try
        {
            socket.Push(socket.Gemini ? "{\"toolCall\":{\"functionCalls\":[{\"id\":\"" + id + "\",\"name\":\"inspect_app\",\"args\":{}}]}}"
                : "{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"" + id + "\",\"name\":\"inspect_app\",\"arguments\":\"{}\"}");
            if (!socket.Gemini) socket.Push("{\"type\":\"response.done\",\"response\":{\"status\":\"completed\"}}");
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { voice.ToolCall -= GotCall; }
    }
    internal static void Run(Action<string, Action> test)
    {
        void RunAsync(string name, Func<Task> action) => test(name, () => action().GetAwaiter().GetResult());
        test("Voice endpoints reject remote WS credentials private IP and API key queries", () =>
        {
            foreach (string endpoint in new[] { "http://provider.example", "ws://provider.example/", "wss://u:p@provider.example/", "wss://192.168.1.2/", "wss://provider.example/?key=secret", "wss://provider.example/#secret", "wss://provider.example/?model=other", "wss://provider.example/?model=configured-model&key=secret" })
                Reject<InvalidOperationException>(() => AssistantVoiceSession.ValidateEndpoint(endpoint, "configured-model", false));
            Assert(AssistantVoiceSession.ValidateEndpoint("ws://127.0.0.1:1234/realtime", "configured-model", false).Query == "?model=configured-model");
            Assert(AssistantVoiceSession.ValidateEndpoint("wss://provider.example/realtime?model=configured-model", "configured-model", false).AbsolutePath == "/realtime");
            Assert(AssistantVoiceSession.Protocols.SequenceEqual(new[] { "OpenAIRealtime", "GeminiLive" }));
        });
        RunAsync("Voice requires configured supported protocol model and voice", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            foreach (var p in new[] { new AssistantProvider(), Provider("Anthropic"), new AssistantProvider { RealtimeUrl = "wss://provider.example/", RealtimeModel = "fixture", Voice = "" } })
                Reject<InvalidOperationException>(() => Start(voice, p).GetAwaiter().GetResult());
            Assert(socket.Endpoint == null && !audio.Started);
        });
        RunAsync("Gemini startup rejection preserves safe failure instead of reporting cancellation", async () =>
        {
            foreach (int code in new[] { 400, 401, 403, 404, 429, 500 })
            {
                var socket = new Socket { Ack = false }; var audio = new Audio();
                await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                Task starting = Start(voice, Provider("GeminiLive"));
                await Until(() => socket.Payloads().Length == 1);
                socket.Push(JsonSerializer.Serialize(new { error = new { code, message = "secret-provider-key-and-user-input" } }));
                try { await starting; throw new Exception("Expected rejection"); }
                catch (InvalidOperationException ex)
                {
                    if (!ex.Message.StartsWith("Gemini") || ex.Message.Contains("secret-provider")) throw new Exception("Rejection " + code + ": " + ex.Message);
                    if (code == 429) Assert(ex.Message.Contains("quota"));
                    if (code == 404) Assert(ex.Message.Contains("model not found"));
                }
                await Until(() => socket.Disposed);
                Assert(!audio.Started && !voice.Connected);
            }
        });
        RunAsync("Voice closed before setup is a connection error rather than user stop", async () =>
        {
            var socket = new Socket { Ack = false }; var audio = new Audio();
            await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            Task starting = Start(voice, Provider("GeminiLive"));
            await Until(() => socket.Payloads().Length == 1); socket.Push(null);
            try { await starting; throw new Exception("Expected disconnect"); }
            catch (InvalidOperationException ex) { Assert(ex.Message.Contains("disconnected")); }
            Assert(!audio.Started && !voice.Connected && socket.Disposed);
        });
        foreach (string protocol in AssistantVoiceSession.Protocols)
        {
            RunAsync("Voice " + protocol + " setup rates history safe auth text image and correlated tools", async () =>
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                int userEcho = 0; voice.Transcript += (role, _) => { if (role == "user") Interlocked.Increment(ref userEcho); };
                await Start(voice, Provider(protocol));
                Assert(voice.Connected && audio.Started && audio.Rate == (protocol == "GeminiLive" ? 16000 : 24000));
                Assert(socket.Key == "fixture-key" && !socket.Endpoint!.AbsoluteUri.Contains("fixture-key"));
                string setup = socket.Payloads()[0]; Assert(setup.Contains("configured-model") && setup.Contains("configured-voice") && setup.Contains("inspect_app") && setup.Contains("Speak Indonesian"));
                await voice.SendText("typed input", default); Assert(userEcho == 0);
                if (protocol == "OpenAIRealtime") socket.Push("{\"type\":\"response.created\"}");
                socket.Push(protocol == "GeminiLive" ? "{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AAAAAA==\"}}]}}}" : "{\"type\":\"response.output_audio.delta\",\"delta\":\"AAAAAA==\"}");
                await Until(() => audio.Added == 4);
                await voice.SendImage("data:image/jpeg;base64,AQID", default);
                var got = new TaskCompletionSource<AssistantCall>(TaskCreationOptions.RunContinuationsAsynchronously);
                voice.ToolCall += call => got.TrySetResult(call);
                socket.Push(protocol == "GeminiLive" ? "{\"toolCall\":{\"functionCalls\":[{\"id\":\"call-1\",\"name\":\"inspect_app\",\"args\":{}}]}}" : "{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"call-1\",\"name\":\"inspect_app\",\"arguments\":\"{}\"}");
                if (protocol == "OpenAIRealtime") socket.Push("{\"type\":\"response.done\",\"response\":{\"status\":\"completed\"}}");
                var call = await got.Task.WaitAsync(TimeSpan.FromSeconds(3)); await voice.SendToolResult(call, "approved result", default);
                string result = string.Join("\n", socket.Payloads()); Assert(result.Contains("call-1") && result.Contains("approved result") && result.Contains(protocol == "GeminiLive" ? "functionResponses" : "function_call_output"));
                Reject<InvalidOperationException>(() => voice.SendToolResult(call, "duplicate", default).GetAwaiter().GetResult());
                audio.Capture!(new byte[protocol == "GeminiLive" ? 1600 : 2400]);
                await Until(() => socket.Payloads().Any(p => p.Contains(protocol == "GeminiLive" ? "audio/pcm;rate=16000" : "input_audio_buffer.append")));
                Task stop = voice.Stop(); Assert(audio.Disposed && socket.Aborted && socket.Disposed && !voice.Connected); await stop;
            });
            RunAsync("Voice " + protocol + " waits for setup and cancellation opens no hardware", async () =>
            {
                var socket = new Socket { Ack = false }; var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                using var cancellation = new CancellationTokenSource(); Task starting = Start(voice, Provider(protocol), cancellation.Token);
                await Until(() => socket.Payloads().Length == 1); Assert(!audio.Started && !voice.Connected);
                cancellation.Cancel(); try { await starting; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
                Assert(!audio.Started && socket.Aborted && socket.Disposed); await voice.Stop();
            });
        }
        RunAsync("Voice OpenAI final transcripts real levels and barge in cancel truncate playback", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            var transcripts = new List<string>(); int levels = 0;
            voice.Transcript += (role, text) => { lock (transcripts) transcripts.Add(role + ":" + text); };
            voice.Level += (_, _) => Interlocked.Increment(ref levels);
            await Start(voice); audio.Meter!(0.5f, false); audio.Meter(0.8f, true);
            socket.Push("{\"type\":\"response.created\"}");
            socket.Push("{\"type\":\"response.output_audio.delta\",\"item_id\":\"audio-1\",\"content_index\":0,\"delta\":\"AAAAAA==\"}");
            socket.Push("{\"type\":\"response.output_audio_transcript.done\",\"transcript\":\"hi\"}");
            socket.Push("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"transcript\":\"hello\"}");
            socket.Push("{\"type\":\"input_audio_buffer.speech_started\"}");
            await Until(() => socket.Payloads().Any(p => p.Contains("conversation.item.truncate")));
            Assert(audio.Cleared == 1 && audio.Added == 4 && levels == 2);
            Assert(socket.Payloads().Any(p => p.Contains("response.cancel")));
            lock (transcripts) Assert(transcripts.SequenceEqual(new[] { "assistant:hi", "user:hello" }));
            await voice.Stop(); int before = levels; audio.Meter(1f, true); audio.Capture!(new byte[2400]); Assert(levels == before);
        });
        RunAsync("Voice Gemini combines transcript fragments until turn end and clears interruption", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); voice.Transcript += (role, text) => { if (role == "assistant") result.TrySetResult(text); };
            await Start(voice, Provider("GeminiLive"));
            socket.Push("{\"serverContent\":{\"outputTranscription\":{\"text\":\"hel\"}}}");
            socket.Push("{\"serverContent\":{\"outputTranscription\":{\"text\":\"lo\"},\"turnComplete\":true}}");
            Assert(await result.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "hello");
            socket.Push("{\"serverContent\":{\"interrupted\":true}}"); await Until(() => audio.Cleared == 1); await voice.Stop();
        });
        RunAsync("Voice malformed messages sanitized provider errors and closed sockets stop hardware", async () =>
        {
            foreach (string? wire in new string?[] { "{broken", "[]", "{\"type\":\"error\",\"error\":{\"message\":\"secret fixture-key provider body\"}}", "{\"type\":\"response.output_audio.delta\",\"delta\":\"not-base64\"}", null })
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                string status = ""; voice.Status += value => status = value; await Start(voice); socket.Push(wire);
                await Until(() => audio.Disposed); Assert(!voice.Connected && !status.Contains("fixture-key") && !status.Contains("provider body"));
            }
        });
        RunAsync("Voice output overflow truncates one reply and keeps the session usable", async () =>
        {
            foreach (string protocol in AssistantVoiceSession.Protocols)
            {
                var socket = new Socket(); var audio = new Audio { Overflow = true }; await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                string status = ""; voice.Status += text => status = text;
                await Start(voice, Provider(protocol));
                if (protocol == "OpenAIRealtime") socket.Push("{\"type\":\"response.created\"}");
                socket.Push(protocol == "GeminiLive" ? "{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AAAAAA==\"}}]}}}" : "{\"type\":\"response.output_audio.delta\",\"item_id\":\"audio-1\",\"delta\":\"AAAAAA==\"}");
                await Until(() => status.Contains("playback limit")); Assert(voice.Connected && !audio.Disposed && audio.Added == 4 && audio.Cleared == 1);
                if (protocol == "OpenAIRealtime") await Until(() => socket.Payloads().Any(p => p.Contains("conversation.item.truncate")));
                audio.Overflow = false;
                socket.Push(protocol == "GeminiLive" ? "{\"serverContent\":{\"turnComplete\":true}}" : "{\"type\":\"response.created\"}");
                socket.Push(protocol == "GeminiLive" ? "{\"serverContent\":{\"modelTurn\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/pcm;rate=24000\",\"data\":\"AAAAAA==\"}}]}}}" : "{\"type\":\"response.output_audio.delta\",\"delta\":\"AAAAAA==\"}");
                await Until(() => audio.Added == 8); Assert(voice.Connected); await voice.Stop();
            }
        });
        RunAsync("Voice stop cancels connection before microphone creation", async () =>
        {
            var socket = new Socket { HoldConnect = true }; var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            Task starting = Start(voice); await Until(() => socket.Endpoint != null); await voice.Stop();
            try { await starting; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Assert(!audio.Started && socket.Disposed);
        });
        RunAsync("Voice microphone queue bounds stalled upload and suppresses callbacks after stop", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            await Start(voice); socket.HoldSend = true;
            for (int i = 0; i < 15; i++) audio.Capture!(new byte[2400]);
            await Until(() => audio.Disposed); Assert(!voice.Connected && socket.Aborted);
        });
        RunAsync("Voice native capture cadence stays healthy with 10ms network send latency", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            await Start(voice); socket.SendDelayMs = 10;
            // The old send-plus-50ms delay accumulates more than ten queued frames.
            for (int frame = 0; frame < 72; frame++)
            {
                audio.Capture!(new byte[2400]); await Task.Delay(50); Assert(voice.Connected);
            }
            await Until(() => socket.Payloads().Count(p => p.Contains("input_audio_buffer.append")) == 72);
            Assert(!audio.Disposed); await voice.Stop();
        });
        RunAsync("Voice rejects unadvertised tool calls without executing or emitting them", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            int calls = 0; voice.ToolCall += _ => calls++;
            await Start(voice); socket.Push("{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"bad\",\"name\":\"unadvertised\",\"arguments\":\"{}\"}");
            await Until(() => audio.Disposed); Assert(calls == 0);
        });
        RunAsync("Voice OpenAI waits for all correlated tool results before one continuation", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            var calls = new List<AssistantCall>(); voice.ToolCall += call => { lock (calls) calls.Add(call); };
            await Start(voice); socket.Push("{\"type\":\"response.created\"}");
            foreach (var id in new[] { "call-a", "call-b", "call-b" }) socket.Push("{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"" + id + "\",\"name\":\"inspect_app\",\"arguments\":\"{}\"}");
            socket.Push("{\"type\":\"response.done\",\"response\":{\"status\":\"completed\"}}");
            await Until(() => { lock (calls) return calls.Count == 2; });
            socket.SendDelayMs = 10;
            await Task.WhenAll(voice.SendToolResult(calls[0], "first", default), voice.SendToolResult(calls[1], "second", default));
            var sent = socket.Payloads().Where(p => p.Contains("function_call_output") || p.Contains("response.create")).ToArray();
            Assert(sent.Length == 3 && sent[0].Contains("call-a") && sent[1].Contains("call-b") && sent[2].Contains("response.create")); await voice.Stop();
        });
        RunAsync("Voice result cancellation retries only unsent frames and finishes started frames without aborting", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            await Start(voice); var a = await ReceiveCall(voice, socket, "result-a"); var b = await ReceiveCall(voice, socket, "result-b");
            var sendingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); socket.SendGate = sendingGate;
            int entered = socket.EnteredSends;
            Task first = voice.SendToolResult(a, "first", default); await Until(() => socket.EnteredSends > entered);
            using var canceled = new CancellationTokenSource();
            Task second = voice.SendToolResult(b, "second", canceled.Token); canceled.Cancel();
            try { await second; throw new Exception("Expected pending-send cancellation"); } catch (OperationCanceledException) { }
            Assert(!voice.CallToken(b.Id).IsCancellationRequested);
            sendingGate.TrySetResult(); await first; socket.SendGate = null;
            Assert(!socket.Payloads().Any(p => p.Contains("response.create")));
            await voice.SendToolResult(b, "second", default);
            Assert(socket.Payloads().Count(p => p.Contains("function_call_output")) == 2 && socket.Payloads().Count(p => p.Contains("response.create")) == 1);
            var c = await ReceiveCall(voice, socket, "result-c");
            var inFlightGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); socket.SendGate = inFlightGate; entered = socket.EnteredSends;
            using var afterLock = new CancellationTokenSource(); Task third = voice.SendToolResult(c, "third", afterLock.Token);
            await Until(() => socket.EnteredSends > entered); afterLock.Cancel();
            Assert(!third.IsCompleted && !socket.Aborted && voice.Connected);
            inFlightGate.TrySetResult();
            try { await third; throw new Exception("Expected in-send cancellation"); } catch (OperationCanceledException) { }
            Assert(voice.CallToken(c.Id).IsCancellationRequested && socket.Payloads().Count(p => p.Contains("result-c")) == 1);
            Assert(!socket.Aborted && voice.Connected && socket.Payloads().Count(p => p.Contains("response.create")) == 1);
            Reject<InvalidOperationException>(() => voice.SendToolResult(c, "duplicate", default).GetAwaiter().GetResult());
            socket.SendGate = null; await voice.SendText("session remains usable", default);
            await voice.Stop();
        });
        foreach (string protocol in AssistantVoiceSession.Protocols)
            RunAsync("Voice " + protocol + " server cancellation finishes an in-flight result without socket abort or continuation", async () =>
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                await Start(voice, Provider(protocol)); var call = await ReceiveCall(voice, socket, "late-result"); CancellationToken callToken = voice.CallToken(call.Id);
                var sendingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); socket.SendGate = sendingGate;
                int entered = socket.EnteredSends; Task sending = voice.SendToolResult(call, "already started", default);
                await Until(() => socket.EnteredSends > entered);
                socket.Push(socket.Gemini ? "{\"toolCallCancellation\":{\"ids\":[\"late-result\"]}}" : "{\"type\":\"input_audio_buffer.speech_started\"}");
                await Until(() => callToken.IsCancellationRequested);
                Assert(!sending.IsCompleted && !socket.Aborted && voice.Connected);
                sendingGate.TrySetResult();
                try { await sending; throw new Exception("Expected canceled late result"); } catch (OperationCanceledException) { }
                Assert(socket.Payloads().Count(p => p.Contains("late-result")) == 1 && !socket.Payloads().Any(p => p.Contains("response.create")));
                Assert(!socket.Aborted && voice.Connected); socket.SendGate = null;
                await voice.SendText("next input", default); await voice.Stop();
            });
        foreach (string protocol in AssistantVoiceSession.Protocols)
            RunAsync("Voice " + protocol + " cancels emitted tool tokens on server interruption new input and Stop", async () =>
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                Assert(voice.CallToken("missing").IsCancellationRequested); await Start(voice, Provider(protocol));
                var call = await ReceiveCall(voice, socket, "cancel-call"); CancellationToken token = voice.CallToken(call.Id); Assert(!token.IsCancellationRequested);
                Task waitingForApproval = Task.Delay(Timeout.Infinite, token);
                socket.Push(socket.Gemini ? "{\"toolCallCancellation\":{\"ids\":[\"cancel-call\"]}}" : "{\"type\":\"input_audio_buffer.speech_started\"}");
                await Until(() => token.IsCancellationRequested);
                try { await waitingForApproval; throw new Exception("Expected approval cancellation"); } catch (OperationCanceledException) { }
                Assert(voice.CallToken(call.Id).IsCancellationRequested);
                Reject<InvalidOperationException>(() => voice.SendToolResult(call, "stale", default).GetAwaiter().GetResult());
                var next = await ReceiveCall(voice, socket, "typed-cancel"); CancellationToken nextToken = voice.CallToken(next.Id);
                await voice.SendText("new input", default); Assert(nextToken.IsCancellationRequested);
                var final = await ReceiveCall(voice, socket, "stop-cancel"); CancellationToken finalToken = voice.CallToken(final.Id);
                await voice.Stop(); Assert(finalToken.IsCancellationRequested && voice.CallToken(final.Id).IsCancellationRequested);
            });
        RunAsync("Voice stop cancels blocked send synchronously and restart has no stale callbacks", async () =>
        {
            var first = new Socket(); var second = new Socket(); var sockets = new Queue<Socket>(new[] { first, second }); var devices = new List<Audio>();
            await using var voice = new AssistantVoiceSession(() => sockets.Dequeue(), () => { var audio = new Audio(); devices.Add(audio); return audio; });
            int callbacks = 0; voice.Transcript += (_, _) => callbacks++;
            await Start(voice); first.HoldSend = true; Task sending = voice.SendText("pending", default);
            Task stopping = voice.Stop(); Assert(devices[0].Disposed && first.Aborted); await stopping;
            try { await sending; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            await Start(voice); first.Push("{\"type\":\"response.output_text.done\",\"text\":\"stale\"}"); await Task.Delay(20); Assert(callbacks == 0 && voice.Connected); await voice.Stop();
        });
        foreach (string protocol in AssistantVoiceSession.Protocols)
            RunAsync("Voice " + protocol + " fresh action image precedes output and continuation atomically after rate wait", async () =>
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                await Start(voice, Provider(protocol)); await voice.SendImage("data:image/jpeg;base64,AQID", default);
                var call = await ReceiveCall(voice, socket, "fresh-result");
                bool captured = false, periodicCaptured = false;
                long began = Environment.TickCount64;
                Task action = voice.SendToolResult(call, "completed once", default, _ => { captured = true; return Task.FromResult<string?>("data:image/jpeg;base64,BAUG"); });
                Task periodic = voice.SendImage(_ => { periodicCaptured = true; return Task.FromResult<string?>("data:image/jpeg;base64,BwgJ"); }, default);
                await Task.Delay(60); Assert(!captured && !periodicCaptured && !action.IsCompleted);
                // A rate wait must not starve the ten-frame microphone queue.
                for (int frame = 0; frame < 12; frame++) { audio.Capture!(new byte[2400]); await Task.Delay(50); Assert(voice.Connected); }
                await Until(() => socket.Payloads().Count(p => p.Contains(socket.Gemini ? "audio/pcm;rate=16000" : "input_audio_buffer.append")) == 12);
                await action; Assert(Environment.TickCount64 - began >= 850 && captured && !periodicCaptured);
                var sent = socket.Payloads();
                int image = Array.FindIndex(sent, p => p.Contains("BAUG"));
                int result = Array.FindIndex(sent, p => p.Contains("completed once"));
                Assert(image >= 0 && result == image + 1);
                if (protocol == "OpenAIRealtime") Assert(sent[result + 1].Contains("response.create"));
                await periodic; Assert(periodicCaptured && socket.Payloads().Last().Contains("BwgJ")); await voice.Stop();
            });
        foreach (bool serverCancellation in new[] { false, true })
            RunAsync("Voice image rate wait cancels before capture or output " + (serverCancellation ? "per call" : "per caller"), async () =>
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                await Start(voice); await voice.SendImage("data:image/jpeg;base64,AQID", default);
                var call = await ReceiveCall(voice, socket, "image-wait"); using var cancellation = new CancellationTokenSource(); int captures = 0;
                Task work = voice.SendToolResult(call, "unsent", cancellation.Token, _ => { captures++; return Task.FromResult<string?>("data:image/jpeg;base64,BAUG"); });
                await Task.Delay(30);
                if (serverCancellation) socket.Push("{\"type\":\"input_audio_buffer.speech_started\"}"); else cancellation.Cancel();
                try { await work; throw new Exception("Expected rate-wait cancellation"); } catch (OperationCanceledException) { }
                Assert(captures == 0 && !socket.Payloads().Any(p => p.Contains("function_call_output") || p.Contains("response.create")) && !socket.Aborted && voice.Connected);
                if (!serverCancellation) await voice.SendToolResult(call, "retry", default, _ => Task.FromResult<string?>("data:image/jpeg;base64,BAUG"));
                await voice.Stop();
            });
        RunAsync("Voice slow fresh capture leaves microphone upload free and cancels without stale reuse", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            await Start(voice); var call = await ReceiveCall(voice, socket, "slow-capture"); using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var capture = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task work = voice.SendToolResult(call, "completed", cancellation.Token, token => { started.TrySetResult(); return capture.Task.WaitAsync(token); });
            await started.Task;
            for (int i = 0; i < 12; i++) { audio.Capture!(new byte[2400]); await Task.Delay(50); Assert(voice.Connected); }
            await Until(() => socket.Payloads().Count(p => p.Contains("input_audio_buffer.append")) == 12);
            cancellation.Cancel(); try { await work; throw new Exception("Expected capture cancellation"); } catch (OperationCanceledException) { }
            Assert(!socket.Aborted && voice.Connected && !socket.Payloads().Any(p => p.Contains("input_image") || p.Contains("function_call_output") || p.Contains("response.create")));
            await voice.Stop();
        });
        RunAsync("Voice in-flight fresh image uses session cancellation and skips canceled output and continuation", async () =>
        {
            var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            await Start(voice); var call = await ReceiveCall(voice, socket, "image-flight"); using var cancellation = new CancellationTokenSource();
            var frameGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); socket.SendGate = frameGate; int entered = socket.EnteredSends;
            Task work = voice.SendToolResult(call, "unsent", cancellation.Token, _ => Task.FromResult<string?>("data:image/jpeg;base64,BAUG"));
            await Until(() => socket.EnteredSends > entered); cancellation.Cancel(); Assert(!work.IsCompleted && !socket.Aborted);
            frameGate.TrySetResult(); try { await work; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Assert(voice.Connected && !socket.Aborted && socket.Payloads().Last().Contains("BAUG") && !socket.Payloads().Any(p => p.Contains("function_call_output") || p.Contains("response.create")));
            socket.SendGate = null; await voice.SendText("still usable", default); await voice.Stop();
        });
        foreach (string failure in new[] { "upload", "capture", "cancel", "old-session", "clear" })
            RunAsync("Voice completed outcome survives delivery boundary " + failure, async () =>
            {
                var socket = new Socket(); var audio = new Audio(); await using var voice = new AssistantVoiceSession(() => socket, () => audio);
                using var conversation = new AssistantConversation(new NoTransport());
                using var cancellation = new CancellationTokenSource(); await Start(voice);
                var call = await ReceiveCall(voice, socket, "outcome-" + failure); int deliveries = 0;
                Task<string> Execute(AssistantCall _, CancellationToken token)
                {
                    if (failure == "upload") socket.ThrowSend = true;
                    if (failure == "cancel") cancellation.Cancel();
                    if (failure == "clear") conversation.Clear();
                    return Task.FromResult("Task added exactly once");
                }
                try
                {
                    await AssistantVoiceActionFlow.Run(call, conversation, Execute, () => failure != "old-session", (result, token) =>
                    {
                        deliveries++;
                        return voice.SendToolResult(call, result, token, failure == "capture" ? _ => Task.FromException<string?>(new System.IO.IOException("capture failed")) : null);
                    }, cancellation.Token);
                    Assert(failure is "old-session" or "clear");
                }
                catch (OperationCanceledException) { Assert(failure == "cancel"); }
                catch (InvalidOperationException) { Assert(failure == "upload"); }
                catch (System.IO.IOException) { Assert(failure == "capture"); }
                Assert(conversation.Messages.Count == (failure == "clear" ? 0 : 1));
                if (failure != "clear") Assert(conversation.Messages.Single().Text.Contains("Task added exactly once"));
                Assert(deliveries == (failure is "old-session" or "clear" ? 0 : 1));
                await voice.Stop();
            });
        test("Voice local evidence bounds history and text without adding protocol tool records", () =>
        {
            using var conversation = new AssistantConversation(new NoTransport());
            for (int i = 0; i < 70; i++) conversation.RecordVoiceOutcome(conversation.Epoch, new(i.ToString(), "inspect_app", new string('x', 10000)), new string('y', 50000));
            Assert(conversation.Messages.Count is > 0 and <= 48 && conversation.Messages.Last().Text.Contains("[69]") && conversation.Messages.All(m => m.Text.Length < 15000 && m.Role == "assistant" && m.Calls == null && m.CallId == null));
        });
        test("Voice activity preserves thinking and audible speech across interleaved silent output and microphone ticks", () =>
        {
            var activity = new AssistantVoiceActivity(); activity.Reset();
            activity.Level(0, true, 1000); Assert(activity.State(1000) == AssistantState.Listening);
            activity.Thinking();
            for (int i = 0; i < 20; i++) { activity.Level(.8f, false, 1000 + i * 50); activity.Level(0, true, 1000 + i * 50); Assert(activity.State(1000 + i * 50) == AssistantState.Thinking); }
            activity.Level(.7f, true, 2000); activity.Level(.2f, false, 2050); Assert(activity.State(2050) == AssistantState.Speaking && activity.Energy(2050) == .7f);
            activity.Transcript("assistant"); activity.Level(0, true, 2100); activity.Level(1, false, 2150); Assert(activity.State(2150) == AssistantState.Speaking);
            Assert(activity.State(2221) == AssistantState.Listening);
            activity.Transcript("user"); activity.Level(0, true, 2300); activity.Level(.9f, false, 2350); Assert(activity.State(2350) == AssistantState.Thinking);
            activity.Transcript("assistant"); Assert(activity.State(2400) == AssistantState.Listening);
            activity.Level(.01f, true, 2450); Assert(activity.State(2450) == AssistantState.Listening);
            activity.Level(float.NaN, true, 2500); activity.Level(1, true, 2550); activity.Reset(); Assert(activity.State(2550) == AssistantState.Listening && activity.Energy(2550) == 0);
        });
        test("Voice PCM meter measures signed samples including negative full scale", () =>
        { Assert(AssistantVoiceAudio.Peak(new byte[] { 0, 0, 0, 128 }, 4) == 1f); Assert(AssistantVoiceAudio.Peak(new byte[20], 20) == 0); });
        test("Voice playback buffer accepts long response bursts and repeated generation clears", () =>
        {
            var buffer = new AssistantVoiceAudio.MeteredPcm(_ => { }); var second = new byte[48000]; second[0] = 32;
            for (int i = 0; i < 45; i++) Assert(buffer.Append(second));
            var heard = new byte[48000]; Assert(buffer.Read(heard, 0, heard.Length) == heard.Length && heard[0] == 32);
            buffer.Clear(); Array.Fill(heard, (byte)255); buffer.Read(heard, 0, heard.Length); Assert(heard.All(value => value == 0));
            for (int i = 0; i < 120; i++) Assert(buffer.Append(second)); Assert(!buffer.Append(second));
            buffer.Clear(); Assert(buffer.Append(new byte[] { 64, 0 })); buffer.Read(heard, 0, heard.Length);
            Assert(heard[0] == 64 && heard.Skip(1).All(value => value == 0));
        });
        test("Voice PCM grows on demand and preserves wrapped data across growth and clear",()=>
        {
            var pcm=new AssistantVoiceAudio.MeteredPcm(_=>{});Assert(pcm.CapacityBytes==0);
            var expected=new System.Collections.Generic.Queue<byte>();var random=new Random(5921);
            for(int turn=0;turn<160;turn++)
            {
                var input=new byte[random.Next(1,10000)*2];random.NextBytes(input);Assert(pcm.Append(input));foreach(byte b in input)expected.Enqueue(b);
                int count=random.Next(1,8000)*2;var output=new byte[count+8];Array.Fill(output,(byte)231);Assert(pcm.Read(output,4,count)==count);
                for(int i=0;i<count;i++)Assert(output[i+4]==(expected.Count>0?expected.Dequeue():0));
                Assert(output.Take(4).All(b=>b==231) && output.Skip(count+4).All(b=>b==231));
                if(turn%35==34){pcm.Clear();expected.Clear();Assert(pcm.CapacityBytes==0 && pcm.PlayedMilliseconds==0);}
            }
            pcm.Clear();Assert(pcm.Append(new byte[48000]) && pcm.CapacityBytes==48000);pcm.Clear();Assert(pcm.CapacityBytes==0);
        });
    }
}
