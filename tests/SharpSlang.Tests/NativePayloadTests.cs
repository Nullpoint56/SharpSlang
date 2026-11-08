// The staged native payload (runtimes/<rid>/) is part of the package contract: every file below
// ships, and nothing else does, and each library loads nothing but the system with it.
//
// Staged by tools/build-native.ps1 from the slang/ submodule, or by tools/fetch-native.ps1 from
// Slang's official release while CI still uses that. The two produce the same contract, measured at
// v2026.21: the same file names on win-x64, linux-x64, linux-arm64 and osx-arm64, and the same
// import lists. The lists mirror what build-native.ps1 stages and checks; change both together.
// The measurements behind them are in AGENTS.md#native-packages and AGENTS.md#pin-and-payload.
//
//   runtimes/<rid>/native/   slang-compiler, slang-glslang, slang-standard-module-<version>/
//   runtimes/<rid>/          LICENSE.txt, LICENSES/, third-party-notices/ (outside native/, which
//                            NuGet copies flat into the application's output)
//
// The standard modules are build-native.ps1 -StandardModules All, the default and what the official
// release carries; a payload staged with WithoutNeuralNumerics fails here, naming the missing files.

using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpSlang.Tests;

public sealed class NativePayloadTests
{
    private static string Rid => (OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsLinux() ? "linux-" : OperatingSystem.IsMacOS() ? "osx-" : "unsupported-")
        + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    private static readonly string[] Rids = ["win-x64", "linux-x64", "linux-arm64", "osx-arm64"];

    /// <summary>2026.21 for the pin v2026.21: the version Slang puts in its file and directory names.</summary>
    private static string Version => typeof(NativePayloadTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "SlangVersion").Value!.TrimStart('v');

    private static string StageRoot => Path.Combine(SlangHeader.RepoRoot(), "runtimes", Rid);

    private static string NativeDir => Path.Combine(StageRoot, "native");

    private static string Compiler => OperatingSystem.IsWindows() ? "slang-compiler.dll"
        : OperatingSystem.IsMacOS() ? "libslang-compiler.dylib" : "libslang-compiler.so";

    private static string Glslang => OperatingSystem.IsWindows() ? "slang-glslang.dll"
        : OperatingSystem.IsMacOS() ? $"libslang-glslang-{Version}.dylib" : $"libslang-glslang-{Version}.so";

    // The same names on every RID, measured on the official v2026.21 archives and on what
    // build-native.ps1 builds. Most of the size is the slang.numerics modules (27 MB) and slang.neural
    // (5.5 MB), which arrived after v2026.14.1.
    private static readonly string[] StandardModuleFiles =
    [
        "experimental/workgraph.slang",
        "experimental/workgraph.slang-module",
        "slang/WaveMatrix.slang",
        "slang/accelerate-vector-coopmat.slang",
        "slang/activations.slang",
        "slang/bindless-storage.slang",
        "slang/functional.slang-module",
        "slang/hash-function.slang",
        "slang/iactivation.slang",
        "slang/iencoder.slang",
        "slang/ilayer.slang",
        "slang/inline-vector.slang",
        "slang/istorages.slang",
        "slang/ivector.slang",
        "slang/layers.slang",
        "slang/mma-linear-layout-help.slang",
        "slang/mma-tiled-cuda.slang",
        "slang/mma-tiled-layout-helper.slang",
        "slang/mma-tiled-metal.slang",
        "slang/mma-tiled-vulkan.slang",
        "slang/network-parameter-layout-converter.slang",
        "slang/neural.slang",
        "slang/neural.slang-module",
        "slang/numerics.slang-module",
        "slang/numerics/__builtin.slang-module",
        "slang/numerics/__builtin_differentiable.slang-module",
        "slang/numerics/differentiable.slang-module",
        "slang/permuto-encoder.slang",
        "slang/shared-memory-pool.slang",
        "slang/vectorized-reader.slang",
    ];

