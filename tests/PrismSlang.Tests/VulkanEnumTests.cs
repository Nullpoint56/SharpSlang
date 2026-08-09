// Are the mirrored Vulkan values the right numbers?
//
// PrismSlang.Vulkan copies ~48 numeric values out of the Vulkan specification so that the package
// ships standalone: taking a dependency on one Vulkan binding would push that choice onto every
// consumer, behind a mapping of a few dozen integers. The file says as much, and adds that the
// values "are fixed by the Vulkan specification and will not change, so mirroring them is safe".
//
// That argument is about drift, and it is correct about drift. It says nothing about transcription.
// "It cannot change" is not "it was typed correctly", and the failure is silent: VulkanFormat's
// 16-bit family runs 76, 83, 90, 97, and a wrong-but-valid VkFormat in a
// VkVertexInputAttributeDescription passes validation and quietly reinterprets the bytes.
//
// So the values are checked against Vortice.Vulkan, which is generated from the Khronos vk.xml
// registry. The dependency is on the test project only; PrismSlang.Vulkan still has none.

using System.Reflection;
using PrismSlang.Reflection;
using PrismSlang.Vulkan;
using Vortice.Vulkan;

namespace PrismSlang.Tests;

public class VulkanEnumTests
{
    [Fact]
    public void Every_descriptor_type_matches_Vulkan() =>
        AssertMirrors<VulkanDescriptorType, VkDescriptorType>();

    [Fact]
    public void Every_shader_stage_matches_Vulkan() =>
        AssertMirrors<VulkanShaderStages, VkShaderStageFlags>(Except: ["None"]);

    [Fact]
    public void Every_format_matches_Vulkan() =>
        AssertMirrors<VulkanFormat, VkFormat>();

    /// <summary>
    /// Every member of the mirrored enum must exist in the reference enum with the same value.
    /// </summary>
    /// <param name="Except">
    /// Members with no counterpart to check, each of which needs a reason here rather than a
    /// silent skip.
    /// </param>
    /// <remarks>
    /// <para>
    /// Matched by name, case-insensitively, because the two spell the extension suffixes
    /// differently — <c>AccelerationStructureKhr</c> against <c>AccelerationStructureKHR</c>. A
    /// member with no counterpart at all is reported rather than skipped: a name that exists in
    /// neither Vulkan nor this table is a name somebody invented.
    /// </para>
    /// <para>
    /// One direction only. The mirrors are deliberate subsets — <see cref="VulkanFormat"/> covers
    /// what a vertex input declaration can reach and nothing else — so requiring the reverse would
    /// be requiring the whole of <c>VkFormat</c>.
    /// </para>
    /// </remarks>
    private static void AssertMirrors<TMirror, TReference>(string[]? Except = null)
        where TMirror : struct, Enum
        where TReference : struct, Enum
    {
        var reference = Enum.GetNames<TReference>()
            .ToDictionary(n => n, n => Convert.ToInt64(Enum.Parse<TReference>(n)),
                StringComparer.OrdinalIgnoreCase);

        var skipped = Except ?? [];
        var wrong = new List<string>();
        var absent = new List<string>();

        foreach (var name in Enum.GetNames<TMirror>())
        {
            if (skipped.Contains(name, StringComparer.Ordinal)) continue;

            var mine = Convert.ToInt64(Enum.Parse<TMirror>(name));

            if (!reference.TryGetValue(name, out var theirs))
            {
                absent.Add($"{typeof(TMirror).Name}.{name} = {mine} — no {typeof(TReference).Name} "
                    + $"member of that name");
                continue;
            }

            if (mine != theirs)
            {
                wrong.Add($"{typeof(TMirror).Name}.{name} = {mine}, but "
                    + $"{typeof(TReference).Name}.{name} = {theirs}");
            }
        }

        Assert.True(wrong.Count == 0 && absent.Count == 0,
            $"{typeof(TMirror).Name} disagrees with Vulkan:\n"
            + string.Join("", wrong.Select(w => $"    WRONG VALUE  {w}\n"))
            + string.Join("", absent.Select(a => $"    NO SUCH NAME {a}\n")));
    }

