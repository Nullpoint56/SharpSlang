// What an entry point is called in the emitted module.
//
// These exist because SharpSlang got this wrong in its own documentation, and a consumer built on
// the wrong answer. The Tier 0 spike printed "vertexMain" and "fragmentMain" beside each generated
// module — but those were the *Slang* names it had been handed, not names it read back. The modules
// themselves both said `main`, and nothing checked.
//
// So every claim below is asserted against the bytes rather than against Slang's reflection, and
// the point of the suite is that a Slang version bump that changes the naming rule fails here
// instead of at vkCreatePipeline in somebody's renderer.

using SharpSlang.Baking;
using SharpSlang.Native;
using SharpSlang.Spirv;

namespace SharpSlang.Tests;

public class EntryPointNameTests
{
    internal const string TwoStages = """
        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position, float2 uv)
        { VSOut o; o.pos = float4(position, 1.0); o.uv = uv; return o; }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target { return float4(i.uv, 0, 1); }
        """;

    private static SlangSession Session(SlangGlobalSession global, bool useEntryPointName) =>
        global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = useEntryPointName }],
        });

    [NativeFact]
    public void By_default_every_per_entry_point_module_is_called_main()
    {
        // The finding that started this. Two entry points, two modules, both named `main` — which
        // is legal precisely because they are separate modules, and is why a host that hardcodes
        // "main" appears to work right up until it uses whole-program output.
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: false);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        Assert.Equal(["vertexMain", "fragmentMain"], program.EntryPointNames);

        var v = program.GetSpirvEntryPoint(0, 0);
        var f = program.GetSpirvEntryPoint(1, 0);

        Assert.Equal("main", v.Name);
        Assert.Equal("main", f.Name);

        // The stage is still distinguishable — it is the execution model, not the name.
        Assert.Equal(SpirvExecutionModel.Vertex, v.ExecutionModel);
        Assert.Equal(SpirvExecutionModel.Fragment, f.ExecutionModel);
    }

    [NativeFact]
    public void UseEntryPointNameInSpirv_makes_the_module_use_the_slang_name()
    {
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: true);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        Assert.Equal("vertexMain", program.GetSpirvEntryPoint(0, 0).Name);
        Assert.Equal("fragmentMain", program.GetSpirvEntryPoint(1, 0).Name);
    }

    [ReleasePayloadFact("U16")]
    public void The_same_option_at_link_time_does_nothing() =>
        // U16, asserted as wrong on Slang's release: VulkanUseEntryPointName is a no-op *at link
        // time* only. Patched (0018), the link's options take part (the test below).
        Assert.Equal("main", LinkWithEntryPointName());

    [PatchedFact("U16")]
    public void The_same_option_at_link_time_names_the_entry_point() =>
        Assert.Equal("vertexMain", LinkWithEntryPointName());

    private string LinkWithEntryPointName()
    {
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: false);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");

        using var program = session.Link(module, [vs], new LinkOptions
        {
            CompilerOptions = new LinkTimeOptions
            {
                RawEntries = [new RawCompilerOption(CompilerOptionName.VulkanUseEntryPointName, 1)],
            },
        });

        return program.GetSpirvEntryPoint(0, 0).Name;
    }

    [NativeFact]
    public void Whole_program_output_necessarily_uses_the_real_names()
    {
        // One module holding both stages cannot call them both `main`, so this path always carries
        // the Slang names — the opposite default from getEntryPointCode, on the same program.
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: false);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        using var whole = program.GetTargetCode(0);
        var info = SpirvModule.Read(whole.Span);

        Assert.Equal(["vertexMain", "fragmentMain"], info.EntryPoints.Select(e => e.Name));
    }

    // ---------------------------------------------------------------- through the artifact

    [NativeFact]
    public void The_baked_package_carries_the_module_name_and_the_slang_name_separately()
    {
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: false);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        var package = ShaderPackageBaker.Bake(program, TwoStages, 0);

        var baked = package.EntryPoint("vertexMain")!;
        Assert.Equal("vertexMain", baked.Name);              // how the asset pipeline finds it
        Assert.Equal("main", baked.ModuleEntryPointName);    // what goes in pName
        Assert.Equal(SlangStage.SLANG_STAGE_VERTEX, baked.Stage);
        Assert.Equal(SpirvVersion.V1_5, baked.SpirvVersion);
    }

    [NativeFact]
    public void The_baked_module_name_follows_the_target_option()
    {
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: true);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, fs);

        var package = ShaderPackageBaker.Bake(program, TwoStages, 0);
        Assert.Equal("fragmentMain", package.EntryPoint("fragmentMain")!.ModuleEntryPointName);
    }

    [NativeFact]
    public void The_module_name_agrees_with_the_bytes_in_the_same_package()
    {
        // The property that matters: whatever the naming rule, the recorded name is the one the
        // driver will find in the accompanying code. Nothing here depends on knowing the rule.
        foreach (var useEntryPointName in new[] { false, true })
        {
            using var global = SlangGlobalSession.Create();
            using var session = Session(global, useEntryPointName);
            using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
            using var vs = module.FindEntryPoint("vertexMain");
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, vs, fs);

            var package = ShaderPackageBaker.Bake(program, TwoStages, 0);

            foreach (var e in package.EntryPoints)
            {
                Assert.Equal(
                    SpirvModule.SingleEntryPoint(e.Code).Name,
                    e.ModuleEntryPointName);
            }
        }
    }

    [NativeFact]
    public void The_module_name_survives_the_round_trip()
    {
        using var global = SlangGlobalSession.Create();
        using var session = Session(global, useEntryPointName: true);
        using var module = session.LoadModuleFromSource("t", "t.slang", TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, vs);

        using var buffer = new MemoryStream();
        ShaderPackageBaker.Bake(program, TwoStages, 0).WriteTo(buffer);
        buffer.Position = 0;

        var restored = ShaderPackage.ReadFrom(buffer).EntryPoint("vertexMain")!;
        Assert.Equal("vertexMain", restored.ModuleEntryPointName);
        Assert.Equal(SpirvVersion.V1_5, restored.SpirvVersion);
    }
}
