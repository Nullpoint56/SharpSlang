// Offline baking: compile once at build time, load at run time with no compiler present.

using PrismSlang.Baking;
using PrismSlang.Native;
using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class BakingTests
{
    private const string Shader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D    albedo;
        SamplerState albedoSampler;

        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position, float2 uv)
        {
            VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv; return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        {
            return float4(albedo.Sample(albedoSampler, i.uv).rgb * u.tint * u.exposure, 1.0);
        }
        """;

    private static ShaderPackage BakeShader(string source = Shader)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", source);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        return ShaderPackageBaker.Bake(program, source);
    }

    [NativeFact]
    public void Bake_captures_code_and_layout_for_every_entry_point()
    {
        var package = BakeShader();

        Assert.Equal(["vertexMain", "fragmentMain"], package.EntryPoints.Select(e => e.Name));
        Assert.Equal(SlangStage.SLANG_STAGE_VERTEX, package.EntryPoint("vertexMain")!.Stage);
        Assert.Equal(SlangStage.SLANG_STAGE_PIXEL, package.EntryPoint("fragmentMain")!.Stage);
        Assert.All(package.EntryPoints, e => Assert.NotEmpty(e.Code));

        Assert.Equal(SlangGlobalSession.BuildTag, package.SlangVersion);
        Assert.Equal(32, package.SourceHash.Length);
        Assert.NotEmpty(package.Layout.Parameters);
    }

    [NativeFact]
    public void Package_round_trips_through_a_stream()
    {
        var original = BakeShader();

        using var buffer = new MemoryStream();
        original.WriteTo(buffer);
        buffer.Position = 0;
        var restored = ShaderPackage.ReadFrom(buffer);

        Assert.Equal(original.SlangVersion, restored.SlangVersion);
        Assert.Equal(original.SourceHash, restored.SourceHash);
        Assert.Equal(
            original.EntryPoints.Select(e => (e.Name, e.Stage, e.Code.Length)),
            restored.EntryPoints.Select(e => (e.Name, e.Stage, e.Code.Length)));

        // The bytes must survive exactly — this is what goes to vkCreateShaderModule.
        foreach (var e in original.EntryPoints)
            Assert.Equal(e.Code, restored.EntryPoint(e.Name)!.Code);

        // And the layout a renderer binds against.
        var before = (StructLayout)((ContainerLayout)original.Layout.Parameter("u")!.Type).ElementType;
        var after = (StructLayout)((ContainerLayout)restored.Layout.Parameter("u")!.Type).ElementType;
        Assert.Equal(before.Fields.Select(f => (f.Name, f.UniformOffset)),
                     after.Fields.Select(f => (f.Name, f.UniformOffset)));
    }

    [NativeFact]
    public void Round_tripped_package_carries_usable_descriptor_sets()
    {
        // The point of baking: everything the renderer needs, with no compiler in the process.
        var original = BakeShader();
        using var buffer = new MemoryStream();
        original.WriteTo(buffer);
        buffer.Position = 0;

        var restored = ShaderPackage.ReadFrom(buffer);
        var set = Assert.Single(restored.Layout.DescriptorSets);
        Assert.Equal(3, set.Ranges.Count);
    }

    [NativeFact]
    public void Staleness_is_detectable_without_recompiling()
    {
        var package = BakeShader();

        Assert.True(package.IsUpToDate(Shader, SlangGlobalSession.BuildTag));

        // Changed source.
        Assert.False(package.IsUpToDate(Shader + "\n// touched", SlangGlobalSession.BuildTag));

        // Changed compiler: Slang's layout rules are not stable across versions, so a bake from a
        // different build must be rejected even if the source is byte-identical.
        Assert.False(package.IsUpToDate(Shader, "2025.13.2"));
    }

    [NativeFact]
    public void Round_trips_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prismslang-{Guid.NewGuid():N}.pslb");
        try
        {
            BakeShader().WriteToFile(path);
            var restored = ShaderPackage.ReadFromFile(path);
            Assert.Equal(2, restored.EntryPoints.Count);
            Assert.NotEmpty(restored.EntryPoint("vertexMain")!.Code);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Reading_something_that_is_not_a_package_fails_clearly()
    {
        // A truncated or wrong file must not be interpreted as a shader — the bytes end up at
        // vkCreateShaderModule, where garbage is a device lost rather than an exception.
        using var junk = new MemoryStream([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);
        var ex = Assert.Throws<InvalidDataException>(() => ShaderPackage.ReadFrom(junk));
        Assert.Contains("magic", ex.Message);
    }

    [Fact]
    public void Future_format_version_is_rejected_rather_than_misread()
    {
        using var buffer = new MemoryStream();
        using (var w = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0x424C5350u);  // correct magic
            w.Write(99u);          // a version from the future
        }
        buffer.Position = 0;

        var ex = Assert.Throws<InvalidDataException>(() => ShaderPackage.ReadFrom(buffer));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Hashing_is_stable_and_distinguishes_sources()
    {
        Assert.Equal(ShaderPackage.HashSource("abc"), ShaderPackage.HashSource("abc"));
        Assert.NotEqual(ShaderPackage.HashSource("abc"), ShaderPackage.HashSource("abd"));
        Assert.Equal(32, ShaderPackage.HashSource("abc").Length);
    }
}