    [Fact]
    public void The_mirrors_cover_what_the_projection_can_actually_produce()
    {
        // A value being right is not the same as it being present. ToDescriptorType maps Slang's
        // binding kinds onto VulkanDescriptorType, so every arm of that switch is a member this
        // table has to have — and it is the only place the set is decided.
        var produced = typeof(VulkanProjection)
            .GetMethod(nameof(VulkanProjection.ToDescriptorType), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(produced);
        Assert.Equal(typeof(VulkanDescriptorType), produced.ReturnType);

        // Every descriptor type Vulkan 1.0 defines, so a mapping that needs one finds it here
        // rather than in a follow-up commit.
        foreach (var core in Enum.GetValues<VkDescriptorType>().Where(v => (long)v is >= 0 and <= 10))
        {
            Assert.Contains(
                Enum.GetNames<VulkanDescriptorType>(),
                n => string.Equals(n, core.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }

    [NativeTheory]
    [InlineData("""
        struct WideIn
        {
            float3  position : POSITION;
            double4 wide     : TEXCOORD0;
            float2  after    : TEXCOORD1;
        };

        [shader("vertex")]
        float4 vertexMain(WideIn v) : SV_Position
        { return float4(v.position, 1) + (float)v.wide.x + float4(v.after, 0, 0); }
        """, "wide", "after")]
    [InlineData("""
        struct WideIn
        {
            float3   position : POSITION;
            double3x3 wide    : TEXCOORD0;
        };

        [shader("vertex")]
        float4 vertexMain(WideIn v) : SV_Position
        { return float4(v.position, (float)v.wide[0][0]); }
        """, "wide[0]", "wide[1]")]
    public void A_wide_64_bit_vertex_attribute_overlapping_the_next_one_is_refused(
        string source, string first, string second)
    {
        // The neighbour of the VkFormat bug, found by putting doubles in the corpus. Vulkan gives a
        // three- or four-component 64-bit vector two consecutive locations; Slang gives it one, and
        // decorates the module to match, so the layout and the module agree and both are wrong.
        // That makes this the one check here asserted from the specification rather than measured
        // against the bytes — see the remarks on VulkanProjection.RequireDistinctLocations.
        //
        // The second case is a matrix of them, where every row overlaps the next: the shape a
        // double-precision instance transform would take.
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", source);
        using var ep = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, ep);
        _ = program.GetEntryPointCodeBytes(0);

        var e = Assert.Throws<NotSupportedException>(() => program.Reflect().ToVulkan());
        Assert.Contains("both occupy location", e.Message);
        Assert.Contains(first, e.Message);
        Assert.Contains(second, e.Message);
    }

    [NativeFact]
    public void Narrow_64_bit_attributes_take_one_location_each_and_are_fine()
    {
        // A scalar or two-component 64-bit vector consumes a single location, so these do not
        // overlap and must not be refused. The corpus covers the same shapes against the module.
        const string Source = """
            struct NarrowIn
            {
                float3  position : POSITION;
                double  scalar   : TEXCOORD0;
                double2 pair     : TEXCOORD1;
                float2  after    : TEXCOORD2;
            };

            [shader("vertex")]
            float4 vertexMain(NarrowIn v) : SV_Position
            {
                return float4(v.position, 1) + (float)v.scalar + (float)v.pair.x
                     + float4(v.after, 0, 0);
            }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", Source);
        using var ep = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, ep);
        _ = program.GetEntryPointCodeBytes(0);

        var inputs = program.Reflect().ToVulkan().VertexInputs;

        Assert.Equal([0u, 1u, 2u, 3u], inputs.Select(i => i.Location));
        Assert.Equal(VulkanFormat.R64Sfloat, inputs[1].Format);
        Assert.Equal(VulkanFormat.R64G64Sfloat, inputs[2].Format);
    }

    [Fact]
    public void The_stage_flags_combine_the_way_Vulkan_says_they_do()
    {
        // AllGraphics is the one member that is a computed mask rather than a bit, so it is the one
        // that can be internally consistent and still wrong.
        Assert.Equal(
            (long)VkShaderStageFlags.AllGraphics,
            (long)(VulkanShaderStages.Vertex | VulkanShaderStages.TessellationControl
                 | VulkanShaderStages.TessellationEvaluation | VulkanShaderStages.Geometry
                 | VulkanShaderStages.Fragment));

        Assert.Equal((long)VkShaderStageFlags.AllGraphics, (long)VulkanShaderStages.AllGraphics);
        Assert.Equal(0, (long)VulkanShaderStages.None);
    }
}
