// What allocates after load, measured, and the part that must not.
//
// PrismSlang is an asset-pipeline library with a reader half. Compiling, reflecting, baking and
// projecting allocate by nature — a reflection snapshot is a tree of records, a projection is a
// list of descs — and all of it runs once per shader, in the cooker or at load. What an engine
// touches after that is the projected data: properties, indexers, and a handful of lookups by
// name or index. Those are the paths that have to be allocation-free, because a renderer that
// calls one per draw cannot afford the garbage, and the reader deserves to know which is which.
//
// Measured on the first version of this file: every property and indexer was already zero, and
// every by-name lookup was 80 to 120 bytes, a LINQ closure and an enumerator each. The lookups
// are loops now. This pins both halves — zero where it must be, and the order of magnitude of
// the load-time work so a regression there is at least visible.
//
// GC.GetAllocatedBytesForCurrentThread is the instrument: exact, per thread, and unaffected by
// collections. The suite runs serially (AssemblyInfo), so nothing else allocates on this thread
// mid-measurement, and a warm-up precedes every measurement so tiered compilation and the first
// call's one-time work are not counted.

using System.Runtime.CompilerServices;
using PrismSlang.NRI;
using PrismSlang.Reflection;
using PrismSlang.Vulkan;

namespace PrismSlang.Tests;

public class AllocationTests
{
    private const string Shader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D    albedo;
        SamplerState albedoSampler;
        RWStructuredBuffer<float4> outBuffer;
        [[vk::binding(0, 2)]] Texture2D far;
        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position : POSITION, float2 uv : TEXCOORD0)
        { VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv; return o; }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        { outBuffer[0] = 1; return albedo.Sample(albedoSampler, i.uv) * u.exposure + far.Load(0); }
        """;

    /// <summary>Bytes allocated per call of <paramref name="action"/>, after a warm-up.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double BytesPerCall(Action action, int iterations = 1000)
    {
        for (var i = 0; i < 50; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iterations;
    }

    private static void AssertZero(string what, Action action)
    {
        var bytes = BytesPerCall(action);
        Assert.True(bytes == 0, $"{what} allocates {bytes:N0} bytes per call; an engine may call it per draw, and it must allocate nothing.");
    }

    [NativeFact]
    public void Lookups_on_a_projection_allocate_nothing()
    {
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        var nri = layout.ToNri();
        var vk = layout.ToVulkan();
        var size = new VulkanWorkgroupSize(8, 8, 1);

        AssertZero("NriPipelineProjection.Range(name)", () => nri.Range("albedo"));
        AssertZero("NriPipelineProjection.Range(name) for a miss", () => nri.Range("nothing"));
        AssertZero("NriPipelineProjection.Set(space)", () => nri.Set(2));
        AssertZero("NriPipelineProjection.Stage(name)", () => nri.Stage("fragmentMain"));
        AssertZero("ProjectedDescriptorSet.Range(name)", () => nri.DescriptorSets[0].Range("outBuffer"));
        AssertZero("VulkanPipelineLayout.Set(index)", () => vk.Set(2));
        AssertZero("VulkanPipelineLayout.Stage(name)", () => vk.Stage("vertexMain"));
        AssertZero("VulkanWorkgroupSize.GroupCountFor", () => size.GroupCountFor(1920, 1080));
        // Read into a typed sink rather than GC.KeepAlive(object): the first version of this
        // test boxed the DescriptorRangeDesc struct into that parameter and reported 40 bytes the
        // library never allocated.
        AssertZero("indexing the projected ranges", () => Sink = nri.DescriptorSets[0].Ranges[1].Desc.descriptorNum);
        AssertZero("reading the layout's stage mask", () => Sink = (uint)nri.Layout.ShaderStages);
    }

    private static uint Sink;

    [NativeFact]
    public void Load_time_work_allocates_and_says_how_much()
    {
        // Not a ceiling anyone tuned; an order of magnitude, so a projection that starts
        // allocating a megabyte per shader is a red test rather than a slow load. Measured on the
        // first run: ToNri 5,184 bytes, ToVulkan 4,848 bytes for the shader above.
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        var options = new NriProjectionOptions();

        var toNri = BytesPerCall(() => layout.ToNri(options), iterations: 200);
        var toVulkan = BytesPerCall(() => layout.ToVulkan(), iterations: 200);

        Assert.True(toNri > 0 && toNri < 64 * 1024, $"ToNri allocated {toNri:N0} bytes per call");
        Assert.True(toVulkan > 0 && toVulkan < 64 * 1024, $"ToVulkan allocated {toVulkan:N0} bytes per call");
    }
}
