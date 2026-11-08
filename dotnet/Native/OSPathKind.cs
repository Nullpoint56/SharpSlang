namespace SharpSlang.Native;

/// <summary>Determines how paths map to files on the OS file system</summary>
[NativeTypeName("uint8_t")]
public enum OSPathKind : byte
{
    /// <summary>Paths do not map to the file system</summary>
    None = 0,
    /// <summary>Paths map directly to the file system</summary>
    Direct = 1,
    /// <summary>Only paths gained via PathKind::OperatingSystem map to the</summary>
    /// <remarks>
    /// <para>operating system file system</para>
    /// </remarks>
    OperatingSystem = 2,
}
