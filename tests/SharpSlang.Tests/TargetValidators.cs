// The outside oracle for each compile target that has no device here: its own validator or
// compiler, found on the machine at run time, judging what Slang emitted.
//
//   SPIR-V  spirv-val, at the Vulkan environment the module's version belongs to
//   DXIL    dxc's validator, IDxcValidator in dxil.dll (Microsoft.Direct3D.DXC, beside the tests)
//   GLSL    glslangValidator, compiling the text for Vulkan
//   HLSL    dxc's compiler, IDxcCompiler3 in dxcompiler.dll, to DXIL with its validator on
//   Metal   xcrun metal, compiling to AIR (macOS only)
//   WGSL    naga, which parses and validates
//   CUDA    NVRTC, from a CUDA toolkit
//   C++     the host's C++ compiler (the one NativeAbiTests finds), -fsyntax-only
//
// Discovery is by looking, at run time: PATH, then the directories the tools' own installers use
// (VULKAN_SDK's bin for spirv-val and glslangValidator, CARGO_HOME's or ~/.cargo's bin for naga,
// CUDA_PATH for NVRTC), then the test's own output for dxc. Never a configured path. A tool that
// is not there is a skip naming the tool and how to get it; a tool that is there and objects is a
// failure carrying its message. A tool counts as present only when it runs: a name on PATH that is
// a broken shim is the same as no name at all.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SharpSlang.Native;
using SharpSlang.Spirv;

namespace SharpSlang.Tests;

/// <summary>A compile target's outside judge.</summary>
public enum Validator
{
    SpirvVal,
    DxilValidator,
    GlslangValidator,
    DxcHlsl,
    XcrunMetal,
    Naga,
    Nvrtc,
    HostCxx,
}

/// <summary>What a validator said about one emitted module or source.</summary>
/// <param name="Accepted">Whether it accepted it.</param>
/// <param name="Message">Its words, warnings included; empty when it said nothing.</param>
public sealed record Verdict(bool Accepted, string Message);

