// L1 — the libraries Slang loads by name, loaded first from where they actually are.
//
// PrismSlang's DllImport("slang-compiler") is resolved by .NET: deps.json's native assets, the
// NATIVE_DLL_SEARCH_DIRECTORIES the host passes, the application directory. Slang then loads its
// companions itself, by bare name, with LoadLibrary — slang-glslang, which carries spirv-opt;
// slang-glsl-module for GLSL input; dxcompiler for DXIL — and Windows looks for a bare name in the
// executable's directory, the system directories, the working directory and PATH. Not in the
// directory slang-compiler.dll was loaded from.
//
// Measured on Windows 11 with Slang 2026.14.1 and .NET 11, it broke two ways:
//
//   * A ProjectReference consumer, payload copied flat beside the app. `Engine.exe` worked from
//     any directory, and so did `dotnet Engine.dll` run from the output directory; `dotnet
//     <path>\Engine.dll` from anywhere else failed in SlangGlobalSession.Create with "SPIR-V
//     optimiser unavailable": the executable is dotnet.exe, and the working directory is not the
//     output.
//   * A PackageReference consumer's portable build, the default (no RuntimeIdentifier). The
//     payload stays under runtimes\win-x64\native\, which .NET reads through deps.json and Windows
//     never searches, so spirv-opt was unavailable however the app was started, from its own
//     directory included. dxc, which PrismSlang.Native.Dxc copies to the output root, was found by
//     the apphost and lost under `dotnet app.dll` from elsewhere. A `-r win-x64` build flattens the
//     payload and worked, which left the default build broken for every Windows consumer.
//
// So before the first global session, once per process, the loaded slang-compiler is asked where it
// is and the companions are loaded from there by full path. When Slang later asks for
// "slang-glslang.dll", the loader finds a module of that name already in the process and returns
// it without searching anything: documented LoadLibrary behaviour, and the whole mechanism.
//
// The handles are never freed, and that is load-bearing as well. Slang frees its own when a global
// session is released — measured, neither slang-glslang nor dxcompiler is left in the process after
// one — so without this reference the next global session would search again, and fail again.
//
// Windows only, and on evidence rather than by default. The Linux and macOS payloads already tell
// their loaders to look beside the compiler, read from the binaries' own dynamic-linking records,
// and re-read unchanged from the 2026.21 linux-x64 and osx-arm64 archives:
//
//   * linux-x64 and linux-arm64: libslang-compiler.so carries DT_RUNPATH "$ORIGIN/../lib:$ORIGIN"
//     and no DT_RPATH, and glibc's dlopen searches the calling object's DT_RUNPATH for a bare
//     name, so Slang's own dlopen of libslang-glslang-2026.21.so resolves in the compiler's
//     directory whatever the executable and the working directory are. Measured on linux-x64
//     under WSL: `dotnet PrismSlang.Prober.dll` from another directory, with no preload, loaded
//     libslang-glslang from beside the compiler and optimised 2,048 bytes of SPIR-V to 1,664.
//   * osx-arm64: libslang-compiler.dylib carries LC_RPATH "@loader_path/../lib" and
//     "@loader_path", its install name is @rpath/libslang-compiler.0.2026.21.dylib, and the only
//     name it holds for glslang is the leaf "slang-glslang-". dyld treats a leaf name passed to
//     dlopen as @rpath/<name>, searched through the LC_RPATHs of the image that called it, which
//     here is the compiler's own directory. That one is read from dyld's behaviour, not measured:
//     no Mac took part in this change.
//
// ProberTests launches `dotnet <dll>` from a foreign working directory, on every leg, so each of
// those readings is measured where it applies rather than only stated here.

using System.Runtime.InteropServices;
using PrismSlang.Native;

namespace PrismSlang.Interop;

/// <summary>
/// Loads Slang's companion libraries by full path, from where they are, before Slang looks for
/// them by name.
/// </summary>
internal static unsafe class CompanionLibraries
{
    private static readonly Lock Gate = new();
    private static bool _done;

