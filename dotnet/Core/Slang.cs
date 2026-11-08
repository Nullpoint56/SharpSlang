// L2 — the versions: what a caller compiled against, and what is loaded.
//
// Named as SharpNRI's Nri class names them (AGENTS.md#pin-and-payload). The constant is compiled
// into the caller, so it names the SharpSlang the caller was built against; the properties are
// read from the loaded assemblies at run time. SlangGlobalSession.Create refuses a loaded
// SharpSlang.Descs whose Slang release or package version is not the binding's, and a native
// library whose build tag is not the binding's Slang release.

using SharpSlang.Interop;

namespace SharpSlang;

/// <summary>The versions SharpSlang was built against and the versions loaded in this process.</summary>
public static class Slang
{
    /// <summary>
    /// The Slang release this SharpSlang was built for (<c>2026.21</c>), compiled in from the
    /// SharpSlang.Descs it was built against (<see cref="SlangHeaders.Version"/>). A constant, so it
    /// is compiled into the caller too and names the SharpSlang the caller was built against; the
    /// loaded one's is <see cref="BindingSlangVersion"/>, the loaded native library's
    /// <see cref="SlangGlobalSession.BuildTag"/>.
    /// </summary>
    public const string SlangVersion = SlangHeaders.Version;

    /// <summary>The package version of the loaded SharpSlang (<c>2026210.0.0-beta.1</c>), read at run time.</summary>
    public static string BindingVersion => SlangVersionCheck.Binding;

    /// <summary>The Slang release the loaded SharpSlang was built for, read at run time (<see cref="SlangVersion"/> is compiled into the caller).</summary>
    public static string BindingSlangVersion => SlangVersionCheck.Built;

    /// <summary>
    /// The package version of the loaded SharpSlang.Descs, read at run time;
    /// <see cref="SlangGlobalSession.Create"/> refuses one other than the version SharpSlang
    /// depends on.
    /// </summary>
    public static string DescsVersion => SlangVersionCheck.DescsPackage;

    /// <summary>The Slang release the loaded SharpSlang.Descs was generated from, read at run time (<see cref="SlangHeaders.Version"/> is compiled into the caller).</summary>
    public static string DescsSlangVersion => SlangVersionCheck.Descs;
}