    // Slang's own licence and the SPDX texts it declares, plus the notices of what it links
    // statically: the set Slang's install step ships (slang/CMakeLists.txt, "third-party-notices"),
    // mimalloc only where Slang builds with it, which is Windows. Given a .txt extension because NuGet
    // packs an extensionless file as a directory.
    private static string[] Licences =>
    [
        "LICENSE.txt",
        "LICENSES/Apache-2.0.txt",
        "LICENSES/BSL-1.0.txt",
        "LICENSES/CC-BY-4.0.txt",
        "LICENSES/LLVM-exception.txt",
        "LICENSES/LicenseRef-UOI-NCSA.txt",
        "LICENSES/MIT.txt",
        "LICENSES/Unlicense.txt",
        "third-party-notices/cmark-COPYING.txt",
        "third-party-notices/glslang-LICENSE.txt",
        "third-party-notices/lz4-LICENSE.txt",
        "third-party-notices/miniz-LICENSE.txt",
        "third-party-notices/spirv-tools-LICENSE.txt",
        "third-party-notices/unordered_dense-LICENSE.txt",
        .. OperatingSystem.IsWindows() ? ["third-party-notices/mimalloc-LICENSE.txt"] : Array.Empty<string>(),
    ];

    private static string[] Contract =>
    [
        $"native/{Compiler}",
        $"native/{Glslang}",
        .. StandardModuleFiles.Select(f => $"native/slang-standard-module-{Version}/{f}"),
        .. Licences,
    ];

    private static string? SkipReason(string? os) =>
        !Rids.Contains(Rid) ? $"No payload contract for {Rid}."
        : os is not null && !OperatingSystem.IsOSPlatform(os) ? $"a {os} binary format question"
        : !Directory.Exists(NativeDir) ? $"{NativeDir} is absent. Run: pwsh tools/build-native.ps1 -Rid {Rid}"
        : null;

    /// <summary>A fact that needs the staged payload, and optionally a host OS to read its binaries on.</summary>
    private sealed class PayloadFactAttribute : FactAttribute
    {
        public PayloadFactAttribute(string? os = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber) => Skip = SkipReason(os);
    }

    [PayloadFact]
    public void The_staged_payload_is_exactly_the_contract()
    {
        var expected = Contract;
        var missing = expected
            .Where(f => !File.Exists(Path.Combine(StageRoot, f)) || new FileInfo(Path.Combine(StageRoot, f)).Length == 0)
            .ToList();
        var actual = Directory.EnumerateFiles(StageRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(StageRoot, f).Replace('\\', '/'))
            .ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{StageRoot}: missing or empty {string.Join(", ", missing)}; staged but not in the contract {string.Join(", ", extra)}");
    }

