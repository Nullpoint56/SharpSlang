// The parts of L2 and L3 the walk does not reach, each held to an answer it did not produce
// (AGENTS.md#layers): the reflection of a module and an entry point before any link, the optional
// interfaces behind queryInterface and castAs, and the profile a DXIL target gets when it names
// none.

using SharpSlang.Native;

namespace SharpSlang.Tests;

public class CoreSurfaceTests(ITestOutputHelper output)
{
    private const string Source = """
        struct CsBlock { float4 csTint; };
        ConstantBuffer<CsBlock> csBuffer;
        Texture2D csTexture;
        SamplerState csSampler;
        Texture2D csUnused;

        [shader("fragment")]
        float4 csFragment(float2 uv : TEXCOORD0) : SV_Target
        {
            return csTexture.Sample(csSampler, uv) * csBuffer.csTint;
        }
        """;

    private const string Compute = """
        RWStructuredBuffer<uint> csOut;

        [shader("compute")]
        [numthreads(4, 1, 1)]
        void csCompute(uint3 id : SV_DispatchThreadID) { csOut[id.x] = id.x; }
        """;

    // ---------------------------------------------------------------- reflection before a link

    [NativeFact]
    public void A_module_and_its_entry_point_reflect_before_any_link()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("cs", "cs.slang", Source);
        using var entry = module.FindEntryPoint("csFragment");

        // The module's own layout finds the type the linked program's does.
        var fromModule = module.GetLayout().FindTypeByName("CsBlock");
        Assert.NotNull(fromModule);
        Assert.Equal(SlangTypeKind.SLANG_TYPE_KIND_STRUCT, fromModule.Kind);
        Assert.Equal("CsBlock", fromModule.DeclaredName);
        Assert.Same(module, fromModule.Owner);

        // The declaration tree's root is the module, named as it was loaded.
        var root = module.GetModuleReflection();
        Assert.NotNull(root);
        Assert.Equal(SlangDeclKind.SLANG_DECL_KIND_MODULE, root.Kind);
        Assert.Equal(module.ReflectDeclarations().Children.Count, (int)root.ChildrenCount);

        // The entry point's function is the declaration the program's layout reports for it.
        var function = entry.FunctionReflection;
        Assert.NotNull(function);
        Assert.Equal("csFragment", function.Name);
        Assert.Equal(1u, function.ParameterCount);
        Assert.Equal("uv", function.GetParameter(0)!.Name);
        Assert.Equal("vector<float,4>", function.ResultType!.FullName);

