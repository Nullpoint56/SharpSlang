// Tier 2: generics, interfaces, and specialisation.
//
// This is the capability that justifies moving off GLSL. A shader declares what it needs as an
// interface or a generic parameter; the host supplies concrete types at bake time (specialisation)
// or dispatches between implementations at run time (type conformance). Neither is expressible in
// GLSL, and both replace #ifdef permutation matrices.

using PrismSlang.Interop;
using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class GenericsTests
{
    // A material system in miniature: an interface, two implementations, and a shader that is
    // written once against the interface.
    private const string Materials = """
        interface IMaterial
        {
            float3 shade(float3 albedo);
        }

        struct Lambert : IMaterial
        {
            float3 shade(float3 albedo) { return albedo * 0.5; }
        }

        struct Emissive : IMaterial
        {
            float3 shade(float3 albedo) { return albedo * 4.0; }
        }

        struct NotAMaterial { float x; }

        // Generic over the material: one source, many pipelines.
        float3 applyMaterial<T : IMaterial>(T material, float3 albedo)
        {
            return material.shade(albedo);
        }

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            Lambert m;
            return float4(applyMaterial(m, float3(1, 1, 1)), 1.0);
        }
        """;

    private static (SlangGlobalSession, SlangSession, SlangModule) Load(string source = Materials)
    {
        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        var module = session.LoadModuleFromSource("mat", "mat.slang", source);
        return (global, session, module);
    }

    // ---------------------------------------------------------------- type lookup

    [NativeFact]
    public void Types_can_be_found_by_name()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            Assert.NotNull(module.FindType("Lambert"));
            Assert.NotNull(module.FindType("Emissive"));
            Assert.NotNull(module.FindType("IMaterial"));
            Assert.Null(module.FindType("NoSuchType"));

            var ex = Assert.Throws<SlangException>(() => module.RequireType("NoSuchType"));
            Assert.Contains("NoSuchType", ex.Message);
        }
    }

    [NativeFact]
    public void Subtype_test_distinguishes_implementations_from_unrelated_types()
    {
        // The check a material system needs before offering a type as an IMaterial. Without it the
        // only feedback is a Slang diagnostic at link time.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, fs);

            var iMaterial = program.FindType("IMaterial")!;
            Assert.True(program.IsSubType(program.FindType("Lambert")!, iMaterial));
            Assert.True(program.IsSubType(program.FindType("Emissive")!, iMaterial));
            Assert.False(program.IsSubType(program.FindType("NotAMaterial")!, iMaterial));
        }
    }

    [NativeFact]
    public unsafe void Type_handle_throws_once_its_owner_is_disposed()
    {
        // The handle borrows from the module's reflection. Using it afterwards must be an
        // exception, not a read of freed memory.
        SlangType orphan;
        var (global, session, module) = Load();
        using (global) using (session)
        {
            orphan = module.RequireType("Lambert");
            module.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _ = orphan.Handle is not null);
        }
    }

    // ---------------------------------------------------------------- dynamic dispatch

    [NativeFact]
    public void Type_conformances_link_and_produce_code()
    {
        // Both implementations reachable through one pipeline, selected at run time.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var fs = module.FindEntryPoint("fragmentMain");

            var iMaterial = module.RequireType("IMaterial");
            using var lambert = session.CreateTypeConformance(module.RequireType("Lambert"), iMaterial, 0);
            using var emissive = session.CreateTypeConformance(module.RequireType("Emissive"), iMaterial, 1);

            using var program = session.Link(module, [fs], new LinkOptions
            {
                TypeConformances = [lambert, emissive],
            });

            var spirv = program.GetEntryPointCodeBytes(0);
            Assert.NotEmpty(spirv);
        }
    }

    [NativeFact]
    public void Conformance_for_a_non_implementing_type_is_rejected()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            var iMaterial = module.RequireType("IMaterial");
            var notAMaterial = module.RequireType("NotAMaterial");

            Assert.Throws<SlangException>(
                () => session.CreateTypeConformance(notAMaterial, iMaterial).Dispose());
        }
    }

    // ---------------------------------------------------------------- specialisation

    [NativeFact]
    public void Global_type_parameter_is_reported_and_can_be_specialised()
    {
        // `type_param` is the link-time specialisation hook: the shader says "someone will tell me
        // what Material is", and the host decides per material. Note the syntax — `extern struct`
        // compiles but produces no specialisation parameter at all, which is a silent no-op.
        const string Generic = """
            interface IMaterial { float3 shade(float3 albedo); }
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

        var (global, session, module) = Load(Generic);
        using (global) using (session) using (module)
        {
            using var fs = module.FindEntryPoint("fragmentMain");

            // Unspecialised: the program still has a parameter waiting.
            using var open = session.Link(module, fs);
            Assert.True(open.SpecializationParameterCount > 0,
                "expected an unbound specialisation parameter before substitution");

            var layout = open.Reflect();
            var param = Assert.Single(layout.SpecializationParameters);
            Assert.Equal("Material", param.Name);
            Assert.Equal(SpecializationParameterKind.Type, param.Kind);
            Assert.Contains("IMaterial", param.Constraints);

            // Specialised with each implementation: two distinct pipelines from one source.
            var lambert = SpecialiseWith(session, module, "Lambert");
            var emissive = SpecialiseWith(session, module, "Emissive");

            Assert.NotEmpty(lambert);
            Assert.NotEmpty(emissive);
            Assert.NotEqual(lambert, emissive);
        }

        static byte[] SpecialiseWith(SlangSession session, SlangModule module, string typeName)
        {
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, [fs], new LinkOptions
            {
                SpecializationArguments = [module.RequireType(typeName)],
            });
            Assert.Equal(0, program.SpecializationParameterCount);
            return program.GetEntryPointCodeBytes(0);
        }
    }

    // ---------------------------------------------------------------- declaration tree

    [NativeFact]
    public void Declaration_tree_enumerates_what_the_module_offers()
    {
        // What a material editor reads to populate a type picker without parsing .slang source.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            var tree = module.ReflectDeclarations();

            Assert.Equal(DeclarationKind.Module, tree.Kind);

            var structs = tree.Descendants(DeclarationKind.Struct).Select(d => d.Name).ToList();
            Assert.Equal(["Lambert", "Emissive", "NotAMaterial"], structs);

            // An `interface` declaration comes back as Unsupported — a gap in Slang's declaration
            // reflection, not a modelling choice here. The name survives, so it is still findable;
            // see DeclarationKind's remarks for how to confirm a candidate is an interface.
            var unsupported = tree.Descendants(DeclarationKind.Unsupported).Select(d => d.Name).ToList();
            Assert.Contains("IMaterial", unsupported);

            var functions = tree.Descendants(DeclarationKind.Func).Select(d => d.Name).ToList();
            Assert.Contains("fragmentMain", functions);

            // A generic function is a Generic node carrying the name, not a Func — its inner
            // function decl is nested and unnamed. So enumerating Func alone misses every generic
            // in the module, which is the opposite of what a tooling consumer expects.
            var generics = tree.Descendants(DeclarationKind.Generic).Select(d => d.Name).ToList();
            Assert.Contains("applyMaterial", generics);
            Assert.DoesNotContain("applyMaterial", functions);

            // Compiler internals ($init, $inheritance) are filtered out of Descendants.
            Assert.DoesNotContain(functions, n => n.StartsWith('$'));
        }
    }

    [NativeFact]
    public void Declaration_tree_works_before_linking()
    {
        // Deliberately independent of linking: a module with unbound generics cannot be linked,
        // but tooling still needs to see what is in it.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            Assert.NotEmpty(module.ReflectDeclarations().Children);
        }
    }

    [NativeFact]
    public void Interface_typed_global_is_reported_as_an_existential()
    {
        // The other shape of specialisation: an interface-typed shader parameter, where the
        // concrete type arrives at bind time rather than at link time. Slang reports it through
        // the same API as a type_param but with no name and no constraints, which is the only way
        // to tell the two apart.
        const string Existential = """
            interface IMaterial { float3 shade(float3 a); }
            struct Lambert : IMaterial { float3 shade(float3 a) { return a * 0.5; } }

            uniform IMaterial materialParam;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return float4(materialParam.shade(float3(1,1,1)), 1); }
            """;

        var (global, session, module) = Load(Existential);
        using (global) using (session) using (module)
        {
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, fs);

            var param = Assert.Single(program.Reflect().SpecializationParameters);
            Assert.Equal(SpecializationParameterKind.Existential, param.Kind);
            Assert.True(string.IsNullOrEmpty(param.Name));

            Assert.Equal(LayoutKind.Interface, program.Reflect().Parameter("materialParam")!.Type.Kind);
        }
    }

    [NativeFact]
    public void Generic_type_and_value_parameters_are_reported_on_the_declaration()
    {
        // Where value generics actually live. Slang's program-level specialisation API is
        // type-only: `struct Buffer<let N : int>` yields zero specialisation parameters, so a
        // consumer looking only at ProgramLayout.SpecializationParameters would never see N.
        const string ValueGeneric = """
            interface IThing { float get(); }

            struct Buffer<T : IThing, let N : int>
            {
                float data[N];
            }

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void computeMain() { }
            """;

        var (global, session, module) = Load(ValueGeneric);
        using (global) using (session) using (module)
        {
            using var ep = module.FindEntryPoint("computeMain");
            using var program = session.Link(module, ep);

            // Not a program-level specialisation parameter — this is the point.
            Assert.Empty(program.Reflect().SpecializationParameters);

            var buffer = Assert.Single(
                module.ReflectDeclarations().Descendants(DeclarationKind.Generic),
                d => d.Name == "Buffer");

            Assert.NotNull(buffer.GenericParameters);
            var byName = buffer.GenericParameters!.ToDictionary(p => p.Name);

            Assert.Equal(GenericParameterKind.Type, byName["T"].Kind);
            Assert.Contains("IThing", byName["T"].Constraints);

            Assert.Equal(GenericParameterKind.Value, byName["N"].Kind);
            Assert.Empty(byName["N"].Constraints);
        }
    }

    [NativeFact]
    public void Non_generic_declarations_carry_no_generic_parameters()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            var tree = module.ReflectDeclarations();
            Assert.All(tree.Descendants(DeclarationKind.Struct), d => Assert.Null(d.GenericParameters));
        }
    }

    [NativeFact]
    public void Specialisation_survives_json_round_trip()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, fs);

            var original = program.Reflect();
            var restored = ProgramLayout.FromJson(original.ToJson());

            Assert.Equal(
                original.SpecializationParameters.Select(p => (p.Index, p.Name, p.Kind)),
                restored.SpecializationParameters.Select(p => (p.Index, p.Name, p.Kind)));
        }
    }
}
