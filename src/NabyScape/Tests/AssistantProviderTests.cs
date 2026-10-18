using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal static class AssistantProviderTests
{
    private sealed class Fake(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class UnknownLengthContent(byte[] data) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(data));
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => CreateContentReadStreamAsync();
    }
    private static readonly string[] Protocols = { "ChatCompletions", "Responses", "Anthropic", "Gemini" };
    private const string Image = "data:image/jpeg;base64,AQID";
    private static readonly AssistantTool Tool = new("inspect_app", "Inspect a selected app", JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone());
    private static readonly AssistantMessage[] Messages = { new("system", "Answer in Indonesian"), new("user", "Describe this", Image) };
    private static AssistantProvider Provider(string protocol) => new() { Protocol = protocol, BaseUrl = "https://provider.example/v1/", Model = "user-chosen-model", Tools = true, Vision = true };
    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assistant provider fixture failed."); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected assistant provider rejection: " + typeof(T).Name); }
    private static string Reply(string protocol, bool call = false) => protocol switch
    {
        "ChatCompletions" => call ? "{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":[{\"type\":\"function\",\"id\":\"call_1\",\"function\":{\"name\":\"inspect_app\",\"arguments\":\"{}\"}}]}}]}" : "{\"choices\":[{\"message\":{\"content\":\"Hello\"}}]}",
        "Responses" => call ? "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\",\"id\":\"rs_1\",\"summary\":[],\"encrypted_content\":\"opaque\"},{\"type\":\"function_call\",\"id\":\"fc_1\",\"call_id\":\"call_1\",\"name\":\"inspect_app\",\"arguments\":\"{}\"}]}" : "{\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"Hello\"}]}]}",
        "Anthropic" => call ? "{\"content\":[{\"type\":\"tool_use\",\"id\":\"call_1\",\"name\":\"inspect_app\",\"input\":{}}]}" : "{\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}]}",
        _ => call ? "{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"id\":\"call_1\",\"name\":\"inspect_app\",\"args\":{}},\"thoughtSignature\":\"opaque\"}]}}]}" : "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hello\"}]}}]}"
    };

    internal static void Run(Action<string, Action> test)
    {
        test("Assistant provider presets require an explicit model and capability choices", () =>
        {
            Assert(AssistantProviderCatalog.Presets.Count == 10);
            foreach (string name in AssistantProviderCatalog.Presets)
            {
                var provider = AssistantProviderCatalog.Create(name);
                Assert(provider.Model == "" && !provider.Vision && !provider.Tools && provider.Name == name);
                if (!name.StartsWith("Custom", StringComparison.Ordinal)) AssistantTransport.ValidateEndpoint(provider.BaseUrl);
            }
            Reject<ArgumentException>(() => AssistantProviderCatalog.Create("unrecognized"));
        });
        test("Assistant endpoints block cleartext remote private IP credentials query and fragments", () =>
        {
            foreach (string url in new[] { "", "http://provider.example/v1", "https://a:b@provider.example/v1", "https://provider.example/#fragment", "https://provider.example/?key=secret", "https://192.168.1.1", "https://169.254.169.254", "https://10.0.0.1", "https://[fc00::1]", "https://[::ffff:192.168.1.1]", "https://app.local", "https://app.internal", "https://localhost.evil/../#x", "https://provider.example./" })
                Reject<InvalidOperationException>(() => AssistantTransport.ValidateEndpoint(url));
            foreach (string url in new[] { "https://provider.example/v1", "http://localhost:11434/v1", "http://127.0.0.1:1234/v1", "http://[::1]:1234/v1" }) Assert(AssistantTransport.ValidateEndpoint(url).IsAbsoluteUri);
            Assert(AssistantTransport.IsPublicAddress(IPAddress.Parse("8.8.8.8")) && !AssistantTransport.IsPublicAddress(IPAddress.Parse("100.64.0.1")));
        });
        foreach (string protocol in Protocols)
        {
            test("Assistant " + protocol + " maps text image system tools and safe authentication", () =>
            {
                int requests = 0;
                using var transport = new AssistantTransport(new Fake(async (request, token) =>
                {
                    requests++; Assert(request.RequestUri!.Host == "provider.example" && request.RequestUri.Query.Length == 0);
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); var root = json.RootElement;
                    string wire = root.GetRawText(); Assert(wire.Contains("user-chosen-model") == (protocol != "Gemini"));
                    Assert(wire.Contains("inspect_app") && wire.Contains("Answer in Indonesian"));
                    if (protocol == "ChatCompletions") Assert(root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString() == Image);
                    if (protocol == "Responses") Assert(!root.GetProperty("store").GetBoolean() && wire.Contains("input_image") && wire.Contains("reasoning.encrypted_content"));
                    if (protocol == "Anthropic") Assert(wire.Contains("media_type") && request.Headers.GetValues("anthropic-version").Single() == "2023-06-01");
                    if (protocol == "Gemini") Assert(wire.Contains("inlineData") && wire.Contains("functionDeclarations") && request.RequestUri.AbsolutePath.EndsWith("models/user-chosen-model:generateContent", StringComparison.Ordinal));
                    string auth = protocol == "Anthropic" ? request.Headers.GetValues("x-api-key").Single() : protocol == "Gemini" ? request.Headers.GetValues("x-goog-api-key").Single() : request.Headers.Authorization!.Parameter!;
                    Assert(auth == "fixture-key"); return Ok(Reply(protocol));
                }));
                var reply = transport.Complete(Provider(protocol), "fixture-key", Messages, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult();
                Assert(reply.Text == "Hello" && reply.Calls.Count == 0 && requests == 1);
            });
            test("Assistant " + protocol + " round trips tool IDs arguments and opaque continuation", () =>
            {
                int requests = 0;
                using var transport = new AssistantTransport(new Fake(async (request, token) =>
                {
                    if (++requests == 1) return Ok(Reply(protocol, true));
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); var root = json.RootElement; string wire = root.GetRawText();
                    Assert(wire.Contains("call_1") && wire.Contains("selected app result"));
                    if (protocol == "ChatCompletions") Assert(wire.Contains("tool_call_id") && wire.Contains("tool_calls"));
                    if (protocol == "Responses") Assert(wire.Contains("function_call_output") && wire.Contains("encrypted_content") && root.GetProperty("input").EnumerateArray().Count(i => i.TryGetProperty("type", out var type) && type.GetString() == "function_call") == 1);
                    if (protocol == "Anthropic") Assert(wire.Contains("tool_result") && root.GetProperty("messages")[2].GetProperty("role").GetString() == "user");
                    if (protocol == "Gemini") Assert(wire.Contains("functionResponse") && wire.Contains("thoughtSignature"));
                    return Ok(Reply(protocol));
                }));
                var history = new List<AssistantMessage> { new("user", "inspect") };
                var first = transport.Complete(Provider(protocol), null, history, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult();
                Assert(first.Calls.Count == 1 && first.Calls[0].Id == "call_1" && first.Calls[0].Arguments == "{}");
                if (protocol == "Gemini") Assert(first.Calls[0].ThoughtSignature == "opaque");
                history.Add(new("assistant", first.Text, Calls: first.Calls, ProtocolItems: first.ProtocolItems)); history.Add(new("tool", "selected app result", CallId: first.Calls[0].Id));
                Assert(transport.Complete(Provider(protocol), null, history, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult().Text == "Hello" && requests == 2);
            });
        }
        test("Assistant invalid empty malformed and oversized responses use safe fixed errors", () =>
        {
            foreach (string protocol in Protocols) foreach (string body in new[] { "", "null", "{}", "[]", "{\"error\":{\"message\":\"fixture-secret\"}}", Reply(protocol, true).Replace("inspect_app", "unknown_tool", StringComparison.Ordinal) })
            {
                using var transport = new AssistantTransport(new Fake((_, _) => Task.FromResult(Ok(body))));
                try { transport.Complete(Provider(protocol), "fixture-secret", new[] { new AssistantMessage("user", "test") }, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Invalid response accepted"); }
                catch (IOException ex) { Assert(!ex.ToString().Contains("fixture-secret", StringComparison.Ordinal)); }
            }
            foreach (bool declared in new[] { true, false })
            {
                using var transport = new AssistantTransport(new Fake((_, _) =>
                {
                    var response = Ok(new string('x', AssistantTransport.MaximumBodyBytes + 1));
                    if (!declared) response.Content = new UnknownLengthContent(Encoding.UTF8.GetBytes(new string('x', AssistantTransport.MaximumBodyBytes + 1)));
                    return Task.FromResult(response);
                }));
                Reject<IOException>(() => transport.Complete(Provider("Responses"), null, new[] { new AssistantMessage("user", "test") }, Array.Empty<AssistantTool>(), CancellationToken.None).GetAwaiter().GetResult());
            }
        });
        test("Assistant capability input text call ID and tool count limits fail before network", () =>
        {
            using var transport = new AssistantTransport(new Fake((_, _) => throw new Exception("Network must not run")));
            var provider = Provider("ChatCompletions"); provider.Vision = false;
            Reject<InvalidOperationException>(() => transport.Complete(provider, null, Messages, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
            provider.Vision = true; provider.Model = "";
            Reject<InvalidOperationException>(() => transport.Complete(provider, null, Messages, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult()); provider.Model = "selected";
            foreach (var invalid in new IReadOnlyList<AssistantMessage>[] { Array.Empty<AssistantMessage>(), new[] { new AssistantMessage("user", new string('a', AssistantTransport.MaximumTextCharacters + 1)) }, new[] { new AssistantMessage("tool", "orphan", CallId: "unknown") }, new[] { new AssistantMessage("user", "bad image", "https://external.example/image.jpg") }, new[] { new AssistantMessage("assistant", "", Calls: new[] { new AssistantCall("", "inspect_app", "{}") }) } })
                Reject<InvalidOperationException>(() => transport.Complete(provider, null, invalid, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
            Reject<InvalidOperationException>(() => transport.Complete(provider, null, new[] { new AssistantMessage("user", "test") }, Enumerable.Repeat(Tool, 33).ToArray(), CancellationToken.None).GetAwaiter().GetResult());
            Reject<ArgumentNullException>(() => transport.Complete(provider, null, null!, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
            Reject<InvalidOperationException>(() => transport.Complete(provider, "bad\r\nkey", Messages, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
        });
        test("Assistant rejects disabled or malformed tool calls and partial outputs", () =>
        {
            foreach (string protocol in Protocols)
            {
                foreach (string json in new[] { Reply(protocol, true), Reply(protocol, true).Replace("call_1", "", StringComparison.Ordinal).Replace("inspect_app", "invalid name", StringComparison.Ordinal) })
                {
                    using var transport = new AssistantTransport(new Fake((_, _) => Task.FromResult(Ok(json)))); var provider = Provider(protocol); provider.Tools = false;
                    Reject<IOException>(() => transport.Complete(provider, null, new[] { new AssistantMessage("user", "test") }, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
                }
            }
            using var partial = new AssistantTransport(new Fake((_, _) => Task.FromResult(Ok("{\"status\":\"incomplete\",\"output\":[]}"))));
            Reject<IOException>(() => partial.Complete(Provider("Responses"), null, new[] { new AssistantMessage("user", "test") }, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
        });
        test("Assistant cancellation timeout redirects HTTP errors and localhost remain bounded", () =>
        {
            foreach (int status in new[] { 302, 401, 429, 500 })
            {
                int count = 0;
                using var transport = new AssistantTransport(new Fake((request, _) => { count++; Assert(request.RequestUri!.Host == "localhost" && request.Headers.Authorization == null); return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Headers = { Location = new Uri("https://untrusted.example/") }, Content = new StringContent("fixture-secret") }); }));
                var provider = Provider("ChatCompletions"); provider.BaseUrl = "http://localhost:1234/v1/";
                Reject<IOException>(() => transport.Complete(provider, null, new[] { new AssistantMessage("user", "test") }, Array.Empty<AssistantTool>(), CancellationToken.None).GetAwaiter().GetResult()); Assert(count == 1);
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            using var slow = new AssistantTransport(new Fake(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Ok("{}"); }), TimeSpan.FromMilliseconds(20));
            Reject<OperationCanceledException>(() => slow.Complete(Provider("Responses"), null, Messages, new[] { Tool }, cancelled.Token).GetAwaiter().GetResult());
            Reject<TimeoutException>(() => slow.Complete(Provider("Responses"), null, Messages, new[] { Tool }, CancellationToken.None).GetAwaiter().GetResult());
            using var during = new CancellationTokenSource();
            using var cancellable = new AssistantTransport(new Fake(async (_, token) => { during.Cancel(); await Task.Delay(Timeout.Infinite, token); return Ok("{}"); }));
            Reject<OperationCanceledException>(() => cancellable.Complete(Provider("Responses"), null, Messages, new[] { Tool }, during.Token).GetAwaiter().GetResult());
        });
    }
}
