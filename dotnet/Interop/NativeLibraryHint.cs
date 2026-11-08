// L1 — a missing slang-compiler, explained.
//
// SlangApi imports library "slang-compiler". When the runtime finds none, the bare
// DllNotFoundException names the file and nothing about why or what to do. So the calls an
// application makes into Slang first (SlangGlobalSession.Create, BuildTag, RequirePinnedVersion,
// HashString) catch it and throw one with the file, the RID, the directories the runtime looked in
// and where the library comes from, the runtime's own inside.
//
// The ResolvingUnmanagedDll handler is the runtime's last word before it throws: it runs only after
// every probe and every DllImport resolver missed. It records that for the explanation and returns
// zero. It never answers and never throws: the event stops at the first handler that returns a
// handle or throws, so a handler of ours that did either for "slang-compiler" would keep every
// handler an application registers after SharpSlang loaded from running (SharpNRI's handler threw
// until 191.0.0-beta.4 and did exactly that; AGENTS.md#pin-and-payload). Registering no
// DllImport resolver is the same rule: there is one per assembly, and it is the application's.
//
// CompanionLibraries.Preload (Windows) resolves slang-compiler the way the DllImports do and, when
// that fails, preloads nothing and leaves the failure to the first DllImport, which is the one
// explained here.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using SharpSlang.Native;

namespace SharpSlang.Interop;

internal static class NativeLibraryHint
{
    internal const string Library = "slang-compiler";

    private static int _registered;
    private static volatile bool _missed;

    /// <summary>Whether the runtime has asked the handler for <c>slang-compiler</c> in this process, after every probe missed.</summary>
    internal static bool Missed => _missed;

    /// <summary>Once per process: the handler on the load context SharpSlang was loaded into.</summary>
    internal static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        var context = AssemblyLoadContext.GetLoadContext(typeof(SlangApi).Assembly) ?? AssemblyLoadContext.Default;
        context.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
    }

    /// <summary>The handler. Records a miss of SharpSlang's own import; answers nothing, throws nothing.</summary>
    internal static nint OnResolvingUnmanagedDll(Assembly assembly, string name)
    {
        if (assembly == typeof(SlangApi).Assembly && name == Library) _missed = true;
        return 0;
    }

    /// <summary>Runs a call into Slang, replacing the runtime's bare exception for a missing library with the explanation.</summary>
    internal static T Explained<T>(Func<T> call)
    {
        Register();
        try
        {
            return call();
        }
        catch (DllNotFoundException e)
        {
            throw Explain(e);
        }
    }

    /// <summary>The runtime's exception for slang-compiler, explained; the original inside.</summary>
    internal static DllNotFoundException Explain(DllNotFoundException e) =>
        new($"{Describe(RuntimeInformation.RuntimeIdentifier, SearchedDirectories(), Missed)} ({e.Message})", e);

    /// <summary>
    /// Where the runtime looks for a <c>DllImport</c> of an assembly with no resolver: the native
    /// search directories the host passes (deps.json's native assets, the application directory),
    /// then the assembly's own directory. Read, not guessed: <c>NATIVE_DLL_SEARCH_DIRECTORIES</c> is
    /// the host's list as the runtime received it.
    /// </summary>
    internal static IReadOnlyList<string> SearchedDirectories()
    {
        var directories = new List<string>();
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string host)
        {
            foreach (var d in host.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                directories.Add(d);
        }

        var own = Path.GetDirectoryName(typeof(SlangApi).Assembly.Location);
        if (!string.IsNullOrEmpty(own)) directories.Add(own);
        if (directories.Count == 0) directories.Add(AppContext.BaseDirectory);

        return directories
            .Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
    }

    internal static string Describe(string rid, IReadOnlyList<string> searched, bool handlerAsked)
    {
        var file = rid.StartsWith("win", StringComparison.Ordinal) ? "slang-compiler.dll"
                 : rid.StartsWith("osx", StringComparison.Ordinal) ? "libslang-compiler.dylib"
                 : "libslang-compiler.so";
        var source = rid switch
        {
            "win-x64" or "linux-x64" or "linux-arm64" or "osx-arm64" =>
                $"the SharpSlang package carries it under runtimes/{rid}/native/, and a build copies it beside the application "
                + "(a RID-specific build flat, a portable one through deps.json): check that SharpSlang is referenced by this "
                + "application and that its native assets are not excluded (ExcludeAssets, PrivateAssets)",
            _ => $"SharpSlang packages no library for {rid}; build one from the SharpSlang repository",
        };
        var resolvers = handlerAsked
            ? " No DllImport resolver or ResolvingUnmanagedDll handler supplied one."
            : "";
        return $"SharpSlang could not load the Slang compiler library ({file}) for {rid}. Looked in: "
             + string.Join(", ", searched) + "." + resolvers
             + $" Where it comes from: {source}. A SharpSlang checkout stages it with "
             + $"pwsh tools/fetch-native.ps1 -Rid {rid}, or builds it from source with pwsh tools/build-native.ps1 -Rid {rid}.";
    }
}
