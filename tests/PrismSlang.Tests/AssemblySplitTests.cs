// The acceptance criterion for the assembly split, asserted rather than asserted-to.
//
// The engine team's version, verbatim: a project referencing only the package assembly can
// ReadFromFile, project a ProgramLayout to a VulkanPipelineLayout, call VulkanSpecialization.Pack
// and VulkanEnvironment.Require, and walk FieldLayout.UniformOffset — and its build output contains
// no slang-compiler.dll, no slang-glslang.dll, and no runtimes/ payload.
//
// Both halves are checked here. The first half is tests/PrismSlang.Runtime.Consumer, which does
// exactly those five things and would not compile if any of them needed the compiler; this runs it
// against a real baked package. The second half is a directory listing, because "no payload" is a
// property of the deployment closure and reasoning about project references is not the same claim.

using System.Diagnostics;
using System.Reflection;
using PrismSlang.Baking;

namespace PrismSlang.Tests;

public class AssemblySplitTests
{
    /// <summary>The native files that must never reach a consumer of the reader assembly.</summary>
    /// <remarks>
    /// <c>NRI</c> is here because PrismSlang.NRI references SharpNRI.Descs and not SharpNRI, and
    /// that is the whole reason the Descs package exists: a projection must not drag NRI.dll into
    /// a process that only wanted a desc. One wrong ProjectReference and it would.
    /// </remarks>
    private static readonly string[] Payload =
        ["slang-compiler", "slang-glslang", "slang-rhi", "gfx", "slang", "NRI"];

    private static string ConsumerOutput =>
        typeof(AssemblySplitTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RuntimeConsumerOutput")
            .Value!;

    // ---------------------------------------------------------------- no payload

    [Fact]
    public void The_reader_only_consumer_carries_no_native_payload()
    {
        Assert.True(Directory.Exists(ConsumerOutput),
            $"the consumer was not built; expected its output at {ConsumerOutput}");

        var offenders = Directory
            .EnumerateFiles(ConsumerOutput, "*", SearchOption.AllDirectories)
            .Where(IsNative)
            .Select(f => Path.GetRelativePath(ConsumerOutput, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"a project referencing only PrismSlang.Runtime, PrismSlang.Vulkan and PrismSlang.NRI "
            + $"picked up native files. Something grew a reference to the compiler or to NRI:\n"
            + string.Join("", offenders.Select(f => $"    {f}\n")));

        static bool IsNative(string path)
        {
            var extension = Path.GetExtension(path);
            if (extension is not (".dll" or ".so" or ".dylib")) return false;

            var name = Path.GetFileNameWithoutExtension(path);
            return Payload.Any(p => name.Equals(p, StringComparison.OrdinalIgnoreCase)
                                 || name.StartsWith($"lib{p}", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_reader_only_consumer_has_no_runtimes_tree()
    {
        // The other shape the payload arrives in: packed per-RID rather than copied flat. One
        // ProjectReference to PrismSlang and this directory appears.
        Assert.False(Directory.Exists(Path.Combine(ConsumerOutput, "runtimes")),
            $"a runtimes/ tree appeared in {ConsumerOutput}");
    }

    [Fact]
    public void The_consumer_closure_is_the_reader_assemblies_and_nothing_else()
    {
        // Stated positively so the test says what the deliverable is, not only what it excludes.
        // SharpNRI.Descs is NRI's vocabulary as plain types and is the one dependency the NRI
        // projection brings; SharpNRI.dll, which would bring NRI.dll with it, is the one it must
        // not.
        var assemblies = Directory
            .EnumerateFiles(ConsumerOutput, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "PrismSlang.NRI.dll",
                "PrismSlang.Runtime.Consumer.dll",
                "PrismSlang.Runtime.dll",
                "PrismSlang.Vulkan.dll",
                "SharpNRI.Descs.dll",
            ],
            assemblies);
    }

    // ---------------------------------------------------------------- and it works

    [NativeFact]
    public void The_reader_only_consumer_reads_a_real_package_end_to_end()
    {
        // Bake here, where the compiler is, then hand the file to a process that does not have one.
        var path = Path.Combine(Path.GetTempPath(), $"prismslang-split-{Guid.NewGuid():N}.pslb");
        try
        {
            Bake(path);

            var exe = Path.Combine(ConsumerOutput, "PrismSlang.Runtime.Consumer.dll");
            Assert.True(File.Exists(exe), $"consumer not built at {exe}");

            using var process = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { exe, path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            var output = process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(120_000), "the consumer did not finish within 2 minutes");

            Assert.True(process.ExitCode == 0,
                $"the consumer failed with exit code {process.ExitCode}.\n{output}\n{errors}");

            // Everything the criterion named, in the order the consumer prints it.
            Assert.Contains("descriptor set(s)", output);
            Assert.Contains("root constant block(s)", output);
            Assert.Contains("specialization:", output);
            Assert.Contains("stride", output);
            Assert.Contains("OK - read, projected and packed", output);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Bake(string path)
    {
        const string Source = """
            [vk::constant_id(0)] const int kCascades = 3;

            struct Material { float3 tint; float gloss; float4x4 transform; float3x3 normalMatrix; };
            ConstantBuffer<Material> gMaterial;
            Texture2D    gAlbedo;
            SamplerState gSampler;

            struct VIn  { float3 position : POSITION; float2 uv : TEXCOORD0; };
            struct VOut { float4 clip : SV_Position; float2 uv : TEXCOORD0; };

            [shader("vertex")]
            VOut vertexMain(VIn v)
            {
                VOut o;
                o.clip = mul(gMaterial.transform, float4(v.position, 1));
                o.uv = v.uv;
                return o;
            }

            [shader("fragment")]
            float4 fragmentMain(VOut i) : SV_Target
            {
                float3 n = mul(gMaterial.normalMatrix, float3(0, 0, 1));
                return gAlbedo.Sample(gSampler, i.uv) * float4(gMaterial.tint * n, gMaterial.gloss)
                     * kCascades;
            }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("split", "split.slang", Source);
        using var vertex = module.FindEntryPoint("vertexMain");
        using var fragment = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vertex, fragment);

        ShaderPackageBaker.Bake(program, Source).WriteToFile(path);
    }
}
