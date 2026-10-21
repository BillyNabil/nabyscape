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
            disposed = true; mic = input; input = null; speaker = output; output = null; pcm?.Clear(); pcm = null;
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
        // Grow with queued audio instead of reserving two minutes for a short reply.
        // The same 120-second limit, ordering, silence fill and overflow contract apply.
        private const int MaximumBytes=24000*2*120;
        private byte[] buffer=Array.Empty<byte>();
        private int head,buffered;
        internal int CapacityBytes {get{lock(sync)return buffer.Length;}}
        private long consumed;
        private int generation;
        public WaveFormat WaveFormat {get;}=new(24000,16,1);
        // WaveOut can have 100ms pending in native buffers. Report only conservatively heard audio.
        public int PlayedMilliseconds => Math.Max(0, (int)(Interlocked.Read(ref consumed) / 48) - 100);
        public bool Append(byte[] bytes)
        {
            lock (sync)
            {
                if(bytes.Length>MaximumBytes-buffered)return false;
                int needed=buffered+bytes.Length;
                if(needed>buffer.Length)
                {
                    var grown=new byte[Math.Min(MaximumBytes,Math.Max(needed,Math.Max(48000,buffer.Length*2)))];
                    int first=Math.Min(buffered,buffer.Length-head);
                    if(first>0)Buffer.BlockCopy(buffer,head,grown,0,first);
                    if(buffered>first)Buffer.BlockCopy(buffer,0,grown,first,buffered-first);
                    buffer=grown;head=0;
                }
                if(bytes.Length>0)
                {
                    int tail=(head+buffered)%buffer.Length,first=Math.Min(bytes.Length,buffer.Length-tail);
                    Buffer.BlockCopy(bytes,0,buffer,tail,first);
                    if(bytes.Length>first)Buffer.BlockCopy(bytes,first,buffer,0,bytes.Length-first);
                }
                buffered=needed;return true;
            }
        }
        public void Clear() { lock (sync) { generation++; buffer=Array.Empty<byte>();head=buffered=0;Interlocked.Exchange(ref consumed, 0); } }
        public int Read(byte[] bytes, int offset, int count)
        {
            int actual, epoch;
            lock (sync)
            {
                epoch=generation;actual=Math.Min(count,buffered);
                if(actual>0)
                {
                    int first=Math.Min(actual,buffer.Length-head);Buffer.BlockCopy(buffer,head,bytes,offset,first);
                    if(actual>first)Buffer.BlockCopy(buffer,0,bytes,offset+first,actual-first);
                    head=(head+actual)%buffer.Length;buffered-=actual;
                }
                if(actual<count)Array.Clear(bytes,offset+actual,count-actual);
                Interlocked.Add(ref consumed,actual);
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
