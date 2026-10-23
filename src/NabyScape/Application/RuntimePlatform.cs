using System;
using System.Runtime.InteropServices;

namespace NabyScape;

internal static class RuntimePlatform
{
    // Update the running binary's architecture, including an x64 binary under emulation.
    internal static string Rid=>RuntimeInformation.ProcessArchitecture switch {Architecture.X64=>"win-x64",Architecture.Arm64=>"win-arm64",_=>throw new PlatformNotSupportedException("NabyScape requires Windows x64 or ARM64.")};
    internal static ushort PeMachine=>RuntimeInformation.ProcessArchitecture switch {Architecture.X64=>0x8664,Architecture.Arm64=>0xAA64,_=>throw new PlatformNotSupportedException("Unsupported application architecture.")};
    internal static string SetupAsset=>"NabyScape-"+Rid+"-Setup.exe";
}
