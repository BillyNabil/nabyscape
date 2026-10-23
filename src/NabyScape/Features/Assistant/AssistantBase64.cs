using System;
using System.Buffers.Text;

namespace NabyScape;

internal static class AssistantBase64
{
    internal static bool IsValid(ReadOnlySpan<char> text)
    {
        if(Base64.IsValid(text))return true;
        // Convert accepts unused nonzero pad bits (e.g. AR==); Base64.IsValid
        // deliberately rejects them. Validate that final quartet with Convert
        // using only three stack bytes, retaining the existing wire acceptance.
        Span<char> quartet=stackalloc char[4];int count=0,start=text.Length;
        while(start>0 && count<4)
        {
            char c=text[--start];if(c is ' ' or '\t' or '\r' or '\n')continue;
            quartet[3-count++]=c;
        }
        if(count!=4)return false;
        var prefix=text[..start];
        if(prefix.Contains('=') || !Base64.IsValid(prefix))return false;
        Span<byte> decoded=stackalloc byte[3];
        return Convert.TryFromBase64Chars(quartet,decoded,out _);
    }
}
