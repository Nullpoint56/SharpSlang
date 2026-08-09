using System.Runtime.InteropServices;

namespace PrismSlang.Native;

[StructLayout(LayoutKind.Explicit)]
public unsafe partial struct SlangReflectionGenericArg
{
    [FieldOffset(0)]
    public SlangReflectionType* typeVal;

    [FieldOffset(0)]
    [NativeTypeName("int64_t")]
    public long intVal;

    [FieldOffset(0)]
    [NativeTypeName("bool")]
    public byte boolVal;
}