internal static class TargetValidators
{
    /// <summary>Why this validator cannot run here, or null if it can.</summary>
    public static string? Unavailable(Validator v) => v switch
    {
        Validator.SpirvVal => Tool("spirv-val").Path is null
            ? "spirv-val not found on PATH or in $VULKAN_SDK/bin. Install SPIRV-Tools (apt install spirv-tools, the Vulkan SDK, or brew install spirv-tools)."
            : null,
        Validator.GlslangValidator => Tool("glslangValidator").Path is null
            ? "glslangValidator not found on PATH or in $VULKAN_SDK/bin. Install glslang (apt install glslang-tools, the Vulkan SDK, or brew install glslang)."
            : null,
        Validator.Naga => Tool("naga").Path is null
            ? "naga not found on PATH or in $CARGO_HOME/bin (~/.cargo/bin). Install it with: cargo install naga-cli --locked"
            : null,
        Validator.XcrunMetal => MetalCompiler.Value is null
            ? "xcrun cannot find the metal compiler (xcrun -sdk macosx -f metal). It ships with Xcode, on macOS: install Xcode, or its Metal toolchain (xcodebuild -downloadComponent MetalToolchain)."
            : null,
        Validator.DxilValidator => DxcNative.Unavailable
            ?? (Dxc.Validator.Value is null ? $"dxil.dll is not loadable from {AppContext.BaseDirectory}: the test project copies it from Microsoft.Direct3D.DXC on win-x64" : null),
        Validator.DxcHlsl => Dxc.Compiler.Value is null
            ? $"dxcompiler is not loadable from {AppContext.BaseDirectory}: the test project copies it from Microsoft.Direct3D.DXC on win-x64"
            : null,
        Validator.Nvrtc => Nvrtc.Library.Value is null
            ? "NVRTC not found (nvrtc64_*.dll under $CUDA_PATH/bin or on PATH; libnvrtc.so under /usr/local/cuda/lib64 or on the loader path). Install a CUDA toolkit."
            : null,
        Validator.HostCxx => NativeAbi.Compiler is null ? NativeAbi.Unavailable : null,
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    /// <summary>Where the validator was found and what it says its version is, for the test output.</summary>
    public static string Describe(Validator v) => v switch
    {
        Validator.SpirvVal => Tool("spirv-val").ToString(),
        Validator.GlslangValidator => Tool("glslangValidator").ToString(),
        Validator.Naga => Tool("naga").ToString(),
        Validator.XcrunMetal => $"metal at {MetalCompiler.Value}",
        Validator.DxilValidator => $"dxil.dll {Dxc.Validator.Value}",
        Validator.DxcHlsl => $"dxcompiler {Dxc.Compiler.Value}",
        Validator.Nvrtc => $"NVRTC {Nvrtc.Library.Value}",
        Validator.HostCxx => $"{NativeAbi.Compiler}",
        _ => v.ToString(),
    };

    // ---------------------------------------------------------------- finding executables

    internal sealed record Found(string Name, string? Path, string Version)
    {
        public override string ToString() => Path is null ? $"{Name}: not found" : $"{Name} at {Path} ({Version})";
    }

    private static readonly Dictionary<string, Found> Tools = new(StringComparer.Ordinal);

    internal static Found Tool(string name)
    {
        lock (Tools)
        {
            if (!Tools.TryGetValue(name, out var found)) Tools[name] = found = Locate(name);
            return found;
        }
    }

    private static Found Locate(string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (Environment.GetEnvironmentVariable("VULKAN_SDK") is { Length: > 0 } sdk)
        {
            directories.Add(System.IO.Path.Combine(sdk, "Bin"));
            directories.Add(System.IO.Path.Combine(sdk, "bin"));
        }

        var cargo = Environment.GetEnvironmentVariable("CARGO_HOME") is { Length: > 0 } home
            ? home
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo");
        directories.Add(System.IO.Path.Combine(cargo, "bin"));

        foreach (var directory in directories.Distinct(StringComparer.Ordinal))
        {
            var candidate = System.IO.Path.Combine(directory, file);
            if (!File.Exists(candidate)) continue;

            var probe = Run(candidate, ["--version"], workingDirectory: null);
            if (probe.ExitCode == 0)
                return new Found(name, candidate, probe.Output.Split('\n')[0].Trim());
        }

        return new Found(name, null, "");
    }

    private static readonly Lazy<string?> MetalCompiler = new(() =>
    {
        var found = Run("xcrun", ["-sdk", "macosx", "-f", "metal"], workingDirectory: null);
        return found.ExitCode == 0 ? found.Output.Trim() : null;
    });

    internal static (int ExitCode, string Output) Run(string file, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        try
        {
            var info = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
            foreach (var a in arguments) info.ArgumentList.Add(a);

            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                return (-1, $"{file} did not finish in 2 minutes");
            }

            return (process.ExitCode, (stdout.Result + stderr.Result).Trim());
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, e.Message);
        }
    }

    // ---------------------------------------------------------------- judging

    /// <summary>A file name for a case/entry key.</summary>
    private static string Safe(string name) => name.Replace('/', '.').Replace(':', '.');

    /// <summary>A scratch directory for one module's files.</summary>
    private static string Scratch(string name)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sharpslang-validators", name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>The Vulkan environment a SPIR-V version belongs to, as spirv-val and glslang spell it.</summary>
    /// <remarks>
    /// The newest Vulkan whose core accepts that version (1.0 for Vulkan 1.0, 1.3 for 1.1, 1.5 for
    /// 1.2, 1.6 for 1.3), with 1.4 as Vulkan 1.1 plus VK_KHR_spirv_1_4, which is how spirv-val names
    /// it. The module's own header decides, so a shader built for 1.6 is held to Vulkan 1.3 rules.
    /// </remarks>
    public static string VulkanEnvironment(SpirvVersion version) => version switch
    {
        SpirvVersion.V1_0 => "vulkan1.0",
        SpirvVersion.V1_1 or SpirvVersion.V1_2 or SpirvVersion.V1_3 => "vulkan1.1",
        SpirvVersion.V1_4 => "vulkan1.1spv1.4",
        SpirvVersion.V1_5 => "vulkan1.2",
        SpirvVersion.V1_6 => "vulkan1.3",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "no Vulkan environment for this SPIR-V version"),
    };

    public static Verdict SpirvVal(string name, byte[] module, IReadOnlyList<string> flags)
    {
        var directory = Scratch("spirv");
        var path = System.IO.Path.Combine(directory, Safe(name) + ".spv");
        File.WriteAllBytes(path, module);

        var environment = VulkanEnvironment(SpirvModule.Read(module).Version);
        var (exit, text) = Run(Tool("spirv-val").Path!, ["--target-env", environment, .. flags, path], directory);
        return new Verdict(exit == 0, $"[{environment}{string.Concat(flags.Select(f => " " + f))}] {text}".Trim());
    }

    /// <summary>glslang's stage name for a Slang stage.</summary>
    private static string GlslangStage(SlangStage stage) => stage switch
    {
        SlangStage.SLANG_STAGE_VERTEX => "vert",
        SlangStage.SLANG_STAGE_HULL => "tesc",
        SlangStage.SLANG_STAGE_DOMAIN => "tese",
        SlangStage.SLANG_STAGE_GEOMETRY => "geom",
        SlangStage.SLANG_STAGE_FRAGMENT => "frag",
        SlangStage.SLANG_STAGE_COMPUTE => "comp",
        SlangStage.SLANG_STAGE_MESH => "mesh",
        SlangStage.SLANG_STAGE_AMPLIFICATION => "task",
        SlangStage.SLANG_STAGE_RAY_GENERATION => "rgen",
        SlangStage.SLANG_STAGE_INTERSECTION => "rint",
        SlangStage.SLANG_STAGE_ANY_HIT => "rahit",
        SlangStage.SLANG_STAGE_CLOSEST_HIT => "rchit",
        SlangStage.SLANG_STAGE_MISS => "rmiss",
        SlangStage.SLANG_STAGE_CALLABLE => "rcall",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>
    /// glslangValidator compiling Slang's GLSL for Vulkan 1.2, the environment of Slang's default
    /// SPIR-V version: the GLSL target is Vulkan GLSL (it declares sets and bindings), which an
    /// OpenGL compile would refuse for reasons that are not defects.
    /// </summary>
    public static Verdict Glslang(string name, SlangStage stage, byte[] source)
    {
        var directory = Scratch("glsl");
        var path = System.IO.Path.Combine(directory, Safe(name) + ".glsl");
        File.WriteAllBytes(path, source);

        var (exit, text) = Run(Tool("glslangValidator").Path!,
            ["-V", "--target-env", "vulkan1.2", "-S", GlslangStage(stage), "-o", System.IO.Path.Combine(directory, Safe(name) + ".spv"), path],
            directory);
        return new Verdict(exit == 0, StripEcho(text, path));
    }

    /// <summary>glslang echoes the file name as its first line; the rest is the verdict.</summary>
    private static string StripEcho(string text, string path) =>
        string.Join('\n', text.Split('\n').Where(l => l.Trim() != path && l.Trim() != System.IO.Path.GetFileName(path))).Trim();

    public static Verdict Naga(string name, byte[] source)
    {
        var directory = Scratch("wgsl");
        var path = System.IO.Path.Combine(directory, Safe(name) + ".wgsl");
        File.WriteAllBytes(path, source);

        var (exit, text) = Run(Tool("naga").Path!, [path], directory);
        return new Verdict(exit == 0, text);
    }

    public static Verdict Metal(string name, byte[] source)
    {
        var directory = Scratch("metal");
        var path = System.IO.Path.Combine(directory, Safe(name) + ".metal");
        File.WriteAllBytes(path, source);

        var (exit, text) = Run("xcrun", ["-sdk", "macosx", "metal", "-c", path, "-o", System.IO.Path.Combine(directory, Safe(name) + ".air")], directory);
        return new Verdict(exit == 0, text);
    }

    /// <summary>The host C++ compiler, syntax and semantics only, against Slang's own C++ prelude.</summary>
    public static Verdict Cxx(string name, byte[] source, string preludeDirectory)
    {
        var directory = Scratch("cpp");
        var path = System.IO.Path.Combine(directory, Safe(name) + ".cpp");
        File.WriteAllBytes(path, source);

        var (exit, text) = Run(NativeAbi.Compiler!, ["-std=c++17", "-fsyntax-only", "-I", preludeDirectory, path], directory);
        return new Verdict(exit == 0, text);
    }

    // ---------------------------------------------------------------- dxc, in process

    /// <summary>dxcompiler.dll and dxil.dll from the test's output, driven through their COM interfaces.</summary>
    internal static unsafe class Dxc
    {
        private static readonly Guid ClsidValidator = new("8CA3E215-F728-4CF3-8CDD-88AF917587A1");
        private static readonly Guid ClsidUtils = new("6245D6AF-66E0-48FD-80B4-4D271796748C");
        private static readonly Guid ClsidCompiler = new("73E22D93-E6CE-47F3-B5BF-F0664F39C1B0");
        private static readonly Guid IidValidator = new("A6E82BD2-1FD7-4826-9811-2857E797F49A");
        private static readonly Guid IidUtils = new("4605C4CB-2019-492A-ADA4-65F20BB7D67F");
        private static readonly Guid IidCompiler3 = new("228B4687-5A6A-4730-900C-9702B2203F54");
        private static readonly Guid IidOperationResult = new("CEDB484A-D4E9-445A-B991-CA21CA157DC2");
        private static readonly Guid IidVersionInfo = new("B04F5B50-2059-4F12-A8FF-A1E0CDE1CC7E");

        private sealed record Library(nint Handle, string Path)
        {
            public override string ToString() => Path;
        }

        /// <summary>dxil.dll, or null.</summary>
        public static readonly Lazy<string?> Validator = new(() => Load("dxil")?.ToString());

        /// <summary>dxcompiler.dll, or null.</summary>
        public static readonly Lazy<string?> Compiler = new(() => Load("dxcompiler")?.ToString());

        private static readonly Dictionary<string, Library?> Loaded = new(StringComparer.Ordinal);

        private static Library? Load(string name)
        {
            lock (Loaded)
            {
                if (Loaded.TryGetValue(name, out var known)) return known;

                Library? found = null;
                var file = OperatingSystem.IsWindows() ? name + ".dll" : $"lib{name}.so";
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, file);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)
                    && NativeLibrary.TryGetExport(handle, "DxcCreateInstance", out _))
                {
                    found = new Library(handle, $"{path} {VersionOf(handle)}");
                }

                Loaded[name] = found;
                return found;
            }
        }

        private static nint Create(Library library, Guid clsid, Guid iid)
        {
            var create = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)NativeLibrary.GetExport(library.Handle, "DxcCreateInstance");
            nint instance = 0;
            var hr = create(&clsid, &iid, &instance);
            if (hr < 0 || instance == 0) throw new InvalidOperationException($"DxcCreateInstance({clsid}) failed: 0x{hr:X8}");
            return instance;
        }

        private static string VersionOf(nint handle)
        {
            try
            {
                var library = new Library(handle, "");
                var info = Create(library, ClsidValidator, IidVersionInfo);
                uint major, minor;
                _ = ((delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)Slot(info, 3))(info, &major, &minor);
                Release(info);
                return $"(IDxcVersionInfo {major}.{minor})";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }

        private static nint Slot(nint self, int index) => (*(nint**)self)[index];

        private static void Release(nint self)
        {
            if (self != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(self, 2))(self);
        }

        /// <summary>A blob over pinned memory, from IDxcUtils::CreateBlobFromPinned (slot 4).</summary>
        private static nint PinnedBlob(nint utils, byte* data, int size)
        {
            nint blob = 0;
            var hr = ((delegate* unmanaged[Stdcall]<nint, void*, uint, uint, nint*, int>)Slot(utils, 4))(utils, data, (uint)size, 0, &blob);
            if (hr < 0) throw new InvalidOperationException($"IDxcUtils::CreateBlobFromPinned failed: 0x{hr:X8}");
            return blob;
        }

        /// <summary>GetStatus (slot 3) and GetErrorBuffer (slot 5) of an IDxcOperationResult.</summary>
        private static Verdict Read(nint result)
        {
            int status;
            _ = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(result, 3))(result, &status);

            nint errors = 0;
            _ = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(result, 5))(result, &errors);
            var text = "";
            if (errors != 0)
            {
                var pointer = ((delegate* unmanaged[Stdcall]<nint, void*>)Slot(errors, 3))(errors);
                var size = ((delegate* unmanaged[Stdcall]<nint, nuint>)Slot(errors, 4))(errors);
                text = pointer == null || size == 0 ? "" : Encoding.UTF8.GetString((byte*)pointer, (int)size).TrimEnd('\0').Trim();
                Release(errors);
            }

            return new Verdict(status >= 0, status >= 0 ? text : $"0x{status:X8}: {text}".Trim());
        }

        /// <summary>IDxcValidator::Validate (slot 3), default flags: validate, change nothing.</summary>
        public static Verdict Validate(byte[] container)
        {
            var validatorLibrary = Load("dxil")!;
            var compilerLibrary = Load("dxcompiler")
                ?? throw new InvalidOperationException("dxcompiler is needed for IDxcUtils, to make the blob dxil.dll validates");

            var validator = Create(validatorLibrary, ClsidValidator, IidValidator);
            var utils = Create(compilerLibrary, ClsidUtils, IidUtils);
            nint blob = 0, result = 0;
            try
            {
                fixed (byte* data = container)
                {
                    blob = PinnedBlob(utils, data, container.Length);
                    var hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint*, int>)Slot(validator, 3))(validator, blob, 0, &result);
                    if (hr < 0) return new Verdict(false, $"IDxcValidator::Validate returned 0x{hr:X8}");
                    return Read(result);
                }
            }
            finally
            {
                Release(result);
                Release(blob);
                Release(utils);
                Release(validator);
            }
        }

        /// <summary>IDxcCompiler3::Compile (slot 3) with these arguments; dxc validates what it compiles.</summary>
        public static Verdict Compile(byte[] source, IReadOnlyList<string> arguments)
        {
            var compiler = Create(Load("dxcompiler")!, ClsidCompiler, IidCompiler3);
            var pins = arguments.Select(a => GCHandle.Alloc((a + "\0").ToCharArray(), GCHandleType.Pinned)).ToList();
            nint result = 0;
            try
            {
                var argv = pins.Select(p => p.AddrOfPinnedObject()).ToArray();
                fixed (byte* data = source)
                fixed (nint* args = argv)
                {
                    var buffer = new DxcBuffer { Ptr = data, Size = (nuint)source.Length, Encoding = 65001 };
                    var iid = IidOperationResult;
                    var hr = ((delegate* unmanaged[Stdcall]<nint, DxcBuffer*, nint*, uint, nint, Guid*, nint*, int>)Slot(compiler, 3))(
                        compiler, &buffer, args, (uint)argv.Length, 0, &iid, &result);
                    if (hr < 0) return new Verdict(false, $"IDxcCompiler3::Compile returned 0x{hr:X8}");
                    return Read(result);
                }
            }
            finally
            {
                Release(result);
                foreach (var p in pins) p.Free();
                Release(compiler);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DxcBuffer
        {
            public void* Ptr;
            public nuint Size;
            public uint Encoding;
        }
    }

    // ---------------------------------------------------------------- NVRTC, in process

    /// <summary>NVRTC from an installed CUDA toolkit, through its C API.</summary>
    internal static unsafe class Nvrtc
    {
        private sealed record Found(nint Handle, string Path, string Include)
        {
            public override string ToString() => Path;
        }

        private static readonly Lazy<Found?> LazyLibrary = new(Locate);

        /// <summary>The library's path, or null.</summary>
        public static readonly Lazy<string?> Library = new(() => LazyLibrary.Value?.ToString());

        private static IEnumerable<string> Roots()
        {
            foreach (var variable in Environment.GetEnvironmentVariables().Keys.Cast<string>()
                         .Where(k => k.StartsWith("CUDA_PATH", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(k => k.Length))
            {
                if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root) yield return root;
            }

            if (!OperatingSystem.IsWindows())
            {
                yield return "/usr/local/cuda";
                foreach (var d in Directory.Exists("/usr/local") ? Directory.GetDirectories("/usr/local", "cuda-*") : []) yield return d;
            }
        }

        private static Found? Locate()
        {
            var candidates = new List<(string Library, string Include)>();
            foreach (var root in Roots().Distinct(StringComparer.Ordinal))
            {
                var bin = System.IO.Path.Combine(root, OperatingSystem.IsWindows() ? "bin" : "lib64");
                if (!Directory.Exists(bin)) continue;
                var pattern = OperatingSystem.IsWindows() ? "nvrtc64_*.dll" : "libnvrtc.so*";
                foreach (var file in Directory.GetFiles(bin, pattern).Where(f => !f.Contains(".alt.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    candidates.Add((file, System.IO.Path.Combine(root, "include")));
            }

            foreach (var (file, include) in candidates)
            {
                if (NativeLibrary.TryLoad(file, out var handle) && NativeLibrary.TryGetExport(handle, "nvrtcCreateProgram", out _))
                {
                    int major = 0, minor = 0;
                    var version = (delegate* unmanaged[Cdecl]<int*, int*, int>)NativeLibrary.GetExport(handle, "nvrtcVersion");
                    _ = version(&major, &minor);
                    return new Found(handle, $"{file} ({major}.{minor})", include);
                }
            }

            return null;
        }

        /// <summary>Compiles CUDA source to PTX; the program log is the message.</summary>
        public static Verdict Compile(string name, byte[] source, string preludeDirectory)
        {
            var found = LazyLibrary.Value!;
            nint F(string export) => NativeLibrary.GetExport(found.Handle, export);
            var create = (delegate* unmanaged[Cdecl]<nint*, byte*, byte*, int, byte**, byte**, int>)F("nvrtcCreateProgram");
            var compile = (delegate* unmanaged[Cdecl]<nint, int, byte**, int>)F("nvrtcCompileProgram");
            var logSize = (delegate* unmanaged[Cdecl]<nint, nuint*, int>)F("nvrtcGetProgramLogSize");
            var getLog = (delegate* unmanaged[Cdecl]<nint, byte*, int>)F("nvrtcGetProgramLog");
            var destroy = (delegate* unmanaged[Cdecl]<nint*, int>)F("nvrtcDestroyProgram");

            // SLANG_CUDA_ENABLE_HALF: the prelude's own instruction for half support ("Define
            // SLANG_CUDA_ENABLE_HALF to use the cuda_fp16 include", slang-cuda-prelude.h:21), which a
            // consumer of Slang's CUDA source has to follow; without it every half type is undefined.
            // compute_75: the architecture Slang's own NVRTC path picks for NVRTC 12.8 and later
            // (source/compiler-core/slang-nvrtc-compiler.cpp at v2026.21); NVRTC's default is older
            // than cuda_fp16's half intrinsics (::hsin and the rest are undefined below sm_53).
            string[] options = ["--std=c++17", "--gpu-architecture=compute_75", "-DSLANG_CUDA_ENABLE_HALF=1", $"-I{preludeDirectory}", $"-I{found.Include}"];
            var text = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source).TrimEnd('\0') + "\0");
            var fileName = Encoding.UTF8.GetBytes(name + ".cu\0");
            var encoded = options.Select(o => Encoding.UTF8.GetBytes(o + "\0")).ToArray();
            var pins = encoded.Select(o => GCHandle.Alloc(o, GCHandleType.Pinned)).ToList();

            nint program = 0;
            try
            {
                var argv = new byte*[pins.Count];
                for (var i = 0; i < argv.Length; i++) argv[i] = (byte*)pins[i].AddrOfPinnedObject();
                fixed (byte* src = text)
                fixed (byte* n = fileName)
                fixed (byte** args = argv)
                {
                    var created = create(&program, src, n, 0, null, null);
                    if (created != 0) return new Verdict(false, $"nvrtcCreateProgram returned {created}");

                    var result = compile(program, argv.Length, args);
                    nuint size;
                    _ = logSize(program, &size);
                    var log = new byte[(int)size];
                    fixed (byte* l = log) _ = getLog(program, l);
                    var message = Encoding.UTF8.GetString(log).TrimEnd('\0').Trim();
                    return new Verdict(result == 0, result == 0 ? message : $"nvrtcCompileProgram returned {result}: {message}");
                }
            }
            finally
            {
                if (program != 0) _ = destroy(&program);
                foreach (var p in pins) p.Free();
            }
        }
    }
}

/// <summary>A theory needing this validator; skipped naming the tool and how to get it.</summary>
public sealed class ValidatorTheoryAttribute : TheoryAttribute
{
    public ValidatorTheoryAttribute(
        Validator validator,
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if ((SlangNative.Available ? TargetValidators.Unavailable(validator) : SlangNative.SkipReason) is { } reason) Skip = reason;
    }
}

/// <summary>The fact counterpart.</summary>
public sealed class ValidatorFactAttribute : FactAttribute
{
    public ValidatorFactAttribute(
        Validator validator,
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if ((SlangNative.Available ? TargetValidators.Unavailable(validator) : SlangNative.SkipReason) is { } reason) Skip = reason;
    }
}
