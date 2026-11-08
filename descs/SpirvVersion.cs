// A managed desc form: the SPIR-V version a target emits, as Slang's spirv_1_x profiles name it.
//
// Here rather than in the binding or the reader because both need it and neither may reference the
// other (AGENTS.md#layers): SharpSlang's TargetOptions takes one and checks the emitted header
// against it, and SharpSlang.Pipeline's BakedEntryPoint records the one the module carries. The
// numeric values are the SPIR-V header's version word, and they are written into every baked
// package, so a renamed or renumbered member changes the file format (AGENTS.md#lint names the
// CA1707 exemption for that reason).

namespace SharpSlang.Spirv;

/// <summary>
/// A SPIR-V version, encoded exactly as the module header's second word encodes it.
/// </summary>
/// <remarks>
/// The numeric values are the header word, so comparing two of these compares the versions and no
/// conversion is needed on the way in or out.
/// </remarks>
public enum SpirvVersion
{
    /// <summary>No version pinned. Slang picks; measured to be 1.5 in slang-compiler 2026.21.</summary>
    Unspecified = 0,

    V1_0 = 0x0001_0000,
    V1_1 = 0x0001_0100,
    V1_2 = 0x0001_0200,
    V1_3 = 0x0001_0300,
    V1_4 = 0x0001_0400,
    V1_5 = 0x0001_0500,
    V1_6 = 0x0001_0600,
}

/// <summary>Helpers over <see cref="SpirvVersion"/>.</summary>
public static class SpirvVersions
{
    /// <summary>Major component, e.g. 1.</summary>
    public static int Major(this SpirvVersion v) => ((int)v >> 16) & 0xFF;

    /// <summary>Minor component, e.g. 5.</summary>
    public static int Minor(this SpirvVersion v) => ((int)v >> 8) & 0xFF;

    /// <summary>Renders as <c>1.5</c>.</summary>
    public static string ToDisplayString(this SpirvVersion v) =>
        v == SpirvVersion.Unspecified
            ? "(unspecified)"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v.Major()}.{v.Minor()}");

    /// <summary>
    /// The Slang target profile that pins this version, e.g. <c>spirv_1_5</c>.
    /// </summary>
    /// <remarks>
    /// Measured against slang-compiler 2026.21: the profile is the only knob that moves the
    /// emitted header. Passing <c>-capability spirv_1_6</c> at link time is silently ignored and
    /// still emits 1.5 (AGENTS.md#compilation-linking-and-diagnostics).
    /// </remarks>
    public static string? ToProfileName(this SpirvVersion v) =>
        v == SpirvVersion.Unspecified
            ? null
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"spirv_{v.Major()}_{v.Minor()}");

    /// <summary>Parses a <c>spirv_1_5</c>-style profile name back to a version, or Unspecified.</summary>
    public static SpirvVersion FromProfileName(string? profile)
    {
        if (profile is null) return SpirvVersion.Unspecified;

        const string prefix = "spirv_";
        if (!profile.StartsWith(prefix, StringComparison.Ordinal)) return SpirvVersion.Unspecified;

        var parts = profile[prefix.Length..].Split('_');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var minor))
        {
            return SpirvVersion.Unspecified;
        }

        var encoded = (major << 16) | (minor << 8);
        return Enum.IsDefined(typeof(SpirvVersion), encoded) ? (SpirvVersion)encoded : SpirvVersion.Unspecified;
    }
}
