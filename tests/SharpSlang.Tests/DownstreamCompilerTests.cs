// Is the SPIR-V optimiser actually there?
//
// Without slang-glslang beside the compiler, Slang emits unoptimised SPIR-V and reports it exactly
// once per global session, on a successful result — so shaders 2..n of a batch are silently
// oversized with no diagnostic at all. Measured on the shader below: 2,048 bytes without the
// optimiser against 1,664 with it at maximal, a 23% regression that nothing in the build would say
// a word about.
//
// These tests are also the assertion that the *package* ships it. A run with a Minimal payload
// fails here rather than shipping fat shaders.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class DownstreamCompilerTests
{
    private const string Heavy = """
        struct U { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<U> u;
        Texture2D albedo;
        SamplerState samp;
        float3 heavy(float3 c)
        {
            for (int i = 0; i < 4; ++i) { c = c * 1.0 + 0.0; c = normalize(c + 0.0001); }
            return c;
        }
        [shader("fragment")]
        float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
        {
            float3 c = albedo.Sample(samp, uv).rgb * u.tint * u.exposure;
            return float4(heavy(c), 1.0);
        }
        """;

    [NativeFact]
    public void The_staged_payload_includes_the_spirv_optimiser()
    {
        // The engine-facing requirement, asserted rather than assumed: fetch-native.ps1 -Profile
        // Spirv must actually put slang-glslang where the compiler can load it.
        using var global = SlangGlobalSession.Create(new GlobalSessionOptions
        {
            // Off, so a missing library fails *this* assert with a readable message rather than
            // throwing out of Create and skipping past the explanation.
            RequireSpirvOptimizer = false,
        });

        Assert.True(
            global.IsDownstreamCompilerAvailable(SlangPassThrough.SLANG_PASS_THROUGH_SPIRV_OPT),
            "slang-glslang is not loadable, so every shader compiles unoptimised. " +
            "Run: pwsh tools/fetch-native.ps1 -Profile Spirv");
    }

    [NativeFact]
    public void Requiring_the_optimiser_is_the_default_and_succeeds_on_a_correct_payload()
    {
        using var global = SlangGlobalSession.Create();
        global.RequireSpirvOptimizer();
    }

    [NativeFact]
    public void Availability_is_reported_per_compiler_not_globally()
    {
        // A guard that returned true for everything would pass the test above while proving
        // nothing. slang-llvm is deliberately not staged — the payload trims it, it is 82 MB and a
        // Vulkan renderer never needs host codegen.
        using var global = SlangGlobalSession.Create();

        Assert.True(global.IsDownstreamCompilerAvailable(SlangPassThrough.SLANG_PASS_THROUGH_SPIRV_OPT));
        Assert.False(global.IsDownstreamCompilerAvailable(SlangPassThrough.SLANG_PASS_THROUGH_LLVM));
    }

    [NativeFact]
    public void The_optimiser_measurably_changes_the_output()
    {
        // Proves the library is not merely present but actually running. If spirv-opt were silently
        // skipped, every level would produce the same bytes — which is exactly the symptom of the
        // missing-library case this whole file exists for.
        using var global = SlangGlobalSession.Create();

        var none = Compile(global, SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE);
        var maximal = Compile(global, SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL);

        Assert.True(maximal.Length < none.Length,
            $"optimised output ({maximal.Length} bytes) is not smaller than unoptimised " +
            $"({none.Length}); spirv-opt is present but appears not to be running");
    }

    private static byte[] Compile(SlangGlobalSession global, SlangOptimizationLevel level)
    {
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource($"d{level}", $"d{level}.slang", Heavy);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, [ep], new LinkOptions
        {
            CompilerOptions = new LinkTimeOptions { Optimization = level },
        });
        return program.GetEntryPointCodeBytes(0);
    }

    [NativeFact]
    public void Opting_out_is_possible_for_a_deliberately_minimal_deployment()
    {
        // The flag has to be usable, or a Minimal payload becomes unusable rather than merely
        // unoptimised.
        using var global = SlangGlobalSession.Create(new GlobalSessionOptions
        {
            RequireSpirvOptimizer = false,
        });

        Assert.NotEmpty(SlangGlobalSession.BuildTag);
    }
}
