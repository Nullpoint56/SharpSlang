// Turning a linked program into a shippable package: SharpSlang.Pipeline.Bake, the cooker.
//
// Separated from ShaderPackage itself, which lives in SharpSlang.Pipeline. Reading a package needs
// nothing but managed code; *making* one needs a LinkedProgram, a SlangModule and the compiler
// behind them. So the split runs exactly along that line: the container, its reader, its writer and
// its staleness checks are on the runtime side; this, the only method that compiles anything, is
// here, on SharpSlang's public API (AGENTS.md#layers).

using System.Security.Cryptography;
using System.Text;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

namespace SharpSlang.Baking;

/// <summary>Compiles a linked program into a <see cref="ShaderPackage"/>.</summary>
public static class ShaderPackageBaker
{

    /// <summary>
    /// Compiles and captures everything needed at run time: code per entry point plus the layout.
    /// </summary>
    /// <param name="program">A linked program.</param>
    /// <param name="source">The source it was compiled from, hashed for staleness checks.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public static ShaderPackage Bake(LinkedProgram program, string source, int targetIndex = 0) =>
        Bake(program, source, dependencies: [], targetIndex);

    /// <summary>
    /// Compiles and captures everything needed at run time, plus the files it was compiled from.
    /// </summary>
    /// <param name="program">A linked program.</param>
    /// <param name="module">
    /// The module it was linked from, read for its dependency list — itself, its imports and its
    /// includes — so a later build can tell whether any of them changed.
    /// </param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    /// <remarks>
    /// Prefer this over the string overload for anything loaded from disk. Hashing only the named
    /// file leaves a bake claiming to be current after a module it imports has changed, and the
    /// symptom is a shader running the version before last.
    /// </remarks>
    public static ShaderPackage Bake(LinkedProgram program, SlangModule module, int targetIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(module);

        var dependencies = new List<ShaderDependency>();
        var combined = new List<byte>();

        // Ordered by path so the combined hash does not depend on the order Slang happened to
        // resolve imports in, which is not something a shader author controls.
        foreach (var path in module.DependencyFilePaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            byte[] hash;
            try
            {
                hash = SHA256.HashData(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A synthetic path from LoadModuleFromSource, or a virtual file behind a
                // SlangFileSystem. Not an error: it simply cannot be staleness-checked on disk.
                continue;
            }

            dependencies.Add(new ShaderDependency(path, hash));
            combined.AddRange(Encoding.UTF8.GetBytes(path));
            combined.AddRange(hash);
        }

        return Bake(program, SHA256.HashData(combined.ToArray()), dependencies, targetIndex);
    }

    private static ShaderPackage Bake(
        LinkedProgram program,
        string source,
        IReadOnlyList<ShaderDependency> dependencies,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Bake(program, ShaderPackage.HashSource(source), dependencies, targetIndex);
    }

    private static ShaderPackage Bake(
        LinkedProgram program,
        byte[] sourceHash,
        IReadOnlyList<ShaderDependency> dependencies,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(program);

        var layout = program.Reflect(targetIndex);

        // Read the layout back before handing the package out. The layout JSON is the one lossy
        // conversion in the container: every other field is written and read symmetrically, while
        // this one goes through a serializer whose options (nulls dropped, enums as strings) and
        // annotations (`required`) have to agree with each other, and the consumer cannot know
        // which parts are lossy. They disagreed once: a `required` nullable dropped on write made
        // every package with an unbounded array unreadable, and a consumer's cook caught the read
        // exception in its up-to-date check and silently re-cooked, so the defect only surfaced at
        // pipeline creation. The check costs one serialize and one parse, at build time.
        VerifyLayoutRoundTrips(layout.ToJson());

        var entryPoints = new List<BakedEntryPoint>(program.EntryPointNames.Count);
        for (var i = 0; i < program.EntryPointNames.Count; i++)
        {
            var name = program.EntryPointNames[i];
            var code = program.GetEntryPointCodeBytes(i, targetIndex);

            // Read the module's own name and version rather than deriving them. This is the whole
            // point of baking: the runtime should never need to parse SPIR-V or know Slang's
            // naming rules, and neither should the pipeline builder.
            var moduleName = "main";
            var spirvVersion = SpirvVersion.Unspecified;
            SpirvWorkgroupSize? workgroupSize = null;
            if (SpirvModule.IsSpirv(code))
            {
                var module = SpirvModule.Read(code);
                spirvVersion = module.Version;
                if (module.EntryPoints.Count == 1)
                {
                    moduleName = module.EntryPoints[0].Name;
                    workgroupSize = module.EntryPoints[0].WorkgroupSize;
                }
            }

            entryPoints.Add(new BakedEntryPoint(
                name,
                layout.EntryPoint(name)?.Stage ?? SlangStage.SLANG_STAGE_NONE,
                code,
                moduleName,
                spirvVersion,
                workgroupSize));
        }

        return new ShaderPackage
        {
            SlangVersion = SlangGlobalSession.BuildTag,
            SourceHash = sourceHash,
            Layout = layout,
            EntryPoints = entryPoints,
            Dependencies = dependencies,
        };
    }

    /// <summary>
    /// Throws unless <paramref name="json"/>, a serialized <see cref="ProgramLayout"/>, reads back
    /// and serializes to exactly the same text.
    /// </summary>
    /// <remarks>
    /// Only the public <see cref="ProgramLayout.ToJson"/> and <see cref="ProgramLayout.FromJson"/>,
    /// because they are what <see cref="ShaderPackage.ReadFrom"/> runs on, and because
    /// SharpSlang.Pipeline grants this assembly no internals by decision.
    /// </remarks>
    internal static void VerifyLayoutRoundTrips(string json)
    {
        const string Defect =
            "The baked layout does not read back. This is a defect in SharpSlang's layout " +
            "serializer, not in the shader; please report it with the shader that triggered it.";

        string reread;
        try
        {
            reread = ProgramLayout.FromJson(json).ToJson();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException
                                       or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException($"{Defect} Reading it failed: {e.Message}", e);
        }

        if (string.Equals(json, reread, StringComparison.Ordinal))
            return;

        var written = json.Split('\n');
        var read = reread.Split('\n');
        var line = 0;
        while (line < written.Length && line < read.Length
               && string.Equals(written[line], read[line], StringComparison.Ordinal))
            line++;

        throw new InvalidOperationException(
            $"{Defect} The first difference is on line {line + 1}: written as " +
            $"{Describe(written, line)}, read back as {Describe(read, line)}.");

        static string Describe(string[] lines, int index) =>
            index < lines.Length ? $"'{lines[index].Trim()}'" : "the end of the text";
    }
}
