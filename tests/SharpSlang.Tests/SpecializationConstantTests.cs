// Specialization constants, from the Slang declaration through to VkSpecializationInfo.
//
// The load-bearing test is the first one: every reflected constant id is checked against the SpecId
// decoration in the emitted module. Nothing else in this file matters if that correspondence is
// wrong, because a constant id the host and the module disagree on produces a pipeline that builds
// cleanly and runs with the compiled-in default — the worst kind of failure to debug.

using System.Runtime.InteropServices;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Vulkan;

namespace SharpSlang.Tests;

public class SpecializationConstantTests
{
    private const string Shader = """
        [vk::constant_id(0)] const int   kLightCount = 4;
        [vk::constant_id(1)] const bool  kUseShadows = true;
        [vk::constant_id(2)] const float kBias       = 0.0125;
        [vk::constant_id(9)] const uint  kFlags      = 7;
        [SpecializationConstant] const int kAuto = 11;

        struct U { float4x4 mvp; };
        ConstantBuffer<U> u;
        Texture2D albedo;
        SamplerState samp;

        [shader("fragment")]
        float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
        {
            float v = kLightCount + kBias + kFlags + kAuto;
            float3 c = albedo.Sample(samp, uv).rgb * (kUseShadows ? v : 0);
            return float4(c, u.mvp[0][0]);
        }
        """;

    private sealed record Compiled(ProgramLayout Layout, byte[] Code);

    private static Compiled Compile(string source = Shader)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("s", "s.slang", source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        return new Compiled(program.Reflect(), program.GetEntryPointCodeBytes(0));
    }

    // ---------------------------------------------------------------- ids

    [NativeFact]
    public void Reflected_ids_match_the_emitted_SpecId_decorations()
    {
        var compiled = Compile();

        var fromSpirv = SpecIdsByName(compiled.Code);
        var fromReflection = compiled.Layout.SpecializationConstants
            .ToDictionary(c => c.Name, c => c.ConstantId);

        Assert.Equal(fromSpirv, fromReflection);

        // Specifically: an explicit id far from the others, and one Slang assigned itself.
        Assert.Equal(9u, fromReflection["kFlags"]);
        Assert.Equal(3u, fromReflection["kAuto"]);
    }

    [NativeFact]
    public void Constants_are_ordered_by_id_not_by_declaration()
    {
        var ids = Compile().Layout.SpecializationConstants.Select(c => c.ConstantId).ToList();
        Assert.Equal(ids.Order(), ids);
    }

