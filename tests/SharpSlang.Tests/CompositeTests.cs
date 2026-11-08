// Link's three steps, each reachable as the one Slang call it is (AGENTS.md#layers):
// ISession::createCompositeComponentType, IComponentType::specialize, and IComponentType::link /
// linkWithOptions. SlangSession.Link is the composite of the three, and the oracle here is that
// the steps taken one at a time emit byte for byte what Link emits.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class CompositeTests(ITestOutputHelper output)
{
    private const string Source = """
        struct Uniforms { float4 tint; };
        ConstantBuffer<Uniforms> uniforms;
        Texture2D albedo;
        SamplerState albedoSampler;

        [shader("fragment")]
        float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
        {
            return albedo.Sample(albedoSampler, uv) * uniforms.tint;
        }
        """;

    private const string Generic = """
        interface IMaterial { float3 shade(float3 a); }
        struct Lambert  : IMaterial { float3 shade(float3 a) { return a * 0.5; } }
        struct Emissive : IMaterial { float3 shade(float3 a) { return a * 4.0; } }

        type_param Material : IMaterial;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            Material m;
            return float4(m.shade(float3(1, 1, 1)), 1.0);
        }
        """;

    private static SlangSession NewSession(SlangGlobalSession global) =>
        global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });

    [NativeFact]
    public void Composing_then_linking_one_call_at_a_time_emits_what_Link_emits()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("cm", "cm.slang", Source);
        using var entry = module.FindEntryPoint("fragmentMain");

        using var composite = session.CreateCompositeComponentType(module, entry);
        Assert.Equal(["fragmentMain"], composite.EntryPointNames);
        Assert.Equal(0, composite.SpecializationParameterCount);
        Assert.NotEqual(0, composite.NativeHandle);

        using var stepped = composite.Link();
        using var whole = session.Link(module, entry);

        Assert.Equal(whole.EntryPointNames, stepped.EntryPointNames);
        Assert.Equal(whole.GetEntryPointCodeBytes(0, 0), stepped.GetEntryPointCodeBytes(0, 0));
        Assert.Equal(whole.ToJson(0), stepped.ToJson(0));
    }

    [NativeFact]
    public void Specializing_then_linking_one_call_at_a_time_emits_what_Link_emits()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("cg", "cg.slang", Generic);
        using var entry = module.FindEntryPoint("fragmentMain");

        using var composite = session.CreateCompositeComponentType(module, entry);
        Assert.Equal(1, composite.SpecializationParameterCount);

        var lambert = module.RequireType(0, "Lambert");
        using var specialized = composite.Specialize(lambert);
        Assert.Equal(0, specialized.SpecializationParameterCount);
        Assert.Equal(["fragmentMain"], specialized.EntryPointNames);

        using var stepped = specialized.Link();
        using var whole = session.Link(module, [entry], new LinkOptions { SpecializationArguments = [lambert] });
        Assert.Equal(whole.GetEntryPointCodeBytes(0, 0), stepped.GetEntryPointCodeBytes(0, 0));

        // And it is a specialization: the other implementation emits other code.
        using var emissive = composite.Specialize(module.RequireType(0, "Emissive"));
        using var other = emissive.Link();
        Assert.NotEqual(stepped.GetEntryPointCodeBytes(0, 0), other.GetEntryPointCodeBytes(0, 0));
    }

    [NativeFact]
    public void Linking_with_options_one_call_at_a_time_emits_what_Link_emits()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        // LinkOptionsTests' shader: enough redundant arithmetic that the optimiser's level shows.
        const string Heavy = """
            Texture2D albedo;
            SamplerState samp;
            float3 heavy(float3 c)
            {
                for (int i = 0; i < 4; ++i) { c = c * 1.0 + 0.0; c = normalize(c + 0.0001); }
                return c;
            }
            [shader("fragment")]
            float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target { return float4(heavy(albedo.Sample(samp, uv).rgb), 1.0); }
            """;
        using var module = session.LoadModuleFromSource("co", "co.slang", Heavy);
        using var entry = module.FindEntryPoint("fragmentMain");
        var options = new LinkTimeOptions { Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE };

        using var composite = session.CreateCompositeComponentType(module, entry);
        using var stepped = composite.LinkWithOptions(options);
        using var whole = session.Link(module, [entry], new LinkOptions { CompilerOptions = options });
        Assert.Equal(whole.GetEntryPointCodeBytes(0, 0), stepped.GetEntryPointCodeBytes(0, 0));

        // The level takes effect: a fresh composite linked at MAXIMAL emits other, smaller code
        // (1,188 bytes against NONE's 1,572 at v2026.21).
        var maximal = new LinkTimeOptions { Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL };
        using var fresh = session.CreateCompositeComponentType(module, entry);
        using var optimised = fresh.LinkWithOptions(maximal);
        Assert.True(optimised.GetEntryPointCodeBytes(0, 0).Length < stepped.GetEntryPointCodeBytes(0, 0).Length);
    }

    private (byte[] None, byte[] MaximalFresh, byte[] MaximalAgain) LinkOneCompositeTwice()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        const string Heavy = """
            Texture2D albedo;
            SamplerState samp;
            float3 heavy(float3 c)
            {
                for (int i = 0; i < 4; ++i) { c = c * 1.0 + 0.0; c = normalize(c + 0.0001); }
                return c;
            }
            [shader("fragment")]
            float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target { return float4(heavy(albedo.Sample(samp, uv).rgb), 1.0); }
            """;
        using var module = session.LoadModuleFromSource("co", "co.slang", Heavy);
        using var entry = module.FindEntryPoint("fragmentMain");
        var none = new LinkTimeOptions { Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE };
        var maximal = new LinkTimeOptions { Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL };

        using var composite = session.CreateCompositeComponentType(module, entry);
        using var first = composite.LinkWithOptions(none);
        var firstCode = first.GetEntryPointCodeBytes(0, 0);
        using var again = composite.LinkWithOptions(maximal);
        using var fresh = session.CreateCompositeComponentType(module, entry);
        using var optimised = fresh.LinkWithOptions(maximal);
        var result = (firstCode, optimised.GetEntryPointCodeBytes(0, 0), again.GetEntryPointCodeBytes(0, 0));
        output.WriteLine($"NONE {result.Item1.Length} B, MAXIMAL on a fresh composite {result.Item2.Length} B, " +
                         $"MAXIMAL on the first composite {result.Item3.Length} B");
        return result;
    }

    [ReleasePayloadFact("U30")]
    public void A_composite_linked_a_second_time_emits_its_first_links_code()
    {
        // U30, asserted as wrong on Slang's release: the second link's options are ignored (1,572
        // bytes at NONE and again at MAXIMAL, against 1,188 for a fresh composite at v2026.21).
        var (none, _, again) = LinkOneCompositeTwice();
        Assert.Equal(none, again);
    }

    [PatchedFact("U30")]
    public void A_composite_linked_a_second_time_emits_what_its_second_links_options_say()
    {
        // U30, patched (0008): each link carries its own options, so the second link emits what a
        // fresh composite linked with the same options emits.
        var (none, maximalFresh, again) = LinkOneCompositeTwice();
        Assert.Equal(maximalFresh, again);
        Assert.NotEqual(none, again);
    }

    [NativeFact]
    public void Specializing_with_the_wrong_number_of_arguments_is_refused_with_slangs_diagnostics()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("cw", "cw.slang", Generic);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var composite = session.CreateCompositeComponentType(module, entry);

        var lambert = module.RequireType(0, "Lambert");
        var e = Assert.Throws<SlangException>(() => composite.Specialize(lambert, lambert));
        output.WriteLine(e.Message);
        Assert.Equal("IComponentType::specialize", e.Operation);
    }

    [NativeFact]
    public void The_unlinked_composite_reflects_what_the_linked_program_does()
    {
        // The intermediate object is reachable now, so its getLayout is asked. Measured at
        // v2026.21 (AGENTS.md#reflection-snapshot): the composite of a module and its entry point
        // lays out the same parameters, entry points and descriptor bindings as the program linked
        // from it, and Slang's reflection JSON is identical text.
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("cr", "cr.slang", Source);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var composite = session.CreateCompositeComponentType(module, entry);

        var unlinked = composite.GetLayout(0);
        output.WriteLine($"unlinked: {unlinked.ParameterCount} parameter(s), {unlinked.EntryPointCount} entry point(s)");
        Assert.Equal(3u, unlinked.ParameterCount);
        Assert.Equal(1ul, unlinked.EntryPointCount);
        Assert.Same(composite, unlinked.Owner);

        using var program = composite.Link();
        var linked = program.GetLayout(0);
        Assert.Equal(linked.ParameterCount, unlinked.ParameterCount);
        for (uint i = 0; i < linked.ParameterCount; i++)
        {
            var a = unlinked.GetParameterByIndex(i)!;
            var b = linked.GetParameterByIndex(i)!;
            Assert.Equal(b.Variable!.Name, a.Variable!.Name);
            Assert.Equal(b.BindingIndex, a.BindingIndex);
            Assert.Equal(b.BindingSpace, a.BindingSpace);
        }

        Assert.Equal(linked.ToJson(), unlinked.ToJson());

        // Borrowed from the composite: disposed with it, the layout throws.
        var parameter = unlinked.GetParameterByIndex(0)!;
        composite.Dispose();
        Assert.Throws<ObjectDisposedException>(() => parameter.Variable);
    }

    [NativeFact]
    public unsafe void A_composite_is_a_session_child_and_holds_its_components()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        var module = session.LoadModuleFromSource("cl", "cl.slang", Source);
        var entry = module.FindEntryPoint("fragmentMain");

        var moduleBefore = RefCount(module);
        var composite = session.CreateCompositeComponentType(module, entry);

        // Slang's own retain of a component, apart from the one the wrapper takes (measured, not
        // relied on: the wrapper holds its components either way).
        var slangRetains = RefCount(module) - moduleBefore - 1;
        output.WriteLine($"createCompositeComponentType: Slang retains the module {slangRetains} time(s)");
        Assert.Equal(1, slangRetains);

        Assert.Equal(2, session.LiveChildren);   // the module and the composite
        Assert.Throws<InvalidOperationException>(session.Dispose);

        // A component disposed under the composite leaves it working.
        entry.Dispose();
        module.Dispose();
        using (var program = composite.Link())
        {
            Assert.True(program.GetEntryPointCodeBytes(0, 0).Length > 0);
        }

        composite.Dispose();
        Assert.Equal(0, session.LiveChildren);

        static int RefCount(SlangObject o)
        {
            var p = (ISlangUnknown*)o.NativeHandle;
            p->addRef();
            return (int)p->release();
        }
    }

    [NativeFact]
    public void A_disposed_component_cannot_be_composed()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("cd", "cd.slang", Source);
        var entry = module.FindEntryPoint("fragmentMain");
        entry.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.CreateCompositeComponentType(module, entry));
        Assert.Throws<ArgumentException>(() => session.CreateCompositeComponentType());
    }

    // ---------------------------------------------------------------- the target index, explicit

    [DxcFact]
    public void A_two_target_session_answers_per_target_index()
    {
        // No target is chosen for the caller (AGENTS.md#layers): every call that reads a target
        // takes its index. SPIR-V numbers a set's descriptors consecutively, D3D per register
        // class, so the texture is binding 1 on target 0 and t0 on target 1.
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions(), new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL, Profile = "sm_6_5" }],
        });
        using var module = session.LoadModuleFromSource("ct", "ct.slang", Source);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, entry);

        Assert.Equal(0x07230203u, BitConverter.ToUInt32(program.GetEntryPointCodeBytes(0, 0), 0));
        Assert.Equal("DXBC"u8.ToArray(), program.GetEntryPointCodeBytes(0, 1)[..4]);

        var spirv = program.Reflect(0).Parameter("albedo")!.PrimaryBinding!.Value;
        var dxil = program.Reflect(1).Parameter("albedo")!.PrimaryBinding!.Value;
        Assert.Equal((SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT, 1u), (spirv.Category, spirv.Index));
        Assert.Equal((SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE, 0u), (dxil.Category, dxil.Index));

        Assert.NotEqual(program.ToJson(0), program.ToJson(1));
        Assert.NotEqual(program.GetLayout(0).NativeHandle, program.GetLayout(1).NativeHandle);

        // Types are the program's, not the target's: measured at v2026.21, the same TypeReflection
        // from either target's layout, and the same subtype answer.
        var block0 = program.FindType(0, "Uniforms")!;
        var block1 = program.FindType(1, "Uniforms")!;
        output.WriteLine($"Uniforms: target 0 {block0.NativeHandle:X}, target 1 {block1.NativeHandle:X}");
        Assert.Equal(block0, block1);
        Assert.True(program.IsSubType(0, block0, block0));
        Assert.True(program.IsSubType(1, block1, block1));
        Assert.Equal(module.FindType(0, "Uniforms"), module.FindType(1, "Uniforms"));
    }
}
