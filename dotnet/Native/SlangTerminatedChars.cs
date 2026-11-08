using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpSlang.Native;

/// <summary>Can be requested from ISlangCastable cast to indicate the contained chars are null terminated.</summary>
public unsafe partial struct SlangTerminatedChars
{
    [NativeTypeName("char[1]")]
    public _chars_e__FixedBuffer chars;

    [return: NativeTypeName("const char *")]
    public readonly sbyte* ToSBytePointer()
    {
        return (sbyte*)Unsafe.AsPointer(ref Unsafe.AsRef(in chars));
    }

    public partial struct _chars_e__FixedBuffer
    {
        public sbyte e0;

        [UnscopedRef]
        public ref sbyte this[int index] => ref Unsafe.Add(ref e0, index);

        [UnscopedRef]
        public Span<sbyte> AsSpan(int length) => MemoryMarshal.CreateSpan(ref e0, length);
    }
}
