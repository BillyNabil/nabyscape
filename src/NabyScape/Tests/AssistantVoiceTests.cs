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
        internal bool Ack = true, Aborted, Disposed, HoldConnect, HoldSend;
        internal Uri? Endpoint;
        internal string? Key;
        internal bool Gemini;
        public async Task Connect(Uri endpoint, string? key, bool gemini, CancellationToken token)
        { Endpoint = endpoint; Key = key; Gemini = gemini; if (HoldConnect) await Task.Delay(Timeout.Infinite, token); }
        public async Task Send(string json, CancellationToken token)
        {
            if (HoldSend) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested(); lock (Sent) Sent.Add(json);
            if (Ack && (json.Contains("\"session.update\"") || json.Contains("\"setup\""))) Push(Gemini ? "{\"setupComplete\":{}}" : "{\"type\":\"session.updated\"}");
        }
        public async Task<string?> Receive(CancellationToken token) => await Incoming.Reader.ReadAsync(token);
        internal void Push(string? json) => Incoming.Writer.TryWrite(json);
        internal string[] Payloads() { lock (Sent) return Sent.ToArray(); }
        public void Abort() => Aborted = true;
        public void Dispose() => Disposed = true;
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
        RunAsync("Voice output overflow ends session and unknown tool is never emitted", async () =>
        {
            var socket = new Socket(); var audio = new Audio { Overflow = true }; await using var voice = new AssistantVoiceSession(() => socket, () => audio);
            int tools = 0; voice.ToolCall += _ => tools++;
            await Start(voice); socket.Push("{\"type\":\"response.output_audio.delta\",\"delta\":\"AAAAAA==\"}"); await Until(() => audio.Disposed); Assert(!voice.Connected && tools == 0 && audio.Added == 4);
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
            await voice.SendToolResult(calls[0], "first", default); Assert(!socket.Payloads().Any(p => p.Contains("response.create")));
            await voice.SendToolResult(calls[1], "second", default); Assert(socket.Payloads().Count(p => p.Contains("response.create")) == 1); await voice.Stop();
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
        test("Voice PCM meter measures signed samples including negative full scale", () =>
        { Assert(AssistantVoiceAudio.Peak(new byte[] { 0, 0, 0, 128 }, 4) == 1f); Assert(AssistantVoiceAudio.Peak(new byte[20], 20) == 0); });
    }
}
