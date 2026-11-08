// The packed packages, consumed the way an engine's cooker consumes them, before any is pushed.
//
// Two compiles, the two a cooker needs: SPIR-V with the optimiser required, which is
// GlobalSessionOptions' default, and DXIL through SharpSlang.Dxc. A failure is the binding's
// own exception with its own message, printed to stderr with exit code 1, so publish.yml needs no
// parsing to refuse the release. See the csproj for why the build is portable.

using SharpSlang;
using SharpSlang.Native;

try
{
    Console.WriteLine($"slang {SlangGlobalSession.BuildTag}; process {Environment.ProcessPath}; "
                      + $"working in {Environment.CurrentDirectory}");

    using var global = SlangGlobalSession.Create();

    var spirv = Compile(global, new TargetOptions(), "fragmentMain", """
        struct U { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<U> u;
        Texture2D albedo;
        SamplerState samp;

        [shader("fragment")]
        float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
        {
            float3 c = albedo.Sample(samp, uv).rgb * u.tint * u.exposure;
            return float4(normalize(c + 0.0001), 1.0);
        }
        """);

    if (spirv.Length < 20 || BitConverter.ToUInt32(spirv, 0) != 0x07230203)
        throw new InvalidOperationException("the SPIR-V target produced something that is not a SPIR-V module");

    Console.WriteLine($"spir-v {spirv.Length} bytes, with spirv-opt required");

    var dxil = Compile(global, new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL, Profile = "sm_6_5" }, "csMain", """
        RWStructuredBuffer<float> output;

        [shader("compute")]
        [numthreads(64, 1, 1)]
        void csMain(uint3 id : SV_DispatchThreadID) { output[id.x] = id.x * 2.0; }
        """);

    // A DXBC container, and a signed one: the 16 bytes after the magic are zeros when it is not,
    // and D3D12 refuses an unsigned container unless experimental shader models are enabled, which
    // Developer Mode alone does not do (measured with it on; DxilTests records the words). The
    // pinned dxc signs with the validator inside dxcompiler.dll, so this does not need dxil.dll.
    if (dxil.Length < 20 || !dxil.AsSpan(0, 4).SequenceEqual("DXBC"u8))
        throw new InvalidOperationException("the DXIL target produced something that is not a DXBC container");
    if (!dxil.AsSpan(4, 16).ContainsAnyExcept((byte)0))
        throw new InvalidOperationException("the DXIL container is unsigned");

    Console.WriteLine($"dxil {dxil.Length} bytes, signed");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
    return 1;
}

static byte[] Compile(SlangGlobalSession global, TargetOptions target, string entryPoint, string source)
{
    using var session = global.CreateSession(new SessionOptions { Targets = [target] });
    using var module = session.LoadModuleFromSource(entryPoint, $"{entryPoint}.slang", source);
    using var entry = module.FindEntryPoint(entryPoint);
    using var program = session.Link(module, entry);
    return program.GetEntryPointCodeBytes(0);
}
