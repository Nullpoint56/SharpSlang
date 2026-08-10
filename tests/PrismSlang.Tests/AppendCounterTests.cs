// An AppendStructuredBuffer costs two descriptors, and only one of them is a parameter.
//
// Slang emits a separate storage buffer to hold the count and reports it as an ordinary binding
// range named `counter`. Three declared buffers in the corpus's append-buffers shader produce five
// bindings — and the parameter list has three entries, at bindings 0, 2 and 4. A renderer that
// builds its descriptor set layout from ProgramLayout.Parameters, which is the obvious thing to do,
// is short two storage buffers the shader will read.
//
// Worse for a shader with both an append and a consume buffer: both counters are named `counter`,
// so even a renderer reading the descriptor-set view cannot say which belongs to which. That is
// what CounterBinding is for.

using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Vulkan;

namespace PrismSlang.Tests;

public class AppendCounterTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static ProgramLayout Reflect()
    {
        var shaderCase = ShaderCorpus.Case("append-buffers");

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("ab", "ab.slang", shaderCase.Source);
        using var entry = module.FindEntryPoint("abCompute");
        using var program = session.Link(module, entry);
        return program.Reflect();
    }

    [NativeFact]
    public void Three_declared_buffers_occupy_five_bindings()
    {
        // The premise, and the reason the rest of this file exists. Asserted rather than described
        // because if Slang ever folded the counter into the buffer, everything below becomes wrong
        // in the safe direction and this is what would say so.
        var layout = Reflect();
        var set = Assert.Single(layout.DescriptorSets);

        foreach (var r in set.Ranges)
            output.WriteLine($"binding {r.IndexOffset} {r.Name} counter->{r.CounterBinding}");

        Assert.Equal(3, layout.Parameters.Count);
        Assert.Equal(5, set.Ranges.Count);
    }

    [NativeFact]
    public void Each_append_buffer_names_the_binding_of_its_own_counter()
    {
        var layout = Reflect();
        var set = Assert.Single(layout.DescriptorSets);

        var append = set.Ranges.Single(r => r.Name == "abAppend");
        var consume = set.Ranges.Single(r => r.Name == "abConsume");
        var plain = set.Ranges.Single(r => r.Name == "abPlain");

        // The counter sits immediately after its buffer, which is worth pinning: a host that
        // assumed otherwise would still work, and a host that assumed *this* and got it wrong
        // would bind the other buffer's counter and silently corrupt both.
        Assert.Equal(append.IndexOffset + 1, append.CounterBinding);
        Assert.Equal(consume.IndexOffset + 1, consume.CounterBinding);

        // An RWStructuredBuffer has no counter, so the field distinguishes the two kinds rather
        // than being set on everything buffer-shaped.
        Assert.Null(plain.CounterBinding);
    }

    [NativeFact]
    public void The_two_counters_are_indistinguishable_by_name_and_that_is_the_point()
    {
        // Both are called `counter`. Without CounterBinding there is nothing in the layout that
        // says which append buffer either belongs to — the pairing exists only in the order they
        // happen to appear.
        var layout = Reflect();
        var set = Assert.Single(layout.DescriptorSets);

        var counters = set.Ranges.Where(r => r.IsCounter).ToList();
        Assert.Equal(2, counters.Count);
        Assert.Equal(["counter", "counter"], counters.Select(c => c.Name));

        // And every counter is claimed by exactly one buffer.
        var claimed = set.Ranges.Where(r => r.CounterBinding is not null)
            .Select(r => r.CounterBinding!.Value)
            .ToList();

        Assert.Equal(counters.Select(c => c.IndexOffset).Order(), claimed.Order());
    }

    [NativeFact]
    public void No_counter_appears_in_the_parameter_list()
    {
        // Which is why building a descriptor set layout from Parameters is short two bindings.
        // RequireEveryParameterIsPlaced checks the other direction — every parameter has a binding
        // — and cannot see this one.
        var layout = Reflect();

        Assert.DoesNotContain("counter", layout.Parameters.Select(p => p.Name));
        Assert.Equal(["abAppend", "abConsume", "abPlain"],
            layout.Parameters.Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [NativeFact]
    public void The_projection_carries_the_pairing_through_to_Vulkan()
    {
        // Where a renderer actually reads it: VkDescriptorSetLayoutBinding does not have a concept
        // of a counter, so the pairing has to survive the projection or it is lost at the last step.
        var vulkan = Reflect().ToVulkan();
        var set = Assert.Single(vulkan.DescriptorSets);

        var append = set.Bindings.Single(b => b.Name == "abAppend");
        Assert.Equal(append.Binding + 1, append.CounterBinding);

        // Every binding, counters included, is a storage buffer the host must supply.
        Assert.All(set.Bindings, b => Assert.Equal(VulkanDescriptorType.StorageBuffer, b.DescriptorType));
        Assert.Equal(5, set.Bindings.Count);
    }

    [NativeFact]
    public void A_shader_with_no_append_buffer_reports_no_counters()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("nc", "nc.slang", """
            RWStructuredBuffer<float4> ncOut;

            [shader("compute")]
            [numthreads(64, 1, 1)]
            void ncCompute(uint id : SV_DispatchThreadID) { ncOut[id] = 1; }
            """);
        using var entry = module.FindEntryPoint("ncCompute");
        using var program = session.Link(module, entry);

        var layout = program.Reflect();
        Assert.All(layout.DescriptorSets.SelectMany(s => s.Ranges), r => Assert.Null(r.CounterBinding));
    }

    [NativeFact]
    public void The_counter_survives_a_round_trip()
    {
        var layout = Reflect();
        var restored = ProgramLayout.FromJson(layout.ToJson());

        Assert.Equal(
            layout.DescriptorSets.SelectMany(s => s.Ranges).Select(r => (r.Name, r.CounterBinding)),
            restored.DescriptorSets.SelectMany(s => s.Ranges).Select(r => (r.Name, r.CounterBinding)));
    }
}
