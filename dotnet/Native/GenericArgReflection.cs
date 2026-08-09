using System.Runtime.InteropServices;

namespace PrismSlang.Native;

[StructLayout(LayoutKind.Explicit)]
public unsafe partial struct GenericArgReflection
{
    [FieldOffset(0)]
    [NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* typeVal;

    [FieldOffset(0)]
    [NativeTypeName("int64_t")]
    public long intVal;

    [FieldOffset(0)]
    [NativeTypeName("bool")]
    public byte boolVal;
}
