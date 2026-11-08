namespace SharpSlang.Native;

/// <summary>Defines an archive type used to holds a 'file system' type structure.</summary>
[NativeTypeName("SlangArchiveTypeIntegral")]
public enum SlangArchiveType
{
    SLANG_ARCHIVE_TYPE_UNDEFINED = 0,
    SLANG_ARCHIVE_TYPE_ZIP = 1,
    /// <summary>Riff container with no compression</summary>
    SLANG_ARCHIVE_TYPE_RIFF = 2,
    SLANG_ARCHIVE_TYPE_RIFF_DEFLATE = 3,
    SLANG_ARCHIVE_TYPE_RIFF_LZ4 = 4,
    SLANG_ARCHIVE_TYPE_COUNT_OF,
}
