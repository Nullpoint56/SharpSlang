// Offline baking — docs §4.4 reason 3.
//
// A shipped game should not carry a 36 MB shader compiler. Since the reflection snapshot is plain
// immutable managed data, the asset pipeline can compile once at build time and write the SPIR-V
// and the layout together; the runtime then loads a package and never touches PrismSlang's native
// payload at all.
//
// The container is deliberately trivial and self-describing rather than a general asset format:
// PrismEngine has its own pipeline, and this should slot into it, not compete with it. Everything
// is written to a Stream, so a package can equally live in a pak file or a memory buffer.
//
//   magic     "PSLB"                       4 bytes
//   version   uint32                       format version, not Slang's
//   slang     length-prefixed UTF-8        the Slang build that produced this
//   source    32 bytes                     SHA-256 of the source, for staleness checks
//   layout    length-prefixed UTF-8 JSON   the reflection snapshot
//   deps      uint32 + [ path, 32-byte hash ] * n   files this was compiled from
//   count     uint32                       number of entry points
//   entries   [ name, moduleEntryPointName, stage, spirvVersion, workgroup, length, bytes ] * count
//
//   workgroup uint8 present + [ value, specId+1 ] * 3   threads per group, 0 byte if absent
//
// All integers little-endian. BinaryReader/Writer default to that on every platform .NET targets,
// and the format is not intended for cross-endian transport.

using System.Security.Cryptography;
using System.Text;
using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Spirv;

namespace PrismSlang.Baking;

/// <summary>Compiled code for one entry point.</summary>
/// <param name="Name">Entry point function name, as declared in Slang.</param>
/// <param name="Stage">Pipeline stage, so a loader can build pipelines without re-reflecting.</param>
/// <param name="Code">Target code — SPIR-V for a Vulkan build.</param>
/// <param name="ModuleEntryPointName">
/// The entry point's name <em>inside</em> <paramref name="Code"/> — what goes in
/// <c>VkPipelineShaderStageCreateInfo::pName</c>. Read from the module, never assumed.
/// </param>
/// <param name="SpirvVersion">
/// The SPIR-V version of <paramref name="Code"/>, so a loader can reject a module its device
/// cannot accept before handing it to the driver. <c>Unspecified</c> for a non-SPIR-V target.
/// </param>
/// <param name="WorkgroupSize">
/// Threads per group for a compute-like stage, read from the module. Null for the raster and
/// ray-tracing stages, and for a non-SPIR-V target.
/// </param>
/// <remarks>
/// <para>
/// <paramref name="Name"/> and <paramref name="ModuleEntryPointName"/> are separate members
/// because they are separate things, and conflating them is the single most likely way to get a
/// pipeline that fails to create. Slang names every per-entry-point module <c>main</c> by default,
/// whatever the function was called; <c>TargetOptions.UseEntryPointNameInSpirv</c> makes it use the
/// Slang name instead. Look up shaders by <paramref name="Name"/>; hand the driver
/// <paramref name="ModuleEntryPointName"/>.
/// </para>
/// <para>
/// <paramref name="WorkgroupSize"/> is here rather than left to the layout for the same reason:
/// reflection reports <c>0</c> for a dimension driven by a specialization constant, and the module
/// is the only place that carries the constant id and the declared default.
/// </para>
/// </remarks>
public sealed record BakedEntryPoint(
    string Name,
    SlangStage Stage,
    byte[] Code,
    string ModuleEntryPointName = "main",
    SpirvVersion SpirvVersion = SpirvVersion.Unspecified,
    SpirvWorkgroupSize? WorkgroupSize = null);

/// <summary>One file a package was compiled from, and what it contained at the time.</summary>
/// <param name="Path">The path Slang resolved, absolute for anything found through a search path.</param>
/// <param name="Hash">SHA-256 of its contents when the package was baked.</param>
public sealed record ShaderDependency(string Path, byte[] Hash);

/// <summary>
/// A compiled shader and its layout, ready to ship. Load with <see cref="ReadFrom"/> and the
/// compiler is never needed at run time.
/// </summary>
public sealed class ShaderPackage
{
    private const uint Magic = 0x424C5350; // "PSLB" little-endian

    // 2 added the module entry point name and SPIR-V version per entry point. 3 added the
    // dependency file list. 4 added the workgroup size. Older packages cannot be upgraded in
    // place — none of the module name, the import graph or the workgroup size is recoverable from
    // what an earlier version stored — so ReadFrom rejects them and says rebake. Rebaking is cheap;
    // a package that silently reports "main" for a module that says otherwise, or that claims to be
    // current after its imports moved, is not.
    private const uint FormatVersion = 4;

    /// <summary>The Slang build that produced this package, e.g. <c>2026.14.1</c>.</summary>
    public required string SlangVersion { get; init; }

    /// <summary>
    /// SHA-256 of the source this was compiled from. Compare against the current source to detect
    /// a stale bake without recompiling.
    /// </summary>
    public required byte[] SourceHash { get; init; }

