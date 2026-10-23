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
            return new(false,false,"Choose a chat connection","Add an API provider or connect Hermes, then save a chat model.");
        else if(connectionChecks.TryGetValue(chat.Id,out var check) && check.Configuration==ConnectionIdentity(chat))
            return new(true,check.Success,check.Success?"Ready · chat checked":"Connection needs attention",$"{check.Detail}\nChecked {check.Checked:HH:mm} on this device. Voice and tools are checked separately.");
        return new(true,false,"Saved · not tested",chat.Name+" · "+chat.Model+"\nCheck the saved connection to confirm this model can reply.");
    }
    private AssistantSetupReadiness AssessSetup()
    {
        var text=ChatSetupFor(Provider);
        AssistantSetupState speech;
        var local=Options.LocalVoice;
        if(Speech.Busy)speech=new(false,false,"Updating speech components",Speech.Status);
        else if(!local.Enabled)
        {
            var provider=VoiceProvider;
            bool configured=provider!=null && !string.IsNullOrWhiteSpace(provider.RealtimeUrl) && !string.IsNullOrWhiteSpace(provider.RealtimeModel);
            speech=new(configured,false,configured?"Saved · voice not tested":"Choose a voice connection",configured?"The saved realtime model is "+provider!.RealtimeModel+". Start a conversation to connect; the microphone is off until then.":"Choose automatic local speech, your own models, or a realtime provider. Voice is optional.");
        }
        else if(!text.Configured)speech=new(false,false,"A chat model is still needed","Local speech listens and speaks on this PC. An API provider or Hermes generates the reply.");
        else if(local.Managed)
        {
            try
            {
                var asr=AssistantOwnSpeech.Asr(local);var tts=AssistantOwnSpeech.Tts(local);
                bool installed=Speech.Installed(asr,tts,local.ManagedGpu);
                speech=new(installed,false,installed?"Installed · speech not tested":"Finish local speech setup",installed?asr.Name+" + "+tts.Name+"\nFiles and engines are present. Hardware is checked again when you start voice.":"Install the selected pack or prepare your model files in Voice setup.");
            }
            catch(Exception){speech=new(false,false,"Check your model files","Open Voice setup to select valid listening and speaking models and prepare their engines.");}
        }
        else
        {
            try
            {
                LocalAssistantVoice.Endpoint(local.AsrUrl);if(local.TtsEngine!="Windows voice")LocalAssistantVoice.Endpoint(local.TtsUrl);
                speech=new(true,false,"External speech configured","Start your local services and use Check local speech services in Voice setup. Saving an address does not start or verify a model.");
            }
            catch(Exception){speech=new(false,false,"Check local service addresses","Use valid localhost addresses for listening and speaking in Voice setup.");}
        }
        return new(text,speech);
    }
}
