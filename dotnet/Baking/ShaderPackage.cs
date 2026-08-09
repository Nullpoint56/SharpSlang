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
//   count     uint32                       number of entry points
//   entries   [ name, stage, length, bytes ] * count
//
// All integers little-endian. BinaryReader/Writer default to that on every platform .NET targets,
// and the format is not intended for cross-endian transport.

using System.Security.Cryptography;
using System.Text;
using PrismSlang.Native;
using PrismSlang.Reflection;

namespace PrismSlang.Baking;

/// <summary>Compiled code for one entry point.</summary>
/// <param name="Name">Entry point function name.</param>
/// <param name="Stage">Pipeline stage, so a loader can build pipelines without re-reflecting.</param>
/// <param name="Code">Target code — SPIR-V for a Vulkan build.</param>
public sealed record BakedEntryPoint(string Name, SlangStage Stage, byte[] Code);

/// <summary>
/// A compiled shader and its layout, ready to ship. Load with <see cref="ReadFrom"/> and the
/// compiler is never needed at run time.
/// </summary>
public sealed class ShaderPackage
{
    private const uint Magic = 0x424C5350; // "PSLB" little-endian
    private const uint FormatVersion = 1;

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
    public bool IsUpToDate(string source, string currentSlangVersion) =>
        SourceHash.AsSpan().SequenceEqual(HashSource(source))
        && string.Equals(SlangVersion, currentSlangVersion, StringComparison.Ordinal);

    // ---------------------------------------------------------------- baking

    /// <summary>
    /// Compiles and captures everything needed at run time: code per entry point plus the layout.
    /// </summary>
    /// <param name="program">A linked program.</param>
    /// <param name="source">The source it was compiled from, hashed for staleness checks.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public static ShaderPackage Bake(LinkedProgram program, string source, int targetIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(source);

        var layout = program.Reflect(targetIndex);

        var entryPoints = new List<BakedEntryPoint>(program.EntryPointNames.Count);
        for (var i = 0; i < program.EntryPointNames.Count; i++)
        {
            var name = program.EntryPointNames[i];
            entryPoints.Add(new BakedEntryPoint(
                name,
                layout.EntryPoint(name)?.Stage ?? SlangStage.SLANG_STAGE_NONE,
                program.GetEntryPointCodeBytes(i, targetIndex)));
        }

        return new ShaderPackage
        {
            SlangVersion = SlangGlobalSession.BuildTag,
            SourceHash = HashSource(source),
            Layout = layout,
            EntryPoints = entryPoints,
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

        w.Write((uint)EntryPoints.Count);
        foreach (var e in EntryPoints)
        {
            w.Write(e.Name);
            w.Write((uint)e.Stage);
            w.Write((uint)e.Code.Length);
            w.Write(e.Code);
        }
    }

    /// <summary>Reads a package. Requires no compiler and no native library.</summary>
    /// <exception cref="InvalidDataException">Not a package, or a format version we cannot read.</exception>
    public static ShaderPackage ReadFrom(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (r.ReadUInt32() != Magic)
            throw new InvalidDataException("not a PrismSlang shader package (bad magic)");

        var version = r.ReadUInt32();
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"shader package format version {version}, this build reads {FormatVersion}");

        var slangVersion = r.ReadString();
        var sourceHash = r.ReadBytes(32);
        var layout = ProgramLayout.FromJson(r.ReadString());

        var count = r.ReadUInt32();
        var entryPoints = new List<BakedEntryPoint>((int)count);
        for (var i = 0u; i < count; i++)
        {
            var name = r.ReadString();
            var stage = (SlangStage)r.ReadUInt32();
            var length = r.ReadUInt32();
            entryPoints.Add(new BakedEntryPoint(name, stage, r.ReadBytes((int)length)));
        }

        return new ShaderPackage
        {
            SlangVersion = slangVersion,
            SourceHash = sourceHash,
            Layout = layout,
            EntryPoints = entryPoints,
        };
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
