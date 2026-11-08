// The acceptance criterion for the assembly split, asserted rather than asserted-to.
//
// The acceptance criterion, verbatim: a project referencing only the package assembly can
// ReadFromFile, project a ProgramLayout to an NRI pipeline layout desc, read its specialization
// constants, and walk FieldLayout.UniformOffset — and its build output contains no slang-compiler.dll, no slang-glslang.dll, and no runtimes/ payload.
//
// Both halves are checked here. The first half is tests/SharpSlang.Pipeline.Consumer, which does
// exactly those four things and would not compile if any of them needed the compiler; this runs it
// against a real baked package. The second half is a directory listing, because "no payload" is a
// property of the deployment closure and reasoning about project references is not the same claim.

using System.Diagnostics;
using System.Reflection;
using SharpSlang.Baking;
using SharpSlang.Pipeline.NRI;

namespace SharpSlang.Tests;

public class AssemblySplitTests
{
    /// <summary>The native files that must never reach a consumer of the reader assembly.</summary>
    /// <remarks>
    /// <c>NRI</c> is here because SharpSlang.Pipeline.NRI references SharpNRI.Descs and not SharpNRI, and
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
            $"a project referencing only SharpSlang.Pipeline and SharpSlang.Pipeline.NRI "
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
        // ProjectReference to SharpSlang and this directory appears.
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
                "SharpNRI.Descs.dll",
                "SharpSlang.Descs.dll",
                "SharpSlang.Pipeline.Consumer.dll",
                "SharpSlang.Pipeline.NRI.dll",
                "SharpSlang.Pipeline.dll",
            ],
            assemblies);
    }

    [Fact]
    public void The_descs_assembly_names_no_native_library()
    {
        // NativeSplit.props puts SlangApi.cs, the one generated file with DllImports, in SharpSlang
        // and the rest of L0 in SharpSlang.Descs. Measured from the loaded assembly, so a file moved
        // across the split, or a hand-written DllImport added to descs/, fails here.
        var descs = typeof(Native.SlangUUID).Assembly;
        Assert.Equal("SharpSlang.Descs", descs.GetName().Name);

        var imports = descs.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => $"{m.DeclaringType}.{m.Name}")
            .ToList();
        Assert.True(imports.Count == 0, $"SharpSlang.Descs declares P/Invokes:\n{string.Join("\n", imports)}");

        Assert.Equal(["System.Runtime"], descs.GetReferencedAssemblies().Select(a => a.Name));
        Assert.Equal("SharpSlang", typeof(Native.SlangApi).Assembly.GetName().Name);
    }

    // ---------------------------------------------------------------- the binding and the pipeline

    [Fact]
    public void The_binding_names_no_pipeline_package()
    {
        // AGENTS.md#layers: the binding knows nothing about the pipeline. Measured from metadata:
        // its references are the runtime's and SharpSlang.Descs, and it grants none of the
        // pipeline packages its internals.
        var binding = typeof(SlangGlobalSession).Assembly;
        var references = binding.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.Contains("SharpSlang.Descs", references);
        Assert.DoesNotContain(references, r => r.StartsWith("SharpSlang.Pipeline", StringComparison.Ordinal));
        Assert.All(references, r => Assert.True(
            r is "SharpSlang.Descs" or "System" or "Microsoft.Win32.Primitives" || r.StartsWith("System.", StringComparison.Ordinal),
            $"SharpSlang references {r}"));

        var grants = binding.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName).ToList();
        Assert.DoesNotContain(grants, g => g.StartsWith("SharpSlang.Pipeline", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SharpSlang.Pipeline", new[] { "SharpSlang.Descs" })]
    [InlineData("SharpSlang.Pipeline.Bake", new[] { "SharpSlang", "SharpSlang.Descs", "SharpSlang.Pipeline" })]
    [InlineData("SharpSlang.Pipeline.NRI", new[] { "SharpNRI.Descs", "SharpSlang.Descs", "SharpSlang.Pipeline" })]
    public void Each_pipeline_package_references_what_its_slot_allows(string name, string[] allowed)
    {
        // The dependency graph of AGENTS.md#native-packages, from each assembly's own metadata:
        // the reader takes the descs, the cooker the binding and the reader, the projection the
        // reader and NRI's descs. Nothing else of this repository or of SharpNRI.
        var assembly = Load(name);
        var ours = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(r => r.StartsWith("SharpSlang", StringComparison.Ordinal) || r.StartsWith("SharpNRI", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(allowed.Order(StringComparer.Ordinal), ours);
    }

    [Fact]
    public void The_cooker_calls_no_native_function_and_no_binding_internal()
    {
        // The template's <Binding>.<Feature> slot: SharpSlang's public API only (owner,
        // 2026-11-08). Read from the cooker's IL metadata: no reference to SlangApi (the
        // P/Invokes), to a COM interface struct or to a reflection handle of the generated layer,
        // and nothing the binding declares non-public.
        var cooker = Load("SharpSlang.Pipeline.Bake");
        using var file = File.OpenRead(cooker.Location);
        using var pe = new System.Reflection.PortableExecutable.PEReader(file);
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);

        var native = new List<string>();
        foreach (var handle in md.TypeReferences)
        {
            var type = md.GetTypeReference(handle);
            var ns = md.GetString(type.Namespace);
            var typeName = md.GetString(type.Name);
            if (ns != "SharpSlang.Native") continue;

            // The vocabulary (enums) is what a cooker names; the import class, the interface
            // structs and the reflection handles are the layers below L2.
            var resolved = typeof(Native.SlangUUID).Assembly.GetType($"{ns}.{typeName}")
                ?? typeof(Native.SlangApi).Assembly.GetType($"{ns}.{typeName}");
            if (resolved is null || !resolved.IsEnum) native.Add($"{ns}.{typeName}");
        }

        Assert.True(native.Count == 0,
            "the cooker names the generated layer below the binding's L2:\n" + string.Join("\n", native));

        Assert.DoesNotContain(typeof(SlangGlobalSession).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>(),
            a => a.AssemblyName == "SharpSlang.Pipeline.Bake");
    }

    [Fact]
    public void A_cooker_consumer_carries_the_compiler()
    {
        // The other side of the reader-only consumer: a project referencing the cooker builds
        // with the binding and the host RID's payload beside it. The cooker's own build output is
        // that consumer, as a ProjectReference sees it.
        var binary = Path.GetDirectoryName(Load("SharpSlang.Pipeline.Bake").Location)!;
        var cookerOutput = Path.Combine(SlangHeader.RepoRoot(), "bake", "bin",
            Path.GetFileName(Path.GetDirectoryName(binary)!), Path.GetFileName(binary));

        Assert.True(Directory.Exists(cookerOutput), $"the cooker was not built at {cookerOutput}");
        var files = Directory.EnumerateFiles(cookerOutput).Select(Path.GetFileName).ToList();

        foreach (var expected in new[] { "SharpSlang.dll", "SharpSlang.Descs.dll", "SharpSlang.Pipeline.dll", "SharpSlang.Pipeline.Bake.dll" })
            Assert.Contains(expected, files);

        Assert.Contains(files, f => f!.Contains("slang-compiler", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f!.StartsWith("SharpSlang.Pipeline.NRI", StringComparison.Ordinal));
    }

    private static Assembly Load(string name) => name switch
    {
        "SharpSlang.Pipeline" => typeof(Reflection.ProgramLayout).Assembly,
        "SharpSlang.Pipeline.Bake" => typeof(Reflection.ReflectionWalker).Assembly,
        "SharpSlang.Pipeline.NRI" => typeof(Pipeline.NRI.NriPipelineProjection).Assembly,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not a pipeline package"),
    };

    // ---------------------------------------------------------------- and it works

    [NativeFact]
    public void The_reader_only_consumer_reads_a_real_package_end_to_end()
    {
        // Bake here, where the compiler is, then hand the file to a process that does not have one.
        var path = Path.Combine(Path.GetTempPath(), $"sharpslang-split-{Guid.NewGuid():N}.pslb");
        try
        {
            Bake(path);

            var exe = Path.Combine(ConsumerOutput, "SharpSlang.Pipeline.Consumer.dll");
            Assert.True(File.Exists(exe), $"consumer not built at {exe}");

            using var process = Process.Start(new ProcessStartInfo(DotnetHost.Path)
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

            // Everything the criterion named, in the order the consumer prints it, the projection
            // read back from the package rather than computed (stored at bake time below).
            Assert.Contains("NRI: read back from the package", output);
            Assert.Contains("descriptor set(s)", output);
            Assert.Contains("root constant block(s)", output);
            Assert.Contains("specialization:", output);
            Assert.Contains("stride", output);
            Assert.Contains("OK - read and projected", output);
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

        // The engine projects at bake time (owner, 2026-11-08): the cooker stores the NRI
        // projection in the package, and the reader-only consumer reads it back.
        ShaderPackageBaker.Bake(program, Source, 0)
            .WithNriProjection(new NriProjectionOptions { UnboundedArrayCap = 64 })
            .WriteToFile(path);
    }
}
