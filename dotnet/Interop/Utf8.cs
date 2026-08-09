// L1 — the smallest slice of interop hygiene the Tier 0 spike needs.
//
// The generated surface is `sbyte*` end to end: Slang takes and returns UTF-8 C strings
// everywhere. Nothing in the generated layer converts them, and marshalling helpers are not
// something a binding generator can infer.

using System.Runtime.InteropServices;
using System.Text;

namespace PrismSlang.Interop;

/// <summary>UTF-8 conversions across the native boundary.</summary>
internal static unsafe class Utf8
{
    /// <summary>
    /// Allocates a NUL-terminated UTF-8 copy of <paramref name="value"/> in unmanaged memory.
    /// Caller frees with <see cref="Free"/>. Returns null for a null input, which Slang reads as
    /// "not supplied" for optional parameters.
    /// </summary>
    internal static sbyte* Alloc(string? value)
    {
        if (value is null) return null;

        var count = Encoding.UTF8.GetByteCount(value);
        var p = (byte*)NativeMemory.Alloc((nuint)count + 1);
        fixed (char* src = value)
        {
            Encoding.UTF8.GetBytes(src, value.Length, p, count);
        }
        p[count] = 0;
        return (sbyte*)p;
    }

    internal static void Free(sbyte* p)
    {
        if (p is not null) NativeMemory.Free(p);
    }

    /// <summary>Reads a NUL-terminated UTF-8 string produced by Slang. Null in, null out.</summary>
    internal static string? Read(sbyte* p) => p is null ? null : Marshal.PtrToStringUTF8((nint)p);

    /// <summary>
    /// Scoped allocation, so call sites read as ordinary C# rather than try/finally ladders.
    /// </summary>
    internal readonly ref struct Scoped
    {
        private readonly sbyte* _p;

        internal Scoped(string? value) => _p = Alloc(value);

        public static implicit operator sbyte*(Scoped s) => s._p;

        public void Dispose() => Free(_p);
    }
}
