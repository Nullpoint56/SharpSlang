// What a function looks like, not merely that it exists.
//
// The declaration tree already said a module offers a function called `shade`. A material system
// binding a hook needs the rest: what it returns, what it takes, which parameters it writes back,
// and what attributes the author put on it. That is spReflectionFunction_*, and none of it was
// reached until now.
//
// Everything asserted here was measured first. The type spellings in particular are not the ones
// anyone would guess.

using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class FunctionSignatureTests
{
    private const string Source = """
        struct Surface
        {
            float3 albedo;
            float  roughness;

            float3 tinted(float3 light) { return albedo * light; }
        };

        [__AttributeUsage(_AttributeTargets.Function)]
        struct MaterialHookAttribute { int order; };

        [MaterialHook(3)]
        float3 shade(Surface s, float3 light, out float occlusion)
        {
            occlusion = 1;
            return s.albedo * light;
        }

        void accumulate(inout float4 target, float4 sample) { target += sample; }

        int   overloaded(int a)            { return a; }
        float overloaded(float a, float b) { return a + b; }

        [mutating]
        void neverCalled() { }

        float3 takesABuffer(StructuredBuffer<float4> data, uint i) { return data[i].xyz; }

        StructuredBuffer<float4> gData;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            Surface s; s.albedo = 1; s.roughness = 0;
            float o;
            float4 acc = 0;
            accumulate(acc, float4(shade(s, 1, o), o));
            return acc + overloaded(1.0f, 2.0f) + float4(takesABuffer(gData, 0) + s.tinted(1), 0);
        }
        """;

    private static (SlangGlobalSession, SlangSession, SlangModule) Load()
    {
        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        var module = session.LoadModuleFromSource("fn", "fn.slang", Source);
        return (global, session, module);
    }

    private static FunctionSignature Declared(SlangModule module, string name) =>
        module.ReflectDeclarations()
            .Descendants(DeclarationKind.Func)
            .Where(d => d.Name == name)
            .Select(d => d.Function)
            .FirstOrDefault(f => f is not null)
        ?? throw new InvalidOperationException($"no function '{name}' in the declaration tree");

    // ---------------------------------------------------------------- the signature

    [NativeFact]
    public void A_function_declaration_carries_its_whole_signature()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            var shade = Declared(module, "shade");

            Assert.Equal("shade", shade.Name);
            Assert.Equal(["s", "light", "occlusion"], shade.Parameters.Select(p => p.Name));
            Assert.Equal("Surface", shade.Parameters[0].Type);
        }
    }

    [NativeFact]
    public void Type_names_are_Slangs_canonical_spelling_not_the_sources()
    {
        // The one that will surprise a caller matching on strings. `float3` in the source is
        // `vector<float,3>` in reflection, and `StructuredBuffer<float4>` is
        // `StructuredBuffer<vector<float,4>, DefaultDataLayout>` — the implicit layout parameter
        // included. These come from GetFullName, which is the right call precisely because it does
        // not drop the generic arguments; GetName would say "StructuredBuffer" and lose the
        // element type, which is the part a hook check needs.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            Assert.Equal("vector<float,3>", Declared(module, "shade").ResultType);
            Assert.Equal("vector<float,3>", Declared(module, "shade").Parameters[1].Type);

            Assert.Equal(
                "StructuredBuffer<vector<float,4>, DefaultDataLayout>",
                Declared(module, "takesABuffer").Parameters[0].Type);

            // A function returning nothing says so rather than reporting null.
            Assert.Equal("void", Declared(module, "accumulate").ResultType);
        }
    }

    [NativeFact]
    public void Parameter_direction_is_reported()
    {
        // The distinction a host cannot guess from the type and must not get wrong: an `out`
        // parameter's incoming value is undefined.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            var shade = Declared(module, "shade");
            Assert.Equal(ParameterDirection.In, shade.Parameters[0].Direction);
            Assert.Equal(ParameterDirection.In, shade.Parameters[1].Direction);
            Assert.Equal(ParameterDirection.Out, shade.Parameters[2].Direction);

            var accumulate = Declared(module, "accumulate");
            Assert.Equal(ParameterDirection.InOut, accumulate.Parameters[0].Direction);
            Assert.Equal(ParameterDirection.In, accumulate.Parameters[1].Direction);
        }
    }

    [NativeFact]
    public void Modifiers_and_user_attributes_come_through()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            Assert.Contains("mutating", Declared(module, "neverCalled").Modifiers ?? []);

            var attributes = Declared(module, "shade").Attributes;
            Assert.NotNull(attributes);
            var hook = Assert.Single(attributes, a => a.Name == "MaterialHook");
            Assert.Equal(3, Assert.Single(hook.Arguments));
        }
    }

    [NativeFact]
    public void A_function_nobody_calls_is_still_reported()
    {
        // Reflection describes the module, not the reachable set. A material system enumerating
        // hooks needs the ones nothing has bound yet — that is the whole point of enumerating.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            Assert.Equal("void neverCalled()", Declared(module, "neverCalled").ToString());
        }
    }

    // ---------------------------------------------------------------- overloads

    [NativeFact]
    public void The_declaration_tree_reports_overloads_as_siblings()
    {
        // Two declarations, two nodes, and neither says it is overloaded. Worth pinning because
        // it is the opposite of what the resolved-by-name path below does with the same source,
        // and a consumer that uses only one of the two views will believe whichever it saw.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            var nodes = module.ReflectDeclarations()
                .Descendants(DeclarationKind.Func)
                .Where(d => d.Name == "overloaded")
                .ToList();

            Assert.Equal(2, nodes.Count);
            Assert.All(nodes, n => Assert.False(n.Function!.IsOverloaded));

            Assert.Equal(
                ["float overloaded(float a, float b)", "int overloaded(int a)"],
                nodes.Select(n => n.Function!.ToString()).Order(StringComparer.Ordinal));
        }
    }

    [NativeFact]
    public void Resolved_by_name_an_overload_set_is_one_function_with_the_real_ones_underneath()
    {
        // And the outer signature is not one of the candidates — it is empty. `void overloaded()`
        // is what Slang returns for the group itself, so a caller that reads Name/Parameters
        // without checking IsOverloaded gets a function that takes nothing and returns nothing,
        // which is not a signature any declaration in the source has.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var entry = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, entry);

            var overloaded = program.FindFunction("overloaded");
            Assert.NotNull(overloaded);
            Assert.True(overloaded.IsOverloaded);
            Assert.Equal("void overloaded()", overloaded.ToString());

            Assert.Equal(
                ["float overloaded(float a, float b)", "int overloaded(int a)"],
                overloaded.Overloads!.Select(o => o.ToString()).Order(StringComparer.Ordinal));

            // A name with one declaration is not an overload set, and reads normally.
            var shade = program.FindFunction("shade");
            Assert.NotNull(shade);
            Assert.False(shade.IsOverloaded);
            Assert.Equal(3, shade.Parameters.Count);
        }
    }

    [NativeFact]
    public void A_name_the_program_does_not_have_is_null_rather_than_an_exception()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var entry = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, entry);

            Assert.Null(program.FindFunction("noSuchFunction"));
        }
    }

    // ---------------------------------------------------------------- methods on a type

    [NativeFact]
    public void A_method_is_found_on_its_type_and_a_free_function_is_not()
    {
        // The question a material system actually asks: this concrete type was chosen, does it
        // have the method the hook needs, and does it take the right arguments.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var entry = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, entry);

            var surface = program.FindType("Surface");
            Assert.NotNull(surface);

            var tinted = program.FindFunction(surface, "tinted");
            Assert.NotNull(tinted);
            Assert.Equal("vector<float,3> tinted(vector<float,3> light)", tinted.ToString());

            // `shade` takes a Surface; it is not a member of one. The distinction matters because
            // both are spelled `shade(s, …)` at a call site in Slang's UFCS-ish way.
            Assert.Null(program.FindFunction(surface, "shade"));
        }
    }
}
