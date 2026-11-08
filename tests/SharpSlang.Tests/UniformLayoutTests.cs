// Can a host actually put bytes where the shader will read them?
//
// Same method as PipelineLayoutTests, aimed at memory rather than at bindings: compile, then ask
// the emitted module where it decorated each member, each matrix row and each array element, and
// require reflection to agree. Every defect this file pins was reported by reflection in a way that
// looked reasonable and produced corrupt data — a matrix packed to 36 bytes into a 48-byte slot, an
// instance buffer whose element size was 0, a dispatch divisor of zero.

using SharpSlang.Baking;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Spirv;
using SharpSlang.Vulkan;

namespace SharpSlang.Tests;

public class UniformLayoutTests
{
    private static (byte[] Code, ProgramLayout Layout) Compile(
        string source,
        string entryPoint,
        SlangMatrixLayoutMode matrix = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions()],
            MatrixLayout = matrix,
        });
        using var module = session.LoadModuleFromSource("m", "m.slang", source);
        using var ep = module.FindEntryPoint(entryPoint);
        using var program = session.Link(module, ep);

        return (program.GetEntryPointCodeBytes(0), program.Reflect());
    }

    private static StructLayout Element(ProgramLayout layout, string parameter) =>
        (StructLayout)((ContainerLayout)layout.Parameter(parameter)!.Type).ElementType;

    // ---------------------------------------------------------------- constant buffer members

    private const string MaterialShader = """
        struct Material
        {
            float3   tint;
            float    gloss;
            float4x4 xform;
            float3x3 normalMatrix;
            float2   uvScale[3];
            float    cutoff;
        };
        ConstantBuffer<Material> gMaterial;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            float3 c = gMaterial.tint * gMaterial.gloss * gMaterial.cutoff;
            c += mul(gMaterial.normalMatrix, c);
            c.xy += gMaterial.uvScale[1];
            return mul(gMaterial.xform, float4(c, 1));
        }
        """;

    [NativeFact]
    public void Every_member_sits_where_the_module_says_it_does()
    {
        var (code, layout) = Compile(MaterialShader, "fragmentMain");
        var members = SpirvDecorations.Members(code);

        foreach (var field in Element(layout, "gMaterial").Fields)
            Assert.Equal(members[field.Name].Offset, field.UniformOffset);

        // And not vacuously: the shader was written so the offsets are not simply 0, 4, 8...
        Assert.Equal([0ul, 12ul, 16ul, 80ul, 128ul, 176ul],
            Element(layout, "gMaterial").Fields.Select(f => f.UniformOffset));
    }

    [NativeFact]
    public void A_matrix_reports_the_stride_the_module_decorates()
    {
        // The bug: nothing reported a stride at all, so the only way to place a row was to divide
        // the size by a count and hope. A float3x3 is 48 bytes with 16-byte rows — pack it to 36
        // and every row after the first lands 4 bytes early.
        var (code, layout) = Compile(MaterialShader, "fragmentMain");
        var members = SpirvDecorations.Members(code);

        foreach (var field in Element(layout, "gMaterial").Fields)
        {
            if (field.Type is not MatrixLayout m) continue;
            Assert.Equal(members[field.Name].MatrixStride, m.Stride);
        }

        var normalMatrix = (MatrixLayout)Element(layout, "gMaterial").Field("normalMatrix")!.Type;
        Assert.Equal(16ul, normalMatrix.Stride);
        Assert.Equal(48ul, normalMatrix.UniformSize);
    }

    [NativeFact]
    public void An_array_reports_the_stride_the_module_decorates()
    {
        var (code, layout) = Compile(MaterialShader, "fragmentMain");
        var uvScale = (ArrayLayout)Element(layout, "gMaterial").Field("uvScale")!.Type;

        // float2 padded to 16 in a constant buffer, not packed to 8.
        Assert.Equal(16ul, uvScale.ElementStride);
        Assert.Contains((uint)uvScale.ElementStride, SpirvDecorations.ArrayStrides(code));
    }

    private const string MatrixShapes = """
        struct M
        {
            float4x4 a;
            float3x3 b;
            float2x4 c;
            float4x2 d;
        };
        ConstantBuffer<M> gM;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            return mul(gM.a, float4(1)) + float4(mul(gM.b, float3(1)), 0)
                 + float4(mul(gM.c, float4(1)), 0, 0) + float4(mul(gM.d, float2(1)));
        }
        """;

    [NativeTheory]
    [InlineData(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR)]
    [InlineData(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR)]
    public void The_stride_derivation_holds_for_every_shape_in_both_modes(SlangMatrixLayoutMode mode)
    {
        // The stride is derived — Slang has no API for it — so this is the test that stops the
        // derivation quietly drifting away from what the module does. Non-square shapes in both
        // modes are the cases where dividing by the wrong axis still produces a plausible number.
        var (code, layout) = Compile(MatrixShapes, "fragmentMain", mode);
        var members = SpirvDecorations.Members(code);

        foreach (var field in Element(layout, "gM").Fields)
        {
            var m = (MatrixLayout)field.Type;
            Assert.Equal(mode, m.Mode);
            Assert.Equal(members[field.Name].MatrixStride, m.Stride);
        }
    }

    [NativeTheory]
    [InlineData(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR)]
    [InlineData(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR)]
    public void The_module_decorates_the_opposite_word_to_the_one_reflection_reports(
        SlangMatrixLayoutMode mode)
    {
        // Not a contradiction, and worth pinning so nobody "fixes" it: HLSL writes rows-by-columns
        // while SPIR-V's OpTypeMatrix is a list of columns, so the same bytes get opposite names.
        // A host that reads Mode as if it were the SPIR-V decoration transposes every matrix.
        var (code, _) = Compile(MatrixShapes, "fragmentMain", mode);

        var rowMajorInModule = mode == SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR;
        foreach (var (_, member) in SpirvDecorations.Members(code))
        {
            if (member.MatrixStride is null) continue;
            Assert.Equal(rowMajorInModule, member.IsRowMajor);
        }
    }

    [NativeFact]
    public void A_component_offset_accounts_for_the_padding_and_the_major_axis()
    {
        var (_, rowMajor) = Compile(MatrixShapes, "fragmentMain");
        var (_, columnMajor) = Compile(
            MatrixShapes, "fragmentMain", SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR);

        // float3x3, 16-byte stride: row 2 column 1 is at 2*16 + 1*4 under row-major, and at
        // 1*16 + 2*4 under column-major. Packed 3x3 arithmetic would say 28 for both.
        var b = (MatrixLayout)Element(rowMajor, "gM").Field("b")!.Type;
        Assert.Equal(36ul, b.ComponentOffset(row: 2, column: 1));

        var bColumn = (MatrixLayout)Element(columnMajor, "gM").Field("b")!.Type;
        Assert.Equal(24ul, bColumn.ComponentOffset(row: 2, column: 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => b.ComponentOffset(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.ComponentOffset(0, 3));
    }

    // ---------------------------------------------------------------- structured buffers

    private const string InstanceShader = """
        struct Instance { float3 pos; float scale; float4x4 xform; };
        StructuredBuffer<Instance>   gInstances;
        RWStructuredBuffer<float>    gScalars;
        ByteAddressBuffer            gRaw;
        Texture2D                    gTex;

        [shader("compute")]
        [numthreads(64, 1, 1)]
        void computeMain(uint id : SV_DispatchThreadID)
        {
            gScalars[id] = gInstances[id].scale + gRaw.Load(0) + gTex.Load(0).x;
        }
        """;

    [NativeFact]
    public void A_structured_buffer_reports_its_element_layout()
    {
        // The bug: ResultType named the struct and reported a size of 0 with no fields, because
        // Slang hands the result out as a type rather than a type layout. Nothing said an Instance
        // was 80 bytes or where xform sat, so an instance buffer could not be filled.
        var (code, layout) = Compile(InstanceShader, "computeMain");
        var instances = (ResourceLayout)layout.Parameter("gInstances")!.Type;

        var element = Assert.IsType<StructLayout>(instances.ElementType);
        Assert.Equal(80ul, element.UniformSize);
        Assert.Equal(80ul, instances.ElementStride);
        Assert.Contains((uint)instances.ElementStride, SpirvDecorations.ArrayStrides(code));

        Assert.Equal([0ul, 12ul, 16ul], element.Fields.Select(f => f.UniformOffset));
        Assert.Equal(16ul, ((MatrixLayout)element.Field("xform")!.Type).Stride);
    }

    [NativeFact]
    public void A_scalar_element_is_reported_too()
    {
        var (_, layout) = Compile(InstanceShader, "computeMain");
        var scalars = (ResourceLayout)layout.Parameter("gScalars")!.Type;

        Assert.Equal(LayoutKind.Scalar, scalars.ElementType?.Kind);
        Assert.Equal(4ul, scalars.ElementStride);
    }

    [NativeFact]
    public void A_resource_with_no_element_layout_reports_none_rather_than_a_guess()
    {
        var (_, layout) = Compile(InstanceShader, "computeMain");

        foreach (var name in new[] { "gRaw", "gTex" })
        {
            var resource = (ResourceLayout)layout.Parameter(name)!.Type;
            Assert.Null(resource.ElementType);
            Assert.Equal(0ul, resource.ElementStride);
        }
    }

    [NativeFact]
    public void The_element_layout_survives_a_bake()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", InstanceShader);
        using var ep = module.FindEntryPoint("computeMain");
        using var program = session.Link(module, ep);

        var restored = ProgramLayout.FromJson(ShaderPackageBaker.Bake(program, InstanceShader).Layout.ToJson());
        var instances = (ResourceLayout)restored.Parameter("gInstances")!.Type;

        Assert.Equal(80ul, instances.ElementStride);
        Assert.Equal(80ul, instances.ElementType!.UniformSize);
        Assert.Equal(16ul, ((MatrixLayout)((StructLayout)instances.ElementType).Field("xform")!.Type).Stride);
    }
}