        using var program = session.Link(module, entry);
        var reflected = program.GetLayout().FindEntryPointByName("csFragment")!;
        Assert.Equal(SlangStage.SLANG_STAGE_FRAGMENT, reflected.Stage);
        Assert.Equal(function.Name, reflected.Function!.Name);
        Assert.Equal(function.ParameterCount, reflected.Function.ParameterCount);
    }

    [NativeFact]
    public void A_specialized_generic_reports_its_arguments_through_the_reflection_objects()
    {
        // The L2 route the cooker's SpecializeGeneric takes, asked directly: the generic container,
        // the specialization, and each parameter's concrete argument.
        const string Generic = """
            interface ICsShade { float3 shade(float3 a); }
            struct CsLambert : ICsShade { float3 shade(float3 a) { return a; } }
            struct CsWrapper<T : ICsShade, let N : int> { T inner; float4 values[N]; }

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { CsWrapper<CsLambert, 2> w; return w.values[0]; }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("csg", "csg.slang", Generic);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, entry);

        var shader = program.GetLayout();
        var generic = shader.FindTypeByName("CsWrapper")!.GenericContainer;
        Assert.NotNull(generic);
        Assert.Equal(1u, generic.TypeParameterCount);
        Assert.Equal(1u, generic.ValueParameterCount);
        Assert.Equal(SlangDeclKind.SLANG_DECL_KIND_STRUCT, generic.InnerKind);

        var typeParameter = generic.GetTypeParameter(0)!;
        Assert.Equal("T", typeParameter.Name);
        Assert.Equal(1u, generic.GetTypeParameterConstraintCount(typeParameter));
        Assert.Equal("ICsShade", generic.GetTypeParameterConstraintType(typeParameter, 0)!.DeclaredName);

        var lambert = shader.FindTypeByName("CsLambert")!;
        Assert.True(shader.IsSubType(lambert, shader.FindTypeByName("ICsShade")!));

        var specialized = shader.SpecializeGeneric(generic, GenericArgument.OfType(lambert), GenericArgument.OfInt(3));
        Assert.Equal("CsLambert", specialized.GetConcreteType(specialized.GetTypeParameter(0)!)!.DeclaredName);
        Assert.Equal(3, specialized.GetConcreteIntVal(specialized.GetValueParameter(0)!));

        var type = specialized.InnerDecl!.Type!.ApplySpecializations(specialized)!;
        output.WriteLine($"specialized: {type.FullName}");
        Assert.Equal("CsWrapper<CsLambert, 3>", type.FullName);

        // The same answer the cooker's snapshot gives.
        var snapshot = program.SpecializeGeneric("CsWrapper", GenericArgument.OfType(lambert), GenericArgument.OfInt(3))!;
        Assert.Equal(type.FullName, snapshot.Name);

        // The mismatch guard (U3) is the L2 call's, not the cooker's.
        var refusal = Assert.Throws<ArgumentException>(() => shader.SpecializeGeneric(generic, GenericArgument.OfInt(1), GenericArgument.OfInt(3)));
        Assert.Contains("access-violates", refusal.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- L3: IComponentType2

    [NativeFact]
    public void The_entry_points_compile_result_is_the_code_and_the_metadata_of_the_entry_point()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("cs", "cs.slang", Source);
        using var entry = module.FindEntryPoint("csFragment");
        using var program = session.Link(module, entry);

        // Acquired once.
        var extension = program.TryComponentType2;
        Assert.NotNull(extension);
        Assert.Same(extension, program.TryComponentType2);
        Assert.Same(extension, program.ComponentType2);
        Assert.NotEqual(0, extension.NativeHandle);

        using var result = extension.GetEntryPointCompileResult(0);
        // Measured at v2026.21: two items, the code and a debug-module slot that is empty without
        // separate debug information (SLANG_E_NOT_AVAILABLE, which the binding passes through).
        output.WriteLine($"items: {result.ItemCount}");
        Assert.Equal(2u, result.ItemCount);
        var empty = Assert.Throws<SharpSlang.Interop.SlangException>(() => result.GetItemData(1));
        Assert.Equal(SlangResult.NotAvailable, empty.Result);
        Assert.Equal(1, program.LiveChildren);

        using (var code = result.GetItemData(0))
        {
            Assert.Equal(program.GetEntryPointCodeBytes(0), code.ToArray());
        }

        // The metadata that came with the compile answers as the entry point's own metadata does,
        // for every binding of the layout, the unused texture included.
        var layout = program.Reflect();
        using var fromResult = result.GetMetadata();
        using var fromProgram = program.GetEntryPointMetadata(0);
        var bindings = layout.Parameters.SelectMany(p => p.Bindings).ToList();
        Assert.NotEmpty(bindings);
        foreach (var b in bindings)
        {
            Assert.Equal(fromProgram.IsUsed(b.Category, b.Space, b.Index), fromResult.IsUsed(b.Category, b.Space, b.Index));
        }

        Assert.Contains(fromResult.UnusedBindings(layout), b => b == layout.Parameters.Single(p => p.Name == "csUnused").PrimaryBinding);
    }

    [NativeFact]
    public void The_whole_program_compile_result_is_the_target_code()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("csc", "csc.slang", Compute);
        using var entry = module.FindEntryPoint("csCompute");
        using var program = session.Link(module, entry);

        using var result = program.ComponentType2.GetTargetCompileResult(0);
        Assert.True(result.ItemCount >= 1);
        using var code = result.GetItemData(0);
        using var target = program.GetTargetCode();
        Assert.Equal(target.ToArray(), code.ToArray());

        // The result is a child of the program, and refuses to outlive it.
        Assert.Throws<InvalidOperationException>(program.Dispose);
    }

    // ---------------------------------------------------------------- L3: IBindlessResourceMetadata

    [NativeFact]
    public void An_ordinary_shader_uses_no_bindless_heap_and_says_so_twice()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("cs", "cs.slang", Source);
        using var entry = module.FindEntryPoint("csFragment");
        using var program = session.Link(module, entry);

        using var metadata = program.GetEntryPointMetadata(0);
        var bindless = metadata.TryBindlessResourceMetadata;
        Assert.NotNull(bindless);
        Assert.Same(bindless, metadata.BindlessResourceMetadata);

        // The code generator's answer: no heap. The front end's reservation stays put whatever
        // the code uses, as slang.h says of it: measured space 1 on this shader at v2026.21.
        Assert.False(bindless.UsesBindlessResourceHeap);
        Assert.Equal(1u, program.Reflect().BindlessSpaceIndex);
        Assert.Equal(1, program.GetLayout().BindlessSpaceIndex);
    }

    [NativeFact]
    public void A_descriptor_handle_shader_uses_the_bindless_heap_and_reserves_a_space()
    {
        const string Bindless = """
            uniform DescriptorHandle<Texture2D> csHandle;
            RWStructuredBuffer<float4> csOut;

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void csBindless() { csOut[0] = csHandle.Load(int3(0, 0, 0)); }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("csb", "csb.slang", Bindless);
        using var entry = module.FindEntryPoint("csBindless");
        using var program = session.Link(module, entry);

        using var metadata = program.GetEntryPointMetadata(0);
        var space = program.GetLayout().BindlessSpaceIndex;
        var uses = metadata.BindlessResourceMetadata.UsesBindlessResourceHeap;
        output.WriteLine($"bindless space {space}, usesBindlessResourceHeap {uses}");

        Assert.True(uses, "a shader reading through a DescriptorHandle reports no bindless heap");
        Assert.True(space >= 0, "a shader reading through a DescriptorHandle reserves no bindless space");
        Assert.Equal((uint)space, program.Reflect().BindlessSpaceIndex);
    }

    // ---------------------------------------------------------------- no hidden defaults

    [DxcFact]
    public void Without_a_profile_a_DXIL_target_gets_Slangs_own_shader_model()
    {
        // The binding names no DXIL profile of its own (AGENTS.md#dxil): an unprofiled target is
        // Slang's choice, and `sm_6_5` named is what it says. Read from the DXIL part's program
        // header, which dxc writes: (kind << 16) | (major << 4) | minor.
        var unprofiled = ShaderModel(new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL });
        var named = ShaderModel(new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL, Profile = "sm_6_5" });
        output.WriteLine($"unprofiled {unprofiled}, sm_6_5 {named}");

        Assert.Equal((6, 5), named);
        Assert.Equal((6, 0), unprofiled);

        static (int Major, int Minor) ShaderModel(TargetOptions target)
        {
            using var global = SlangGlobalSession.Create();
            using var session = global.CreateSession(new SessionOptions { Targets = [target] });
            using var module = session.LoadModuleFromSource("csd", "csd.slang", Compute);
            using var entry = module.FindEntryPoint("csCompute");
            using var program = session.Link(module, entry);
            var container = program.GetEntryPointCodeBytes(0);

            // DXBC: 'DXBC', 16-byte digest, version, size, part count, part offsets.
            Assert.Equal("DXBC"u8.ToArray(), container[..4]);
            var parts = BitConverter.ToUInt32(container, 28);
            for (var i = 0; i < parts; i++)
            {
                var offset = (int)BitConverter.ToUInt32(container, 32 + (4 * i));
                if (!container.AsSpan(offset, 4).SequenceEqual("DXIL"u8)) continue;
                var version = BitConverter.ToUInt32(container, offset + 8);
                return ((int)((version >> 4) & 0xF), (int)(version & 0xF));
            }

            throw new InvalidOperationException("the container has no DXIL part");
        }
    }
}
