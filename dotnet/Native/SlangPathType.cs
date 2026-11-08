namespace SharpSlang.Native;

/// <summary>Type that identifies how a path should be interpreted</summary>
[NativeTypeName("SlangPathTypeIntegral")]
public enum SlangPathType : uint
{
    /// <summary>Path specified specifies a directory.</summary>
    SLANG_PATH_TYPE_DIRECTORY = 0,
    /// <summary>Path specified is to a file.</summary>
    SLANG_PATH_TYPE_FILE = 1,
}
