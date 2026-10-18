using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;

namespace NabyScape;

internal interface IAssistantKeys
{
    string? Read(string id);
    bool Exists(string id);
    void Write(string id,string value);
    void Remove(string id);
}

internal sealed class AssistantMemoryKeys : IAssistantKeys
{
    private readonly Dictionary<string,string> values=new();
    public bool Exists(string id)=>values.ContainsKey(id);
    public string? Read(string id)=>values.GetValueOrDefault(id);
    public void Write(string id,string value)=>values[id]=value;
    public void Remove(string id)=>values.Remove(id);
}

// Generic credentials belong to the current Windows user, never settings/layout JSON.
internal sealed class AssistantKeys : IAssistantKeys
{
    private static string Target(string id)
    {
        if(id.Length!=32 || !Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid provider identity.");
        return "NabyScape/Assistant/"+id;
    }
    public bool Exists(string id)
    {if(!CredRead(Target(id),1,0,out var pointer))return false;CredFree(pointer);return true;}
    public string? Read(string id)
    {
        if(!CredRead(Target(id),1,0,out var pointer))return null;
        try
        {
            var credential=Marshal.PtrToStructure<Credential>(pointer);
            if(credential.Size>2560)throw new InvalidOperationException("The stored credential is invalid.");
            var bytes=new byte[credential.Size];Marshal.Copy(credential.Blob,bytes,0,bytes.Length);
            try{return Encoding.UTF8.GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        finally{CredFree(pointer);}
    }
    public void Write(string id,string value)
    {
        var bytes=Encoding.UTF8.GetBytes(value.Trim());
        if(bytes.Length==0 || bytes.Length>2560 || value.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new ArgumentException("Enter a valid API key.");
        var pointer=Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes,0,pointer,bytes.Length);
            var credential=new Credential{Type=1,TargetName=Target(id),Size=(uint)bytes.Length,Blob=pointer,Persist=2,UserName="NabyScape"};
            if(!CredWrite(ref credential,0))throw new InvalidOperationException("Windows could not save the API key.");
        }
        finally{Marshal.Copy(new byte[bytes.Length],0,pointer,bytes.Length);Marshal.FreeHGlobal(pointer);CryptographicOperations.ZeroMemory(bytes);}
    }
    public void Remove(string id)
    {if(!CredDelete(Target(id),1,0) && Marshal.GetLastWin32Error()!=1168)throw new InvalidOperationException("Windows could not remove the API key.");}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Credential
    {public uint Flags,Type;public string TargetName;public string? Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint Size;public IntPtr Blob;public uint Persist,AttributeCount;public IntPtr Attributes;public string? Alias,UserName;}
    [DllImport("advapi32",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32")]private static extern void CredFree(IntPtr credential);
}
