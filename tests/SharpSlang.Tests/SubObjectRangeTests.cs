// Slang's third view of the binding data — and rather less than its name promises.
//
// The parameter list says soMaterial exists. The descriptor-set view says which set holds it.
// The sub-object view was expected to say that the set *is* soMaterial, and it does not: it says
// which of the global scope's binding ranges are containers, and nothing reliable about where they
// landed. Two of the three things this file originally set out to assert turned out to be false,
// which is the reason it says what it says.

using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class SubObjectRangeTests(ITestOutputHelper output)
{
    private const string Source = """
        struct SoMaterial { float4 tint; };
        struct SoObject   { float4x4 model; };

        Texture2D                  soGlobalTexture;
        SamplerState               soGlobalSampler;
        ParameterBlock<SoMaterial> soMaterial;
        ParameterBlock<SoObject>   soObject;
        ConstantBuffer<SoMaterial> soPlainBuffer;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            return soGlobalTexture.Sample(soGlobalSampler, 0)
                 + soMaterial.tint
                 + mul(soObject.model, float4(1))
                 + soPlainBuffer.tint;
        }
        """;

    private static ProgramLayout Reflect()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("so", "so.slang", Source);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, entry);
        return program.Reflect(0);
    }

    [NativeFact]
    public void A_container_is_reported_as_a_sub_object_of_the_global_scope()
    {
        var layout = Reflect();

        foreach (var r in layout.SubObjectRanges)
            output.WriteLine($"sub-object: bindingRange {r.BindingRangeIndex} {r.Name}");

        Assert.NotEmpty(layout.SubObjectRanges);

        // By name, which is the part that makes the range usable at all — without it a host has
        // an index into a list and no way to say which parameter it belongs to.
        //
        // The names do not come from getSubObjectRangeOffset, which is where they look like they
        // should be. That returns a variable layout with no variable behind it and every name came
        // back null; they come from the binding range it points at instead.
        var names = layout.SubObjectRanges.Select(r => r.Name).ToList();
        Assert.Contains("soMaterial", names);
        Assert.Contains("soObject", names);
    }

    [NativeFact]
    public void Every_container_qualifies_not_only_a_parameter_block()
    {
        // The assumption this file was written to confirm, and it was wrong. A
        // ConstantBuffer<T> holding nothing but values is reported here alongside the two
        // ParameterBlocks — so the view answers "is it a container", not "does it take a register
        // space of its own", and anyone using it as a ParameterBlock detector gets a false
        // positive on every constant buffer in the shader.
        var layout = Reflect();

        Assert.Equal(
            ["soMaterial", "soObject", "soPlainBuffer"],
            layout.SubObjectRanges.Select(r => r.Name).Order(StringComparer.Ordinal));

        // What does survive: the things bound directly are not here.
        Assert.DoesNotContain("soGlobalTexture", layout.SubObjectRanges.Select(r => r.Name));
        Assert.DoesNotContain("soGlobalSampler", layout.SubObjectRanges.Select(r => r.Name));
    }

    [NativeFact]
    public void The_set_a_block_occupies_comes_from_the_descriptor_set_view_not_from_here()
    {
        // The second wrong assumption, and the reason SubObjectRange carries no space at all.
        // getSubObjectRangeSpaceOffset returns 0 for soMaterial, which occupies set 1, and 0 for
        // soObject, which occupies set 2; getBindingRangeDescriptorSetIndex reports 0 for both,
        // being an index into the enclosing scope's own list. Emitting either would have put a
        // wrong number in an authoritative-looking field.
        var layout = Reflect();

        foreach (var set in layout.DescriptorSets)
            output.WriteLine($"set {set.SpaceOffset}: {string.Join(", ", set.Ranges.Select(r => r.Name ?? "?"))}");

        var material = layout.SubObjectRanges.Single(r => r.Name == "soMaterial");
        var occupied = layout.DescriptorSets.Single(s => s.Ranges.Any(r => r.Name == material.Name));

        Assert.NotEqual(0u, occupied.SpaceOffset);
    }

    [NativeFact]
    public void A_shader_that_nests_nothing_reports_no_sub_object_ranges()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("flat", "flat.slang", """
            Texture2D    flTexture;
            SamplerState flSampler;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return flTexture.Sample(flSampler, 0); }
            """);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, entry);

        Assert.Empty(program.Reflect(0).SubObjectRanges);
    }

    [NativeFact]
    public void The_snapshot_survives_a_round_trip()
    {
        // Everything on ProgramLayout has to serialize — the whole point is baking it beside the
        // SPIR-V — and a new collection is exactly what gets added to the record and forgotten in
        // the converter.
        var layout = Reflect();
        var restored = ProgramLayout.FromJson(layout.ToJson());

        Assert.Equal(
            layout.SubObjectRanges.Select(r => (r.BindingRangeIndex, r.Name)),
            restored.SubObjectRanges.Select(r => (r.BindingRangeIndex, r.Name)));
    }
}
