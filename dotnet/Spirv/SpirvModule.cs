// L2/L3 boundary — reading back what Slang actually emitted.
//
// This is deliberately not a SPIR-V library. It answers exactly three questions that cannot be
// answered from Slang's reflection, because they are properties of the emitted module rather than
// of the program that produced it:
//
//   1. What name did the entry point get?  Slang's reflection reports the *Slang* function name.
//      The SPIR-V OpEntryPoint name is something else entirely (see SpirvEntryPoint below), and it
//      is the one that goes into VkPipelineShaderStageCreateInfo::pName. Getting this wrong is a
//      VK_ERROR_UNKNOWN or a silently missing stage, not a compile error.
//   2. What SPIR-V version is in the header?  The target profile requests one; nothing guarantees
//      the emitted module honours it, and a module above the device's supported version fails at
//      vkCreateShaderModule with no useful message.
//   3. Is this SPIR-V at all?  A truncated or misaligned blob is worth catching at bake time.
//
// Everything here is Vulkan-neutral: SPIR-V is the target format, not the API. The Vulkan reading
// of it (which SPIR-V version a given Vulkan version accepts) lives in PrismSlang.Vulkan.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace PrismSlang.Spirv;

/// <summary>
/// A SPIR-V version, encoded exactly as the module header's second word encodes it.
/// </summary>
/// <remarks>
/// The numeric values are the header word, so comparing two of these compares the versions and no
/// conversion is needed on the way in or out.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SpirvVersion>))]
public enum SpirvVersion
{
    /// <summary>No version pinned. Slang picks; measured to be 1.5 in slang-compiler 2026.14.1.</summary>
    Unspecified = 0,

    V1_0 = 0x0001_0000,
    V1_1 = 0x0001_0100,
    V1_2 = 0x0001_0200,
    V1_3 = 0x0001_0300,
    V1_4 = 0x0001_0400,
    V1_5 = 0x0001_0500,
    V1_6 = 0x0001_0600,
}

/// <summary>Helpers over <see cref="SpirvVersion"/>.</summary>
public static class SpirvVersions
{
    /// <summary>Major component, e.g. 1.</summary>
    public static int Major(this SpirvVersion v) => ((int)v >> 16) & 0xFF;

    /// <summary>Minor component, e.g. 5.</summary>
    public static int Minor(this SpirvVersion v) => ((int)v >> 8) & 0xFF;

    /// <summary>Renders as <c>1.5</c>.</summary>
    public static string ToDisplayString(this SpirvVersion v) =>
        v == SpirvVersion.Unspecified ? "(unspecified)" : $"{v.Major()}.{v.Minor()}";

    /// <summary>
    /// The Slang target profile that pins this version, e.g. <c>spirv_1_5</c>.
    /// </summary>
    /// <remarks>
    /// Measured against slang-compiler 2026.14.1: the profile is the only knob that moves the
    /// emitted header. Passing <c>-capability spirv_1_6</c> at link time is silently ignored and
    /// still emits 1.5. See docs §9.8.
    /// </remarks>
    public static string? ToProfileName(this SpirvVersion v) =>
        v == SpirvVersion.Unspecified ? null : $"spirv_{v.Major()}_{v.Minor()}";

    /// <summary>Parses a <c>spirv_1_5</c>-style profile name back to a version, or Unspecified.</summary>
    public static SpirvVersion FromProfileName(string? profile)
    {
        if (profile is null) return SpirvVersion.Unspecified;

        const string prefix = "spirv_";
        if (!profile.StartsWith(prefix, StringComparison.Ordinal)) return SpirvVersion.Unspecified;

        var parts = profile[prefix.Length..].Split('_');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor))
        {
            return SpirvVersion.Unspecified;
        }

        var encoded = (major << 16) | (minor << 8);
        return Enum.IsDefined(typeof(SpirvVersion), encoded) ? (SpirvVersion)encoded : SpirvVersion.Unspecified;
    }
}

/// <summary>SPIR-V execution models, i.e. what a shader stage is in the emitted module.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpirvExecutionModel>))]
public enum SpirvExecutionModel : uint
{
    Vertex = 0,
    TessellationControl = 1,
    TessellationEvaluation = 2,
    Geometry = 3,
    Fragment = 4,
    GLCompute = 5,
    Kernel = 6,
    TaskNV = 5267,
    MeshNV = 5268,
    RayGenerationKHR = 5313,
    IntersectionKHR = 5314,
    AnyHitKHR = 5315,
    ClosestHitKHR = 5316,
    MissKHR = 5317,
    CallableKHR = 5318,
    TaskEXT = 5364,
    MeshEXT = 5365,
}

/// <summary>
/// One <c>OpEntryPoint</c> in an emitted module.
/// </summary>
/// <param name="ExecutionModel">The stage, as SPIR-V names it.</param>
/// <param name="Name">
/// The entry point's name <em>in the module</em> — what goes in
/// <c>VkPipelineShaderStageCreateInfo::pName</c>.
/// </param>
/// <remarks>
/// This is not the Slang function name, and assuming it is has cost people pipelines. Measured
/// against slang-compiler 2026.14.1:
/// <list type="bullet">
/// <item><c>getEntryPointCode</c> emits one module per entry point, and names it <c>main</c>
/// regardless of what the function was called.</item>
/// <item><c>getTargetCode</c> emits one module holding every entry point, necessarily under their
/// own names.</item>
/// <item>Setting <c>VulkanUseEntryPointName</c> on the session or target makes the first case use
/// the Slang name too. On a <em>link</em> it does nothing.</item>
/// </list>
/// So there is no rule to hardcode on either side. Read it from the module.
/// </remarks>
public sealed record SpirvEntryPoint(SpirvExecutionModel ExecutionModel, string Name);

