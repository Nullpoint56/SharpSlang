// L1 — the Slang version the three parts were built from must be one.
//
// Slang promises no ABI between releases (AGENTS.md#pin-and-payload). Each part records its
// version: SharpSlang and SharpSlang.Descs as AssemblyMetadata("SlangVersion") from
// Directory.Build.props, the native library as its build tag (spGetBuildTagString, a flat
// `const char*()` with no receiver, so the one call whose ABI cannot be wrong). Within one Slang
// version the SharpSlang.Descs package must still be the one SharpSlang depends on exactly
// (AssemblyMetadata "SharpSlangDescsVersion"): NuGet lets a direct reference to another version win
// the restore with only NU1608, and the package's SHARPSLANG005 is what fails that build first.
// The same check as SharpNRI's NriVersionCheck, at global-session creation, which is the first
// thing an application asks of Slang.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpSlang.Native;

namespace SharpSlang.Interop;

internal static class SlangVersionCheck
{
    /// <summary>The Slang release SharpSlang was built for (<c>2026.21</c>): its SlangVersion metadata.</summary>
    internal static readonly string Built = SlangVersion(typeof(SlangVersionCheck).Assembly);

    /// <summary>The Slang release the loaded SharpSlang.Descs was generated from.</summary>
    internal static readonly string Descs = SlangVersion(typeof(SlangStage).Assembly);

    /// <summary>The package version of the loaded SharpSlang.</summary>
    internal static readonly string Binding = PackageVersion(typeof(SlangVersionCheck).Assembly);

    /// <summary>The package version of the loaded SharpSlang.Descs.</summary>
    internal static readonly string DescsPackage = PackageVersion(typeof(SlangStage).Assembly);

    /// <summary>The SharpSlang.Descs package version SharpSlang was built against and depends on exactly.</summary>
    internal static readonly string BuiltDescsPackage = Metadata(typeof(SlangVersionCheck).Assembly, "SharpSlangDescsVersion");

    /// <summary>
    /// The exception for a loaded SharpSlang.Descs of another Slang release or another package
    /// version than the one SharpSlang depends on, or null. Arguments, not the loaded values, so
    /// the refusal is testable in a process that loaded the right one.
    /// </summary>
    internal static SlangVersionMismatchException? DescsMismatch(string descsSlang, string descsPackage)
    {
        var path = DescsPath();
        if (descsSlang != Built)
        {
            return new SlangVersionMismatchException(Built, descsSlang, $"""
                SharpSlang {Binding} is built for Slang {Built}, and the loaded SharpSlang.Descs
                ({descsPackage}, {path}) was generated from Slang {descsSlang}. Every enum and
                struct of one Slang release read as another's misreads renumbered values silently.

                Reference the SharpSlang.Descs version SharpSlang depends on ({BuiltDescsPackage}),
                or drop the direct reference.
                """) { LibraryPath = path };
        }

        if (descsPackage != BuiltDescsPackage)
        {
            return new SlangVersionMismatchException(BuiltDescsPackage, descsPackage, $"""
                SharpSlang {Binding} depends on SharpSlang.Descs {BuiltDescsPackage} exactly, and
                the loaded one ({path}) is {descsPackage}. NuGet lets a direct reference to another
                version win with only the warning NU1608.

                Drop the direct SharpSlang.Descs reference or make it {BuiltDescsPackage}.
                """) { LibraryPath = path };
        }

        return null;
    }

