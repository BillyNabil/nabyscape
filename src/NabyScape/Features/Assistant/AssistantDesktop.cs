using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Forms=System.Windows.Forms;

namespace NabyScape;

internal sealed record AssistantScreenTarget(string Id,string Name,Rectangle Bounds){public override string ToString()=>Name;}
internal sealed record AssistantScreenFrame(AssistantScreenTarget Target,int Width,int Height,string DataUrl);
internal interface IAssistantDesktopBackend
{
    IReadOnlyList<AssistantScreenTarget> Targets();
    void Begin(IntPtr owner);
    void End();
    AssistantScreenFrame Capture(AssistantScreenTarget target);
    void Execute(AssistantScreenFrame frame,JsonElement action,CancellationToken cancellationToken);
}
internal sealed class AssistantDesktop : IDisposable
{
    private readonly IAssistantDesktopBackend backend;
    private readonly SemaphoreSlim captureLock=new(1);
    private AssistantScreenTarget? selected;
    private int epoch;
    private DateTimeOffset capturedAt;
    internal bool Sharing=>selected!=null;
    internal bool ControlEnabled {get;set;}
    internal AssistantScreenFrame? Frame {get;private set;}
    internal AssistantDesktop(IAssistantDesktopBackend? backend=null)=>this.backend=backend??new AssistantDesktopNative();
    internal IReadOnlyList<AssistantScreenTarget> Targets()=>backend.Targets();
    internal void Start(string id,IntPtr owner)
    {
        Stop();selected=Targets().SingleOrDefault(t=>t.Id==id)??throw new InvalidOperationException("Choose an available display.");
        try{backend.Begin(owner);}catch{selected=null;backend.End();throw;}
    }
    internal async Task<AssistantScreenFrame> Capture(CancellationToken cancellationToken)
    {
        var target=selected??throw new InvalidOperationException("Screen sharing is off.");int generation=epoch;
        await captureLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(generation!=epoch || selected!=target)throw new OperationCanceledException("Screen sharing stopped.",cancellationToken);
            var frame=await Task.Run(()=>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(generation!=Volatile.Read(ref epoch))throw new OperationCanceledException("Screen sharing stopped.",cancellationToken);
                return backend.Capture(target);
            },cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if(generation!=epoch || selected!=target)throw new OperationCanceledException("Screen sharing stopped.",cancellationToken);
            Frame=frame;capturedAt=DateTimeOffset.UtcNow;return frame;
        }
        finally{captureLock.Release();}
    }
    internal Task Execute(string arguments,CancellationToken cancellationToken)
    {
        if(!Sharing || !ControlEnabled || Frame==null)throw new InvalidOperationException("Enable screen sharing and computer control for this session.");
        if(DateTimeOffset.UtcNow-capturedAt>TimeSpan.FromSeconds(60))throw new InvalidOperationException("The screen context expired. Share a fresh frame first.");
        using var json=JsonDocument.Parse(arguments);var action=json.RootElement.Clone();var frame=Frame;int generation=epoch;
        AssistantDesktopNative.Validate(frame,action);
        cancellationToken.ThrowIfCancellationRequested();
        // Native input is short and cancellation-aware; no queued worker can act after Stop.
        if(generation!=epoch)throw new OperationCanceledException(cancellationToken);
        backend.Execute(frame,action,cancellationToken);return Task.CompletedTask;
    }
    internal void Stop(){epoch++;selected=null;ControlEnabled=false;Frame=null;backend.End();}
    public void Dispose()=>Stop();
}

