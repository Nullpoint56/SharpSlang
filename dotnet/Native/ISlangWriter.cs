using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>A stream typically of text, used for outputting diagnostic as well as other information.</summary>
[NativeTypeName("struct ISlangWriter : ISlangUnknown")]
public unsafe partial struct ISlangWriter
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangWriter*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, uint>)(lpVtbl[1]))((ISlangWriter*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, uint>)(lpVtbl[2]))((ISlangWriter*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Begin an append buffer. NOTE! Only one append buffer can be active at any time.</summary>
    /// <param name="maxNumChars">The maximum of chars that will be appended</param>
    /// <returns>The start of the buffer for appending to.</returns>
    [VtblIndex(3)]
    [return: NativeTypeName("char *")]
    public sbyte* beginAppendBuffer([NativeTypeName("size_t")] nuint maxNumChars)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, nuint, sbyte*>)(lpVtbl[3]))((ISlangWriter*)Unsafe.AsPointer(ref this), maxNumChars);
    }

    /// <summary>Ends the append buffer, and is equivalent to a write of the append buffer. NOTE! That an endAppendBuffer is not necessary if there are no characters to write.</summary>
    /// <param name="buffer">is the start of the data to append and must be identical to last value returned from beginAppendBuffer</param>
    /// <param name="numChars">must be a value less than or equal to what was returned from last call to beginAppendBuffer</param>
    /// <returns>Result, will be SLANG_OK on success</returns>
    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int endAppendBuffer([NativeTypeName("char *")] sbyte* buffer, [NativeTypeName("size_t")] nuint numChars)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, sbyte*, nuint, int>)(lpVtbl[4]))((ISlangWriter*)Unsafe.AsPointer(ref this), buffer, numChars);
    }

    /// <summary>Write text to the writer</summary>
    /// <param name="chars">The characters to write out</param>
    /// <param name="numChars">The amount of characters</param>
    /// <returns>SLANG_OK on success</returns>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int write([NativeTypeName("const char *")] sbyte* chars, [NativeTypeName("size_t")] nuint numChars)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, sbyte*, nuint, int>)(lpVtbl[5]))((ISlangWriter*)Unsafe.AsPointer(ref this), chars, numChars);
    }

    /// <summary>Flushes any content to the output</summary>
    [VtblIndex(6)]
    public void flush()
    {
        ((delegate* unmanaged[Stdcall]<ISlangWriter*, void>)(lpVtbl[6]))((ISlangWriter*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Determines if the writer stream is to the console, and can be used to alter the output</summary>
    /// <returns>Returns true if is a console writer</returns>
    [VtblIndex(7)]
    [return: NativeTypeName("SlangBool")]
    public bool isConsole()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, byte>)(lpVtbl[7]))((ISlangWriter*)Unsafe.AsPointer(ref this)) != 0;
    }

    /// <summary>Set the mode for the writer to use</summary>
    /// <param name="mode">The mode to use</param>
    /// <returns>SLANG_OK on success</returns>
    [VtblIndex(8)]
    [return: NativeTypeName("SlangResult")]
    public int setMode(SlangWriterMode mode)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, SlangWriterMode, int>)(lpVtbl[8]))((ISlangWriter*)Unsafe.AsPointer(ref this), mode);
    }
}
