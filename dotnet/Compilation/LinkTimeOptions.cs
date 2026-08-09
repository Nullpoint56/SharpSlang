// L2 — per-link compiler options, via IComponentType::linkWithOptions.
//
// Distinct from SessionOptions: those configure a session and apply to everything compiled in it,
// whereas these apply to one link. That matters for a shader cache — a debug and a release variant
// of the same shader can share a session and differ only here, rather than needing two sessions.
//
// The native shape is an array of CompilerOptionEntry { name, value }, where the value is a small
// tagged union. CompilerOptionName has 159 members covering the entire slangc command line; most
// are meaningless at link time. The typed properties below cover the ones a renderer actually
// varies per link, and RawEntries is the escape hatch for the rest.

using PrismSlang.Native;

namespace PrismSlang;

/// <summary>A compiler option not covered by <see cref="LinkTimeOptions"/>'s typed properties.</summary>
/// <param name="Name">The option to set.</param>
/// <param name="IntValue">Value for an integer-valued option.</param>
/// <param name="StringValue">Value for a string-valued option. Non-null selects the string form.</param>
public readonly record struct RawCompilerOption(
    CompilerOptionName Name,
    int IntValue = 0,
    string? StringValue = null);

/// <summary>Options applied to a single link.</summary>
/// <remarks>
/// Only options measured to actually change the generated code get a typed property. Slang accepts
/// the full <c>CompilerOptionName</c> range here without complaint and ignores most of it, so a
/// typed property for an option that does nothing would read as a supported feature and silently
/// not work. Measured against slang-compiler 2026.14.1 by diffing emitted SPIR-V:
///
/// <list type="bullet">
/// <item><c>Optimization</c> — works. None 2,052 bytes / default 1,868 / Maximal 1,668 on the
/// same shader.</item>
/// <item><c>FloatingPointMode</c> — works. Precise adds 36 bytes; Fast matches the default,
/// because Fast <em>is</em> the default.</item>
/// <item><c>DebugInformation</c> — <b>no effect at link time.</b> It is a target/session-level
/// option; set it on the session instead.</item>
/// <item><c>VulkanUseEntryPointName</c> — <b>no effect,</b> and unnecessary: Slang already emits
/// the entry point's own name by default in this version.</item>
/// </list>
///
/// The last two are reachable through <see cref="RawEntries"/> if a future Slang honours them.
/// </remarks>
public sealed class LinkTimeOptions
{
    /// <summary>Optimisation level for the generated code. Null leaves the session default.</summary>
    public SlangOptimizationLevel? Optimization { get; init; }

    /// <summary>Floating-point strictness. Null leaves the session default.</summary>
    public SlangFloatingPointMode? FloatingPointMode { get; init; }

    /// <summary>
    /// Any other compiler option, passed through verbatim. Note that Slang silently ignores
    /// options that are not meaningful at link time — verify the effect rather than assuming it.
    /// </summary>
    public IReadOnlyList<RawCompilerOption> RawEntries { get; init; } = [];

    /// <summary>True when nothing is set, so the caller can take the plain link path.</summary>
    internal bool IsEmpty =>
        Optimization is null && FloatingPointMode is null && RawEntries.Count == 0;

    /// <summary>Flattens to the native option list, in a stable order.</summary>
    internal List<RawCompilerOption> ToEntries()
    {
        var entries = new List<RawCompilerOption>();

        if (Optimization is { } opt)
            entries.Add(new RawCompilerOption(CompilerOptionName.Optimization, (int)opt));

        if (FloatingPointMode is { } fp)
            entries.Add(new RawCompilerOption(CompilerOptionName.FloatingPointMode, (int)fp));

        entries.AddRange(RawEntries);
        return entries;
    }
}