    [NativeFact]
    public void Types_and_defaults_come_through()
    {
        var byName = Compile().Layout.SpecializationConstants.ToDictionary(c => c.Name);

        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_INT32, byName["kLightCount"].ScalarType);
        Assert.Equal(4L, byName["kLightCount"].DefaultInt);

        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_BOOL, byName["kUseShadows"].ScalarType);
        Assert.Equal(1L, byName["kUseShadows"].DefaultInt);

        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_UINT32, byName["kFlags"].ScalarType);
        Assert.Equal(7L, byName["kFlags"].DefaultInt);

        // Read through the float getter because the type says so, not by probing: Slang's
        // GetDefaultValueFloat *succeeds* on the uint constant above and would turn 7 into 7.0f.
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32, byName["kBias"].ScalarType);
        Assert.Equal(0.0125, byName["kBias"].DefaultFloat!.Value, 6);
        Assert.Null(byName["kBias"].DefaultInt);
    }

    [NativeFact]
    public void Sizes_come_from_the_scalar_type_not_from_slangs_reported_size()
    {
        var layout = Compile().Layout;
        var byName = layout.SpecializationConstants.ToDictionary(c => c.Name);

        Assert.Equal(4u, byName["kLightCount"].ByteSize);
        Assert.Equal(4u, byName["kBias"].ByteSize);
        Assert.Equal(4u, byName["kFlags"].ByteSize);

        // VkBool32, not one byte.
        Assert.Equal(4u, byName["kUseShadows"].ByteSize);

        // What Slang actually reports, and why it cannot be used: one *slot*, and zero bytes.
        var parameter = layout.Parameter("kLightCount")!;
        Assert.Equal(0uL, parameter.Type.UniformSize);
        Assert.Equal(1uL, parameter.Type.Sizes
            .Single(s => s.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SPECIALIZATION_CONSTANT)
            .Size);
    }

    [NativeFact]
    public void A_shader_without_them_reports_none()
    {
        const string Plain = """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return float4(1,1,1,1); }
            """;

        Assert.Empty(Compile(Plain).Layout.SpecializationConstants);
    }

    // ---------------------------------------------------------------- not descriptors

    [NativeFact]
    public void They_are_flagged_so_nothing_mistakes_them_for_descriptors()
    {
        var layout = Compile().Layout;

        Assert.True(layout.Parameter("kLightCount")!.IsSpecializationConstant);
        Assert.False(layout.Parameter("u")!.IsSpecializationConstant);
        Assert.False(layout.Parameter("albedo")!.IsSpecializationConstant);
    }

    [NativeFact]
    public void They_do_not_steal_the_names_of_real_bindings()
    {
        // The regression this guards. A specialization constant reports space 0 and an index that
        // is really its constant_id, so [vk::constant_id(0)] kLightCount occupies exactly the same
        // (space 0, index 0) coordinates as the constant buffer at set 0 binding 0 — and it comes
        // first in the parameter list, so a first-writer-wins name table labelled the buffer
        // "kLightCount".
        var vulkan = Compile().Layout.ToVulkan();
        var set = Assert.Single(vulkan.DescriptorSets);

        var names = set.Bindings.Where(b => b.Name is not null).Select(b => b.Name!).ToList();
        Assert.Equal(["u", "albedo", "samp"], names);
        Assert.DoesNotContain(names, n => n.StartsWith('k'));

        // And they are not descriptors at all.
        Assert.Equal(3, set.Bindings.Count);
        Assert.Empty(vulkan.PushConstants);
    }

    // ---------------------------------------------------------------- VkSpecializationInfo

    [NativeFact]
    public void The_vulkan_projection_carries_the_map()
    {
        var vulkan = Compile().Layout.ToVulkan();

        Assert.Equal(
            [0u, 1u, 2u, 3u, 9u],
            vulkan.SpecializationConstants.Select(e => e.ConstantId));

        Assert.All(vulkan.SpecializationConstants, e => Assert.Equal(4u, e.Size));

        // Offsets are distinct, in order, and within the buffer a Pack of everything would make.
        var offsets = vulkan.SpecializationConstants.Select(e => e.Offset).ToList();
        Assert.Equal(offsets.Order(), offsets);
        Assert.Equal(offsets.Count, offsets.Distinct().Count());
    }

    [NativeFact]
    public void Packing_produces_a_buffer_the_map_entries_actually_index()
    {
        var layout = Compile().Layout;

        var packed = layout.Pack(new Dictionary<string, object>
        {
            ["kLightCount"] = 8,
            ["kUseShadows"] = false,
            ["kBias"] = 0.5f,
        });

        Assert.Equal([0u, 1u, 2u], packed.MapEntries.Select(e => e.ConstantId));
        Assert.False(packed.IsEmpty);

        foreach (var entry in packed.MapEntries)
        {
            Assert.True(entry.Offset + entry.Size <= packed.Data.Length,
                $"'{entry.Name}' at {entry.Offset}+{entry.Size} runs past a {packed.Data.Length}-byte buffer");
        }

        Assert.Equal(8, Read<int>(packed, "kLightCount"));
        Assert.Equal(0u, Read<uint>(packed, "kUseShadows"));   // VkBool32 false
        Assert.Equal(0.5f, Read<float>(packed, "kBias"));
    }

    [NativeFact]
    public void Unsupplied_constants_are_left_out_so_the_module_default_stands()
    {
        var layout = Compile().Layout;

        var only = layout.Pack(new Dictionary<string, object> { ["kFlags"] = 3u });
        var entry = Assert.Single(only.MapEntries);
        Assert.Equal(9u, entry.ConstantId);
        Assert.Equal(3u, Read<uint>(only, "kFlags"));

        Assert.True(layout.Pack(new Dictionary<string, object>()).IsEmpty);
    }

    [NativeFact]
    public void Defaults_can_be_written_out_explicitly()
    {
        var packed = Compile().Layout.Pack(new Dictionary<string, object>(), includeDefaults: true);

        Assert.Equal(5, packed.MapEntries.Count);
        Assert.Equal(4, Read<int>(packed, "kLightCount"));
        Assert.Equal(1u, Read<uint>(packed, "kUseShadows"));
        Assert.Equal(0.0125f, Read<float>(packed, "kBias"), 6);
        Assert.Equal(7u, Read<uint>(packed, "kFlags"));
        Assert.Equal(11, Read<int>(packed, "kAuto"));
    }

    [NativeFact]
    public void A_misspelled_constant_is_an_error_not_a_silent_no_op()
    {
        // A material referring to a constant the shader no longer declares means the two have
        // drifted. Dropping it quietly leaves the shader on its default and the material claiming
        // otherwise.
        var layout = Compile().Layout;

        var ex = Assert.Throws<ArgumentException>(
            () => layout.Pack(new Dictionary<string, object> { ["kLightCont"] = 8 }));

        Assert.Contains("kLightCont", ex.Message);
        Assert.Contains("kLightCount", ex.Message);   // names what is available
    }

    [NativeFact]
    public void Values_that_do_not_fit_the_declared_type_are_rejected()
    {
        var layout = Compile().Layout;

        // A float where the shader declared an int is a data error, not a rounding opportunity.
        Assert.Throws<ArgumentException>(
            () => layout.Pack(new Dictionary<string, object> { ["kLightCount"] = 1.5f }));

        // Wrong direction is fine: an integer for a float constant is how anyone writes 1 in JSON.
        var ok = layout.Pack(new Dictionary<string, object> { ["kBias"] = 1 });
        Assert.Equal(1.0f, Read<float>(ok, "kBias"));
    }

    [Fact]
    public void Out_of_range_integers_are_rejected_rather_than_truncated()
    {
        var small = new SpecializationConstant("k", 0, SlangScalarType.SLANG_SCALAR_TYPE_UINT8);
        var layout = Layout(small);

        Assert.Equal(1u, small.ByteSize);

        var ex = Assert.Throws<ArgumentException>(
            () => layout.Pack(new Dictionary<string, object> { ["k"] = 300 }));
        Assert.Contains("300", ex.Message);

        Assert.Equal(255, layout.Pack(new Dictionary<string, object> { ["k"] = 255 }).Data[0]);
    }

    [Fact]
    public void A_uint64_above_long_MaxValue_is_encoded_not_rejected()
    {
        // The packer routes integers through long. A uint64 constant can legitimately hold more
        // than that, so it takes its own path — otherwise a valid value fails as out of range.
        var layout = Layout(new SpecializationConstant("big", 0, SlangScalarType.SLANG_SCALAR_TYPE_UINT64));

        const ulong Value = ulong.MaxValue - 3;
        Assert.Equal(Value, Read<ulong>(layout.Pack(new Dictionary<string, object> { ["big"] = Value }), "big"));
    }

    [Fact]
    public void Values_are_naturally_aligned()
    {
        var layout = Layout(
            new SpecializationConstant("byte0", 0, SlangScalarType.SLANG_SCALAR_TYPE_UINT8),
            new SpecializationConstant("wide1", 1, SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64));

        var packed = layout.Pack(new Dictionary<string, object> { ["byte0"] = 1, ["wide1"] = 2.0 });

        Assert.Equal(0u, packed.MapEntries[0].Offset);
        Assert.Equal(8u, packed.MapEntries[1].Offset);   // padded from 1 up to 8
        Assert.Equal(2.0, Read<double>(packed, "wide1"));
    }

    // ---------------------------------------------------------------- through the artifact

    [NativeFact]
    public void They_survive_the_bake()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("s", "s.slang", Shader);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        using var buffer = new MemoryStream();
        Baking.ShaderPackageBaker.Bake(program, Shader).WriteTo(buffer);
        buffer.Position = 0;

        var restored = Baking.ShaderPackage.ReadFrom(buffer).Layout;

        Assert.Equal(
            [("kLightCount", 0u), ("kUseShadows", 1u), ("kBias", 2u), ("kAuto", 3u), ("kFlags", 9u)],
            restored.SpecializationConstants.Select(c => (c.Name, c.ConstantId)));

        // The type and default have to survive too, or the packer cannot work offline.
        Assert.Equal(0.0125, restored.SpecializationConstant("kBias")!.DefaultFloat!.Value, 6);
        Assert.Equal(4u, restored.SpecializationConstant("kUseShadows")!.ByteSize);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A snapshot carrying nothing but these constants, for cases Slang cannot produce.</summary>
    private static ProgramLayout Layout(params SpecializationConstant[] constants) => new()
    {
        Parameters = [],
        EntryPoints = [],
        DescriptorSets = [],
        GlobalConstantBufferSize = 0,
        GlobalConstantBufferBinding = 0,
        SpecializationConstants = constants,
    };

    private static T Read<T>(VulkanSpecializationInfo info, string name) where T : struct
    {
        var entry = info.MapEntries.Single(e => e.Name == name);
        return MemoryMarshal.Read<T>(info.Data.AsSpan((int)entry.Offset, (int)entry.Size));
    }

    /// <summary>SpecId decorations in the emitted module, keyed by the decorated variable's name.</summary>
    private static Dictionary<string, uint> SpecIdsByName(ReadOnlySpan<byte> spirv)
    {
        var words = MemoryMarshal.Cast<byte, uint>(spirv);
        var specIds = new Dictionary<uint, uint>();   // target id -> constant id
        var names = new Dictionary<uint, string>();   // target id -> OpName

        var i = 5;
        while (i < words.Length)
        {
            var count = (int)(words[i] >> 16);
            var op = words[i] & 0xFFFF;
            if (count < 1 || i + count > words.Length) break;

            switch (op)
            {
                case 71 when count >= 4 && words[i + 2] == 1:   // OpDecorate ... SpecId
                    specIds[words[i + 1]] = words[i + 3];
                    break;

                case 5 when count >= 3:                          // OpName
                    names[words[i + 1]] = DecodeString(words.Slice(i + 2, count - 2));
                    break;
            }

            i += count;
        }

        return specIds
            .Where(kv => names.ContainsKey(kv.Key))
            .ToDictionary(kv => names[kv.Key], kv => kv.Value);
    }

    private static string DecodeString(ReadOnlySpan<uint> words)
    {
        var bytes = new List<byte>();
        foreach (var w in words)
        {
            for (var b = 0; b < 4; b++)
            {
                var c = (byte)((w >> (b * 8)) & 0xFF);
                if (c == 0) return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add(c);
            }
        }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }
}
