using System;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

// No device or UI dependencies: microphone energy and audible playback are distinct.
internal sealed class AssistantVoiceActivity
{
    private const float SpeechThreshold = .015f;
    private long playbackUntil;
    private bool thinking;
    private float microphone, output;
    internal void Reset() { playbackUntil = 0; thinking = false; microphone = output = 0; }
    internal void Thinking() { thinking = true; playbackUntil = 0; output = 0; }
    internal void Transcript(string role) { if (role == "user") Thinking(); else thinking = false; }
    internal void Level(float peak, bool playback, long now)
    {
        peak = float.IsFinite(peak) ? Math.Clamp(peak, 0, 1) : 0;
        if (!playback) { microphone = peak; return; }
        output = peak;
        if (peak >= SpeechThreshold) { playbackUntil = now + 220; thinking = false; }
    }
    internal AssistantState State(long now) => now < playbackUntil ? AssistantState.Speaking : thinking ? AssistantState.Thinking : AssistantState.Listening;
    internal float Energy(long now) => State(now) == AssistantState.Speaking ? output : thinking ? 0 : microphone;
}

internal static class AssistantVoiceActionFlow
{
    internal static async Task Run(AssistantCall call, AssistantConversation conversation,
        Func<AssistantCall, CancellationToken, Task<string>> execute, Func<bool> stillCurrent,
        Func<string, CancellationToken, Task> deliver, CancellationToken token)
    {
        int epoch = conversation.Epoch;
        string result = await execute(call, token);
        // Execute may complete immediately before Stop/call cancellation. Evidence
        // must survive that boundary and failed uploads; explicit Clear wins.
        conversation.RecordVoiceOutcome(epoch, call, result);
        if (conversation.Epoch != epoch || !stillCurrent()) return;
        await deliver(result, token);
    }
}
