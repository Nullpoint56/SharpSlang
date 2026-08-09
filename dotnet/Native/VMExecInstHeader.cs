using System.Runtime.CompilerServices;
namespace PrismSlang.Native;

public unsafe partial struct VMExecInstHeader
{
    [NativeTypeName("slang::VMExtFunction")]
    public delegate* unmanaged[Cdecl]<IByteCodeRunner*, VMExecInstHeader*, void*, void> functionPtr;

    [NativeTypeName("uint32_t")]
    public uint opcodeExtension;

    [NativeTypeName("uint32_t")]
    public uint operandCount;

    [return: NativeTypeName("slang::VMExecInstHeader *")]
    public VMExecInstHeader* getNextInst()
    {
        return (VMExecInstHeader*)((VMExecOperand*)((VMExecInstHeader*)Unsafe.AsPointer(ref this) + 1) + operandCount);
    }

    [return: NativeTypeName("slang::VMExecOperand &")]
    public readonly VMExecOperand* getOperand([NativeTypeName("SlangInt")] long index)
    {
        return ((VMExecOperand*)((VMExecInstHeader*)Unsafe.AsPointer(ref Unsafe.AsRef(in this)) + 1) + index);
    }
}
