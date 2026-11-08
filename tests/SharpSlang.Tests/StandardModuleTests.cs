// Slang's standard modules (slang.neural, slang.numerics, slang.functional, experimental.workgraph):
// where Slang looks for them, and that the binding can import them.
//
// Slang resolves an import it did not find on the session's search paths beside the library it is
// running from: the directory of the module that exports slang_createGlobalSession (GetModuleFileNameW
// on Windows, dladdr elsewhere), then slang-standard-module-<version>/, then the import path with dots
// as folders and .slang-module appended (getStandardModuleDirPath and findStandardModulePath,
// source/slang/slang-session.cpp:27-55, the search at :2087). Every one of them is declared
// [ExperimentalModule], and a module so declared is refused with E00104 unless the session sets
// CompilerOptionName::ExperimentalFeature (slang-session.cpp:1747), which is a session option, not a
// target one. AGENTS.md#native-packages has the package layout that keeps the folder beside the
// compiler in every kind of output.

using SharpSlang.Interop;

namespace SharpSlang.Tests;

public class StandardModuleTests
{
    private static string Source(string import) => $$"""
        import {{import}};
        RWStructuredBuffer<float> output;
        [shader("compute")]
        [numthreads(1, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID) { output[id.x] = 1.0; }
        """;

    // The folder Slang reads them from: beside the compiler the process loaded
    private static string ModuleDirectory()
    {
        var compiler = SlangVersionCheck.LoadedLibrary();
        Assert.NotNull(compiler);
        return Path.Combine(Path.GetDirectoryName(compiler)!, $"slang-standard-module-{SlangHeaders.Version}");
    }

    // The importing module's dependency files name the imported module's .slang sources where the
    // payload ships them (neural and workgraph), resolved in the folder its .slang-module was read
    // from, and nothing for the two shipped as .slang-module alone (numerics, functional). Slang
    // never names the .slang-module file itself there (measured, v2026.21, win-x64).
    [NativeTheory]
    [InlineData("slang.neural", "slang/neural.slang", 22)]
    [InlineData("slang.numerics", null, 0)]
    [InlineData("slang.functional", null, 0)]
    [InlineData("experimental.workgraph", "experimental/workgraph.slang", 1)]
    public void With_experimental_features_a_standard_module_imports_from_beside_the_loaded_compiler(string import, string? source, int sources)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { ExperimentalFeatures = true });
        using var module = session.LoadModuleFromSource("usesStandardModule", "usesStandardModule.slang", Source(import));

        var folder = Path.GetFullPath(ModuleDirectory()) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var imported = module.DependencyFilePaths.Where(p => p != "usesStandardModule.slang").Select(Path.GetFullPath).ToArray();
        Assert.All(imported, p => Assert.StartsWith(folder, p, comparison));
        Assert.Equal(sources, imported.Length);
        if (source is not null) Assert.Contains(Path.GetFullPath(Path.Combine(folder, source)), imported, StringComparer.FromComparison(comparison));
    }

    [NativeFact]
    public void Without_experimental_features_Slang_finds_a_standard_module_and_refuses_it()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions());

        var refused = Assert.Throws<SlangException>(() =>
            session.LoadModuleFromSource("usesNeural", "usesNeural.slang", Source("slang.neural")));
        Assert.Contains("E00104", refused.Message, StringComparison.Ordinal);
        Assert.Contains("'slang/neural' is an experimental module", refused.Message, StringComparison.Ordinal);

        // A module that is not there is another error: the file was not found
        var missing = Assert.Throws<SlangException>(() =>
            session.LoadModuleFromSource("usesNothing", "usesNothing.slang", Source("slang.nosuchmodule")));
        Assert.Contains("E00001", missing.Message, StringComparison.Ordinal);
    }
}
