// When Slang does not know, and says so with a number.
//
// An array whose length is a specialization constant has no size until the pipeline is created.
// Slang reports that as 0xFFFF_FFFF_FFFF_FFFE — a marker sitting one below ulong.MaxValue, which
// arithmetic accepts without complaint. Cast to uint it becomes 0xFFFFFFFE and reaches a driver as
// a push-constant range; used as an offset it points four exabytes past the buffer.
//
// Worse, asking how many elements such an array has is fatal: spReflectionType_GetElementCount
// access-violates, and so does the GetSpecializedElementCount the C++ header presents as the
// resolving version. So the count has to be inferred from the size marker and never requested.

using SharpSlang.Native;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class UnknownSizeTests
{
    private static ProgramLayout Compile(string source, string entryPoint)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", source);
        using var ep = module.FindEntryPoint(entryPoint);
        using var program = session.Link(module, ep);
        _ = program.GetEntryPointCodeBytes(0, 0);
        return program.Reflect(0);
    }

    private const string SpecSized = """
        [vk::constant_id(0)] const int kCascades = 4;

        struct Block
        {
            float4x4 fixedMatrices[3];
            float4x4 specMatrices[kCascades];
            float    trailing;
        };
        ConstantBuffer<Block> gBlock;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            return mul(gBlock.fixedMatrices[0], float4(1))
                 + mul(gBlock.specMatrices[0], float4(1)) + gBlock.trailing;
        }
        """;

    private static StructLayout Block(ProgramLayout layout) =>
        (StructLayout)((ContainerLayout)layout.Parameter("gBlock")!.Type).ElementType;

    [NativeFact]
    public void Reflecting_a_specialization_sized_array_does_not_crash()
    {
        // The whole test. Before the guard, walking this shader threw an SEHException out of
        // spReflectionType_GetElementCount, and every caller of Reflect() inherited it.
        var block = Block(Compile(SpecSized, "fragmentMain"));

        Assert.Equal(["fixedMatrices", "specMatrices", "trailing"], block.Fields.Select(f => f.Name));
    }

    [NativeFact]
    public void The_array_says_its_length_is_not_known_yet()
    {
        var block = Block(Compile(SpecSized, "fragmentMain"));

        var spec = (ArrayLayout)block.Field("specMatrices")!.Type;
        Assert.Null(spec.ElementCount);
        Assert.True(spec.IsSpecializationSized);
        Assert.False(spec.IsUniformSizeKnown);

        // The element stride is still known — only the number of them is not.
        Assert.Equal(64ul, spec.ElementStride);
    }

    [NativeFact]
    public void A_fixed_array_in_the_same_block_is_unaffected()
    {
        var block = Block(Compile(SpecSized, "fragmentMain"));

        var fixedArray = (ArrayLayout)block.Field("fixedMatrices")!.Type;
        Assert.Equal(3, fixedArray.ElementCount);
        Assert.False(fixedArray.IsSpecializationSized);
        Assert.True(fixedArray.IsUniformSizeKnown);
        Assert.Equal(192ul, fixedArray.UniformSize);
        Assert.True(block.Field("fixedMatrices")!.IsUniformOffsetKnown);
    }

    [NativeFact]
    public void A_field_after_it_reports_that_it_has_no_offset()
    {
        // Not a bug in Slang: where `trailing` lands genuinely depends on kCascades. The bug was
        // handing the marker back as a byte offset, which is a number a host will happily use.
        var trailing = Block(Compile(SpecSized, "fragmentMain")).Field("trailing")!;

        Assert.False(trailing.IsUniformOffsetKnown);
        Assert.Equal(CategorySize.Unknown, trailing.UniformOffset);
    }

    [NativeFact]
    public void An_unbounded_array_is_a_different_thing_and_still_reports_as_one()
    {
        const string Source = """
            struct Element { float4 value; };
            StructuredBuffer<Element> gElements;
            Texture2D gTextures[];

            [shader("fragment")]
            float4 fragmentMain(uint i : INDEX) : SV_Target
            { return gElements[0].value + gTextures[i].Load(0); }
            """;

        var layout = Compile(Source, "fragmentMain");
        var range = layout.ToNri(new NriProjectionOptions { UnboundedArrayCap = 64 }).Range("gTextures")!.Value.Range;

        // Unbounded, and the count NRI is given is the host's cap: the shader has no number to offer.
        Assert.True(range.IsUnbounded);
        Assert.Equal(64u, range.Desc.descriptorNum);
    }

    [NativeTheory]
    [InlineData("[[vk::push_constant]] ConstantBuffer<Block> gUnsized;")]
    [InlineData("ConstantBuffer<Block> gUnsized;")]
    public void A_buffer_with_no_fixed_size_is_refused_rather_than_dropped(string declaration)
    {
        // Both forms lose every binding: no descriptor range, no push-constant range, nothing —
        // and the emitted module drops the descriptor too, so comparing the two would find them in
        // perfect agreement while the shader still reads the buffer. Only an invariant catches it.
        var source = $$"""
            [vk::constant_id(0)] const int kCount = 4;
            struct Block { float4 values[kCount]; };
            {{declaration}}

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return gUnsized.values[0]; }
            """;

        var layout = Compile(source, "fragmentMain");

        // Slang placed it nowhere, and says so by reporting no bindings at all.
        Assert.Empty(layout.Parameter("gUnsized")!.Bindings);

        var e = Assert.Throws<NotSupportedException>(() => layout.ToNri());
        Assert.Contains("gUnsized", e.Message);
        Assert.Contains("specialization constant", e.Message);
    }

    [NativeFact]
    public void A_parameter_the_layout_cannot_place_is_named_in_the_refusal()
    {
        // The general invariant, stated on its own: a parameter with no binding of any kind is in
        // the shader and in no part of the pipeline layout, whatever the reason.
        const string Source = """
            [vk::constant_id(0)] const int kCount = 2;
            struct Block { float4x4 transforms[kCount]; };
            ConstantBuffer<Block> gTransforms;
            Texture2D gAlbedo;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target
            { return mul(gTransforms.transforms[0], float4(1)) + gAlbedo.Load(0); }
            """;

        var layout = Compile(Source, "fragmentMain");

        // The texture is fine; only the unsized buffer is not.
        Assert.Single(layout.Parameter("gAlbedo")!.Bindings);

        var e = Assert.Throws<NotSupportedException>(() => layout.ToNri());
        Assert.Contains("gTransforms", e.Message);
        Assert.Contains("no binding of any kind", e.Message);
    }

    [NativeFact]
    public void A_fixed_push_constant_block_still_works()
    {
        const string Source = """
            struct Push { float4 values[4]; };
            [[vk::push_constant]] ConstantBuffer<Push> gPush;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return gPush.values[0]; }
            """;

        var block = Assert.Single(Compile(Source, "fragmentMain").ToNri().RootConstants);

        Assert.Equal(64u, block.Desc.size);
        Assert.Equal(0u, block.Index);
    }

    [NativeFact]
    public void The_markers_are_the_values_Slang_documents()
    {
        // slang.h: SLANG_UNBOUNDED_SIZE is ~size_t(0), SLANG_UNKNOWN_SIZE is one below it. Pinned
        // because everything above depends on the exact values, and they are mirrored here rather
        // than imported.
        Assert.Equal(ulong.MaxValue, CategorySize.Unbounded);
        Assert.Equal(ulong.MaxValue - 1, CategorySize.Unknown);

        Assert.True(new CategorySize(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM, 0).IsKnown);
        Assert.True(new CategorySize(
            SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM, ulong.MaxValue - 2).IsKnown);
        Assert.False(new CategorySize(
            SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM, CategorySize.Unknown).IsKnown);
        Assert.False(new CategorySize(
            SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM, CategorySize.Unbounded).IsKnown);
    }
}