    /// <summary>The exception for a native library whose build tag is not the pinned release, or null.</summary>
    internal static SlangVersionMismatchException? NativeMismatch(string pinned, string loaded)
    {
        if (loaded == pinned) return null;

        var path = LoadedLibrary();
        return new SlangVersionMismatchException(pinned, loaded, $$"""
            SharpSlang was generated against Slang {{pinned}}, but the loaded
            slang-compiler ({{path ?? "path unknown"}}) reports {{loaded}}.

            Slang does not promise a stable ABI between releases. These bindings call COM vtables
            by slot ordinal, taken from {{pinned}}'s headers; against a different release those
            ordinals may name different functions, and the result is not a clean failure.

            Stage the pinned payload:

                pwsh tools/fetch-native.ps1 -Profile Spirv

            If runtimes/ holds a payload from an earlier pin, that is the usual cause — the
            directory is gitignored, so bumping the version alone does not replace it.

            If running {{loaded}} is deliberate, pass
            new GlobalSessionOptions { RequirePinnedSlangVersion = false } to Create.
            """) { LibraryPath = path };
    }

    private static string SlangVersion(Assembly assembly) => Metadata(assembly, "SlangVersion").TrimStart('v');

    private static string Metadata(Assembly assembly, string key)
    {
        foreach (var a in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (a.Key == key && a.Value is { } v) return v;

        // Not reachable from a package this repository built: the metadata comes from
        // Directory.Build.props, which every project imports. Reachable from a fork that dropped
        // it, and saying so beats degrading to a check that silently passes.
        throw new InvalidOperationException(
            $"{assembly.GetName().Name} carries no {key} assembly metadata, so the Slang version it "
            + "was built for is unknown and cannot be checked against what is loaded. Its project must "
            + $"emit <AssemblyMetadata Include=\"{key}\" ... /> from Directory.Build.props.");
    }

    // The NuGet version: the informational version without Source Link's "+<commit>"
    private static string PackageVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} carries no informational version");
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    // Empty in a single-file or Native AOT application, which the message then says
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Location is reported as loaded from memory")]
    private static string DescsPath()
    {
        var location = typeof(SlangStage).Assembly.Location;
        return string.IsNullOrEmpty(location) ? "loaded from memory" : location;
    }

    /// <summary>The file the loaded slang-compiler was mapped from, from the process module list; failure path only.</summary>
    internal static string? LoadedLibrary()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule m in process.Modules)
            {
                var name = Path.GetFileName(m.FileName);
                if (name.Equals("slang-compiler.dll", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("libslang-compiler.", StringComparison.Ordinal))
                {
                    return m.FileName;
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }

        return OperatingSystem.IsWindows() ? null : MappedFrom();
    }

    /// <summary>The file a Unix loader mapped slang-compiler from, asked of dladdr.</summary>
    /// <remarks>
    /// The process module list is not enough on macOS: on the GitHub-hosted macos-15 runner it did
    /// not list libslang-compiler.dylib although the library was loaded (run 42475073038,
    /// 2026-11-08), so the guard named no path there. dladdr on one of the library's own exports
    /// answers on Linux and macOS alike. TryLoad with the DllImports' own resolution returns the
    /// module they are bound to, as CompanionLibraries relies on for Windows.
    /// </remarks>
    private static unsafe string? MappedFrom()
    {
        if (!NativeLibrary.TryLoad("slang-compiler", typeof(SlangApi).Assembly, null, out var handle)
            || !NativeLibrary.TryGetExport(handle, "spGetBuildTagString", out var symbol))
        {
            return null;
        }

        DlInfo info;
        return dladdr(symbol, &info) != 0 && info.FileName != 0 ? Marshal.PtrToStringUTF8(info.FileName) : null;
    }

    /// <summary>Dl_info, the same four pointers on glibc and on macOS.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo
    {
        public nint FileName;
        public nint FileBase;
        public nint SymbolName;
        public nint SymbolAddress;
    }

    // Not in SlangApi, which is generated from slang.h; the one libc call the binding makes. "libc"
    // resolves to glibc on Linux and to libSystem on macOS; dladdr has been in glibc's libc proper
    // since 2.34, which is the payload's floor (AGENTS.md#pin-and-payload).
    [DllImport("libc", ExactSpelling = true)]
    private static extern unsafe int dladdr(nint address, DlInfo* info);
}
