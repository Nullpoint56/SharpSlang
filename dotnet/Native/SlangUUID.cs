using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>A "Universally Unique Identifier" (UUID)</summary>
/// <remarks>
/// <para>The Slang API uses UUIDs to identify interfaces when using <c>queryInterface</c>.</para>
/// <para>This type is compatible with the <c>GUID</c> type defined by the Component Object Model (COM), but Slang is not dependent on COM.</para>
/// </remarks>
public partial struct SlangUUID
{
    [NativeTypeName("uint32_t")]
    public uint data1;

    [NativeTypeName("uint16_t")]
    public ushort data2;

    [NativeTypeName("uint16_t")]
    public ushort data3;

    [NativeTypeName("uint8_t[8]")]
    public _data4_e__FixedBuffer data4;

    [InlineArray(8)]
    public partial struct _data4_e__FixedBuffer
    {
        public byte e0;
    }
}
