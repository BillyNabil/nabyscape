using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NabyScape;

public sealed class ActivityStep
{
    public string Kind { get; set; } = "open";
    public Shortcut Target { get; set; } = new();
    public int Minutes { get; set; } = 25;
    public bool KeepDisplayOn { get; set; }
    public string Label => Kind=="focus" ? $"Focus · {Minutes} min" : Kind=="awake" ? $"Keep awake · {Minutes} min" : "Open · "+Target.Name;
}
public sealed class ActivityDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<ActivityStep> Steps { get; set; } = new();
}
public sealed class ActivityActionsData { public List<ActivityDefinition> Activities { get; set; } = new(); }
public sealed record ActivityStepResult(string Label,bool Success,string Detail);

/// <summary>Explicit, UI-thread activity execution. Only definitions are persisted.</summary>
public sealed class ActivityActionsService : IDisposable
{
    private readonly Store store;
    private readonly Countdown focus;
    private readonly KeepAwakeService awake;
    private readonly Action saveSession;
    private readonly Action<Exception> log;
    private readonly Action<Shortcut> launch;
    private readonly Func<DateTimeOffset> clock;
    private readonly List<ActivityDefinition> definitions=new();
    private readonly List<ActivityStepResult> results=new();
    private Guid? focusOwner;
    private bool awakeOwner,ownAwakeChange,disposed,executing;
    public IReadOnlyList<ActivityDefinition> Definitions => definitions;
    public IReadOnlyList<ActivityStepResult> Results => results;
    public ActivityDefinition? ActiveActivity { get; private set; }
    public string? LastError { get; private set; }
    public event Action? Changed;
    public ActivityActionsService(Store store,Countdown focus,KeepAwakeService awake,Action saveSession,Action<Exception> log,
        Action<Shortcut>? launch=null,Func<DateTimeOffset>? clock=null)
    {
        this.store=store;this.focus=focus;this.awake=awake;this.saveSession=saveSession;this.log=log;
        this.launch=launch??ShortcutService.Open;this.clock=clock??(()=>DateTimeOffset.UtcNow);
        foreach(var item in (store.Load<ActivityActionsData>("activities").Activities??new()).Take(40))
        {
            try { Validate(item);if(definitions.All(d=>d.Id!=item.Id))definitions.Add(Clone(item)); }
            catch(Exception ex) { Report(ex); }
        }
        awake.Changed+=AwakeChanged;
    }
    private void AwakeChanged(){if(!ownAwakeChange)awakeOwner=false;}
    public bool ReplacesFocus(ActivityDefinition item) => focus.Remaining(clock())>0 && item.Steps.Any(s=>s.Kind=="focus");
    public static ActivityDefinition Clone(ActivityDefinition item) => new(){Id=item.Id,Name=item.Name,Steps=item.Steps.Select(s=>new ActivityStep{Kind=s.Kind,Minutes=s.Minutes,KeepDisplayOn=s.KeepDisplayOn,Target=new(){Name=s.Target?.Name??"",Target=s.Target?.Target??"",AppId=s.Target?.AppId}}).ToList()};
    public static void Validate(ActivityDefinition item)
    {
        if(item==null || !Guid.TryParseExact(item.Id,"N",out _) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length>80)
            throw new ArgumentException("Give the activity a name of 1–80 characters.");
        if(item.Steps==null || item.Steps.Count is <1 or >20)throw new ArgumentException("Add between 1 and 20 steps.");
        if(item.Steps.Count(s=>s?.Kind=="focus")>1 || item.Steps.Count(s=>s?.Kind=="awake")>1)throw new ArgumentException("Use one Focus and one Keep awake step per activity.");
        foreach(var step in item.Steps)
        {
            if(step==null || step.Kind is not ("open" or "focus" or "awake"))throw new ArgumentException("Choose a supported activity step.");
            if(step.Kind!="open") {if(step.Minutes<1 || step.Minutes>(step.Kind=="focus"?240:1440))throw new ArgumentException(step.Kind=="focus"?"Focus duration must be 1–240 minutes.":"Keep awake duration must be 1–1440 minutes.");continue;}
            var target=step.Target;
            if(target==null || string.IsNullOrWhiteSpace(target.Name) || target.Name.Length>200)throw new ArgumentException("Choose a target and give it a name.");
            if(target.AppId!=null) {if(!WindowsAppCatalog.ValidAppId(target.AppId))throw new ArgumentException("Choose a valid installed app.");continue;}
            string value=target.Target?.Trim()??"";
            if(value.Length==0 || value.Length>4096)throw new ArgumentException("Choose a file, folder, app, or website.");
            if(Uri.TryCreate(value,UriKind.Absolute,out var uri) && !uri.IsFile) {CleanLink.ValidateUrl(value,4096,"website");continue;}
            if(!Path.IsPathFullyQualified(value) || value.IndexOfAny(new[]{'\r','\n','\0','"'})>=0)throw new ArgumentException("Choose a full file or folder path, or an HTTP/HTTPS website. Commands and arguments are not supported.");
            _=Path.GetFullPath(value);
        }
    }
    public bool Save(ActivityDefinition item)
    {
        if(disposed)return false;
        try
        {
            Validate(item);var copy=Clone(item);copy.Name=copy.Name.Trim();int index=definitions.FindIndex(d=>d.Id==copy.Id);
            var next=definitions.ToList();if(index<0){if(next.Count>=40)throw new InvalidOperationException("You can save up to 40 activities.");next.Add(copy);}else next[index]=copy;
            store.Save("activities",new ActivityActionsData{Activities=next});definitions.Clear();definitions.AddRange(next);LastError=null;Changed?.Invoke();return true;
        }
        catch(Exception ex){Report(ex);Changed?.Invoke();return false;}
    }
    public bool Remove(string id)
    {
        if(disposed)return false;
        try{var next=definitions.Where(d=>d.Id!=id).ToList();store.Save("activities",new ActivityActionsData{Activities=next});definitions.Clear();definitions.AddRange(next);LastError=null;Changed?.Invoke();return true;}
        catch(Exception ex){Report(ex);Changed?.Invoke();return false;}
    }
    public bool Start(string id,bool replaceFocusConfirmed=false)
    {
        if(disposed || executing || ActiveActivity!=null)return false;
        var definition=definitions.FirstOrDefault(d=>d.Id==id);if(definition==null)return false;
        try {Validate(definition);if(ReplacesFocus(definition) && !replaceFocusConfirmed){LastError="Confirm replacing the current Focus session first.";return false;}}
        catch(Exception ex){Report(ex);return false;}
        executing=true;ActiveActivity=Clone(definition);results.Clear();LastError=null;
        try
        {
            foreach(var step in ActiveActivity.Steps)
            {
                try
                {
                    if(step.Kind=="open")launch(step.Target);
                    else if(step.Kind=="focus") {focus.Start(step.Minutes*60,clock(),"work");focusOwner=focus.RunId;saveSession();}
                    else {ownAwakeChange=true;try{awake.Start(TimeSpan.FromMinutes(step.Minutes),step.KeepDisplayOn,clock());awakeOwner=true;}finally{ownAwakeChange=false;}}
                    results.Add(new(step.Label,true,step.Kind=="open"?"Opened":"Started"));
                }
                catch(Exception ex){Report(ex);results.Add(new(step.Label,false,ex.Message));}
            }
        }
        finally{executing=false;Changed?.Invoke();}
        return true;
    }
    public void Stop()
    {
        if(disposed || executing)return;
        LastError=null;
        if(focusOwner.HasValue && focus.RunId==focusOwner.Value){focus.Reset();try{saveSession();}catch(Exception ex){Report(ex);}}
        focusOwner=null;
        if(awakeOwner)
        {
            ownAwakeChange=true;try{awake.Stop();awakeOwner=false;}catch(Exception ex){Report(ex);Changed?.Invoke();return;}finally{ownAwakeChange=false;}
        }
        ActiveActivity=null;Changed?.Invoke();
    }
    private void Report(Exception ex){LastError=ex.Message;log(ex);}
    public void Dispose(){if(disposed)return;Stop();awake.Changed-=AwakeChanged;disposed=true;}
}
