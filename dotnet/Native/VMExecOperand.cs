namespace PrismSlang.Native;

public unsafe partial struct VMExecOperand
{
    [NativeTypeName("uint8_t **")]
    public byte** section;

    public uint _bitfield;

    [NativeTypeName("uint32_t : 8")]
    public uint type
    {
        readonly get
        {
            return _bitfield & 0xFFu;
        }

        set
        {
            _bitfield = (_bitfield & ~0xFFu) | (value & 0xFFu);
        }
    }

    [NativeTypeName("uint32_t : 24")]
    public uint size
    {
        readonly get
        {
            return (_bitfield >> 8) & 0xFFFFFFu;
        }

        set
        {
            _bitfield = (_bitfield & ~(0xFFFFFFu << 8)) | ((value & 0xFFFFFFu) << 8);
        }
    }

    [NativeTypeName("uint32_t")]
    public uint offset;

    public readonly void* getPtr()
    {
        return *section + offset;
    }

    [return: NativeTypeName("slang::OperandDataType")]
    public readonly OperandDataType getType()
    {
        return (OperandDataType)(type);
    }
}
