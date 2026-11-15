// What is loaded must be what SharpSlang was built against, and a build that would load something
// else fails first (AGENTS.md#pin-and-payload).
//
// Three run-time comparisons at global-session creation: the native library's build tag, the
// loaded SharpSlang.Descs' Slang release, and its package version, each against SharpSlang's own
// metadata. Provoking them for real means a second Slang payload or a second Descs package on the
// load path; what can be wrong is the comparison and the message, so SlangVersionCheck takes the
// loaded values as arguments and is driven with the wrong ones here. The two build-time codes are
// driven by building a project in a temporary directory against this repository's targets.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public sealed class VersionGuardTests(ITestOutputHelper output)
{
    private static string Pin(string property)
    {
        var props = File.ReadAllText(Path.Combine(SlangHeader.RepoRoot(), "Directory.Build.props"));
        var m = Regex.Match(props, $"<{property}>([^<]+)</{property}>");
        Assert.True(m.Success, $"Directory.Build.props does not set {property}");
        return m.Groups[1].Value.Trim();
    }

    // ---------------------------------------------------------------- the versions, exposed twice

    [Fact]
    public void The_version_properties_say_what_is_built_and_what_is_loaded()
    {
        var slang = Pin("SlangVersion").TrimStart('v');

        // Compiled into this assembly: what the test was built against.
        Assert.Equal(Slang.SlangVersion, slang);
        Assert.Equal(SlangHeaders.Version, slang);

        // Read from the loaded assemblies.
        Assert.Equal(slang, Slang.BindingSlangVersion);
        Assert.Equal(slang, Slang.DescsSlangVersion);
        Assert.Equal(Pin("SharpSlangVersion"), Slang.BindingVersion);
        Assert.Equal(Pin("SharpSlangDescsVersion"), Slang.DescsVersion);
        Assert.Equal(Slang.BindingSlangVersion, SlangGlobalSession.PinnedBuildTag);
    }

    [Fact]
    public void Every_package_major_is_the_slang_version_as_YYYYRRP()
    {
        // v2026.21 is 2026210 (AGENTS.md#decisions); the targets compute it, these say it again.
        var major = Encode(Pin("SlangVersion"));
        foreach (var pin in (string[])["SharpSlangVersion", "SharpSlangDescsVersion", "SharpSlangPipelineVersion"])
            Assert.StartsWith(major + ".", Pin(pin), StringComparison.Ordinal);

        // The assembly version cannot hold 2026210 (16 bits a part): split where the encoding joins.
        var expected = new Version(int.Parse(major[..4], System.Globalization.CultureInfo.InvariantCulture),
                                   int.Parse(major[4..], System.Globalization.CultureInfo.InvariantCulture), 0, 0);
        Assert.Equal(expected, typeof(SlangGlobalSession).Assembly.GetName().Version);
        Assert.Equal(expected, typeof(SlangStage).Assembly.GetName().Version);

        static string Encode(string tag)
        {
            var parts = tag.TrimStart('v').Split('.');
            return parts[0] + parts[1].PadLeft(2, '0') + (parts.Length > 2 ? parts[2] : "0");
        }
    }

    // ---------------------------------------------------------------- the run-time guard

    [Fact]
    public void The_loaded_descs_are_the_ones_SharpSlang_depends_on()
    {
        Assert.Null(SlangVersionCheck.DescsMismatch(SlangVersionCheck.Descs, SlangVersionCheck.DescsPackage));
    }

    [Fact]
    public void Descs_of_another_slang_release_are_refused_naming_both_and_the_file()
    {
        var e = SlangVersionCheck.DescsMismatch("2026.18.3", "2026183.0.0-beta.1");

        Assert.NotNull(e);
        Assert.Equal(SlangVersionCheck.Built, e.Expected);
        Assert.Equal("2026.18.3", e.Loaded);
        Assert.Equal(typeof(SlangStage).Assembly.Location, e.LibraryPath);
        Assert.Contains($"Slang {SlangVersionCheck.Built}", e.Message, StringComparison.Ordinal);
        Assert.Contains("Slang 2026.18.3", e.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SlangStage).Assembly.Location, e.Message, StringComparison.Ordinal);
        Assert.Contains(SlangVersionCheck.BuiltDescsPackage, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Descs_of_another_package_version_are_refused_naming_both_and_the_file()
    {
        // The same Slang release, another Descs package: NuGet lets a direct reference win with
        // only NU1608, and SHARPSLANG005 is what fails that build first.
        var e = SlangVersionCheck.DescsMismatch(SlangVersionCheck.Descs, "2026210.0.0-beta.9");

        Assert.NotNull(e);
        Assert.Equal(SlangVersionCheck.BuiltDescsPackage, e.Expected);
        Assert.Equal("2026210.0.0-beta.9", e.Loaded);
        Assert.Contains(SlangVersionCheck.BuiltDescsPackage, e.Message, StringComparison.Ordinal);
        Assert.Contains("2026210.0.0-beta.9", e.Message, StringComparison.Ordinal);
        Assert.Contains("NU1608", e.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SlangStage).Assembly.Location, e.Message, StringComparison.Ordinal);
    }

    [NativeFact]
    public void A_native_library_of_another_release_is_refused_naming_its_path()
    {
        // The library is loaded here, so the process module list names it.
        _ = SlangGlobalSession.BuildTag;
        var e = SlangVersionCheck.NativeMismatch(SlangVersionCheck.Built, "2026.18.3");

        Assert.NotNull(e);
        Assert.NotNull(e.LibraryPath);
        Assert.True(File.Exists(e.LibraryPath), $"named a library that is not there: {e.LibraryPath}");
        Assert.Matches(@"(^|[\\/])(lib)?slang-compiler\.", e.LibraryPath);
        Assert.Contains(e.LibraryPath, e.Message, StringComparison.Ordinal);
        Assert.Contains("2026.18.3", e.Message, StringComparison.Ordinal);
        Assert.Null(SlangVersionCheck.NativeMismatch(SlangVersionCheck.Built, SlangVersionCheck.Built));
    }

    // ---------------------------------------------------------------- a missing library, explained

    [Fact]
    public void A_missing_compiler_is_described_by_file_rid_directories_and_source()
    {
        var text = NativeLibraryHint.Describe("linux-arm64", ["/opt/game", "/opt/game/runtimes/linux-arm64/native"], handlerAsked: true);

        Assert.Contains("libslang-compiler.so", text, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", text, StringComparison.Ordinal);
        Assert.Contains("Looked in: /opt/game, /opt/game/runtimes/linux-arm64/native.", text, StringComparison.Ordinal);
        Assert.Contains("the SharpSlang package carries it under runtimes/linux-arm64/native/", text, StringComparison.Ordinal);
        Assert.Contains("pwsh tools/fetch-native.ps1 -Rid linux-arm64", text, StringComparison.Ordinal);
        Assert.Contains("No DllImport resolver or ResolvingUnmanagedDll handler supplied one.", text, StringComparison.Ordinal);

        Assert.Contains("slang-compiler.dll", NativeLibraryHint.Describe("win-x64", ["C:\\app"], false), StringComparison.Ordinal);
        Assert.Contains("libslang-compiler.dylib", NativeLibraryHint.Describe("osx-arm64", ["/app"], false), StringComparison.Ordinal);
        Assert.Contains("SharpSlang packages no library for win-arm64", NativeLibraryHint.Describe("win-arm64", ["C:\\app"], false), StringComparison.Ordinal);
    }

    [Fact]
    public void The_explanation_keeps_the_runtimes_exception_inside()
    {
        var original = new DllNotFoundException("Unable to load DLL 'slang-compiler'");
        var explained = NativeLibraryHint.Explain(original);

        Assert.Same(original, explained.InnerException);
        Assert.Contains("Unable to load DLL 'slang-compiler'", explained.Message, StringComparison.Ordinal);
        Assert.Contains(RuntimeInformation.RuntimeIdentifier, explained.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_handler_answers_nothing_and_throws_nothing()
    {
        // The event stops at the first handler that returns a handle or throws; one of ours that
        // did either would keep an application's handler, registered later, from running.
        Assert.Equal((nint)0, NativeLibraryHint.OnResolvingUnmanagedDll(typeof(SlangApi).Assembly, NativeLibraryHint.Library));
        Assert.True(NativeLibraryHint.Missed);
        Assert.Equal((nint)0, NativeLibraryHint.OnResolvingUnmanagedDll(typeof(VersionGuardTests).Assembly, "something-else"));
    }

    [Fact]
    public void A_handler_registered_after_SharpSlangs_still_runs()
    {
        NativeLibraryHint.Register();
        var context = AssemblyLoadContext.GetLoadContext(typeof(SlangApi).Assembly)!;
        var asked = new List<string>();

        context.ResolvingUnmanagedDll += Theirs;
        try
        {
            Assert.False(NativeLibrary.TryLoad("sharpslang-no-such-library", typeof(SlangApi).Assembly, null, out _));
        }
        finally
        {
            context.ResolvingUnmanagedDll -= Theirs;
        }

        Assert.Contains("sharpslang-no-such-library", asked);

        nint Theirs(Assembly assembly, string name)
        {
            asked.Add(name);
            return 0;
        }
    }

    // ---------------------------------------------------------------- the build-time codes

    private (int ExitCode, string Output) Build(string projectXml, params string[] arguments)
    {
        var directory = Directory.CreateTempSubdirectory("sharpslang-version-");
        try
        {
            var project = Path.Combine(directory.FullName, "probe.csproj");
            File.WriteAllText(project, projectXml.Replace("$(Repo)", SlangHeader.RepoRoot(), StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(directory.FullName, "assets.json"), """{ "libraries": { "SharpSlang.Descs/2026210.0.0-beta.2": { } } }""");

            var info = new ProcessStartInfo(DotnetHost.Path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory.FullName,
            };
            info.ArgumentList.Add("msbuild");
            info.ArgumentList.Add(project);
            info.ArgumentList.Add("-nologo");
            info.ArgumentList.Add("-nodeReuse:false");
            foreach (var a in arguments) info.ArgumentList.Add(a);

            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(120_000), "msbuild did not finish within 2 minutes");
            var text = stdout.Result + stderr.Result;
            output.WriteLine(text);
            return (process.ExitCode, text);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private const string VersionProject = """
        <Project>
          <PropertyGroup>
            <PackageId>Probe</PackageId>
            <SlangVersion>{0}</SlangVersion>
            <Version>{1}</Version>
          </PropertyGroup>
          <Import Project="$(Repo)/Directory.Build.targets" />
        </Project>
        """;

    [Theory]
    [InlineData("v2026.21", "2026210")]
    [InlineData("v2026.18.3", "2026183")]
    [InlineData("v2026.4", "2026040")]
    public void The_targets_encode_the_slang_version_as_YYYYRRP(string tag, string major)
    {
        var (exit, text) = Build(VersionProject.Replace("{0}", tag, StringComparison.Ordinal).Replace("{1}", major + ".2.3-beta.1", StringComparison.Ordinal),
                                 "-getProperty:SharpSlangVersionMajor", "-getProperty:AssemblyVersion");
        Assert.Equal(0, exit);
        Assert.Contains($"\"SharpSlangVersionMajor\": \"{major}\"", text, StringComparison.Ordinal);
        Assert.Contains($"\"AssemblyVersion\": \"{major[..4]}.{int.Parse(major[4..], System.Globalization.CultureInfo.InvariantCulture)}.2.3\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v2026.21", "2026183.0.0-beta.1", "does not start with 2026210")]
    [InlineData("v2026.18.3", "2026210.0.0-beta.1", "does not start with 2026183")]
    [InlineData("v2026.21.10", "2026210.0.0-beta.1", "has no YYYYRRP encoding")]
    public void A_package_major_other_than_the_slang_version_fails_SHARPSLANG004(string tag, string version, string words)
    {
        var (exit, text) = Build(VersionProject.Replace("{0}", tag, StringComparison.Ordinal).Replace("{1}", version, StringComparison.Ordinal), "-t:SharpSlangVersionMajor");
        Assert.NotEqual(0, exit);
        Assert.Contains("SHARPSLANG004", text, StringComparison.Ordinal);
        Assert.Contains(words, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_matching_major_builds_a_real_project()
    {
        // The SDK's own build, so the guard runs where it is hooked (before Build) and the split
        // assembly version gets past the compiler that refused 2026210.0.0.0 with CS7034.
        var (exit, text) = Build("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <SlangVersion>v2026.18.3</SlangVersion>
                <Version>2026183.1.2-beta.1</Version>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <Import Project="$(Repo)/Directory.Build.targets" />
            </Project>
            """, "-restore", "-t:Build", "-getProperty:AssemblyVersion", "-getTargetResult:Build");
        Assert.True(exit == 0, text);
        Assert.Contains("\"AssemblyVersion\": \"2026.183.1.2\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SHARPSLANG004", text, StringComparison.Ordinal);
    }

    private const string DescsProject = """
        <Project>
          <PropertyGroup>
            <SharpSlangPackedVersion>2026210.0.0-beta.1</SharpSlangPackedVersion>
            <SharpSlangDescsDependency>{0}</SharpSlangDescsDependency>
            <ProjectAssetsFile>$(MSBuildProjectDirectory)/assets.json</ProjectAssetsFile>
          </PropertyGroup>
          <Import Project="$(Repo)/dotnet/buildTransitive/SharpSlang.targets" />
          <Target Name="Build" />
        </Project>
        """;

    [Fact]
    public void Restoring_another_descs_version_fails_SHARPSLANG005()
    {
        // assets.json resolves SharpSlang.Descs 2026210.0.0-beta.2, as a direct reference that won
        // the restore with NU1608 would.
        var (exit, text) = Build(DescsProject.Replace("{0}", "2026210.0.0-beta.1", StringComparison.Ordinal), "-t:Build");
        Assert.NotEqual(0, exit);
        Assert.Contains("SHARPSLANG005", text, StringComparison.Ordinal);
        Assert.Contains("SharpSlang.Descs 2026210.0.0-beta.1 exactly", text, StringComparison.Ordinal);
        Assert.Contains("restores SharpSlang.Descs 2026210.0.0-beta.2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Restoring_the_descs_version_SharpSlang_depends_on_builds()
    {
        var (exit, text) = Build(DescsProject.Replace("{0}", "2026210.0.0-beta.2", StringComparison.Ordinal), "-t:Build");
        Assert.True(exit == 0, text);
        Assert.DoesNotContain("SHARPSLANG005", text, StringComparison.Ordinal);
    }
}

/// <summary>The dotnet host that owns the runtime the tests run on, for child processes.</summary>
/// <remarks>
/// Not "dotnet" from PATH: verify-linux.sh runs the suite through ~/.dotnet/dotnet with nothing on
/// PATH, and a bare "dotnet" failed to start there (WSL, 093382b). The shared runtime sits at
/// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c> and the host at the root, as
/// ProberTests finds it.
/// </remarks>
internal static class DotnetHost
{
    internal static string Path { get; } = Find();

    private static string Find()
    {
        var runtime = RuntimeEnvironment.GetRuntimeDirectory();
        var muxer = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(runtime, "..", "..", "..")),
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(muxer) ? muxer : "dotnet";
    }
}
