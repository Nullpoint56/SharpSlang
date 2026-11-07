// Gates for tests that need NRI itself, not only its descs.
//
// NriProjectionTests needs nothing but the compiler: the projection is arithmetic over managed
// data onto PrismNRI.Descs, which has no native library. NriDeviceTests hands the result to a real
// NRI device, which needs NRI's library (NRI.dll, libNRI.so) to load and a backend to exist, and
// on a machine with neither it must skip with a reason that says what to do — not fail with a
// DllNotFoundException that reads like a broken build.
//
// win-x64 and linux-x64, and every other platform skips with its own reason. PrismNRI ships
// win-x64, linux-x64 and osx-arm64 payloads. linux-x64 was let in after it was watched working:
// under WSL (Ubuntu 24.04, lavapipe from Mesa 25.2.8) the NONE and Vulkan tests ran and the
// dispatches read back what the shaders wrote. The Vulkan half needs a Khronos layer as new as
// the Vulkan headers NRI is built with: an older one, such as the Ubuntu package the linux-x64
// runner installs, reports NRI's device creation itself as an error, and the Vulkan gate skips
// with that measurement rather than let every test fail on it (VulkanCreationRejected).
// linux-arm64 is a measurement too: the package has no payload for it, so there is no library to
// load. macOS is a decision: the payload is there, but macOS is not an engine target yet. Both
// keep running the projection tests that need no device. On Linux the D3D12 gate skips by
// absence: NriTestDevice reports "D3D12 is Windows only".
//
// And gates for dxc, which the DXIL tests need whether or not a device is involved. Those are
// measurements, not decisions: dxc is either beside slang-compiler or it is not.

using System.Text.RegularExpressions;
using PrismNRI.Native;
using PrismNRI.Testing;
using Architecture = System.Runtime.InteropServices.Architecture; // not NRI's GPU architecture enum
using RuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;

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
/// package rather than skip with it. The device half first, so linux-x64 reports that D3D12 does
/// not exist there ("D3D12 is Windows only", NriTestDevice's own reason) rather than the absence
/// of a dxc it would never have had, and linux-arm64 and macOS report their platform reasons.
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
    internal const string NotOnMacOS =
        "NRI device tests do not run on macOS, by decision: PrismNRI ships an osx-arm64 payload, "
        + "but macOS is not an engine target yet. The projection tests run everywhere.";

    internal const string NoLinuxArm64Payload =
        "NRI device tests do not run on linux-arm64: the PrismNRI package carries no linux-arm64 "
        + "native library, so there is no libNRI.so to load. The projection tests run everywhere.";

    internal const string NotLoadable =
        "NRI is not loadable. Run: pwsh tools/fetch-nri.ps1, then restore; the PrismNRI package "
        + "carries NRI.dll for win-x64 and libNRI.so for linux-x64.";

    /// <summary>Why a test on this backend cannot run here, or null if it can.</summary>
    /// <remarks>
    /// The compiler first, because every device test compiles a corpus shader before it talks to
    /// a device, and the Slang skip reason is the one a fresh clone needs to see. Then the
    /// platform, then the library, then the backend — each probe only meaningful once the
    /// previous one passed, and each with its own reason — and on Vulkan, last, whether the
    /// validation layer accepts NRI's own device creation.
    /// </remarks>
    internal static string? Unavailable(GraphicsAPI api)
    {
        if (!SlangNative.Available) return SlangNative.SkipReason;
        if (Unsupported is { } platform) return platform;
        if (!Loadable.Value) return NotLoadable;
        if (NriTestDevice.Unavailable(api) is { } backend) return backend;
        return api == GraphicsAPI.VK ? VulkanCreationRejected.Value : null;
    }

    /// <summary>
    /// Why the Khronos layer rejects a bare NRI Vulkan device, or null if it accepts one.
    /// </summary>
    /// <remarks>
    /// By trying, as VulkanTestDevice probes its layer: one validated device, created and
    /// disposed, and any ERROR it logged. Nothing of PrismSlang's is involved yet, so an error
    /// here is about the layer and NRI, never the projection — and without this every Vulkan
    /// test fails on it, each before it has asked its question. A layer older than the Vulkan
    /// headers NRI is built with does this, because NRI chains every structure and extension the
    /// driver offers into vkCreateDevice and the layer reports each one it does not know.
    ///
    /// Measured under WSL on lavapipe (Mesa 25.2.8) with PrismNRI 0.3.0-alpha: Ubuntu 24.04's
    /// layer, 1.3.275, logs VUID-VkDeviceCreateInfo-pNext-pNext and
    /// VUID-VkPhysicalDeviceProperties2-pNext-pNext for structures it predates; LunarG's apt
    /// layer, 1.4.313, logs the unknown VkStructureType 1000620000 (that of
    /// VK_EXT_zero_initialize_device_memory, which it predates);
    /// the SDK's 1.4.363 logs nothing, and the tests run. So this skips on the linux-x64 runner,
    /// which installs Ubuntu's layer, and the reason quotes the header version the layer itself
    /// says it validates against. Only device creation is looked at: an error a test provokes
    /// afterwards still fails that test.
    ///
    /// And only that one cause. Every error measured from an old layer names a structure it does
    /// not know ("unknown VkStructureType"), so the skip requires every creation error to say so.
    /// Any other error at device creation is a real one, NRI's or the driver's, and returning null
    /// lets the Vulkan tests run and fail on it with the message in front of whoever runs them. A
    /// skip that fired on any error would turn exactly that regression into a quiet skipped row on
    /// a workstation, where nothing counts skips.
    /// </remarks>
    private static readonly Lazy<string?> VulkanCreationRejected = new(() =>
    {
        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        var errors = fixture.Errors.Select(e => (e.ToString() ?? "").ReplaceLineEndings(" ")).ToList();
        if (errors.Count == 0) return null;
        if (!errors.All(e => e.Contains("unknown VkStructureType", StringComparison.Ordinal))) return null;

        var text = errors[0];
        var header = Regex.Match(text, @"version \d+ of the Vulkan header");
        return "the Khronos validation layer rejects NRI's own Vulkan device creation, before any "
             + "projection is involved"
             + (header.Success ? $" (the layer validates against {header.Value})" : "")
             + ", so every Vulkan test would fail on it. It is older than the Vulkan headers NRI "
             + "is built with: 1.3.275 (Ubuntu 24.04) and 1.4.313 (LunarG's apt) do this, the "
             + "Vulkan SDK's 1.4.363 does not. The first error: "
             + (text.Length > 400 ? text[..400] + "…" : text);
    });

    /// <summary>Why this platform runs no device test, or null for win-x64 and linux-x64.</summary>
    /// <remarks>
    /// The process architecture rather than the OS's: it is the process that loads the library,
    /// and an x64 process under emulation on an ARM64 machine loads the x64 payload.
    /// </remarks>
    private static string? Unsupported
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            if (OperatingSystem.IsMacOS()) return NotOnMacOS;
            if (arch == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return null;
            if (OperatingSystem.IsLinux() && arch == Architecture.Arm64) return NoLinuxArm64Payload;
            return $"NRI device tests run on win-x64 and linux-x64; this process is "
                 + $"{RuntimeInformation.RuntimeIdentifier}, for which the PrismNRI package carries "
                 + "no native library. The projection tests run everywhere.";
        }
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