    /// <summary>Once per process, and before the first <c>slang_createGlobalSession2</c>.</summary>
    /// <remarks>
    /// <para>
    /// Missing files are not an error, here or anywhere below. What is absent stays absent, and
    /// <see cref="SlangGlobalSession.RequireSpirvOptimizer"/> and
    /// <see cref="SlangGlobalSession.RequireDxc"/> refuse it with the fix in their message, exactly
    /// as they did before this existed; this only makes what is present findable.
    /// </para>
    /// <para>
    /// Not slang-llvm, though Slang loads it the same way: no fetch-native profile stages it, and
    /// mapping 82 MB on the chance of a CPU target a shader cooker never asks for is the wrong
    /// default.
    /// </para>
    /// </remarks>
    internal static void Preload()
    {
        if (!OperatingSystem.IsWindows()) return;

        lock (Gate)
        {
            if (_done) return;

            // The resolution the DllImports themselves use — the deps.json native assets, the
            // host's search directories, the assembly directory — so the handle is the module
            // they are bound to: loading a path that is already loaded returns the loaded module.
            // On failure nothing is preloaded and nothing is remembered. The DllImport that follows
            // throws DllNotFoundException naming every path it tried, which is a better message
            // than one composed here, and a later call is free to succeed.
            if (!NativeLibrary.TryLoad("slang-compiler", typeof(SlangApi).Assembly, null, out var compiler))
                return;

            var compilerDirectory = Path.GetDirectoryName(ModuleFileName(compiler));

            if (compilerDirectory is not null)
            {
                // Globbed rather than named: Windows names them without a version today, and the
                // Unix payloads show the form a versioned name takes (libslang-glslang-2026.21).
                // slang-glsl-module is only there with fetch-native's Glsl profile. Slang loads it
                // by name once GLSL input is enabled, measured, though a GLSL compute shader came
                // out as the same 720 bytes of SPIR-V with and without it.
                foreach (var pattern in (string[])["slang-glslang*.dll", "slang-glsl-module*.dll"])
                {
                    foreach (var file in Directory.GetFiles(compilerDirectory, pattern))
                        NativeLibrary.TryLoad(file, out _);
                }
            }

            PreloadDxc(compilerDirectory);
            _done = true;
        }
    }

    /// <summary>dxc, from the first of the compiler's directory and the application's that has it.</summary>
    /// <remarks>
    /// <para>
    /// Both directories, because dxcompiler.dll and dxil.dll are copied to the root of the output
    /// — by PrismSlang.Native.Dxc's targets, or by a cooker importing them itself — which is beside
    /// the compiler when the payload is flat (a ProjectReference, a RID-specific build) and one
    /// <c>runtimes\win-x64\native\</c> away from it in a portable package build. The first that has
    /// it, not both, so that two different dxcompiler.dll files never share one process under one
    /// module name.
    /// </para>
    /// <para>
    /// dxil.dll too, though the pinned dxc does not use it. dxc 1.9.2609.5 signs with the
    /// validator built into dxcompiler.dll and never loads dxil.dll, measured: its output was byte
    /// for byte the same with dxil.dll loaded first, loaded second, or deleted; with dxil.dll in
    /// the working directory and first on PATH, where a load by name would have found it, nothing
    /// loaded it; and its digest is the real one, because dxil.dll's own validator, handed
    /// compute-fixed's 2,908-byte container with the digest zeroed, wrote back the same 16 bytes.
    /// dxc 1.8.2502.7, from the package cache, likewise wrote a byte-identical container with
    /// dxil.dll present and deleted. The release notes in the package say why: 1.8.2502
    /// open-sourced the validator's hash, and from 1.8.2505 the compiler always uses its internal
    /// validator instead of searching for dxil.dll. Releases before 1.8.2502 signed only through
    /// dxil.dll and wanted it loaded before dxcompiler.dll — read from Microsoft's description of
    /// them and from the order Dawn loads the two in, not measured here. So dxil.dll is loaded
    /// first, for a dxc a consumer brings that still signs that way, and two megabytes of mapping
    /// is the whole cost.
    /// </para>
    /// </remarks>
    private static void PreloadDxc(string? compilerDirectory)
    {
        foreach (var directory in (string?[])[compilerDirectory, AppContext.BaseDirectory])
        {
            // Never empty in practice; an empty one would make the path below relative to the
            // working directory, which is the search this exists to avoid.
            if (string.IsNullOrEmpty(directory)) continue;

            var dxcompiler = Path.Combine(directory, "dxcompiler.dll");
            if (!File.Exists(dxcompiler)) continue;

            NativeLibrary.TryLoad(Path.Combine(directory, "dxil.dll"), out _);
            NativeLibrary.TryLoad(dxcompiler, out _);
            return;
        }
    }

    /// <summary>The file a loaded module was mapped from, as the loader recorded it.</summary>
    /// <remarks>
    /// Grown until it fits: MAX_PATH is not the limit once long paths are enabled, and
    /// GetModuleFileNameW truncates a longer path and returns the buffer's size.
    /// </remarks>
    private static string? ModuleFileName(nint module)
    {
        for (var capacity = 260; capacity <= 32_768; capacity *= 2)
        {
            var buffer = new char[capacity];
            uint length;
            fixed (char* p = buffer)
            {
                length = GetModuleFileNameW(module, p, (uint)capacity);
            }

            if (length == 0) return null;
            if (length < capacity) return new string(buffer, 0, (int)length);
        }

        return null;
    }

    // Not in SlangApi, which is generated from slang.h and counted against it; this is the one
    // Win32 call the binding makes, and its arguments are blittable.
    [DllImport("kernel32", ExactSpelling = true)]
    private static extern uint GetModuleFileNameW(nint module, char* filename, uint size);
}
