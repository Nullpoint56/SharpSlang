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
