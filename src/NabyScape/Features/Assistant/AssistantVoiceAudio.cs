using System;
using System.Threading;
using NAudio.Wave;

namespace NabyScape;

/// <summary>Windows default input/output; constructed without opening devices. PCM never leaves memory.</summary>
internal sealed class AssistantVoiceAudio : IAssistantVoiceAudio
{
    private readonly object gate = new();
    private WaveInEvent? input;
    private WaveOutEvent? output;
    private MeteredPcm? pcm;
    private bool disposed;
    public int PlayedMilliseconds => pcm?.PlayedMilliseconds ?? 0;
    public void Start(int inputRate, Action<byte[]> capture, Action<float, bool> level, Action failure)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (input != null) throw new InvalidOperationException("Audio already started.");
            pcm = new MeteredPcm(value => level(value, true));
            output = new WaveOutEvent { DeviceNumber = -1, DesiredLatency = 100, NumberOfBuffers = 2 };
            output.PlaybackStopped += (_, e) => { if (e.Exception != null && !Volatile.Read(ref disposed)) ThreadPool.QueueUserWorkItem(_ => failure()); };
            output.Init(pcm); output.Play();
            input = new WaveInEvent { DeviceNumber = -1, WaveFormat = new WaveFormat(inputRate, 16, 1), BufferMilliseconds = 50, NumberOfBuffers = 3 };
            input.DataAvailable += (_, e) =>
            {
                if (Volatile.Read(ref disposed)) return;
                try
                {
                    byte[] bytes = new byte[e.BytesRecorded]; Buffer.BlockCopy(e.Buffer, 0, bytes, 0, bytes.Length);
                    capture(bytes);
                }
                catch { if (!Volatile.Read(ref disposed)) ThreadPool.QueueUserWorkItem(_ => failure()); }
            };
            input.RecordingStopped += (_, e) => { if (e.Exception != null && !Volatile.Read(ref disposed)) ThreadPool.QueueUserWorkItem(_ => failure()); };
            input.StartRecording();
        }
    }
    public bool Append(byte[] bytes)
    { lock (gate) return !disposed && pcm != null && pcm.Append(bytes); }
    public void Clear()
    {
        lock (gate)
        {
            if (disposed) return;
            // Keep one playback worker alive. Stop/Play here can race the previous
            // worker's PlaybackStopped event and leave the replacement silent.
            // Clear generation-buffered PCM; at most the native 100ms tail remains.
            pcm?.Clear();
        }
    }
    public void Dispose()
    {
        WaveInEvent? mic; WaveOutEvent? speaker;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; mic = input; input = null; speaker = output; output = null; pcm?.Clear();
        }
        try { mic?.StopRecording(); } finally
        {
            try { mic?.Dispose(); } finally { try { speaker?.Stop(); } finally { speaker?.Dispose(); } }
        }
    }
    internal static float Peak(byte[] bytes, int count)
    {
        int max = 0;
        for (int i = 0; i + 1 < count; i += 2) max = Math.Max(max, Math.Abs((int)(short)(bytes[i] | bytes[i + 1] << 8)));
        return max / 32768f;
    }
    internal sealed class MeteredPcm(Action<float> level) : IWaveProvider
    {
        private readonly object sync = new();
        // Providers commonly stream many seconds of speech faster than playback.
        // Two minutes of mono PCM16 is bounded to 5.76MB and supports ordinary replies.
        private readonly BufferedWaveProvider buffer = new(new WaveFormat(24000, 16, 1)) { BufferDuration = TimeSpan.FromSeconds(120), ReadFully = true, DiscardOnBufferOverflow = false };
        private long consumed;
        private int generation;
        public WaveFormat WaveFormat => buffer.WaveFormat;
        // WaveOut can have 100ms pending in native buffers. Report only conservatively heard audio.
        public int PlayedMilliseconds => Math.Max(0, (int)(Interlocked.Read(ref consumed) / 48) - 100);
        public bool Append(byte[] bytes)
        {
            lock (sync)
            {
                if (buffer.BufferedBytes + bytes.Length > buffer.BufferLength) return false;
                buffer.AddSamples(bytes, 0, bytes.Length); return true;
            }
        }
        public void Clear() { lock (sync) { generation++; buffer.ClearBuffer(); Interlocked.Exchange(ref consumed, 0); } }
        public int Read(byte[] bytes, int offset, int count)
        {
            int actual, epoch;
            lock (sync)
            {
                epoch = generation; actual = Math.Min(count, buffer.BufferedBytes); buffer.Read(bytes, offset, count); Interlocked.Add(ref consumed, actual);
            }
            // Never invoke app code on WaveOut's playback thread; Stop may join that thread.
            float peak = 0;
            for (int i = offset; i + 1 < offset + actual; i += 2) peak = Math.Max(peak, Math.Abs((int)(short)(bytes[i] | bytes[i + 1] << 8)) / 32768f);
            float value = peak;
            ThreadPool.QueueUserWorkItem(_ => { if (Volatile.Read(ref generation) == epoch) level(value); });
            return count;
        }
    }
}