/// <summary>What a SPIR-V module header and its entry point declarations say.</summary>
/// <param name="Version">Version from the header word.</param>
/// <param name="Generator">Generator magic number.</param>
/// <param name="Bound">Upper bound on result ids — a rough proxy for module size.</param>
/// <param name="EntryPoints">Every <c>OpEntryPoint</c>, in module order.</param>
public sealed record SpirvModuleInfo(
    SpirvVersion Version,
    uint Generator,
    uint Bound,
    IReadOnlyList<SpirvEntryPoint> EntryPoints);

/// <summary>Reads the few facts about an emitted SPIR-V module that reflection cannot report.</summary>
public static class SpirvModule
{
    /// <summary>The SPIR-V magic number, <c>0x07230203</c>.</summary>
    public const uint Magic = 0x07230203;

    private const int HeaderWords = 5;
    private const uint OpEntryPoint = 15;

    /// <summary>Whether this looks like a SPIR-V module at all.</summary>
    public static bool IsSpirv(ReadOnlySpan<byte> code) =>
        code.Length >= HeaderWords * 4
        && code.Length % 4 == 0
        && MemoryMarshal.Cast<byte, uint>(code)[0] == Magic;

    /// <summary>
    /// The header's version word, without walking the instruction stream. Cheap enough to call on
    /// every generated module.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a SPIR-V module.</exception>
    public static SpirvVersion ReadVersion(ReadOnlySpan<byte> code)
    {
        RequireSpirv(code);
        // Mask off the reserved high and low bytes; only major/minor are meaningful.
        return (SpirvVersion)(MemoryMarshal.Cast<byte, uint>(code)[1] & 0x00FF_FF00);
    }

    /// <summary>Header plus every <c>OpEntryPoint</c>.</summary>
    /// <exception cref="InvalidDataException">Not a SPIR-V module, or its instruction stream is malformed.</exception>
    public static SpirvModuleInfo Read(ReadOnlySpan<byte> code)
    {
        RequireSpirv(code);

        var words = MemoryMarshal.Cast<byte, uint>(code);
        var entryPoints = new List<SpirvEntryPoint>();

        var i = HeaderWords;
        while (i < words.Length)
        {
            var count = (int)(words[i] >> 16);
            var opcode = words[i] & 0xFFFF;

            if (count < 1 || i + count > words.Length)
                throw new InvalidDataException(
                    $"malformed SPIR-V at word {i}: instruction length {count} runs past the end of a " +
                    $"{words.Length}-word module");

            // OpEntryPoint: <execution model> <entry point id> <name literal> <interface ids...>
            if (opcode == OpEntryPoint && count >= 4)
            {
                entryPoints.Add(new SpirvEntryPoint(
                    (SpirvExecutionModel)words[i + 1],
                    ReadLiteralString(words.Slice(i + 3, count - 3))));
            }

            i += count;
        }

        return new SpirvModuleInfo(
            (SpirvVersion)(words[1] & 0x00FF_FF00),
            words[2],
            words[3],
            entryPoints);
    }

    /// <summary>
    /// The single entry point's name, for the common one-module-per-stage case.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Not SPIR-V, or the module declares no entry point.
    /// </exception>
    public static SpirvEntryPoint SingleEntryPoint(ReadOnlySpan<byte> code)
    {
        var info = Read(code);
        return info.EntryPoints.Count switch
        {
            1 => info.EntryPoints[0],
            0 => throw new InvalidDataException("SPIR-V module declares no entry point"),
            _ => throw new InvalidDataException(
                $"SPIR-V module declares {info.EntryPoints.Count} entry points " +
                $"({string.Join(", ", info.EntryPoints.Select(e => e.Name))}); expected exactly one"),
        };
    }

    private static void RequireSpirv(ReadOnlySpan<byte> code)
    {
        if (code.Length % 4 != 0)
            throw new InvalidDataException(
                $"not SPIR-V: {code.Length} bytes is not a whole number of 32-bit words");

        if (code.Length < HeaderWords * 4)
            throw new InvalidDataException(
                $"not SPIR-V: {code.Length} bytes is shorter than the {HeaderWords * 4}-byte header");

        var magic = MemoryMarshal.Cast<byte, uint>(code)[0];
        if (magic != Magic)
            throw new InvalidDataException($"not SPIR-V: magic 0x{magic:X8}, expected 0x{Magic:X8}");
    }

    /// <summary>
    /// A SPIR-V literal string: UTF-8 packed four bytes per word, low byte first, NUL-terminated
    /// and zero-padded to the next word boundary.
    /// </summary>
    private static string ReadLiteralString(ReadOnlySpan<uint> words)
    {
        // The operand run is the name followed by every interface id, so its length is unbounded in
        // principle. Stack for the normal case, heap for the pathological one.
        var byteCount = words.Length * 4;
        Span<byte> buffer = byteCount <= 512 ? stackalloc byte[512] : new byte[byteCount];
        buffer = buffer[..byteCount];
        var length = 0;

        foreach (var word in words)
        {
            for (var b = 0; b < 4; b++)
            {
                var c = (byte)((word >> (b * 8)) & 0xFF);
                if (c == 0) return Encoding.UTF8.GetString(buffer[..length]);
                buffer[length++] = c;
            }
        }

        // No terminator before the end of the operand run. Malformed, but the bytes are readable
        // and reporting them beats throwing on something we only use for a name.
        return Encoding.UTF8.GetString(buffer[..length]);
    }
}
