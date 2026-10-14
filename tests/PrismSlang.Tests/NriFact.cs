// Gates for tests that need NRI itself, not only its descs.
//
// NriProjectionTests needs nothing but the compiler: the projection is arithmetic over managed
// data onto PrismNRI.Descs, which has no native library. NriDeviceTests hands the result to a real
// NRI device, which needs NRI.dll to load and a backend to exist, and on a machine with neither it
// must skip with a reason that says what to do — not fail with a DllNotFoundException that reads
// like a broken build.
//
// Windows only, and that is a decision rather than a measurement: PrismNRI ships win-x64,
// linux-x64 and osx-arm64 payloads, but its Linux leg is unproven here and macOS is not a target
// for the engine yet. So the device tests target the one platform the engine runs on, state so
// in the skip reason elsewhere, and the other three CI legs keep running the projection tests
// that need no device. Widening this is one line, once a leg has been watched working.
//
// And gates for dxc, which the DXIL tests need whether or not a device is involved. Those are
// measurements, not decisions: dxc is either beside slang-compiler or it is not.

using PrismNRI.Native;
using PrismNRI.Testing;

namespace PrismSlang.Tests;

/// <summary>A fact needing NRI's NONE backend: the library loadable, nothing else.</summary>
public sealed class NriNoneFactAttribute : FactAttribute
{
    public NriNoneFactAttribute()
    {
        if (NriNative.Unavailable(GraphicsAPI.NONE) is { } reason) Skip = reason;
    }
}

/// <summary>The theory counterpart.</summary>
public sealed class NriNoneTheoryAttribute : TheoryAttribute
{
    public NriNoneTheoryAttribute()
    {
        if (NriNative.Unavailable(GraphicsAPI.NONE) is { } reason) Skip = reason;
    }
}

/// <summary>A fact needing NRI's Vulkan backend on a real or software device, with validation.</summary>
public sealed class NriVulkanFactAttribute : FactAttribute
{
    public NriVulkanFactAttribute()
    {
        if (NriNative.Unavailable(GraphicsAPI.VK) is { } reason) Skip = reason;
    }
}

/// <summary>The theory counterpart.</summary>
public sealed class NriVulkanTheoryAttribute : TheoryAttribute
{
    public NriVulkanTheoryAttribute()
    {
        if (NriNative.Unavailable(GraphicsAPI.VK) is { } reason) Skip = reason;
    }
}

/// <summary>
/// A fact needing NRI's D3D12 backend, with the debug layer, and dxc to compile the DXIL it runs.
/// </summary>
/// <remarks>
/// Both halves in one gate, because no D3D12 test here can do anything without the other: every
/// one of them compiles a corpus shader to DXIL before it creates a device, and a machine with a
/// D3D12 adapter but no dxc would otherwise fail inside CreateSession with a message about a
/// package rather than skip with it. The device half first, so a Linux leg reports the platform
/// decision rather than the absence of a Windows DLL it would never have had.
/// </remarks>
public sealed class NriD3D12FactAttribute : FactAttribute
{
    public NriD3D12FactAttribute()
    {
        if ((NriNative.Unavailable(GraphicsAPI.D3D12) ?? DxcNative.Unavailable) is { } reason) Skip = reason;
    }
}

/// <summary>The theory counterpart.</summary>
public sealed class NriD3D12TheoryAttribute : TheoryAttribute
{
    public NriD3D12TheoryAttribute()
    {
        if ((NriNative.Unavailable(GraphicsAPI.D3D12) ?? DxcNative.Unavailable) is { } reason) Skip = reason;
    }
}

/// <summary>A fact needing Slang to emit DXIL: the compiler loadable, and dxc beside it.</summary>
/// <remarks>
/// Not gated on the platform, unlike the device gates above, because there is nothing to decide:
/// PrismSlang.Native.Dxc copies dxc for win-x64 only, so on every other leg the probe below finds
/// nothing and the test skips by absence. Gating on Windows as well would only mean a Linux build
/// that somehow had dxc beside it would refuse to say so.
/// </remarks>
public sealed class DxcFactAttribute : FactAttribute
{
    public DxcFactAttribute()
    {
        if (DxcNative.Unavailable is { } reason) Skip = reason;
    }
}

/// <summary>The theory counterpart.</summary>
public sealed class DxcTheoryAttribute : TheoryAttribute
{
    public DxcTheoryAttribute()
    {
        if (DxcNative.Unavailable is { } reason) Skip = reason;
    }
}

internal static class DxcNative
{
    internal const string NotLoadable =
        "dxc is not loadable. Restore the test project, which references Microsoft.Direct3D.DXC and copies it beside the compiler on win-x64; a consumer adds PrismSlang.Native.Dxc";

    /// <summary>Why DXIL cannot be compiled here, or null if it can.</summary>
    /// <remarks>
    /// The compiler first, for the same reason as <see cref="NriNative.Unavailable"/>: without
    /// slang-compiler there is no session to ask, and its skip reason is the one a fresh clone
    /// needs. Then dxc, asked the way <c>SlangGlobalSession.RequireDxc</c> asks it — Slang's own
    /// pass-through probe, which loads dxcompiler by name from the directory Slang searches — so
    /// the gate and the refusal it stands in front of cannot disagree about what "loadable" means.
    /// </remarks>
    internal static string? Unavailable =>
        !SlangNative.Available ? SlangNative.SkipReason
        : !Loadable.Value ? NotLoadable
        : null;

    private static readonly Lazy<bool> Loadable = new(() =>
    {
        // A fresh global session, disposed at once: availability is a property of the process's
        // library search, not of any one session, and a probe that kept a session alive would
        // hold a compiler for the life of the test run for nothing.
        using var global = SlangGlobalSession.Create();
        return global.IsDownstreamCompilerAvailable(PrismSlang.Native.SlangPassThrough.SLANG_PASS_THROUGH_DXC);
    });
}

internal static class NriNative
{
    internal const string NotWindows =
        "NRI device tests target win-x64 for now: PrismNRI's Linux leg is unproven and macOS is "
        + "not a target. The projection tests run everywhere.";

    internal const string NotLoadable =
        "NRI is not loadable. Run: pwsh tools/fetch-nri.ps1, then restore; the PrismNRI package "
        + "carries NRI.dll for win-x64.";

    /// <summary>Why a test on this backend cannot run here, or null if it can.</summary>
    /// <remarks>
    /// The compiler first, because every device test compiles a corpus shader before it talks to
    /// a device, and the Slang skip reason is the one a fresh clone needs to see. Then the
    /// platform, then the library, then the backend — each probe only meaningful once the
    /// previous one passed, and each with its own reason.
    /// </remarks>
    internal static string? Unavailable(GraphicsAPI api)
    {
        if (!SlangNative.Available) return SlangNative.SkipReason;
        if (!OperatingSystem.IsWindows()) return NotWindows;
        if (!Loadable.Value) return NotLoadable;
        return NriTestDevice.Unavailable(api);
    }

    private static readonly Lazy<bool> Loadable = new(() =>
    {
        try
        {
            // Cheapest call that proves NRI.dll resolved: a format-table lookup, no device.
            return !string.IsNullOrEmpty(PrismNRI.Nri.GetFormatName(Format.R8_UNORM));
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    });
}
