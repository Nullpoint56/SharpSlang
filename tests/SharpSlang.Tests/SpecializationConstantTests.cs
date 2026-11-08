// Specialization constants, from the Slang declaration through to the baked snapshot.
//
// The load-bearing test is the first one: every reflected constant id is checked against the SpecId
// decoration in the emitted module. Nothing else in this file matters if that correspondence is
// wrong, because a constant id the host and the module disagree on produces a pipeline that builds
// cleanly and runs with the compiled-in default — the worst kind of failure to debug.

using System.Runtime.InteropServices;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Pipeline.NRI;

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
        var nri = Compile().Layout.ToNri();
        var set = Assert.Single(nri.DescriptorSets);

        var names = set.Ranges.Where(r => r.Name is not null).Select(r => r.Name!).ToList();
        Assert.Equal(["u", "albedo", "samp"], names);
        Assert.DoesNotContain(names, n => n.StartsWith('k'));

        // And they are not descriptors at all.
        Assert.Equal(3, set.Ranges.Count);
        Assert.Empty(nri.RootConstants);
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

        // The type and default have to survive too, or a host cannot specialize from a baked package.
        Assert.Equal(0.0125, restored.SpecializationConstant("kBias")!.DefaultFloat!.Value, 6);
        Assert.Equal(4u, restored.SpecializationConstant("kUseShadows")!.ByteSize);
    }

    // ---------------------------------------------------------------- helpers

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
