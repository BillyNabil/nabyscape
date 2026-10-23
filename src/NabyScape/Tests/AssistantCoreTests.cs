using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal static class AssistantCoreTests
{
    private sealed class Transport(Func<IReadOnlyList<AssistantMessage>,CancellationToken,Task<AssistantReply>> complete):IAssistantTransport
    {public Task<AssistantReply> Complete(AssistantProvider p,string? key,IReadOnlyList<AssistantMessage> messages,IReadOnlyList<AssistantTool> tools,CancellationToken token)=>complete(messages,token);}
    private sealed class DesktopBackend:IAssistantDesktopBackend
    {
        internal int Captures,Inputs,Ends;
        internal readonly ManualResetEventSlim Started=new(),Release=new(true);
        internal bool FailBegin;
        internal readonly AssistantScreenTarget Target=new("fixture","Fixture display",new Rectangle(-1920,0,1920,1080));
        public IReadOnlyList<AssistantScreenTarget> Targets()=>new[]{Target};
        public void Begin(IntPtr owner){if(FailBegin)throw new InvalidOperationException();}
        public void End()=>Ends++;
        public AssistantScreenFrame Capture(AssistantScreenTarget target){Interlocked.Increment(ref Captures);Started.Set();if(!Release.Wait(3000))throw new Exception("Fixture capture timeout");return new(target,1280,720,"data:image/jpeg;base64,AQID");}
        public void Execute(AssistantScreenFrame frame,JsonElement action,CancellationToken token){token.ThrowIfCancellationRequested();Inputs++;}
    }
    private static void Assert(bool value){if(!value)throw new Exception("Assistant core fixture failed");}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static JsonElement Json(string value){using var doc=JsonDocument.Parse(value);return doc.RootElement.Clone();}
    private static readonly AssistantProvider Provider=new(){Tools=true,Vision=true};
    private static readonly AssistantTool Tool=new("fixture","Fixture action",Json("{\"type\":\"object\"}"));
    private static readonly AssistantReply Done=new("Done",Array.Empty<AssistantCall>());
    private static Task Send(AssistantConversation chat,string text="Hello",Func<AssistantCall,CancellationToken,Task<string>>? action=null,CancellationToken token=default)=>chat.Send(Provider,null,"Fixture",text,null,new[]{Tool},action??((_,_)=>Task.FromResult("ok")),token);
    internal static void Run(Action<string,Action> test)
    {
        test("Assistant complete tool turn preserves protocol metadata, IDs and fresh screen",()=>Task.Run(async()=>
        {
            int turns=0,actions=0;var metadata=new[]{Json("{\"encrypted_content\":\"opaque-fixture\"}")};
            using var chat=new AssistantConversation(new Transport((messages,token)=>
            {
                turns++;if(turns==1)return Task.FromResult(new AssistantReply("Review",new[]{new AssistantCall("id-1","fixture","{}")},metadata));
                Assert(messages.Any(m=>m.Role=="tool" && m.CallId=="id-1" && m.Text=="result"));
                Assert(messages.Any(m=>m.ProtocolItems?.Single().GetProperty("encrypted_content").GetString()=="opaque-fixture"));
                Assert(messages.Count(m=>m.ImageDataUrl!=null)==1 && messages.Last().ImageDataUrl=="fresh");return Task.FromResult(Done);
            }));
            await chat.Send(Provider,null,"Fixture","Hello","old",new[]{Tool},(_,_)=>{actions++;return Task.FromResult("result");},refreshScreen:_=>Task.FromResult<string?>("fresh"));
            Assert(turns==2 && actions==1 && chat.Messages.Last().Text=="Done");
        }).GetAwaiter().GetResult());
        test("Assistant never executes an unoffered action",()=>Task.Run(async()=>
        {int n=0,executed=0;using var chat=new AssistantConversation(new Transport((_,_)=>Task.FromResult(++n==1?new AssistantReply("",new[]{new AssistantCall("x","unoffered","{}")}):Done)));await Send(chat,action:(_,_)=>{executed++;return Task.FromResult("");});Assert(executed==0 && chat.Messages.Any(m=>m.Role=="tool" && m.Text.Contains("unavailable")));}).GetAwaiter().GetResult());
        test("Assistant rejects concurrent turns and cancellation rolls back unfinished calls",()=>Task.Run(async()=>
        {var started=new TaskCompletionSource();using var chat=new AssistantConversation(new Transport(async(_,token)=>{started.SetResult();await Task.Delay(Timeout.Infinite,token);return Done;}));var first=Send(chat);await started.Task;try{await Send(chat);throw new Exception();}catch(InvalidOperationException){}chat.Stop();try{await first;throw new Exception();}catch(OperationCanceledException){}Assert(!chat.Busy && chat.Messages.Count==0);}).GetAwaiter().GetResult());
        test("Assistant Clear rejects late provider replies and prevents subsequent actions",()=>Task.Run(async()=>
        {var started=new TaskCompletionSource();var reply=new TaskCompletionSource<AssistantReply>();int actions=0;using var chat=new AssistantConversation(new Transport((_,_)=>{started.SetResult();return reply.Task;}));var work=Send(chat,action:(_,_)=>{actions++;return Task.FromResult("ok");});await started.Task;chat.Clear();reply.SetResult(new("late",new[]{new AssistantCall("late","fixture","{}")}));try{await work;throw new Exception();}catch(OperationCanceledException){}Assert(actions==0 && chat.Messages.Count==0);}).GetAwaiter().GetResult());
        test("Assistant action round limit rolls back incomplete turns",()=>Task.Run(async()=>
        {int rounds=0;using var chat=new AssistantConversation(new Transport((_,_)=>Task.FromResult(new AssistantReply("",new[]{new AssistantCall((++rounds).ToString(),"fixture","{}")}))));try{await Send(chat);throw new Exception();}catch(InvalidOperationException){}Assert(rounds==6 && chat.Messages.Count==2 && chat.LastTurnHasActionResults && chat.Messages.All(m=>m.Calls==null && m.Role!="tool") && !chat.Busy);}).GetAwaiter().GetResult());
        test("Assistant preserves completed action evidence when its later summary fails",()=>Task.Run(async()=>
        {int rounds=0,executed=0;using var chat=new AssistantConversation(new Transport((_,_)=>++rounds==1?Task.FromResult(new AssistantReply("",new[]{new AssistantCall("saved","fixture","{} ")})):Task.FromException<AssistantReply>(new System.IO.IOException("offline"))));try{await Send(chat,action:(_,_)=>{executed++;return Task.FromResult("Task added once");});throw new Exception();}catch(System.IO.IOException){}Assert(executed==1 && chat.LastTurnHasActionResults && chat.Messages.Last().Text.Contains("Task added once") && chat.Messages.All(m=>m.Calls==null && m.Role!="tool"));}).GetAwaiter().GetResult());
        test("Assistant failed provider save never associates a staged key with the old destination",()=>
        {var old=new AssistantProvider{BaseUrl="https://old.example/v1/"};var options=new AssistantOptions{SelectedProvider=old.Id,SelectedVoiceProvider=old.Id,Providers=new(){old}};var keys=new AssistantMemoryKeys();keys.Write(old.Id,"old-key");var candidate=new AssistantProvider{Id=old.Id,BaseUrl="https://new.example/v1/"};string staged="";Reject<System.IO.IOException>(()=>AssistantProviderPersistence.Save(options,old.Id,candidate,"new-key",keys,next=>{staged=next.SelectedProvider;Assert(staged!=old.Id && keys.Read(staged)=="new-key");throw new System.IO.IOException();}));Assert(options.Providers.Single().BaseUrl=="https://old.example/v1/" && options.SelectedProvider==old.Id && keys.Read(old.Id)=="old-key" && !keys.Exists(staged));});
        test("Assistant successful destination changes rotate identity without carrying a key",()=>
        {var old=new AssistantProvider{BaseUrl="https://old.example/v1/"};var options=new AssistantOptions{SelectedProvider=old.Id,SelectedVoiceProvider=old.Id,Providers=new(){old}};var keys=new AssistantMemoryKeys();keys.Write(old.Id,"old-key");var candidate=new AssistantProvider{Id=old.Id,BaseUrl="https://new.example/v1/"};var saved=AssistantProviderPersistence.Save(options,old.Id,candidate,"",keys,_=>{});Assert(saved.SelectedProvider!=old.Id && saved.SelectedVoiceProvider==saved.SelectedProvider && !keys.Exists(saved.SelectedProvider) && !keys.Exists(old.Id) && options.Providers.Single().BaseUrl=="https://old.example/v1/");});
        test("Assistant conversation bounds reject oversized replies",()=>Task.Run(async()=>
        {using var chat=new AssistantConversation(new Transport((_,_)=>Task.FromResult(new AssistantReply(new string('x',64001),Array.Empty<AssistantCall>()))));try{await Send(chat);throw new Exception();}catch(InvalidOperationException){}Assert(chat.Messages.Count==0);}).GetAwaiter().GetResult());
        test("Assistant queued captures cannot start after sharing stops",()=>Task.Run(async()=>
        {var fake=new DesktopBackend();fake.Release.Reset();using var desktop=new AssistantDesktop(fake);desktop.Start(fake.Target.Id,IntPtr.Zero);var first=desktop.Capture(default);Assert(fake.Started.Wait(2000));var queued=desktop.Capture(default);desktop.Stop();fake.Release.Set();foreach(var work in new[]{first,queued}){try{await work;throw new Exception();}catch(OperationCanceledException){}}Assert(fake.Captures==1 && desktop.Frame==null);}).GetAwaiter().GetResult());
        test("Assistant failed capture setup releases backend resources",()=>
        {var fake=new DesktopBackend{FailBegin=true};using var desktop=new AssistantDesktop(fake);Reject<InvalidOperationException>(()=>desktop.Start(fake.Target.Id,IntPtr.Zero));Assert(fake.Ends==2 && !desktop.Sharing);});
        test("Assistant options exclude secrets and begin with all permissions off",()=>
        {var options=new AssistantOptions();Assert(!options.Enabled && options.Providers.Count==0);var keys=new AssistantMemoryKeys();string id=Guid.NewGuid().ToString("N");keys.Write(id,"fixture-secret");options.Providers.Add(new(){Id=id});string json=JsonSerializer.Serialize(options);Assert(!json.Contains("fixture-secret") && keys.Exists(id));keys.Remove(id);Assert(!keys.Exists(id));});
    }
}
