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
// of it (which SPIR-V version a given Vulkan version accepts) is the host's.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;

namespace SharpSlang.Spirv;

// SpirvVersion and its helpers are in SharpSlang.Descs (descs/SpirvVersion.cs): the binding
// names them too, and neither assembly references the other (AGENTS.md#layers).

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
/// One dimension of a compute workgroup.
/// </summary>
/// <param name="Value">
/// The size, or — when <paramref name="SpecializationConstantId"/> is set — the default the shader
/// declared for the constant that drives it.
/// </param>
/// <param name="SpecializationConstantId">
/// The <c>constant_id</c> whose value decides this dimension at pipeline creation, if one does.
/// </param>
public readonly record struct SpirvWorkgroupDimension(uint Value, uint? SpecializationConstantId = null)
{
    /// <summary>Whether the host decides this dimension when it creates the pipeline.</summary>
    public bool IsSpecialized => SpecializationConstantId is not null;

    public override string ToString() =>
        IsSpecialized ? $"spec({SpecializationConstantId}, default {Value})" : Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A compute entry point's workgroup size, as the module declares it.
/// </summary>
/// <remarks>
/// The reason this is read from the module rather than taken from reflection: with
/// <c>[numthreads(kGroupX, 1, 1)]</c> driven by a specialization constant, Slang's
/// <c>getComputeThreadGroupSize</c> reports <c>0</c> for that dimension. The module says what is
/// actually going on — <c>OpExecutionModeId LocalSizeId</c> naming the constants, against
/// <c>OpExecutionMode LocalSize</c> with literals for a fixed size — so the constant ids and the
/// declared defaults are both recoverable here and nowhere else.
/// </remarks>
public sealed record SpirvWorkgroupSize(
    SpirvWorkgroupDimension X,
    SpirvWorkgroupDimension Y,
    SpirvWorkgroupDimension Z)
{
    /// <summary>Whether any dimension is decided at pipeline creation.</summary>
    public bool IsSpecialized => X.IsSpecialized || Y.IsSpecialized || Z.IsSpecialized;

    /// <summary>
    /// The workgroup size once the host's specialization values are applied.
    /// </summary>
    /// <param name="specializationValues">
    /// Constant id to value, as the host will supply them in <c>VkSpecializationInfo</c>. A
    /// dimension whose constant is absent keeps the default declared in the shader, which is what
    /// Vulkan does with an unsupplied constant.
    /// </param>
    public (uint X, uint Y, uint Z) Resolve(IReadOnlyDictionary<uint, uint>? specializationValues = null) =>
        (Apply(X, specializationValues), Apply(Y, specializationValues), Apply(Z, specializationValues));

    private static uint Apply(
        SpirvWorkgroupDimension dimension,
        IReadOnlyDictionary<uint, uint>? values) =>
        dimension.SpecializationConstantId is { } id
        && values is not null
        && values.TryGetValue(id, out var supplied)
            ? supplied
            : dimension.Value;

    public override string ToString() => $"[{X}, {Y}, {Z}]";
}

/// <summary>
/// One <c>OpEntryPoint</c> in an emitted module.
/// </summary>
/// <param name="ExecutionModel">The stage, as SPIR-V names it.</param>
/// <param name="Name">
/// The entry point's name <em>in the module</em> — what goes in
/// <c>VkPipelineShaderStageCreateInfo::pName</c>.
/// </param>
/// <param name="WorkgroupSize">
/// The compute workgroup size, for a compute, task or mesh entry point. Null for the raster and
/// ray-tracing stages, which declare none.
/// </param>
/// <remarks>
/// This is not the Slang function name, and assuming it is has cost people pipelines. Measured
/// against slang-compiler 2026.21:
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
public sealed record SpirvEntryPoint(
    SpirvExecutionModel ExecutionModel,
    string Name,
    SpirvWorkgroupSize? WorkgroupSize = null);

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
    private const uint OpExecutionMode = 16;
    private const uint OpDecorate = 71;
    private const uint OpConstant = 43;
    private const uint OpSpecConstant = 50;
    private const uint OpExecutionModeId = 331;

    private const uint ExecutionModeLocalSize = 17;
    private const uint ExecutionModeLocalSizeId = 38;
    private const uint DecorationSpecId = 1;

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

        // Collected by id and resolved at the end: the execution mode that carries the workgroup
        // size appears near the top of the module, while the constants it names are declared much
        // further down, so nothing can be resolved in a single forward pass.
        var declared = new List<(uint Id, SpirvExecutionModel Model, string Name)>();
        var literalSize = new Dictionary<uint, (uint X, uint Y, uint Z)>();
        var idSize = new Dictionary<uint, (uint X, uint Y, uint Z)>();
        var constants = new Dictionary<uint, uint>();
        var specIds = new Dictionary<uint, uint>();

        var i = HeaderWords;
        while (i < words.Length)
        {
            var count = (int)(words[i] >> 16);
            var opcode = words[i] & 0xFFFF;

            if (count < 1 || i + count > words.Length)
                throw new InvalidDataException(
                    $"malformed SPIR-V at word {i}: instruction length {count} runs past the end of a " +
                    $"{words.Length}-word module");

            switch (opcode)
            {
                // OpEntryPoint: <execution model> <entry point id> <name literal> <interface ids...>
                case OpEntryPoint when count >= 4:
                    declared.Add((
                        words[i + 2],
                        (SpirvExecutionModel)words[i + 1],
                        ReadLiteralString(words.Slice(i + 3, count - 3))));
                    break;

                // OpExecutionMode: <entry point id> <mode> <literals...>
                case OpExecutionMode when count >= 6 && words[i + 2] == ExecutionModeLocalSize:
                    literalSize[words[i + 1]] = (words[i + 3], words[i + 4], words[i + 5]);
                    break;

                // OpExecutionModeId: <entry point id> <mode> <ids...>
                case OpExecutionModeId when count >= 6 && words[i + 2] == ExecutionModeLocalSizeId:
                    idSize[words[i + 1]] = (words[i + 3], words[i + 4], words[i + 5]);
                    break;

                // OpConstant / OpSpecConstant: <result type> <result id> <value words...>. Only the
                // first value word is read: a workgroup dimension is a 32-bit integer.
                case OpConstant or OpSpecConstant when count >= 4:
                    constants[words[i + 2]] = words[i + 3];
                    break;

                case OpDecorate when count >= 4 && words[i + 2] == DecorationSpecId:
                    specIds[words[i + 1]] = words[i + 3];
                    break;
            }

            i += count;
        }

        var entryPoints = declared
            .Select(e => new SpirvEntryPoint(
                e.Model,
                e.Name,
                WorkgroupSizeOf(e.Id, literalSize, idSize, constants, specIds)))
            .ToList();

        return new SpirvModuleInfo(
            (SpirvVersion)(words[1] & 0x00FF_FF00),
            words[2],
            words[3],
            entryPoints);
    }

    /// <summary>The workgroup size declared for one entry point id, or null if it declares none.</summary>
    /// <remarks>
    /// Two spellings, and which one appears is decided by the shader rather than by the target.
    /// <c>LocalSize</c> carries three literals. <c>LocalSizeId</c> carries three result ids, each
    /// either an <c>OpConstant</c> — a dimension that just happened to be written as a constant —
    /// or an <c>OpSpecConstant</c> carrying its declared default and a <c>SpecId</c> decoration
    /// naming the <c>constant_id</c> the host will supply.
    /// </remarks>
    private static SpirvWorkgroupSize? WorkgroupSizeOf(
        uint entryPoint,
        Dictionary<uint, (uint X, uint Y, uint Z)> literalSize,
        Dictionary<uint, (uint X, uint Y, uint Z)> idSize,
        Dictionary<uint, uint> constants,
        Dictionary<uint, uint> specIds)
    {
        if (literalSize.TryGetValue(entryPoint, out var literal))
        {
            return new SpirvWorkgroupSize(
                new SpirvWorkgroupDimension(literal.X),
                new SpirvWorkgroupDimension(literal.Y),
                new SpirvWorkgroupDimension(literal.Z));
        }

        if (!idSize.TryGetValue(entryPoint, out var ids)) return null;

        return new SpirvWorkgroupSize(Dimension(ids.X), Dimension(ids.Y), Dimension(ids.Z));

        SpirvWorkgroupDimension Dimension(uint id) => new(
            constants.GetValueOrDefault(id),
            specIds.TryGetValue(id, out var spec) ? spec : null);
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
