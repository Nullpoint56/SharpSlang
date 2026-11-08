using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>Description of a Slang global session.</summary>
public partial struct SlangGlobalSessionDesc
{
    /// <summary>Size of this struct.</summary>
    [NativeTypeName("uint32_t")]
    public uint structureSize;

    /// <summary>Slang API version.</summary>
    [NativeTypeName("uint32_t")]
    public uint apiVersion;

    /// <summary>Specify the oldest Slang language version that any sessions will use.</summary>
    [NativeTypeName("uint32_t")]
    public uint minLanguageVersion;

    /// <summary>Whether to enable GLSL support.</summary>
    [NativeTypeName("bool")]
    public byte enableGLSL;

    /// <summary>Reserved for future use.</summary>
    [NativeTypeName("uint32_t[16]")]
    public _reserved_e__FixedBuffer reserved;

    [InlineArray(16)]
    public partial struct _reserved_e__FixedBuffer
    {
        public uint e0;
    }
}
