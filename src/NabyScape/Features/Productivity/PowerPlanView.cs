using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;
internal sealed record PowerPlan(Guid Id,string Name,bool Active)
{
    public override string ToString()=>Name+(Active?" · Active":"");
}
internal sealed class PowerPlanView : StackPanel
{
    private readonly ComboBox choices=new(){MinWidth=180,Margin=new(0,12,0,8)};
    private readonly TextBlock status=UI.Text("",12,UI.Muted);
    private readonly Button apply,refresh;
    private CancellationTokenSource? lifetime;
    internal PowerPlanView()
    {
        Children.Add(UI.Heading("Power plan",19));Children.Add(UI.Text("Choose from the power plans available on this PC.",12,UI.Muted));
        UI.Named(choices,"Available Windows power plans");Children.Add(choices);
        apply=UI.Button("Apply power plan",async()=>await Apply());refresh=UI.Button("Refresh plans",async()=>await Refresh());Children.Add(apply);Children.Add(refresh);Children.Add(status);
        Loaded+=async(_,_)=>{lifetime=new();await Refresh();};Unloaded+=(_,_)=>{lifetime?.Cancel();};
    }
    internal static PowerPlan[] Parse(string text)
    {
        var plans=new List<PowerPlan>();
        foreach(string line in text.Split('\n'))
        {
            var match=Regex.Match(line,@"([0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12})\s+\((.*)\)\s*(\*)?",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            if(match.Success && Guid.TryParse(match.Groups[1].Value,out var id))plans.Add(new(id,match.Groups[2].Value,match.Groups[3].Success));
        }
        return plans.ToArray();
    }
    private static async Task<string> Run(string argument,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var process=new Process{StartInfo=new(){FileName=System.IO.Path.Combine(Environment.SystemDirectory,"powercfg.exe"),Arguments=argument,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        process.Start();
        try
        {
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);string result=await output,detail=await error;
            if(process.ExitCode!=0)throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)?"Windows could not change this power plan.":detail.Trim());
            return result;
        }
        catch(OperationCanceledException){try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}throw;}
    }
    private void Busy(bool value){apply.IsEnabled=refresh.IsEnabled=choices.IsEnabled=!value;}
    private async Task Refresh()
    {
        if(lifetime==null)return;Busy(true);
        try
        {
            var plans=Parse(await Run("/list",lifetime.Token));if(lifetime.IsCancellationRequested)return;
            choices.ItemsSource=plans;choices.SelectedItem=Array.Find(plans,p=>p.Active);if(choices.SelectedIndex<0 && plans.Length>0)choices.SelectedIndex=0;
            status.Text=plans.Length==0?"No selectable plans were returned by Windows.":plans.Length==1?"This PC exposes one power plan. Additional power modes may be managed by Windows or your manufacturer.":"Select a plan, then Apply. Changes affect the whole PC.";
        }
        catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text="Windows took too long to respond. Try Refresh.";}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)status.Text=ex.Message;}
        finally{Busy(false);}
    }
    private async Task Apply()
    {
        if(lifetime==null || choices.SelectedItem is not PowerPlan selected)return;Busy(true);
        try{await Run("/setactive "+selected.Id.ToString("D"),lifetime.Token);if(lifetime.IsCancellationRequested)return;await Refresh();status.Text="Applied · "+selected.Name;}
        catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text="Change timed out. Refresh to check the active plan.";}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)status.Text="Not applied · "+ex.Message;}
        finally{Busy(false);}
    }
}
