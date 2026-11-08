// Tier 0 spike, on the L2 API.
//
// Originally written against raw vtable pointers to prove the path worked (AGENTS.md#test-runner). Ported here
// to the managed API — which is also the honest test of whether L2 is actually pleasant to use.
// Compare against git history: the pointer plumbing, manual release() calls, and stackalloc
// component arrays are all gone.

using SharpSlang;
using SharpSlang.Interop;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

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
        Targets = [new TargetOptions { SpirvVersion = SpirvVersion.V1_5 }],
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
        using var code = program.GetEntryPointCode(i, 0);
        if (!Validate(code.Span, program.EntryPointNames[i])) failures++;
    }

    // The name in the module is not the name of the Slang function, and printing only the latter
    // beside each module — which this spike used to do — reads as if it were. That impression
    // reached a consumer, which then hardcoded the wrong half. Print both.
    Console.WriteLine("\n  entry point names:");
    Console.WriteLine($"    {"slang",-14} {"in module",-14} note");
    for (var i = 0; i < program.EntryPointNames.Count; i++)
    {
        var ep = program.GetSpirvEntryPoint(i, 0);
        var note = ep.Name == program.EntryPointNames[i]
            ? ""
            : "<- this is what goes in VkPipelineShaderStageCreateInfo::pName";
        Console.WriteLine($"    {program.EntryPointNames[i],-14} {ep.Name,-14} {note}");
    }

    // Tier 1 — walk the reflection. The snapshot is immutable managed data and outlives `program`.
    var layout = program.Reflect(0);

    Console.WriteLine("\n  parameters:");
    foreach (var p in layout.Parameters)
    {
        var b = p.PrimaryBinding;
        Console.WriteLine($"    {p.Name,-14} {p.Type.Kind,-14} set {b?.Space} binding {b?.Index}");

        // Data-driven materials read exactly this: field name, type, byte offset — and, for a
        // matrix, the stride. The size alone is not enough to place a row: a float3x3 is 48 bytes
        // because its three rows sit 16 apart, so packing nine floats corrupts it.
        if (p.Type is ContainerLayout { ElementType: StructLayout s })
            foreach (var f in s.Fields)
                Console.WriteLine(
                    $"      .{f.Name,-12} {Describe(f.Type),-12} offset {f.UniformOffset,3}  " +
                    $"size {f.Type.UniformSize}" +
                    (f.Type is MatrixLayout m ? $"  stride {m.Stride} ({Major(m.Mode)})" : ""));
    }

    Console.WriteLine("\n  descriptor sets:");
    foreach (var set in layout.DescriptorSets)
        foreach (var r in set.Ranges)
            Console.WriteLine(
                $"    set {set.SpaceOffset} binding {r.IndexOffset}  {r.BindingType}  " +
                $"x{r.DescriptorCount}  {r.Name ?? "(unnamed)"}");

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

// Short human-readable name for a field's type.
static string Describe(TypeLayout t) => t switch
{
    ScalarLayout s => Short(s.ScalarType),
    VectorLayout v => $"{Short(v.ElementType)}{v.ElementCount}",
    MatrixLayout m => $"{Short(m.ElementType)}{m.RowCount}x{m.ColumnCount}",
    ArrayLayout a => $"{Describe(a.ElementType)}[{a.ElementCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""}]",
    _ => t.Name ?? t.Kind.ToString(),
};

// Which axis a matrix's stride steps along, in Slang's terms.
//
// Worth spelling out rather than printing the enum: the emitted module decorates the opposite
// word, because HLSL counts rows where SPIR-V counts columns. Printing "ROW_MAJOR" beside a module
// that says `ColMajor` is exactly the kind of half-answer that sent a consumer the wrong way
// over entry-point names.
static string Major(SlangMatrixLayoutMode mode) =>
    mode == SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR ? "per column" : "per row";

static string Short(SlangScalarType s) => s.ToString()
    .Replace("SLANG_SCALAR_TYPE_", "")
    .Replace("FLOAT32", "float")
    .Replace("INT32", "int")
    .Replace("UINT32", "uint")
    .ToLowerInvariant();

// Structural check on the emitted module: magic number, version, word alignment.
static bool Validate(ReadOnlySpan<byte> spirv, string entryPoint)
{
    try
    {
        var module = SpirvModule.Read(spirv);
        Console.WriteLine(
            $"  [ok] {entryPoint,-13} {spirv.Length,6} bytes  " +
            $"SPIR-V {module.Version.ToDisplayString()}  bound={module.Bound}");
        return true;
    }
    catch (InvalidDataException ex)
    {
        Console.WriteLine($"  [!!] {entryPoint}: {ex.Message}");
        return false;
    }
}
