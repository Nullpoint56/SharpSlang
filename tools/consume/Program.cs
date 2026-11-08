// Package consumption smoke test (SharpSlang.Consume.csproj), one mode per run, chosen by
// consume.ps1 through a define:
//
//   CONSUME_CORE    the binding and the dxc package: the loaded slang-compiler is the one in this
//                   application's output; SPIR-V compiles with the optimiser required (the
//                   default); on win-x64 a signed DXIL container compiles through dxc; with
//                   CONSUME_BAKE a baked package is written to args[0] for the reader run.
//   CONSUME_READER  the reader alone: reads args[0], projects nothing native, loads no compiler.
//   CONSUME_DXC     the dxc package alone: nothing to call; the output is what consume.ps1 checks.
//
// Every check prints one line, PASS, FAIL or NOTYET (a check whose prerequisite the packages do not
// have yet), and the exit code is 1 when any FAILed.

#if CONSUME_CORE || CONSUME_READER
using System.Diagnostics;
#endif
using System.Runtime.InteropServices;

var failed = false;
void Report(string outcome, string check, string detail)
{
    Console.WriteLine($"{outcome} {check}: {detail}");
    failed |= outcome == "FAIL";
}

Console.WriteLine($"process {Environment.ProcessPath}, RID {RuntimeInformation.RuntimeIdentifier}, base {AppContext.BaseDirectory}, cwd {Environment.CurrentDirectory}");
#if CONSUME_CORE || CONSUME_READER
var compilerNames = new[] { "slang-compiler.dll", "libslang-compiler.so", "libslang-compiler.dylib" };
string[] LoadedCompilers() => [.. Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
    .Select(m => m.FileName).Where(f => compilerNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))];
#endif

#if CONSUME_CORE
bool InOutput(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

try
{
    Console.WriteLine($"slang {SlangGlobalSession.BuildTag}");
    var loaded = LoadedCompilers();
    if (loaded.Length == 0) Report("FAIL", "loaded library", "no slang-compiler in the process after the first call");
    else if (loaded.All(InOutput)) Report("PASS", "loaded library", string.Join(", ", loaded));
    else Report("FAIL", "loaded library", $"{string.Join(", ", loaded)} is not in the application's output");

    using var global = SlangGlobalSession.Create();
    const string Fragment = """
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
        """;
    using (var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] }))
    using (var module = session.LoadModuleFromSource("fragmentMain", "fragmentMain.slang", Fragment))
    using (var entry = module.FindEntryPoint("fragmentMain"))
    using (var program = session.Link(module, entry))
    {
        var spirv = program.GetEntryPointCodeBytes(0);
        if (spirv.Length >= 20 && BitConverter.ToUInt32(spirv, 0) == 0x07230203) Report("PASS", "SPIR-V", $"{spirv.Length} bytes, spirv-opt required");
        else Report("FAIL", "SPIR-V", "the SPIR-V target produced something that is not a SPIR-V module");
#if CONSUME_BAKE
        if (args.Length > 0)
        {
            ShaderPackageBaker.Bake(program, Fragment).WriteToFile(args[0]);
            Report("PASS", "bake", $"wrote {args[0]} for the reader run");
        }
#endif
    }

    if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
    {
        const string Compute = """
            RWStructuredBuffer<float> output;

            [shader("compute")]
            [numthreads(64, 1, 1)]
            void csMain(uint3 id : SV_DispatchThreadID) { output[id.x] = id.x * 2.0; }
            """;
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL, Profile = "sm_6_5" }] });
        using var module = session.LoadModuleFromSource("csMain", "csMain.slang", Compute);
        using var entry = module.FindEntryPoint("csMain");
        using var program = session.Link(module, entry);
        var dxil = program.GetEntryPointCodeBytes(0);
        // A DXBC container, signed: the 16 bytes after the magic are zeros when it is not
        if (dxil.Length < 20 || !dxil.AsSpan(0, 4).SequenceEqual("DXBC"u8)) Report("FAIL", "DXIL", "not a DXBC container");
        else if (!dxil.AsSpan(4, 16).ContainsAnyExcept((byte)0)) Report("FAIL", "DXIL", "the container is unsigned");
        else Report("PASS", "DXIL", $"{dxil.Length} bytes, signed");
    }
    else Console.WriteLine("DXIL: not on win-x64, nothing to compile it with");
}
catch (Exception e)
{
    Report("FAIL", "compile", $"{e.GetType().Name}: {e.Message}");
}
#elif CONSUME_READER
try
{
    if (args.Length == 0) throw new ArgumentException("pass the baked package the core run wrote");
    var package = ShaderPackage.ReadFromFile(args[0]);
    Report("PASS", "read", $"slang {package.SlangVersion}, {package.EntryPoints.Count} entry point(s), {package.Layout.Parameters.Count} parameter(s)");
}
catch (Exception e)
{
    Report("FAIL", "read", $"{e.GetType().Name}: {e.Message}");
}
var loadedByReader = LoadedCompilers();
if (loadedByReader.Length == 0) Report("PASS", "no compiler loaded", "the reader ran without slang-compiler");
else Report("FAIL", "no compiler loaded", string.Join(", ", loadedByReader));
#elif CONSUME_DXC
var dxc = OperatingSystem.IsWindows() ? "dxcompiler.dll" : null;
if (dxc is null) Console.WriteLine("dxc: not on Windows, the package copies nothing");
else if (File.Exists(Path.Combine(AppContext.BaseDirectory, dxc))) Report("PASS", "dxc beside the application", dxc);
else Report("FAIL", "dxc beside the application", $"no {dxc} in {AppContext.BaseDirectory}");
#else
Report("FAIL", "mode", "built without a CONSUME_* define: run through consume.ps1");
#endif

return failed ? 1 : 0;
