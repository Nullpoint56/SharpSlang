// Tier 0 spike, on the L2 API.
//
// Originally written against raw vtable pointers to prove the path worked (docs §8). Ported here
// to the managed API — which is also the honest test of whether L2 is actually pleasant to use.
// Compare against git history: the pointer plumbing, manual release() calls, and stackalloc
// component arrays are all gone.

using System.Runtime.InteropServices;
using PrismSlang;
using PrismSlang.Interop;
using PrismSlang.Native;
using PrismSlang.Reflection;

const string Source = """
    // A constant buffer, a texture, a sampler, varyings, and two stages — enough that Tier 1
    // reflection has something real to walk.
    struct Uniforms
    {
        float4x4 mvp;
        float3   tint;
        float    exposure;
    };
    ConstantBuffer<Uniforms> u;

    Texture2D    albedo;
    SamplerState albedoSampler;

    struct VSOut
    {
        float4 pos : SV_Position;
        float2 uv  : TEXCOORD0;
    };

    [shader("vertex")]
    VSOut vertexMain(float3 position, float2 uv)
    {
        VSOut o;
        o.pos = mul(u.mvp, float4(position, 1.0));
        o.uv  = uv;
        return o;
    }

    [shader("fragment")]
    float4 fragmentMain(VSOut i) : SV_Target
    {
        float3 c = albedo.Sample(albedoSampler, i.uv).rgb * u.tint * u.exposure;
        return float4(c, 1.0);
    }
    """;

try
{
    Console.WriteLine($"slang-compiler {SlangGlobalSession.BuildTag}");

    using var global = SlangGlobalSession.Create();
    using var session = global.CreateSession(new SessionOptions
    {
        Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        MatrixLayout = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
    });

    using var module = session.LoadModuleFromSource("spike", "spike.slang", Source);
    Console.WriteLine($"  module '{module.Name}' — {module.EntryPointCount} entry points");

    using var vertex = module.FindEntryPoint("vertexMain");
    using var fragment = module.FindEntryPoint("fragmentMain");

    using var program = session.Link(module, vertex, fragment);
    Console.WriteLine("  linked");

    var failures = 0;
    for (var i = 0; i < program.EntryPointNames.Count; i++)
    {
        using var code = program.GetEntryPointCode(i);
        if (!Validate(code.Span, program.EntryPointNames[i])) failures++;
    }

    // Tier 1 — walk the reflection. The snapshot is immutable managed data and outlives `program`.
    var layout = program.Reflect();

    Console.WriteLine("\n  parameters:");
    foreach (var p in layout.Parameters)
    {
        var b = p.PrimaryBinding;
        Console.WriteLine($"    {p.Name,-14} {p.Type.Kind,-14} set {b?.Space} binding {b?.Index}");

        // Data-driven materials read exactly this: field name, type, byte offset.
        if (p.Type is ContainerLayout { ElementType: StructLayout s })
            foreach (var f in s.Fields)
                Console.WriteLine($"      .{f.Name,-12} {Describe(f.Type),-12} offset {f.UniformOffset,3}  size {f.Type.UniformSize}");
    }

    Console.WriteLine("\n  descriptor sets:");
    foreach (var set in layout.DescriptorSets)
        foreach (var r in set.Ranges)
            Console.WriteLine($"    set {set.SpaceOffset} binding {r.IndexOffset}  {r.BindingType}  x{r.DescriptorCount}");

    Console.WriteLine("\n  entry points:");
    foreach (var ep in layout.EntryPoints)
        Console.WriteLine($"    {ep.Name,-14} {ep.Stage}{(ep.ThreadGroupSize is { } tg ? $" {tg}" : "")}");

    Console.WriteLine(failures == 0
        ? "\nPASSED — source to SPIR-V through the managed API."
        : $"\nFAILED — {failures} entry point(s) produced invalid SPIR-V.");
    return failures == 0 ? 0 : 1;
}
catch (SlangException ex)
{
    Console.Error.WriteLine($"\nFAILED: {ex.Message}");
    return 1;
}
catch (DllNotFoundException)
{
    Console.Error.WriteLine("""
        Could not load 'slang-compiler'. Stage the native payload first:

            pwsh tools/fetch-native.ps1

        Note the library is 'slang-compiler', not 'slang' — renamed in v2025.21.
        """);
    return 1;
}

/// <summary>Short human-readable name for a field's type.</summary>
static string Describe(TypeLayout t) => t switch
{
    ScalarLayout s => Short(s.ScalarType),
    VectorLayout v => $"{Short(v.ElementType)}{v.ElementCount}",
    MatrixLayout m => $"{Short(m.ElementType)}{m.RowCount}x{m.ColumnCount}",
    ArrayLayout a => $"{Describe(a.ElementType)}[{a.ElementCount?.ToString() ?? ""}]",
    _ => t.Name ?? t.Kind.ToString(),
};

static string Short(SlangScalarType s) => s.ToString()
    .Replace("SLANG_SCALAR_TYPE_", "")
    .Replace("FLOAT32", "float")
    .Replace("INT32", "int")
    .Replace("UINT32", "uint")
    .ToLowerInvariant();

/// <summary>Structural check on the emitted module: magic number, version, word alignment.</summary>
static bool Validate(ReadOnlySpan<byte> spirv, string entryPoint)
{
    const uint SpirvMagic = 0x07230203;

    if (spirv.Length < 20 || spirv.Length % 4 != 0)
    {
        Console.WriteLine($"  [!!] {entryPoint}: {spirv.Length} bytes — not a whole number of SPIR-V words");
        return false;
    }

    var words = MemoryMarshal.Cast<byte, uint>(spirv);
    if (words[0] != SpirvMagic)
    {
        Console.WriteLine($"  [!!] {entryPoint}: magic 0x{words[0]:X8}, expected 0x{SpirvMagic:X8}");
        return false;
    }

    Console.WriteLine(
        $"  [ok] {entryPoint,-13} {spirv.Length,6} bytes  " +
        $"SPIR-V {(words[1] >> 16) & 0xFF}.{(words[1] >> 8) & 0xFF}  bound={words[3]}");
    return true;
}
