using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace NabyScape;

internal static class AudioDeviceTests
{
    // Safe in normal self-tests: no endpoint switch, volume write, recording, or mute change.
    internal static void Run(Action<string,bool> check)
    {
        AudioFeedbackTests.Run(check);
        void Test(string name,Func<bool> test)
        {
            bool passed;try{passed=test();}catch{passed=false;}
            check(name,passed);
        }
        Test("Audio output values preserve device identity and display name",()=>
        {
            var output=new AudioOutput("endpoint","Speaker");
            return output.Id=="endpoint" && output.Name=="Speaker" && output==new AudioOutput("endpoint","Speaker");
        });
        Test("Output switching applies console, multimedia, and communications roles",()=>
        {
            var roles=new List<Role>();AudioOutputPolicy.ApplyRoles(roles.Add);
            return roles.SequenceEqual(new[]{Role.Console,Role.Multimedia,Role.Communications});
        });
        Test("A failed output role still attempts the remaining roles and reports failure",()=>
        {
            var roles=new List<Role>();
            try
            {
                AudioOutputPolicy.ApplyRoles(role=>{roles.Add(role);if(role==Role.Console)throw new InvalidOperationException("fixture");});
                return false;
            }
            catch(AggregateException ex)
            {
                return roles.SequenceEqual(new[]{Role.Console,Role.Multimedia,Role.Communications}) &&
                    ex.InnerExceptions.Count==1 && ex.InnerExceptions[0].InnerException?.Message=="fixture";
            }
        });
        Test("Blank output ID is rejected and logged without changing hardware",()=>
        {
            var errors=new List<Exception>();using var audio=new AudioService(errors.Add);
            return !audio.SetOutput(" ") && !string.IsNullOrWhiteSpace(audio.LastError) && errors.Any(e=>e is ArgumentException);
        });
        Test("Audio failures expose operation-specific English recovery guidance and log original exceptions",()=>
        {
            var messages=new HashSet<string>();
            foreach(var operation in Enum.GetValues<AudioOperation>())
            {
                var errors=new List<Exception>();using var audio=new AudioService(errors.Add);
                var native=new System.Runtime.InteropServices.COMException("Access denied: fixture HRESULT",unchecked((int)0x80070005));
                var error=new InvalidOperationException("Untranslated fixture failure",native);
                audio.AudioError(error,operation);
                if(audio.LastError==null || !audio.LastError.Contains("Windows Sound Settings") ||
                    audio.LastError.Contains("fixture") || !messages.Add(audio.LastError) ||
                    errors.Count!=1 || !ReferenceEquals(errors[0],error) || !ReferenceEquals(errors[0].InnerException,native))return false;
            }
            return messages.Count==Enum.GetValues<AudioOperation>().Length;
        });
        Test("Disposed audio service safely rejects hardware operations",()=>
        {
            var errors=new List<Exception>();var audio=new AudioService(errors.Add);audio.Dispose();audio.Dispose();
            audio.Read();audio.SetVolume(.5);audio.ToggleMute();
            return !audio.SetOutput("fixture") && !audio.ToggleMicrophoneMute() && audio.Outputs().Count==0 &&
                !audio.Available && !audio.MicrophoneAvailable && audio.DefaultOutputId=="" && errors.Count>0;
        });
        Test("Read-only output enumeration matches active render endpoints",()=>
        {
            using var audio=new AudioService(_=>{});using var enumerator=new MMDeviceEnumerator();
            var expected=new List<AudioOutput>();
            foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
            {
                using(device)expected.Add(new(device.ID,device.FriendlyName));
            }
            var actual=audio.Outputs();
            return audio.LastError==null && actual.Count==expected.Count && actual.Select(d=>d.Id).Distinct().Count()==actual.Count &&
                actual.All(d=>expected.Contains(d));
        });
        Test("Read-only default output matches the Windows multimedia endpoint",()=>
        {
            using var audio=new AudioService(_=>{});using var enumerator=new MMDeviceEnumerator();audio.Read();
            if(!enumerator.HasDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))
                return !audio.Available && audio.DefaultOutputId=="" && audio.Volume==0 && audio.LastError!=null;
            using var expected=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
            return audio.Available && audio.DefaultOutputId==expected.ID && audio.Device==expected.FriendlyName &&
                audio.Volume>=0 && audio.Volume<=1 && audio.Muted==expected.AudioEndpointVolume.Mute;
        });
        Test("Read-only microphone state uses communications with multimedia fallback",()=>
        {
            using var audio=new AudioService(_=>{});using var enumerator=new MMDeviceEnumerator();audio.Read();
            var role=enumerator.HasDefaultAudioEndpoint(DataFlow.Capture,Role.Communications)?Role.Communications:Role.Multimedia;
            if(!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture,role))
                return !audio.MicrophoneAvailable && !audio.MicrophoneMuted && audio.MicrophoneName=="No microphone" && audio.LastError!=null;
            using var expected=enumerator.GetDefaultAudioEndpoint(DataFlow.Capture,role);
            return audio.MicrophoneAvailable && audio.MicrophoneName==expected.FriendlyName && audio.MicrophoneMuted==expected.AudioEndpointVolume.Mute;
        });
        Test("Repeated audio reads and disposal leave cached device state consistent",()=>
        {
            var audio=new AudioService(_=>{});audio.Read();audio.Read();
            bool valid=audio.Available?audio.DefaultOutputId.Length>0:audio.DefaultOutputId=="";
            audio.Dispose();return valid && !audio.Available && !audio.MicrophoneAvailable && audio.Peak()==0;
        });
    }
}
