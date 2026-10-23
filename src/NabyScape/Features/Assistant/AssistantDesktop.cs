using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal sealed record AssistantScreenTarget(string Id,string Name,Rectangle Bounds){public override string ToString()=>Name;}
internal sealed record AssistantScreenFrame(AssistantScreenTarget Target,int Width,int Height,string DataUrl);
internal interface IAssistantDesktopBackend
{
    IReadOnlyList<AssistantScreenTarget> Targets();
    void Begin(IntPtr owner);
    void End();
    AssistantScreenFrame Capture(AssistantScreenTarget target);
    void Execute(AssistantScreenFrame frame,JsonElement action,CancellationToken token);
}
// Capture only: mouse and keyboard injection have been removed.
internal sealed class AssistantDesktop : IDisposable
{
    private readonly IAssistantDesktopBackend? fixture;
    private readonly SemaphoreSlim captureLock=new(1);
    private AssistantShareCapture? capture;
    private AssistantScreenTarget? selected;
    private int epoch;
    internal event Action? SourceClosed;
    internal bool Sharing=>selected!=null;
    internal string SourceName=>selected?.Name??"";
    internal AssistantScreenFrame? Frame {get;private set;}
    internal AssistantDesktop(IAssistantDesktopBackend? backend=null)=>fixture=backend;
    internal async Task<bool> Pick(IntPtr owner)
    {
        var next=new AssistantShareCapture();int generation=epoch;
        try
        {
            if(!await next.Pick(owner)){next.Dispose();return false;}
            if(generation!=epoch){next.Dispose();return false;}
            Stop();capture=next;next.SourceClosed+=()=>{if(ReferenceEquals(capture,next))SourceClosed?.Invoke();};selected=new(Guid.NewGuid().ToString("N"),next.Name,Rectangle.Empty);return true;
        }
        catch{next.Dispose();throw;}
    }
    internal async Task<AssistantScreenFrame> Capture(CancellationToken token)
    {
        var target=selected??throw new InvalidOperationException("Choose a window or display to share.");int generation=epoch;
        await captureLock.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();if(generation!=epoch)throw new OperationCanceledException(token);
            var frame=capture!=null?await capture.Capture(target,token):fixture!=null?await Task.Run(()=>fixture.Capture(target),token):throw new InvalidOperationException("The sharing source closed. Choose it again.");
            token.ThrowIfCancellationRequested();if(generation!=epoch)throw new OperationCanceledException(token);Frame=frame;return frame;
        }
        finally{captureLock.Release();}
    }
    internal void Stop(){epoch++;selected=null;Frame=null;capture?.Dispose();capture=null;fixture?.End();}
    public void Dispose()=>Stop();
    // Kept for isolated capture fixtures; production always uses the Windows picker.
    internal IReadOnlyList<AssistantScreenTarget> Targets()=>fixture?.Targets()??Array.Empty<AssistantScreenTarget>();
    internal void Start(string id,IntPtr owner)
    {
        if(fixture==null)throw new NotSupportedException("Use the Windows sharing picker.");
        Stop();selected=fixture.Targets().Single(t=>t.Id==id);try{fixture.Begin(owner);}catch{Stop();throw;}
    }
}