    [PayloadFact]
    public void No_licence_text_is_inside_native()
    {
        // NuGet copies runtimes/<rid>/native/ flat into the application's output directory.
        var licences = Directory.EnumerateFiles(NativeDir, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("LICENSE", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(f).Contains("COPYING", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(licences);
    }

    // ------------------------------------------------------------------------------------ Windows
    // With the C runtime linked statically (build-native.ps1, CMAKE_MSVC_RUNTIME_LIBRARY; the official
    // release is built the same way) neither the VC++ redistributable nor the UCRT sets are imported.
    // Slang loads its companions (slang-glslang, dxcompiler, slang-llvm) itself by name at run time,
    // so none is an import, and nothing is delay-loaded.
    private static readonly Dictionary<string, string[]> PeImports = new()
    {
        ["slang-compiler.dll"] = ["ADVAPI32.dll", "KERNEL32.dll", "ole32.dll", "SHELL32.dll"],
        ["slang-glslang.dll"] = ["ADVAPI32.dll", "KERNEL32.dll"],
    };

    [PayloadFact("windows")]
    public void Each_library_imports_exactly_the_system_dlls_of_the_contract()
    {
        foreach (var (library, expected) in PeImports)
        {
            var (imports, delayed) = ReadPeImports(Path.Combine(NativeDir, library));
            AssertSameNames(expected, imports, $"{library} load-time imports", StringComparer.OrdinalIgnoreCase);
            AssertSameNames([], delayed, $"{library} delay-loaded imports", StringComparer.OrdinalIgnoreCase);
        }
    }

    // Import directory: 20-byte descriptors, name RVA at +12. Delay-import directory: 32-byte
    // descriptors, name RVA at +4. Both end with an all-zero descriptor.
    private static (List<string> Imports, List<string> Delayed) ReadPeImports(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var headers = pe.PEHeaders.PEHeader!;
        return (PeNames(pe, headers.ImportTableDirectory, 20, 12), PeNames(pe, headers.DelayImportTableDirectory, 32, 4));
    }

    private static List<string> PeNames(PEReader pe, DirectoryEntry directory, int stride, int nameOffset)
    {
        var names = new List<string>();
        if (directory.RelativeVirtualAddress == 0) return names;
        var table = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader();
        for (var offset = 0; offset + stride <= table.Length; offset += stride)
        {
            table.Offset = offset + nameOffset;
            var nameRva = table.ReadInt32();
            if (nameRva == 0) break;
            var name = pe.GetSectionData(nameRva).GetReader();
            var bytes = new List<byte>();
            for (byte b; name.RemainingBytes > 0 && (b = name.ReadByte()) != 0;) bytes.Add(b);
            names.Add(Encoding.ASCII.GetString([.. bytes]));
        }
        return names;
    }

    // -------------------------------------------------------------------------------------- Linux
    // The C++ runtime is the system's libstdc++ (Slang has no static-libstdc++ option); the official
    // release and build-native.ps1 both link exactly these. DT_RUNPATH carries $ORIGIN, which is how
    // Slang's own dlopen of libslang-glslang-<version>.so finds it beside the compiler whatever the
    // working directory (dotnet/Interop/CompanionLibraries.cs).
    private static string[] ElfNeeded =>
    [
        "libstdc++.so.6",
        "libm.so.6",
        "libgcc_s.so.1",
        "libc.so.6",
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ld-linux-aarch64.so.1" : "ld-linux-x86-64.so.2",
    ];

    [PayloadFact("linux")]
    public void Each_library_needs_exactly_the_system_libraries_and_searches_its_own_directory()
    {
        foreach (var library in new[] { Compiler, Glslang })
        {
            var (needed, runpath) = ReadElfDynamic(Path.Combine(NativeDir, library));
            AssertSameNames(ElfNeeded, needed, $"{library} DT_NEEDED", StringComparer.Ordinal);
            // Exactly: an empty entry, which CMake's build-tree RPATH carries, makes glibc search the
            // working directory too (measured with LD_DEBUG=libs).
            Assert.Equal("$ORIGIN/../lib:$ORIGIN", runpath);
        }
    }

    // The oldest glibc and libstdc++ a library loads with are the newest GLIBC_ and GLIBCXX_ versions
    // its version-needs section names. Directory.Build.props pins Slang's release (2.34 and 3.4.29
    // on linux-x64 and linux-arm64 at v2026.21); a build on Ubuntu 24.04 needs 2.38 and 3.4.32 and
    // would not load on 22.04, so CI builds Linux on ubuntu-22.04. A local test build on a newer
    // host (build-native.ps1 -AllowNewerGlibc) is let through only with SHARPSLANG_ALLOW_NEWER_GLIBC=1.
    private sealed class GlibcFloorFactAttribute : FactAttribute
    {
        public GlibcFloorFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
        {
            Skip = SkipReason("linux");
            if (Skip is null && Environment.GetEnvironmentVariable("SHARPSLANG_ALLOW_NEWER_GLIBC") == "1")
                Skip = "SHARPSLANG_ALLOW_NEWER_GLIBC=1: a local test payload, its glibc floor not checked";
        }
    }

    [GlibcFloorFact]
    public void Each_library_loads_on_the_glibc_and_libstdcxx_the_pin_promises()
    {
        var props = File.ReadAllText(Path.Combine(SlangHeader.RepoRoot(), "Directory.Build.props"));
        System.Version Pin(string name) => System.Version.Parse(System.Text.RegularExpressions.Regex.Match(props, $"<{name}>([^<]+)</{name}>").Groups[1].Value.Trim());
        var floor = new Dictionary<string, System.Version> { ["GLIBC"] = Pin("LinuxGlibcFloor"), ["GLIBCXX"] = Pin("LinuxGlibcxxFloor") };

        var above = new List<string>();
        foreach (var library in new[] { Compiler, Glslang })
        {
            var needs = ReadElfVersionNeeds(Path.Combine(NativeDir, library));
            foreach (var (prefix, pinned) in floor)
            {
                var newest = needs
                    .Where(n => n.StartsWith(prefix + "_", StringComparison.Ordinal) && System.Version.TryParse(n[(prefix.Length + 1)..], out _))
                    .Select(n => System.Version.Parse(n[(prefix.Length + 1)..]))
                    .DefaultIfEmpty(new System.Version(0, 0))
                    .Max()!;
                if (newest > pinned) above.Add($"{library} needs {prefix}_{newest}, the pin is {prefix}_{pinned}");
            }
        }

        Assert.True(above.Count == 0,
            "the staged Linux payload needs a newer glibc or libstdc++ than Directory.Build.props pins "
            + $"(LinuxGlibcFloor, LinuxGlibcxxFloor), so it would not load where the pin promises: {string.Join("; ", above)}. "
            + "The build host sets it; build on Ubuntu 22.04 as CI does. A local test build on a newer host "
            + "passes with SHARPSLANG_ALLOW_NEWER_GLIBC=1.");
    }

    // SHT_GNU_verneed (0x6ffffffe): sh_info Elf64_Verneed records (vn_cnt at +2, vn_aux at +8,
    // vn_next at +12), each with vn_cnt Elf64_Vernaux records (vna_name at +8, vna_next at +12),
    // names in the string table sh_link names.
    private static List<string> ReadElfVersionNeeds(string path)
    {
        var elf = File.ReadAllBytes(path);
        var shoff = (long)BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x28));
        var shentsize = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x3A));
        var shnum = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x3C));
        Span<byte> Section(int i) => elf.AsSpan((int)(shoff + i * shentsize), shentsize);

        var names = new List<string>();
        for (var i = 0; i < shnum; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(Section(i)[4..]) != 0x6ffffffe) continue;
            var offset = (int)BinaryPrimitives.ReadUInt64LittleEndian(Section(i)[24..]);
            var link = (int)BinaryPrimitives.ReadUInt32LittleEndian(Section(i)[40..]);
            var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(Section(i)[44..]);
            var strtab = (int)BinaryPrimitives.ReadUInt64LittleEndian(Section(link)[24..]);
            string Str(int at) => Encoding.ASCII.GetString(elf, strtab + at, Array.IndexOf(elf, (byte)0, strtab + at) - (strtab + at));

            for (int n = 0, need = offset; n < count; n++)
            {
                var auxCount = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(need + 2));
                var aux = need + (int)BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(need + 8));
                for (var a = 0; a < auxCount; a++)
                {
                    names.Add(Str((int)BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(aux + 8))));
                    aux += (int)BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(aux + 12));
                }
                need += (int)BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(need + 12));
            }
            return names;
        }
        throw new InvalidDataException($"{path} has no SHT_GNU_verneed section");
    }

    // ELF64 little-endian: the section headers, the SHT_DYNAMIC section, and the string table its
    // sh_link names. DT_NEEDED = 1, DT_RUNPATH = 29; each entry is a 16-byte tag/value pair.
    private static (List<string> Needed, string? Runpath) ReadElfDynamic(string path)
    {
        var elf = File.ReadAllBytes(path);
        Assert.True(elf.AsSpan(0, 4).SequenceEqual("\u007fELF"u8) && elf[4] == 2 && elf[5] == 1, $"{path} is not a little-endian ELF64 file");
        var shoff = (long)BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan(0x28));
        var shentsize = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x3A));
        var shnum = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(0x3C));
        Span<byte> Section(int i) => elf.AsSpan((int)(shoff + i * shentsize), shentsize);

        for (var i = 0; i < shnum; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(Section(i)[4..]) != 6) continue;   // SHT_DYNAMIC
            var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(Section(i)[24..]);
            var size = (long)BinaryPrimitives.ReadUInt64LittleEndian(Section(i)[32..]);
            var link = (int)BinaryPrimitives.ReadUInt32LittleEndian(Section(i)[40..]);
            var strtab = (long)BinaryPrimitives.ReadUInt64LittleEndian(Section(link)[24..]);
            string Str(long at) => Encoding.ASCII.GetString(elf, (int)(strtab + at), Array.IndexOf(elf, (byte)0, (int)(strtab + at)) - (int)(strtab + at));

            var needed = new List<string>();
            string? runpath = null;
            for (var e = offset; e + 16 <= offset + size; e += 16)
            {
                var tag = BinaryPrimitives.ReadInt64LittleEndian(elf.AsSpan((int)e));
                var value = (long)BinaryPrimitives.ReadUInt64LittleEndian(elf.AsSpan((int)e + 8));
                if (tag == 0) break;
                if (tag == 1) needed.Add(Str(value));
                if (tag == 29) runpath = Str(value);
            }
            return (needed, runpath);
        }
        throw new InvalidDataException($"{path} has no SHT_DYNAMIC section");
    }

    // -------------------------------------------------------------------------------------- macOS
    // The system's libc++ and libSystem, nothing from Homebrew or the build machine. LC_RPATH
    // @loader_path is how Slang's dlopen of a leaf name finds slang-glslang beside the compiler
    // (dotnet/Interop/CompanionLibraries.cs). Measured on the official v2026.21 archive by reading
    // its load commands on Windows; build-native.ps1's macOS path, which checks the same with otool,
    // has not run on a Mac.
    private static readonly string[] MachODylibs = ["/usr/lib/libc++.1.dylib", "/usr/lib/libSystem.B.dylib"];

    private static readonly string[] MachORpaths = ["@loader_path/../lib", "@loader_path"];

    [PayloadFact("osx")]
    public void Each_library_links_exactly_the_system_and_searches_its_own_directory()
    {
        foreach (var library in new[] { Compiler, Glslang })
        {
            var (dylibs, rpaths) = ReadMachOLoadCommands(Path.Combine(NativeDir, library));
            AssertSameNames(MachODylibs, dylibs, $"{library} LC_LOAD_DYLIB", StringComparer.Ordinal);
            Assert.Equal(MachORpaths, rpaths);
        }
    }

    // 64-bit Mach-O: a 32-byte header, then ncmds load commands. LC_LOAD_DYLIB (0xc),
    // LC_LOAD_WEAK_DYLIB (0x80000018), LC_REEXPORT_DYLIB (0x8000001f), LC_LOAD_UPWARD_DYLIB
    // (0x80000023) and LC_RPATH (0x8000001c) hold a string at the offset in their third word.
    private static (List<string> Dylibs, List<string> Rpaths) ReadMachOLoadCommands(string path)
    {
        var macho = File.ReadAllBytes(path);
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(macho) == 0xfeedfacf, $"{path} is not a 64-bit Mach-O file");
        var ncmds = BinaryPrimitives.ReadUInt32LittleEndian(macho.AsSpan(16));
        var dylibs = new List<string>();
        var rpaths = new List<string>();
        var at = 32;
        for (var i = 0; i < ncmds; i++)
        {
            var cmd = BinaryPrimitives.ReadUInt32LittleEndian(macho.AsSpan(at));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(macho.AsSpan(at + 4));
            string Str() { var s = at + (int)BinaryPrimitives.ReadUInt32LittleEndian(macho.AsSpan(at + 8)); return Encoding.UTF8.GetString(macho, s, Array.IndexOf(macho, (byte)0, s) - s); }
            if (cmd is 0xc or 0x80000018 or 0x8000001f or 0x80000023) dylibs.Add(Str());
            if (cmd == 0x8000001c) rpaths.Add(Str());
            at += size;
        }
        return (dylibs, rpaths);
    }

    private static void AssertSameNames(string[] expected, List<string> actual, string what, StringComparer comparer)
    {
        var missing = expected.Except(actual, comparer).ToList();
        var extra = actual.Except(expected, comparer).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{what}: missing {string.Join(", ", missing)}; not in the contract {string.Join(", ", extra)} (all: {string.Join(", ", actual)})");
    }
}