    /// <summary>The reflection snapshot.</summary>
    public required ProgramLayout Layout { get; init; }

    /// <summary>Compiled code per entry point, in link order.</summary>
    public required IReadOnlyList<BakedEntryPoint> EntryPoints { get; init; }

    /// <summary>
    /// Every file this was compiled from, with its contents' hash at bake time. Empty for a
    /// package baked from an in-memory string.
    /// </summary>
    /// <remarks>
    /// Includes the shader itself, everything it <c>import</c>s, and everything <c>#include</c>d.
    /// This is what <see cref="StaleDependencies"/> checks; without it, editing a shared lighting
    /// module leaves every shader that imports it claiming to be current.
    /// </remarks>
    public IReadOnlyList<ShaderDependency> Dependencies { get; init; } = [];

    /// <summary>Code for one entry point by name, or null.</summary>
    public BakedEntryPoint? EntryPoint(string name) =>
        EntryPoints.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>SHA-256 of a shader source, in the same form as <see cref="SourceHash"/>.</summary>
    public static byte[] HashSource(string source) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(source));

    /// <summary>
    /// Whether this package was built from <paramref name="source"/> by the currently loaded
    /// compiler. False means rebake — either the source changed or the Slang version moved, and
    /// Slang's ABI and layout rules are not stable across versions.
    /// </summary>
    /// <remarks>
    /// Covers only the string it is given. For a shader loaded from disk, which may import others,
    /// use <see cref="IsCurrent"/> instead.
    /// </remarks>
    public bool IsUpToDate(string source, string currentSlangVersion) =>
        SourceHash.AsSpan().SequenceEqual(HashSource(source))
        && string.Equals(SlangVersion, currentSlangVersion, StringComparison.Ordinal);

    /// <summary>
    /// Which recorded dependencies no longer match what is on disk. Empty means nothing changed.
    /// </summary>
    /// <remarks>
    /// Returns the paths rather than a bool so a build log can say <em>which</em> file triggered a
    /// rebuild. A file that has been deleted or become unreadable counts as changed: the package
    /// cannot be shown to be current, and assuming it is would ship a stale shader.
    /// </remarks>
    public IReadOnlyList<string> StaleDependencies()
    {
        var stale = new List<string>();

        foreach (var dependency in Dependencies)
        {
            byte[] current;
            try
            {
                current = SHA256.HashData(File.ReadAllBytes(dependency.Path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stale.Add(dependency.Path);
                continue;
            }

            if (!current.AsSpan().SequenceEqual(dependency.Hash)) stale.Add(dependency.Path);
        }

        return stale;
    }

    /// <summary>
    /// Whether this package can still be used: the compiler has not moved, and every file it was
    /// compiled from is unchanged on disk.
    /// </summary>
    /// <remarks>
    /// A package with no recorded dependencies — baked from a string — cannot be checked this way,
    /// and reports false rather than true. Claiming currency for something unverifiable is the
    /// failure this method exists to prevent.
    /// </remarks>
    public bool IsCurrent(string currentSlangVersion) =>
        Dependencies.Count > 0
        && string.Equals(SlangVersion, currentSlangVersion, StringComparison.Ordinal)
        && StaleDependencies().Count == 0;

    // ---------------------------------------------------------------- baking

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
        return Bake(program, HashSource(source), dependencies, targetIndex);
    }

    private static ShaderPackage Bake(
        LinkedProgram program,
        byte[] sourceHash,
        IReadOnlyList<ShaderDependency> dependencies,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(program);

        var layout = program.Reflect(targetIndex);

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

    // ---------------------------------------------------------------- container

    /// <summary>Writes the package. The stream is left open and positioned after the data.</summary>
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (SourceHash.Length != 32)
            throw new InvalidOperationException($"SourceHash must be 32 bytes, was {SourceHash.Length}");

        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(FormatVersion);
        w.Write(SlangVersion);
        w.Write(SourceHash);
        w.Write(Layout.ToJson());

        w.Write((uint)Dependencies.Count);
        foreach (var d in Dependencies)
        {
            if (d.Hash.Length != 32)
                throw new InvalidOperationException(
                    $"dependency hash for '{d.Path}' must be 32 bytes, was {d.Hash.Length}");
            w.Write(d.Path);
            w.Write(d.Hash);
        }

        w.Write((uint)EntryPoints.Count);
        foreach (var e in EntryPoints)
        {
            w.Write(e.Name);
            w.Write(e.ModuleEntryPointName);
            w.Write((uint)e.Stage);
            w.Write((uint)e.SpirvVersion);
            WriteWorkgroupSize(w, e.WorkgroupSize);
            w.Write((uint)e.Code.Length);
            w.Write(e.Code);
        }
    }

    // A specialization constant id is written biased by one so that 0 can mean "not specialized";
    // id 0 is a real, common constant id and cannot double as the absent marker.
    private static void WriteWorkgroupSize(BinaryWriter w, SpirvWorkgroupSize? size)
    {
        w.Write(size is not null);
        if (size is null) return;

        foreach (var d in new[] { size.X, size.Y, size.Z })
        {
            w.Write(d.Value);
            w.Write(d.SpecializationConstantId + 1 ?? 0);
        }
    }

    private static SpirvWorkgroupSize? ReadWorkgroupSize(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        return new SpirvWorkgroupSize(ReadDimension(r), ReadDimension(r), ReadDimension(r));

        static SpirvWorkgroupDimension ReadDimension(BinaryReader r)
        {
            var value = r.ReadUInt32();
            var biased = r.ReadUInt32();
            return new SpirvWorkgroupDimension(value, biased == 0 ? null : biased - 1);
        }
    }

    /// <summary>Reads a package. Requires no compiler and no native library.</summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not a package this build can read — wrong magic, wrong format version,
    /// truncated, or corrupt. Always this type, whatever went wrong inside.
    /// </exception>
    /// <remarks>
    /// <para>
    /// One failure mode, on purpose. This runs at load time in a shipped game, against a file that
    /// may have been half-written, partly downloaded or corrupted on disk, in a build with no
    /// compiler to fall back on — so a caller has to be able to write one catch and know it covers
    /// the case. Before it did, a corrupted byte in the layout produced a
    /// <c>JsonException</c> and a truncated file an <c>EndOfStreamException</c>, neither of which
    /// is in this method's contract and neither of which a caller was catching.
    /// </para>
    /// <para>
    /// The broad catch below is deliberate for that reason: the set of ways a byte array can be
    /// wrong is not enumerable, and every one of them means the same thing to a caller.
    /// </para>
    /// </remarks>
    public static ShaderPackage ReadFrom(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            return Read(stream);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
        {
            throw new InvalidDataException(
                $"not a readable PrismSlang shader package: {e.Message}", e);
        }
    }

    private static ShaderPackage Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (r.ReadUInt32() != Magic)
            throw new InvalidDataException("not a PrismSlang shader package (bad magic)");

        var version = r.ReadUInt32();
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"shader package format version {version}, this build reads {FormatVersion}. Rebake.");

        var slangVersion = r.ReadString();
        var sourceHash = r.ReadBytes(32);
        var layout = ProgramLayout.FromJson(r.ReadString());

        var dependencyCount = Bounded(r, r.ReadUInt32(), perItem: 33, "dependencies");
        var dependencies = new List<ShaderDependency>((int)dependencyCount);
        for (var i = 0u; i < dependencyCount; i++)
            dependencies.Add(new ShaderDependency(r.ReadString(), r.ReadBytes(32)));

        var count = Bounded(r, r.ReadUInt32(), perItem: 20, "entry points");
        var entryPoints = new List<BakedEntryPoint>((int)count);
        for (var i = 0u; i < count; i++)
        {
            var name = r.ReadString();
            var moduleName = r.ReadString();
            var stage = (SlangStage)r.ReadUInt32();
            var spirvVersion = (SpirvVersion)r.ReadUInt32();
            var workgroupSize = ReadWorkgroupSize(r);
            var length = Bounded(r, r.ReadUInt32(), perItem: 1, "code bytes");
            entryPoints.Add(new BakedEntryPoint(
                name, stage, r.ReadBytes((int)length), moduleName, spirvVersion, workgroupSize));
        }

        return new ShaderPackage
        {
            SlangVersion = slangVersion,
            SourceHash = sourceHash,
            Layout = layout,
            EntryPoints = entryPoints,
            Dependencies = dependencies,
        };
    }

    /// <summary>
    /// Rejects a count or length the rest of the stream cannot possibly satisfy.
    /// </summary>
    /// <param name="count">The value just read from the stream.</param>
    /// <param name="perItem">Fewest bytes one item can occupy.</param>
    /// <param name="what">What is being counted, for the message.</param>
    /// <remarks>
    /// A corrupted length prefix is the one field that turns a bad file into a bad allocation:
    /// <c>BinaryReader.ReadBytes</c> allocates its argument up front, so a flipped byte asking for
    /// two gigabytes gets two gigabytes before the read fails. Checking against what is left in the
    /// stream costs nothing and turns it into a message. Skipped for a stream that cannot report
    /// its length, where there is nothing to check against.
    /// </remarks>
    private static uint Bounded(BinaryReader r, uint count, long perItem, string what)
    {
        if (!r.BaseStream.CanSeek) return count;

        var remaining = r.BaseStream.Length - r.BaseStream.Position;
        if ((long)count * perItem > remaining)
        {
            throw new InvalidDataException(
                $"shader package claims {count} {what}, which needs at least {(long)count * perItem} "
                + $"bytes, but only {remaining} remain. The file is truncated or corrupt.");
        }

        return count;
    }

    /// <summary>Convenience: write to a file, replacing any existing one.</summary>
    public void WriteToFile(string path)
    {
        using var fs = File.Create(path);
        WriteTo(fs);
    }

    /// <summary>Convenience: read from a file.</summary>
    public static ShaderPackage ReadFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return ReadFrom(fs);
    }
}
