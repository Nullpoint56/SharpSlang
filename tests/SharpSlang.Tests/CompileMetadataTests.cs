// IMetadata's optional interfaces: cooperative types, synthetic resources and coverage tracing.
// The oracle for each is the shader it describes: the cooperative types the source declares, the
// lines the source has, and the buffer the synthetic-resource view and the coverage view must
// agree on.

using System.Text.Json;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class CompileMetadataTests(ITestOutputHelper output)
{
    private const string CooperativeMatrix = """
        using namespace linalg;

        RWStructuredBuffer<float> outputBuffer;

        [shader("compute")]
        [numthreads(32,1,1)]
        void computeMain()
        {
            let d = coopMatMulAdd<float, false>(
                CoopMat<half, MemoryScope.Subgroup, 16, 16, CoopMatMatrixUse::MatrixA>(1.0),
                CoopMat<half, MemoryScope.Subgroup, 16, 16, CoopMatMatrixUse::MatrixB>(2.0),
                CoopMat<float, MemoryScope.Subgroup, 16, 16, CoopMatMatrixUse::MatrixAccumulator>(3.0));
            d.Store<CoopMatMatrixLayout::RowMajor>(outputBuffer, 0, 16);
        }
        """;

    private const string Traced = """
        RWStructuredBuffer<float> result;

        [shader("compute")]
        [numthreads(4, 1, 1)]
        void computeMain(uint3 id : SV_DispatchThreadID)
        {
            float v = id.x * 2.0;
            if (id.x > 1)
                v += 1.0;
            result[id.x] = v;
        }
        """;

    private static LinkedProgram Link(SlangSession session, string name, string source)
    {
        using var module = session.LoadModuleFromSource(name, $"{name}.slang", source);
        using var entry = module.FindEntryPoint("computeMain");
        return session.Link(module, entry);
    }

    [NativeFact]
    public void The_cooperative_types_are_the_ones_the_shader_declares()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSessionFromCommandLine(
            "-target", "spirv", "-profile", "spirv_1_6", "-capability", "spvCooperativeMatrixKHR");
        using var program = Link(session, "coop", CooperativeMatrix);
        using var metadata = program.GetEntryPointMetadata(0, 0);

        var cooperative = metadata.CooperativeTypesMetadata;
        var types = cooperative.CooperativeMatrixTypes;
        foreach (var t in types) output.WriteLine($"{t.componentType} {t.scope} {t.rowCount}x{t.columnCount} {t.use}");

        Assert.Equal(3, types.Length);
        Assert.All(types, t =>
        {
            Assert.Equal(SlangScope.SLANG_SCOPE_WAVE, t.scope);
            Assert.Equal((16u, 16u), (t.rowCount, t.columnCount));
        });
        Assert.Contains(types, t => t.componentType == SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16 && t.use == SlangCooperativeMatrixUse.SLANG_COOPERATIVE_MATRIX_USE_A);
        Assert.Contains(types, t => t.componentType == SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16 && t.use == SlangCooperativeMatrixUse.SLANG_COOPERATIVE_MATRIX_USE_B);
        Assert.Contains(types, t => t.componentType == SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32 && t.use == SlangCooperativeMatrixUse.SLANG_COOPERATIVE_MATRIX_USE_ACCUMULATOR);

        var combination = Assert.Single(cooperative.CooperativeMatrixCombinations);
        Assert.Equal((16u, 16u, 16u), (combination.m, combination.n, combination.k));
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16, combination.componentTypeA);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32, combination.componentTypeResult);
        Assert.Equal(0, combination.saturate);

        Assert.Empty(cooperative.CooperativeVectorTypes);
        Assert.Empty(cooperative.CooperativeVectorCombinations);
    }

    private const string CooperativeVector = """
        using namespace linalg;

        RWStructuredBuffer<int32_t> outputBuffer;
        ByteAddressBuffer input;
        RWByteAddressBuffer matrix;

        [shader("compute")]
        [numthreads(1,1,1)]
        void computeMain()
        {
            let vec4 = coopVecLoad<4, int8_t>(input);
            let result = coopVecMatMul<int32_t, 4, 4>(
                vec4, CoopVecComponentType::SignedInt8, matrix, 0,
                CoopVecComponentType::SignedInt8, CoopVecMatrixLayout::RowMajor, false, 4);
            for (int i = 0; i < result.getCount(); ++i)
                outputBuffer[i] = result[i];
        }
        """;

    [NativeFact]
    public void The_cooperative_vectors_are_the_ones_the_shader_multiplies()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSessionFromCommandLine(
            "-target", "spirv", "-profile", "spirv_1_6", "-capability", "spvCooperativeVectorNV");
        using var program = Link(session, "coopvec", CooperativeVector);
        using var metadata = program.GetEntryPointMetadata(0, 0);

        var cooperative = metadata.CooperativeTypesMetadata;
        var types = cooperative.CooperativeVectorTypes;
        foreach (var t in types) output.WriteLine($"{t.componentType} up to {t.maxSize}, training {t.usedForTrainingOp}");

        // An int8 vector of 4 in, an int32 vector of 4 out.
        Assert.Equal(2, types.Length);
        Assert.Contains(types, t => t.componentType == SlangScalarType.SLANG_SCALAR_TYPE_INT8 && t.maxSize == 4);
        Assert.Contains(types, t => t.componentType == SlangScalarType.SLANG_SCALAR_TYPE_INT32 && t.maxSize == 4);

        var combination = Assert.Single(cooperative.CooperativeVectorCombinations);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_INT8, combination.inputType);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_INT8, combination.matrixInterpretation);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_NONE, combination.biasInterpretation);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_INT32, combination.resultType);
        Assert.Equal(0, combination.transpose);

        Assert.Empty(cooperative.CooperativeMatrixTypes);
    }

    [NativeFact]
    public void Coverage_entries_name_the_shaders_own_lines_and_its_buffer()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSessionFromCommandLine("-target", "spirv", "-trace-coverage");
        using var program = Link(session, "traced", Traced);
        using var metadata = program.GetEntryPointMetadata(0, 0);

        var coverage = metadata.CoverageTracingMetadata;
        output.WriteLine($"{coverage.EntryCount} entries, {coverage.CounterCount} counters, buffer {coverage.Buffer}");
        Assert.InRange(coverage.CounterCount, 1, coverage.EntryCount);

        var lines = Traced.Split('\n').Length;
        for (var i = 0; i < coverage.EntryCount; i++)
        {
            var e = coverage.GetEntry(i);
            output.WriteLine($"  {e.Kind} {e.File}:{e.Line} counter {e.CounterIndex}");
            Assert.EndsWith("traced.slang", e.File, StringComparison.Ordinal);
            Assert.InRange(e.Line, 1u, (uint)lines);
            Assert.NotNull(e.CounterIndex);
            Assert.InRange(e.CounterIndex.Value, 0u, (uint)coverage.CounterCount - 1);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => coverage.GetEntry(coverage.EntryCount));

        // The same buffer through the generic view, found again by its id.
        var synthetic = metadata.SyntheticResourceMetadata;
        var buffer = Assert.Single(Enumerable.Range(0, synthetic.ResourceCount).Select(synthetic.GetResource));
        output.WriteLine($"{buffer}");
        Assert.Equal((coverage.Buffer.Space, coverage.Buffer.Binding), (buffer.Space, buffer.Binding));
        Assert.Equal(0, synthetic.FindResourceIndexById(buffer.Id));
        Assert.Null(synthetic.FindResourceIndexById(0));

        using var manifest = JsonDocument.Parse(coverage.ToManifestJson());
        Assert.Contains("traced.slang", manifest.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [NativeFact]
    public void Without_tracing_the_coverage_metadata_is_empty()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var program = Link(session, "untraced", Traced);
        using var metadata = program.GetEntryPointMetadata(0, 0);

        // Slang still answers the cast; the interface reports nothing and no buffer.
        var coverage = metadata.CoverageTracingMetadata;
        Assert.Equal((0, 0), (coverage.EntryCount, coverage.CounterCount));
        Assert.Equal(new SlangCoverageBuffer(-1, -1, 4), coverage.Buffer);
        Assert.Equal(0, metadata.SyntheticResourceMetadata.ResourceCount);
    }
}