internal sealed class AssistantDesktopNative : IAssistantDesktopBackend
{
    private IntPtr owner,targetWindow,hook;
    private uint previousAffinity;
    private bool affinityChanged;
    private readonly ForegroundEvent foregroundEvent;
    internal AssistantDesktopNative()=>foregroundEvent=Foreground;
    public IReadOnlyList<AssistantScreenTarget> Targets()=>Forms.Screen.AllScreens.Select((s,i)=>new AssistantScreenTarget(s.DeviceName,$"Display {i+1} · {s.Bounds.Width} × {s.Bounds.Height}",s.Bounds)).ToArray();
    public void Begin(IntPtr owner)
    {
        this.owner=owner;RememberForeground();
        if(owner!=IntPtr.Zero && GetWindowDisplayAffinity(owner,out previousAffinity))affinityChanged=SetWindowDisplayAffinity(owner,0x11);
        hook=SetWinEventHook(3,3,IntPtr.Zero,foregroundEvent,0,0,0);
        if(hook==IntPtr.Zero)throw new InvalidOperationException("Windows could not observe the active application.");
    }
    public void End(){if(hook!=IntPtr.Zero)UnhookWinEvent(hook);if(affinityChanged && IsWindow(owner))SetWindowDisplayAffinity(owner,previousAffinity);affinityChanged=false;previousAffinity=0;hook=IntPtr.Zero;owner=targetWindow=IntPtr.Zero;}
    private void Foreground(IntPtr handle,uint type,IntPtr window,int obj,int child,uint thread,uint time)
    {if(window!=IntPtr.Zero && window!=owner && IsWindow(window))targetWindow=window;}
    private void RememberForeground(){var window=GetForegroundWindow();if(window!=IntPtr.Zero && window!=owner)targetWindow=window;}
    public AssistantScreenFrame Capture(AssistantScreenTarget target)
    {
        if(!Targets().Any(t=>t.Id==target.Id && t.Bounds==target.Bounds))throw new InvalidOperationException("The selected display changed. Select it again.");
        double scale=Math.Min(1,1280d/Math.Max(target.Bounds.Width,target.Bounds.Height));
        int width=Math.Max(1,(int)Math.Round(target.Bounds.Width*scale)),height=Math.Max(1,(int)Math.Round(target.Bounds.Height*scale));
        using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using var graphics=Graphics.FromImage(bitmap);
        IntPtr source=GetDC(IntPtr.Zero),destination=graphics.GetHdc();
        try
        {
            SetStretchBltMode(destination,4);
            if(source==IntPtr.Zero || !StretchBlt(destination,0,0,width,height,source,target.Bounds.Left,target.Bounds.Top,target.Bounds.Width,target.Bounds.Height,0x40CC0020))throw new InvalidOperationException("Windows could not capture this display.");
        }
        finally{graphics.ReleaseHdc(destination);if(source!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,source);}
        using var output=new MemoryStream();bitmap.Save(output,ImageFormat.Jpeg);
        return new(target,width,height,"data:image/jpeg;base64,"+Convert.ToBase64String(output.ToArray()));
    }
    internal static Point Map(AssistantScreenFrame frame,int x,int y)
    {
        if(x<0 || x>=frame.Width || y<0 || y>=frame.Height)throw new ArgumentException("Screen coordinates are outside the shared image.");
        return new(frame.Target.Bounds.Left+(int)Math.Round(x*(frame.Target.Bounds.Width-1d)/Math.Max(1,frame.Width-1)),frame.Target.Bounds.Top+(int)Math.Round(y*(frame.Target.Bounds.Height-1d)/Math.Max(1,frame.Height-1)));
    }
    internal static void Validate(AssistantScreenFrame frame,JsonElement action)
    {
        if(action.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Computer action must be an object.");
        string kind=action.GetProperty("action").GetString()??"";
        if(kind is "click" or "double_click" or "scroll")
        {
            Map(frame,action.GetProperty("x").GetInt32(),action.GetProperty("y").GetInt32());
            if(action.TryGetProperty("button",out var button) && button.GetString() is not ("left" or "right"))throw new ArgumentException("Choose left or right mouse button.");
            if(kind=="scroll" && action.GetProperty("amount").GetInt32() is <-10 or >10)throw new ArgumentException("Scroll amount is out of range.");
        }
        else if(kind=="key")ParseKeys(action.GetProperty("keys").GetString()??"");
        else if(kind=="type_text")
        {string text=action.GetProperty("text").GetString()??"";if(text.Length>1000 || text.Any(c=>char.IsControl(c) && c!='\n' && c!='\t'))throw new ArgumentException("Type at most 1,000 printable characters.");}
        else throw new ArgumentException("Unknown computer action.");
    }
    public void Execute(AssistantScreenFrame frame,JsonElement action,CancellationToken cancellationToken)
    {
        Validate(frame,action);
        if(!Targets().Any(t=>t.Id==frame.Target.Id && t.Bounds==frame.Target.Bounds))throw new InvalidOperationException("The display changed; capture a fresh frame.");
        string kind=action.GetProperty("action").GetString()??"";
        cancellationToken.ThrowIfCancellationRequested();
        if(kind is "click" or "double_click" or "scroll")
        {
            var point=Map(frame,action.GetProperty("x").GetInt32(),action.GetProperty("y").GetInt32());
            var under=GetAncestor(WindowFromPoint(point),2);
            if(under==owner)throw new InvalidOperationException("Computer actions cannot target the Companion window.");
            if(under!=IntPtr.Zero)targetWindow=under;
            if(!SetCursorPos(point.X,point.Y))throw new InvalidOperationException("Windows could not move the pointer.");
            if(kind=="scroll")
            {int amount=action.GetProperty("amount").GetInt32();if(amount is <-10 or >10)throw new ArgumentException("Scroll amount must be between -10 and 10.");Send(new(){Type=0,Data=new(){Mouse=new(){Data=unchecked((uint)(amount*120)),Flags=0x800}}});}
            else
            {
                bool right=action.TryGetProperty("button",out var button) && button.GetString()=="right";
                for(int i=0;i<(kind=="double_click"?2:1);i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try{Send(new(){Type=0,Data=new(){Mouse=new(){Flags=right?8u:2u}}});}
                    finally{SendInput(1,new[]{new Input{Type=0,Data=new(){Mouse=new(){Flags=right?16u:4u}}}},Marshal.SizeOf<Input>());}
                }
            }
            return;
        }
        if(targetWindow==IntPtr.Zero || targetWindow==owner || !IsWindow(targetWindow))throw new InvalidOperationException("Click the target application, then try again.");
        if(Forms.Screen.FromHandle(targetWindow).DeviceName!=frame.Target.Id)throw new InvalidOperationException("The target app is on another display. Click an app on the shared display first.");
        SetForegroundWindow(targetWindow);
        if(GetForegroundWindow()!=targetWindow)throw new InvalidOperationException("Windows did not activate the target app. Focus it and try again.");
        if(kind=="type_text")
        {
            string text=action.GetProperty("text").GetString()??"";
            if(text.Length>1000 || text.Any(c=>char.IsControl(c) && c!='\n' && c!='\t'))throw new ArgumentException("Type at most 1,000 printable characters.");
            foreach(char c in text)
            {cancellationToken.ThrowIfCancellationRequested();try{Send(new(){Type=1,Data=new(){Key=new(){Scan=c,Flags=4}}});}finally{SendInput(1,new[]{new Input{Type=1,Data=new(){Key=new(){Scan=c,Flags=6}}}},Marshal.SizeOf<Input>());}}
            return;
        }
        if(kind!="key")throw new ArgumentException("Unknown computer action.");
        var keys=ParseKeys(action.GetProperty("keys").GetString()??"");int pressed=0;
        try{foreach(var key in keys){cancellationToken.ThrowIfCancellationRequested();Send(new(){Type=1,Data=new(){Key=new(){Code=key}}});pressed++;}}
        finally{for(int i=pressed-1;i>=0;i--)SendInput(1,new[]{new Input{Type=1,Data=new(){Key=new(){Code=keys[i],Flags=2}}}},Marshal.SizeOf<Input>());}
    }
    internal static ushort[] ParseKeys(string combination)
    {
        var names=combination.ToUpperInvariant().Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(names.Length is <1 or >4)throw new ArgumentException("Use one key or a combination of up to four keys.");
        if(names.Distinct().Count()!=names.Length)throw new ArgumentException("Do not repeat a key in a combination.");
        return names.Select(name=>(ushort)(name switch
        {
            "CTRL"=> (ushort)0x11,"SHIFT"=>0x10,"ALT"=>0x12,"WIN"=>0x5B,"ENTER"=>0x0D,"TAB"=>9,"ESC"=>0x1B,"SPACE"=>0x20,"BACKSPACE"=>8,"DELETE"=>0x2E,"UP"=>0x26,"DOWN"=>0x28,"LEFT"=>0x25,"RIGHT"=>0x27,"HOME"=>0x24,"END"=>0x23,"PAGEUP"=>0x21,"PAGEDOWN"=>0x22,
            _ when name.Length==1 && char.IsAsciiLetterOrDigit(name[0])=>(ushort)name[0],
            _ when name.StartsWith('F') && int.TryParse(name[1..],out int f) && f is >=1 and <=12=>(ushort)(0x6F+f),
            _=>throw new ArgumentException("Unsupported key. Use names such as CTRL+L, ENTER or ESC.")
        })).ToArray();
    }
    private static void Send(Input input){if(SendInput(1,new[]{input},Marshal.SizeOf<Input>())!=1)throw new InvalidOperationException("Windows rejected the input. Elevated or protected applications may require manual interaction.");}
    [StructLayout(LayoutKind.Sequential)]private struct Input {public uint Type;public InputData Data;}
    [StructLayout(LayoutKind.Explicit,Size=32)]private struct InputData {[FieldOffset(0)]public MouseInput Mouse;[FieldOffset(0)]public KeyInput Key;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseInput {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct KeyInput {public ushort Code,Scan;public uint Flags,Time;public UIntPtr Extra;}
    private delegate void ForegroundEvent(IntPtr hook,uint type,IntPtr window,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")]private static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,ForegroundEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
    [DllImport("user32.dll")]private static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]private static extern int SetStretchBltMode(IntPtr dc,int mode);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern bool StretchBlt(IntPtr destination,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,int sourceWidth,int sourceHeight,uint operation);
}
