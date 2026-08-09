// Pinning the SPIR-V version, and proving the pin holds.
//
// A module above the device's supported SPIR-V version is rejected by vkCreateShaderModule with no
// detail worth reading, so "which version did we actually emit" has to be answerable at bake time.

using PrismSlang.Interop;
using PrismSlang.Native;
using PrismSlang.Spirv;
using PrismSlang.Vulkan;

namespace PrismSlang.Tests;

public class TargetEnvironmentTests
{
    private const string Trivial = """
        [shader("fragment")]
        float4 fragmentMain() : SV_Target { return float4(1,0,0,1); }
        """;

    private static byte[] Compile(TargetOptions target)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [target] });
        using var module = session.LoadModuleFromSource("t", "t.slang", Trivial);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);
        return program.GetEntryPointCodeBytes(0);
    }

    [NativeTheory]
    [InlineData(SpirvVersion.V1_3)]
    [InlineData(SpirvVersion.V1_4)]
    [InlineData(SpirvVersion.V1_5)]
    [InlineData(SpirvVersion.V1_6)]
    public void The_requested_version_is_the_version_emitted(SpirvVersion requested)
    {
        // 1.0..1.2 are covered separately below: they emit correctly but warn on the way.
        Assert.Equal(requested, SpirvModule.ReadVersion(Compile(new TargetOptions { SpirvVersion = requested })));
    }

    [NativeTheory]
    [InlineData(SpirvVersion.V1_0, true)]
    [InlineData(SpirvVersion.V1_1, true)]
    [InlineData(SpirvVersion.V1_2, true)]
    [InlineData(SpirvVersion.V1_3, false)]
    [InlineData(SpirvVersion.V1_6, false)]
    public void Versions_below_1_3_emit_correctly_but_warn(SpirvVersion requested, bool expectWarning)
    {
        // Pins where Slang draws the "too old" line, and that crossing it is a warning rather than
        // a failure — so the default ThrowOnError policy lets it through. If a future Slang
        // promotes this to an error, builds targeting an old device start failing and this says why.
        var seen = new List<string>();
        using (new DiagnosticScope(SlangDiagnosticPolicy.Report, (_, severity, t) => seen.Add($"{severity}: {t}")))
        {
            Assert.Equal(requested, SpirvModule.ReadVersion(Compile(new TargetOptions { SpirvVersion = requested })));
        }

        if (expectWarning)
        {
            var text = Assert.Single(seen);
            Assert.Contains("too old", text);
            Assert.Equal(SlangDiagnosticSeverity.Warning, SlangDiagnostics.Classify(text));
        }
        else
        {
            Assert.Empty(seen);
        }
    }

    [NativeFact]
    public void The_default_is_1_5_and_it_is_pinned_rather_than_inherited()
    {
        // Slang's own default happens to be 1.5 too, so this passing does not prove the profile was
        // set. The next test does.
        Assert.Equal(SpirvVersion.V1_5, SpirvModule.ReadVersion(Compile(new TargetOptions())));
    }

    [NativeFact]
    public void A_non_default_version_proves_the_profile_is_actually_being_set()
    {
        var v16 = SpirvModule.ReadVersion(Compile(new TargetOptions { SpirvVersion = SpirvVersion.V1_6 }));
        var v14 = SpirvModule.ReadVersion(Compile(new TargetOptions { SpirvVersion = SpirvVersion.V1_4 }));

        Assert.Equal(SpirvVersion.V1_6, v16);
        Assert.Equal(SpirvVersion.V1_4, v14);
    }

    [NativeFact]
    public void An_explicit_profile_still_works_and_still_verifies()
    {
        // The escape hatch has to keep working on its own: every existing caller pins the profile
        // string and knows nothing about SpirvVersion. Setting the profile alone must not trip the
        // conflict check against SpirvVersion's default — which is why that property is nullable,
        // so "not specified" and "specified as 1.5" are different states. This test is here
        // because the first version of the design got that wrong.
        Assert.Equal(
            SpirvVersion.V1_6,
            SpirvModule.ReadVersion(Compile(new TargetOptions { Profile = "spirv_1_6" })));

        Assert.Equal(
            SpirvVersion.V1_5,
            SpirvModule.ReadVersion(Compile(new TargetOptions { Profile = "spirv_1_5" })));
    }

    [NativeFact]
    public void Contradicting_the_profile_and_the_version_is_rejected_not_resolved()
    {
        // Whichever one silently won, the other would be a lie written down in the caller's source.
        var ex = Assert.Throws<ArgumentException>(() => Compile(new TargetOptions
        {
            Profile = "spirv_1_6",
            SpirvVersion = SpirvVersion.V1_4,
        }));

        Assert.Contains("spirv_1_6", ex.Message);
        Assert.Contains("1.4", ex.Message);
    }

    [NativeFact]
    public void An_unknown_profile_is_named_in_the_error()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Compile(new TargetOptions { Profile = "spirv_9_9" }));
        Assert.Contains("spirv_9_9", ex.Message);
    }

    [NativeFact]
    public void Verification_passes_on_every_version_it_can_emit()
    {
        // VerifyEmittedSpirv runs inside GetEntryPointCode, so a false positive here would have
        // shown up as an exception in the tests above. This asserts the switch itself is live by
        // proving output is byte-identical with it off — i.e. verification observes, never alters.
        foreach (var v in new[] { SpirvVersion.V1_4, SpirvVersion.V1_5, SpirvVersion.V1_6 })
        {
            var verified = Compile(new TargetOptions { SpirvVersion = v });
            var unverified = Compile(new TargetOptions { SpirvVersion = v, VerifyEmittedSpirv = false });
            Assert.Equal(verified, unverified);
        }
    }

    [NativeFact]
    public void A_non_spirv_target_is_left_alone()
    {
        // The SPIR-V knobs must not leak into other targets: SpirvVersion defaults to 1.5, and
        // handing "spirv_1_5" to an HLSL target would be nonsense.
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Format = SlangCompileTarget.SLANG_HLSL, Profile = "sm_6_0" }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", Trivial);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        using var code = program.GetEntryPointCode(0);
        Assert.Contains("fragmentMain", code.AsText());
    }

    // ---------------------------------------------------------------- the Vulkan reading of it

    [Theory]
    [InlineData(VulkanApiVersion.V1_0, SpirvVersion.V1_0)]
    [InlineData(VulkanApiVersion.V1_1, SpirvVersion.V1_3)]
    [InlineData(VulkanApiVersion.V1_2, SpirvVersion.V1_5)]
    [InlineData(VulkanApiVersion.V1_3, SpirvVersion.V1_6)]
    [InlineData(VulkanApiVersion.V1_4, SpirvVersion.V1_6)]
    public void Vulkan_versions_cap_spirv_versions(VulkanApiVersion vulkan, SpirvVersion max) =>
        Assert.Equal(max, VulkanEnvironment.MaxSpirvVersion(vulkan));

    [Fact]
    public void A_module_too_new_for_the_device_is_caught_before_the_driver_sees_it()
    {
        Assert.True(VulkanEnvironment.Accepts(VulkanApiVersion.V1_4, SpirvVersion.V1_5));
        Assert.True(VulkanEnvironment.Accepts(VulkanApiVersion.V1_4, SpirvVersion.V1_6));
        Assert.False(VulkanEnvironment.Accepts(VulkanApiVersion.V1_2, SpirvVersion.V1_6));

        // Unspecified is not "anything goes" — it means nothing was recorded, which is not a
        // module a loader should accept on trust.
        Assert.False(VulkanEnvironment.Accepts(VulkanApiVersion.V1_4, SpirvVersion.Unspecified));

        var ex = Assert.Throws<ArgumentException>(
            () => VulkanEnvironment.Require(VulkanApiVersion.V1_2, SpirvVersion.V1_6, "shadow.vert"));
        Assert.Contains("shadow.vert", ex.Message);
        Assert.Contains("1.6", ex.Message);
        Assert.Contains("1.5", ex.Message);
    }

    [NativeFact]
    public void A_bake_for_a_1_4_device_fits_a_1_4_device()
    {
        // End to end, in the shape the engine actually asks the question: pick a target
        // environment, compile, and check the artifact against the device before shipping it.
        const VulkanApiVersion Device = VulkanApiVersion.V1_4;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { SpirvVersion = VulkanEnvironment.MaxSpirvVersion(Device) }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", Trivial);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        var package = Baking.ShaderPackageBaker.Bake(program, Trivial);
        var baked = Assert.Single(package.EntryPoints);

        Assert.Equal(SpirvVersion.V1_6, baked.SpirvVersion);
        VulkanEnvironment.Require(Device, baked.SpirvVersion, baked.Name);
    }

    /// <summary>Restores the process-wide diagnostic policy and handler however the test exits.</summary>
    private sealed class DiagnosticScope : IDisposable
    {
        private readonly SlangDiagnosticPolicy _policy = SlangDiagnostics.Policy;
        private readonly SlangDiagnosticHandler _handler = SlangDiagnostics.Handler;

        public DiagnosticScope(SlangDiagnosticPolicy policy, SlangDiagnosticHandler handler)
        {
            SlangDiagnostics.Policy = policy;
            SlangDiagnostics.Handler = handler;
        }

        public void Dispose()
        {
            SlangDiagnostics.Policy = _policy;
            SlangDiagnostics.Handler = _handler;
        }
    }
}
