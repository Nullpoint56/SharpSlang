// What would Wrapper<Lambert> lay out as, for a host that has not written a shader using it.
//
// A material system picks an implementation at run time and then has to fill a buffer. Until this
// existed the only way to get the field offsets was to author a shader declaring the specialization
// and reflect that — a shader per candidate, compiled to answer a question about a type.
//
// The route is not the one the API names suggest, and finding that out is most of what this file
// records. spReflection_specializeType takes a type and argument types and looks like exactly this
// call; against a generic reached by name it returns null with no diagnostics at all.

using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class SpecializeGenericTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Source = """
        interface ISgMaterial { float3 shade(float3 albedo); }

        struct SgLambert  : ISgMaterial { float  scale;              float3 shade(float3 a) { return a * scale; } }
        struct SgEmissive : ISgMaterial { float4 glow; float2 uv;    float3 shade(float3 a) { return a + glow.rgb; } }

        struct SgWrapper<T : ISgMaterial> { T inner; float4 tint; };
        struct SgCascades<let N : int>    { float4 splits[N]; };
        struct SgPlain                    { float4 value; };

        [shader("fragment")]
        float4 fragmentMain() : SV_Target { return 1; }
        """;

    private static (SlangGlobalSession, SlangSession, SlangModule, LinkedProgram) Load()
    {
        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        var module = session.LoadModuleFromSource("sg", "sg.slang", Source);
        var entry = module.FindEntryPoint("fragmentMain");
        var program = session.Link(module, entry);
        entry.Dispose();
        return (global, session, module, program);
    }

    // ---------------------------------------------------------------- type arguments

    [NativeFact]
    public void A_type_generic_reports_its_binding_and_its_layout()
    {
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            var lambert = program.FindType("SgLambert");
            Assert.NotNull(lambert);

            var specialized = program.SpecializeGeneric("SgWrapper", GenericArgument.OfType(lambert));
            Assert.NotNull(specialized);

            output.WriteLine($"{specialized.Name}  [{string.Join(", ", specialized.Arguments)}]");

            Assert.Equal("SgWrapper<SgLambert>", specialized.Name);
            var binding = Assert.Single(specialized.Arguments);
            Assert.Equal("T", binding.Parameter);
            Assert.Equal(GenericParameterKind.Type, binding.Kind);
            Assert.Equal("SgLambert", binding.Type);
        }
    }

    [NativeFact]
    public void The_layout_is_the_one_the_chosen_implementation_produces()
    {
        // The point of the whole exercise: two implementations of one interface give the wrapper
        // two different sizes and two different offsets for `tint`, and a host filling a buffer
        // has to know which it got.
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            var lambert = program.SpecializeGeneric(
                "SgWrapper", GenericArgument.OfType(program.FindType("SgLambert")!));
            var emissive = program.SpecializeGeneric(
                "SgWrapper", GenericArgument.OfType(program.FindType("SgEmissive")!));

            var lambertStruct = Assert.IsType<StructLayout>(lambert!.Layout);
            var emissiveStruct = Assert.IsType<StructLayout>(emissive!.Layout);

            foreach (var s in (StructLayout[])[lambertStruct, emissiveStruct])
            {
                output.WriteLine($"{s.Name}: size {s.UniformSize}");
                foreach (var f in s.Fields)
                    output.WriteLine($"    {f.Name,-8} at {f.UniformOffset}");
            }

            // SgLambert is one float; SgEmissive is a float4 and a float2.
            Assert.True(emissiveStruct.UniformSize > lambertStruct.UniformSize,
                "the wrapper around the larger implementation must be the larger type");

            var lambertTint = lambertStruct.Fields.Single(f => f.Name == "tint").UniformOffset;
            var emissiveTint = emissiveStruct.Fields.Single(f => f.Name == "tint").UniformOffset;
            Assert.NotEqual(lambertTint, emissiveTint);
        }
    }

    // ---------------------------------------------------------------- value arguments

    [NativeFact]
    public void A_value_generic_reports_its_value_and_sizes_the_array()
    {
        // Value generics are the case Slang's program-level specialization API cannot express at
        // all — it reports type parameters exclusively (§9.x, GenericParameterKind) — so this is
        // the only way to ask what `SgCascades<4>` costs.
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            var four = program.SpecializeGeneric("SgCascades", GenericArgument.OfInt(4));
            var eight = program.SpecializeGeneric("SgCascades", GenericArgument.OfInt(8));

            Assert.NotNull(four);
            Assert.NotNull(eight);

            output.WriteLine($"{four.Name} -> {(four.Layout as StructLayout)?.UniformSize}");
            output.WriteLine($"{eight.Name} -> {(eight.Layout as StructLayout)?.UniformSize}");

            Assert.Equal("SgCascades<4>", four.Name);

            var binding = Assert.Single(four.Arguments);
            Assert.Equal("N", binding.Parameter);
            Assert.Equal(GenericParameterKind.Value, binding.Kind);
            Assert.Equal(4L, binding.Value);

            // float4 splits[N] — four of them is 64 bytes, and eight is twice that.
            Assert.Equal(64ul, Assert.IsType<StructLayout>(four.Layout).UniformSize);
            Assert.Equal(128ul, Assert.IsType<StructLayout>(eight.Layout).UniformSize);
        }
    }

    // ---------------------------------------------------------------- refusals

    [NativeFact]
    public void A_generic_the_program_does_not_have_is_null()
    {
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            Assert.Null(program.SpecializeGeneric("SgNoSuchThing", GenericArgument.OfInt(1)));
        }
    }

    [NativeFact]
    public void A_type_that_is_not_generic_says_so_rather_than_returning_null()
    {
        // Null means "no such generic", and a plain struct is a different mistake — the caller
        // named something real and asked it the wrong question. Conflating the two would send
        // them looking for a typo.
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            var e = Assert.Throws<ArgumentException>(
                () => program.SpecializeGeneric("SgPlain", GenericArgument.OfInt(1)));

            Assert.Contains("not a generic one", e.Message, StringComparison.Ordinal);
        }
    }

    [NativeFact]
    public void The_wrong_kind_of_argument_is_refused_here_because_Slang_does_not_refuse_it()
    {
        // A value where a type belongs. The first version of this test expected Slang to complain
        // and pass its diagnostics through; instead spReflection_specializeGeneric access-violates
        // and takes the process down — the whole test run aborted with 0xC0000005. Unlike §9.12's
        // spec-sized resource array this is detectable in advance, so it is, and ProberTests
        // asserts the unguarded behaviour from a process nobody minds losing.
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            var wrongKind = Assert.Throws<ArgumentException>(
                () => program.SpecializeGeneric("SgWrapper", GenericArgument.OfInt(3)));
            output.WriteLine(wrongKind.Message);

            // Too many, and too few, are the same failure — both reach Slang as a mismatched list.
            var lambert = program.FindType("SgLambert")!;
            Assert.Throws<ArgumentException>(() => program.SpecializeGeneric(
                "SgWrapper", GenericArgument.OfType(lambert), GenericArgument.OfType(lambert)));

            Assert.Throws<ArgumentException>(
                () => program.SpecializeGeneric("SgCascades", GenericArgument.OfType(lambert)));

            // And the message says what was expected against what arrived, because "invalid
            // argument" would send the caller to read Slang's headers.
            Assert.Contains("1 type argument(s)", wrongKind.Message, StringComparison.Ordinal);
            Assert.Contains("1 value argument(s)", wrongKind.Message, StringComparison.Ordinal);
        }
    }

    [NativeFact]
    public void An_unbuilt_argument_is_an_argument_error_before_anything_native_happens()
    {
        // A default-constructed GenericArgument would read as a type argument with a null type,
        // which is a null dereference inside Slang rather than a message.
        var (global, session, module, program) = Load();
        using (global) using (session) using (module) using (program)
        {
            Assert.Throws<ArgumentException>(
                () => program.SpecializeGeneric("SgWrapper", default(GenericArgument)));

            Assert.Throws<ArgumentException>(
                () => program.SpecializeGeneric("SgWrapper", ReadOnlySpan<GenericArgument>.Empty));
        }
    }

    // ---------------------------------------------------------------- the snapshot rule

    [NativeFact]
    public void The_result_outlives_the_program_it_came_from()
    {
        // Everything in PrismSlang.Reflection is a snapshot; a specialized layout is no different,
        // and this is the property that lets a cooker bake one.
        SpecializedType specialized;

        var (global, session, module, program) = Load();
        using (global) using (session) using (module)
        {
            specialized = program.SpecializeGeneric(
                "SgWrapper", GenericArgument.OfType(program.FindType("SgLambert")!))!;
            program.Dispose();
        }

        Assert.Equal("SgWrapper<SgLambert>", specialized.Name);
        Assert.Equal("SgLambert", Assert.Single(specialized.Arguments).Type);
        Assert.NotEmpty(Assert.IsType<StructLayout>(specialized.Layout).Fields);
    }
}
