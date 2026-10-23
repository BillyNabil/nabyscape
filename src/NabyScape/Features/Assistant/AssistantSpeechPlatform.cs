using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace NabyScape;

internal static class AssistantSpeechPlatform
{
    internal static bool NativePacksSupported=>RuntimeInformation.ProcessArchitecture==Architecture.X64 && RuntimeInformation.OSArchitecture==Architecture.X64 && Avx2.IsSupported && Fma.IsSupported;
    internal const string NativePackUnavailable="Built-in speech packs require a native Windows x64 process and CPU with AVX2/FMA. On ARM64, use external local ASR/TTS services; Windows voice and wake word require compatible installed Windows speech engines.";
    internal static void RequireNativePacks(){if(!NativePacksSupported)throw new PlatformNotSupportedException(NativePackUnavailable);}
}
