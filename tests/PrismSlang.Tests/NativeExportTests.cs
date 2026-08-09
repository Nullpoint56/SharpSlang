// Does every function we say exists actually exist?
//
// L0 declares 280 DllImports against slang-compiler. A DllImport is a promise checked at the moment
// of the first call and not before, so an export Slang renames or drops is not a build error, not a
// startup error, and not a test failure until something happens to call it — at which point it is
// an EntryPointNotFoundException from deep inside a cook, naming a function nobody recognises.
//
// Resolving all of them against the loaded library turns that into one failing test that lists
// exactly which names moved. Cheap, and it is the only check that covers the entry points nothing
// currently calls.

using System.Reflection;
using System.Runtime.InteropServices;
using PrismSlang.Native;

namespace PrismSlang.Tests;

public class NativeExportTests
{
    /// <summary>Every distinct entry point name L0 declares, with the field that declares it.</summary>
    private static List<(string EntryPoint, string Declaration)> Declared()
    {
        var result = new List<(string, string)>();

        foreach (var method in typeof(SlangApi).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var import = method.GetCustomAttribute<DllImportAttribute>();
            if (import is null) continue;

            // ExactSpelling is set on all of these, so the entry point is the method name unless
            // EntryPoint says otherwise.
            result.Add((import.EntryPoint ?? method.Name, $"SlangApi.{method.Name}"));
        }

        return result;
    }

    [NativeFact]
    public void The_binding_declares_the_number_of_entry_points_the_docs_claim()
    {
        // A tripwire on the generator: if a regeneration silently drops half of L0, every other
        // test here still passes because it only exercises what it calls.
        Assert.InRange(Declared().Count, 200, 400);
    }

    [NativeFact]
    public void Every_declared_entry_point_resolves_in_the_loaded_library()
    {
        var handle = LoadedLibrary();

        var missing = Declared()
            .Where(d => !NativeLibrary.TryGetExport(handle, d.EntryPoint, out _))
            .OrderBy(d => d.EntryPoint, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} of {Declared().Count} declared entry points are not exported by "
            + $"slang-compiler {SlangGlobalSession.BuildTag}. Each is an EntryPointNotFoundException "
            + $"waiting for whoever calls it first:\n"
            + string.Join("", missing.Select(d => $"    {d.EntryPoint}   ({d.Declaration})\n")));
    }

    [Fact]
    public void No_entry_point_is_bound_by_a_C_plus_plus_mangling()
    {
        // Found by running the resolution test above under WSL, on its first cross-platform run.
        // Two functions in slang.h are not extern "C", and ClangSharp bound them by their MSVC
        // mangling: ?spReflection_GetSession@@YAPEAUISession@slang@@PEAUSlangProgramLayout@@@Z.
        // That compiles everywhere and resolves only on Windows — on linux-x64 the symbol carries
        // an Itanium mangling and the call throws EntryPointNotFoundException. Both are excluded
        // from generation now; this is what notices if a regeneration brings them back.
        //
        // No native library needed: it is a property of the declarations, not of the binary.
        var mangled = Declared()
            .Where(d => d.EntryPoint.StartsWith('?') || d.EntryPoint.StartsWith("_Z", StringComparison.Ordinal))
            .OrderBy(d => d.Declaration, StringComparer.Ordinal)
            .ToList();

        Assert.True(mangled.Count == 0,
            $"{mangled.Count} entry point(s) are bound by a C++ mangling, which is platform "
            + $"specific and will not resolve off the platform that produced it:\n"
            + string.Join("", mangled.Select(d => $"    {d.Declaration} -> {d.EntryPoint}\n"))
            + "Add the function to the --exclude list in tools/generate-native.ps1.");
    }

    [NativeFact]
    public void No_entry_point_is_declared_twice_under_different_names()
    {
        // Two managed methods bound to one export is how a generator run goes wrong quietly: both
        // compile, both resolve, and one of them has the wrong signature.
        var duplicates = Declared()
            .GroupBy(d => d.EntryPoint, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "entry points declared more than once:\n" + string.Join("", duplicates.Select(
                g => $"    {g.Key} <- {string.Join(", ", g.Select(d => d.Declaration))}\n")));
    }

    /// <summary>
    /// The already-loaded <c>slang-compiler</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library is in the process by the time any of this runs — <see cref="NativeFactAttribute"/>
    /// gates on a call into it — so this is asking for the handle to something already resolved.
    /// The search path still matters: <c>SafeDirectories</c> looks beside the *runtime*, and the
    /// payload sits beside the assembly. That difference passed on Windows and failed under WSL
    /// with a <c>DllNotFoundException</c> listing four names under
    /// <c>Microsoft.NETCore.App</c>, which is the platform difference this project keeps finding.
    /// </para>
    /// <para>
    /// Both the explicit assembly directory and the runtime's default resolution are tried, because
    /// the second is what the <c>DllImport</c>s themselves used and is therefore the definition of
    /// "resolvable" this test is meant to check.
    /// </para>
    /// </remarks>
    private static nint LoadedLibrary()
    {
        const string Name = "slang-compiler";
        var assembly = typeof(SlangApi).Assembly;

        if (NativeLibrary.TryLoad(Name, assembly, DllImportSearchPath.AssemblyDirectory, out var handle))
            return handle;

        if (NativeLibrary.TryLoad(Name, assembly, searchPath: null, out handle))
            return handle;

        throw new InvalidOperationException(
            $"'{Name}' is loadable enough for the DllImports to work — NativeFact gated on it — but "
            + $"NativeLibrary could not resolve it beside {Path.GetDirectoryName(assembly.Location)}. "
            + $"The test's own resolution is wrong, not the payload.");
    }
}
