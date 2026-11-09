using System;
using System.Collections.Generic;

namespace NabyScape;

internal sealed record AssistantSetupState(bool Configured,bool Verified,string Title,string Detail);
internal sealed record AssistantSetupReadiness(AssistantSetupState Chat,AssistantSetupState Voice);

internal sealed partial class AssistantRuntime
{
    private sealed record ConnectionCheck(string Configuration,bool Success,DateTimeOffset Checked,string Detail);
    private readonly Dictionary<string,ConnectionCheck> connectionChecks=new();
    private AssistantSetupReadiness? setupCache;
    internal static string ConnectionIdentity(AssistantProvider p)=>string.Join("\n",p.Id,p.Backend,p.Protocol,p.BaseUrl,p.Model);
    internal AssistantSetupReadiness Setup=>setupCache??=AssessSetup();
    private void SetupChanged(){setupCache=null;Notify();}
    private void SpeechSetupChanged()=>Dispatch(SetupChanged);
    internal void RecordConnectionCheck(AssistantProvider provider,bool success,string detail)
    {
        if(connectionChecks.Count>=12 && !connectionChecks.ContainsKey(provider.Id))connectionChecks.Clear();
        connectionChecks[provider.Id]=new(ConnectionIdentity(provider),success,DateTimeOffset.Now,detail);SetupChanged();
    }
    internal void ForgetConnectionCheck(string id){connectionChecks.Remove(id);setupCache=null;}
    internal AssistantSetupState ChatSetupFor(AssistantProvider? chat)
    {
        if(chat==null || string.IsNullOrWhiteSpace(chat.BaseUrl) || string.IsNullOrWhiteSpace(chat.Model))
            return new(false,false,Localization.Text("Choose a chat connection"),Localization.Text("Add an API provider or connect Hermes, then save a chat model."));
        else if(connectionChecks.TryGetValue(chat.Id,out var check) && check.Configuration==ConnectionIdentity(chat))
            return new(true,check.Success,check.Success?Localization.Text("Ready · chat checked"):Localization.Text("Connection needs attention"),Localization.Format("{0}\nChecked {1} on this device. Voice and tools are checked separately.",check.Detail,check.Checked.ToString("HH:mm")));
        return new(true,false,Localization.Text("Saved · not tested"),chat.Name+" · "+chat.Model+Localization.Text("\nCheck the saved connection to confirm this model can reply."));
    }
    private AssistantSetupReadiness AssessSetup()
    {
        var text=ChatSetupFor(Provider);
        AssistantSetupState speech;
        var local=Options.LocalVoice;
        if(Speech.Busy)speech=new(false,false,Localization.Text("Updating speech components"),Speech.Status);
        else if(!local.Enabled)
        {
            var provider=VoiceProvider;
            bool configured=provider!=null && !string.IsNullOrWhiteSpace(provider.RealtimeUrl) && !string.IsNullOrWhiteSpace(provider.RealtimeModel);
            speech=new(configured,false,configured?Localization.Text("Saved · voice not tested"):Localization.Text("Choose a voice connection"),configured?Localization.Text("The saved realtime model is ")+provider!.RealtimeModel+Localization.Text(". Start a conversation to connect; the microphone is off until then."):Localization.Text("Choose automatic local speech, your own models, or a realtime provider. Voice is optional."));
        }
        else if(!text.Configured)speech=new(false,false,Localization.Text("A chat model is still needed"),Localization.Text("Local speech listens and speaks on this PC. An API provider or Hermes generates the reply."));
        else if(local.Managed)
        {
            try
            {
                var asr=AssistantOwnSpeech.Asr(local);var tts=AssistantOwnSpeech.Tts(local);
                bool installed=Speech.Installed(asr,tts,local.ManagedGpu);
                speech=new(installed,false,installed?Localization.Text("Installed · speech not tested"):Localization.Text("Finish local speech setup"),installed?asr.Name+" + "+tts.Name+Localization.Text("\nFiles and engines are present. Hardware is checked again when you start voice."):Localization.Text("Install the selected pack or prepare your model files in Voice setup."));
            }
            catch(Exception){speech=new(false,false,Localization.Text("Check your model files"),Localization.Text("Open Voice setup to select valid listening and speaking models and prepare their engines."));}
        }
        else
        {
            try
            {
                LocalAssistantVoice.Endpoint(local.AsrUrl);if(local.TtsEngine!="Windows voice")LocalAssistantVoice.Endpoint(local.TtsUrl);
                speech=new(true,false,Localization.Text("External speech configured"),Localization.Text("Start your local services and use Check local speech services in Voice setup. Saving an address does not start or verify a model."));
            }
            catch(Exception){speech=new(false,false,Localization.Text("Check local service addresses"),Localization.Text("Use valid localhost addresses for listening and speaking in Voice setup."));}
        }
        return new(text,speech);
    }
}
