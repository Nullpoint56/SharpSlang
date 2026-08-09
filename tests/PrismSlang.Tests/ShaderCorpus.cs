// The corpus the agreement and coverage tests both run over.
//
// Twelve defects were found by hand-picking a shader, hand-picking a fact, and comparing it with
// the module. That works and it does not scale: it finds what someone thought to ask. These shaders
// exist so two tests can ask everything, of everything —
//
//   ModuleAgreementTests   every fact the module states must appear in the layout, and vice versa
//   SurfaceCoverageTests   every value of Slang's enums must be exercised here or declared not to be
//
// Adding a shader here strengthens both. Names are unique across the whole corpus on purpose: the
// agreement diff keys on them, and a collision would make two different things look like one.

namespace PrismSlang.Tests;

/// <summary>One shader in the corpus.</summary>
/// <param name="Name">Identifies the case in test output.</param>
/// <param name="Source">Slang source.</param>
/// <param name="EntryPoints">Entry points to link and compile, each producing its own module.</param>
/// <param name="KnownGaps">
/// Facts the module states that the layout is known not to report, or the reverse — each with the
/// reason. See <see cref="ShaderCase.Gap"/>; the agreement test requires the difference to be
/// <em>exactly</em> this set, so a gap can neither grow nor be quietly fixed.
/// </param>
public sealed record ShaderCase(
    string Name,
    string Source,
    IReadOnlyList<string> EntryPoints,
    IReadOnlyList<ShaderCase.Gap>? KnownGaps = null)
{
    /// <summary>A difference between module and layout that is understood and accepted, for now.</summary>
    /// <param name="Fact">
    /// The fact as the agreement test renders it, e.g. <c>descriptor globalParams</c>.
    /// </param>
    /// <param name="Reason">Why it differs, and what it would take to close.</param>
    public sealed record Gap(string Fact, string Reason);

    public override string ToString() => Name;
}

/// <summary>Every shader the agreement and coverage tests run over.</summary>
public static class ShaderCorpus
{
    /// <summary>The corpus, in no particular order.</summary>
    public static IReadOnlyList<ShaderCase> All { get; } =
    [
        // ------------------------------------------------------------ descriptors
        new("descriptor-kinds", """
            struct InstanceRow { float3 rowPos; float rowScale; };

            Texture2D                       dkSampled;
            Texture2DArray                  dkSampledArray;
            TextureCube                     dkCube;
            Texture3D                       dkVolume;
            Texture2DMS<float4>             dkMultisampled;
            [format("rgba8")]
            RWTexture2D<float4>             dkStorageFormatted;
            RWTexture3D<float4>             dkStoragePlain;
            SamplerState                    dkSampler;
            SamplerComparisonState          dkCompareSampler;
            Sampler2D                       dkCombined;
            StructuredBuffer<InstanceRow>   dkStructured;
            RWStructuredBuffer<float>       dkStructuredRW;
            ByteAddressBuffer               dkRaw;
            RWByteAddressBuffer             dkRawRW;
            Buffer<float4>                  dkTyped;
            RWBuffer<float4>                dkTypedRW;
            RaytracingAccelerationStructure dkTlas;

            [shader("fragment")]
            float4 dkFragment(float2 uv : TEXCOORD0) : SV_Target
            {
                // Everything declared is referenced. An unused global is stripped from the module
                // while reflection still reports it, which reads as a disagreement without being
                // one.
                RayDesc ray;
                ray.Origin = 0; ray.Direction = float3(0, 0, 1); ray.TMin = 0; ray.TMax = 1;
                RayQuery<RAY_FLAG_NONE> q;
                q.TraceRayInline(dkTlas, RAY_FLAG_NONE, 0xff, ray);

                dkStorageFormatted[uint2(0, 0)] = 1;
                dkStoragePlain[uint3(0, 0, 0)] = 1;
                dkStructuredRW[0] = dkStructured[0].rowScale + asfloat(dkRaw.Load(0));
                dkRawRW.Store(0, 1);
                dkTypedRW[0] = dkTyped[0];
                return dkSampled.Sample(dkSampler, uv)
                     + dkSampledArray.Sample(dkSampler, float3(uv, 0))
                     + dkCube.Sample(dkSampler, float3(uv, 0))
                     + dkVolume.Sample(dkSampler, float3(uv, 0))
                     + dkMultisampled.Load(int2(0, 0), 0)
                     + dkCombined.Sample(uv)
                     + dkSampled.SampleCmp(dkCompareSampler, uv, 0.5);
            }
            """, ["dkFragment"]),

        new("descriptor-arrays", """
            Texture2D    daFixed[4];
            Texture2D    daUnbounded[];
            SamplerState daSampler;

            [shader("fragment")]
            float4 daFragment(uint i : INDEX) : SV_Target
            {
                return daFixed[2].Sample(daSampler, 0) + daUnbounded[i].Sample(daSampler, 0);
            }
            """, ["daFragment"]),

        new("subpass-inputs", """
            [[vk::input_attachment_index(0)]] SubpassInput siGbufferA;
            [[vk::input_attachment_index(1)]] SubpassInput siGbufferB;
            Texture2D siOrdinary;

            [shader("fragment")]
            float4 siFragment() : SV_Target
            {
                return siGbufferA.SubpassLoad() + siGbufferB.SubpassLoad() + siOrdinary.Load(0);
            }
            """, ["siFragment"]),

        // ------------------------------------------------------------ spaces
        new("parameter-blocks", """
            struct PbLeaf  { Texture2D pbLeafTex; };
            struct PbMid   { float2 pbMidPad; ParameterBlock<PbLeaf> pbLeaf; };
            struct PbRoot  { float4 pbRootTint; PbMid pbMid; Texture2D pbRootTex; ParameterBlock<PbLeaf> pbDirect; };

            ParameterBlock<PbRoot> pbBlock;
            Texture2D pbGlobal;

            [shader("fragment")]
            float4 pbFragment() : SV_Target
            {
                return pbBlock.pbRootTint + pbBlock.pbRootTex.Load(0) + pbGlobal.Load(0)
                     + pbBlock.pbMid.pbLeaf.pbLeafTex.Load(0) + pbBlock.pbDirect.pbLeafTex.Load(0)
                     + pbBlock.pbMid.pbMidPad.x;
            }
            """, ["pbFragment"]),

        new("explicit-spaces", """
            [[vk::binding(3, 2)]] Texture2D    esFar;
            [[vk::binding(0, 2)]] SamplerState esFarSampler;
                                  Texture2D    esNear;

            [shader("fragment")]
            float4 esFragment() : SV_Target
            {
                return esFar.Sample(esFarSampler, 0) + esNear.Load(0);
            }
            """, ["esFragment"]),

        // ------------------------------------------------------------ uniform memory
        new("uniform-layout", """
            struct UlMaterial
            {
                float3   ulTint;
                float    ulGloss;
                float4x4 ulTransform;
                float3x3 ulNormalMatrix;
                float2x4 ulWide;
                float4x2 ulTall;
                float2   ulScales[3];
                float    ulCutoff;
            };
            ConstantBuffer<UlMaterial> ulBuffer;

            [shader("fragment")]
            float4 ulFragment() : SV_Target
            {
                float3 c = ulBuffer.ulTint * ulBuffer.ulGloss * ulBuffer.ulCutoff;
                c += mul(ulBuffer.ulNormalMatrix, c);
                c.xy += ulBuffer.ulScales[1];
                c.xy += mul(ulBuffer.ulTall, float2(1, 1)).xy;
                c += mul(ulBuffer.ulWide, float4(1)).xyy;
                return mul(ulBuffer.ulTransform, float4(c, 1));
            }
            """, ["ulFragment"]),

        new("push-constants", """
            struct PcBlock { float4 pcTint; float pcExposure; };
            [[vk::push_constant]] ConstantBuffer<PcBlock> pcPush;
            Texture2D pcTexture;

            [shader("fragment")]
            float4 pcFragment() : SV_Target { return pcPush.pcTint * pcPush.pcExposure + pcTexture.Load(0); }
            """, ["pcFragment"]),

        // ------------------------------------------------------------ specialization
        new("specialization-constants", """
            [vk::constant_id(0)] const int   scCascades = 3;
            [vk::constant_id(9)] const float scBias     = 0.005;
            [SpecializationConstant]  const bool scDebug = false;

            [shader("fragment")]
            float4 scFragment() : SV_Target
            {
                return float4(scCascades, scBias, scDebug ? 1 : 0, 1);
            }
            """, ["scFragment"]),

        // ------------------------------------------------------------ raster interface
        new("vertex-interface", """
            struct ViIn
            {
                float3 viPosition  : POSITION;
                float4x4 viInstance : INSTANCE;
                float2 viTexCoord  : TEXCOORD0;
                uint4  viBones     : BLENDINDICES;
                uint   viVertexId  : SV_VertexID;
            };
            struct ViOut { float4 viClip : SV_Position; float2 viUv : TEXCOORD0; };

            [shader("vertex")]
            ViOut viVertex(ViIn v)
            {
                ViOut o;
                o.viClip = mul(v.viInstance, float4(v.viPosition, 1)) + v.viBones.x + v.viVertexId;
                o.viUv = v.viTexCoord;
                return o;
            }
            """, ["viVertex"]),

        new("fragment-outputs", """
            struct FoGBuffer
            {
                float4 foAlbedo   : SV_Target0;
                float4 foNormal   : SV_Target1;
                float2 foVelocity : SV_Target2;
                float  foDepth    : SV_Depth;
            };

            [shader("fragment")]
            FoGBuffer foFragment()
            {
                FoGBuffer g;
                g.foAlbedo = 1; g.foNormal = 0; g.foVelocity = 0; g.foDepth = 0.5;
                return g;
            }
            """, ["foFragment"]),

        // ------------------------------------------------------------ compute
        new("compute-fixed", """
            RWStructuredBuffer<float> cfOutput;

            [shader("compute")]
            [numthreads(8, 4, 2)]
            void cfCompute(uint3 tid : SV_DispatchThreadID) { cfOutput[tid.x] = 1; }
            """, ["cfCompute"]),

        new("compute-specialized", """
            [vk::constant_id(0)] const uint csGroupX = 64;
            RWStructuredBuffer<float> csOutput;

            [shader("compute")]
            [numthreads(csGroupX, 1, 1)]
            void csCompute(uint3 tid : SV_DispatchThreadID) { csOutput[tid.x] = 1; }
            """, ["csCompute"]),

        // ------------------------------------------------------------ the other stages
        new("tessellation", """
            struct TeControl { float3 tePosition : POSITION; };
            struct TePatch   { float teEdges[3] : SV_TessFactor; float teInner : SV_InsideTessFactor; };
            struct TeOut     { float4 teClip : SV_Position; };

            TePatch teConstants(InputPatch<TeControl, 3> patch)
            {
                TePatch c;
                c.teEdges[0] = c.teEdges[1] = c.teEdges[2] = 1;
                c.teInner = 1;
                return c;
            }

            [shader("hull")]
            [domain("tri")]
            [partitioning("integer")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("teConstants")]
            TeControl teHull(InputPatch<TeControl, 3> patch, uint i : SV_OutputControlPointID)
            {
                return patch[i];
            }

            [shader("domain")]
            [domain("tri")]
            TeOut teDomain(TePatch c, float3 bary : SV_DomainLocation, const OutputPatch<TeControl, 3> patch)
            {
                TeOut o;
                o.teClip = float4(patch[0].tePosition * bary.x, 1);
                return o;
            }
            """, ["teHull", "teDomain"]),

        new("geometry", """
            struct GsIn  { float4 gsInClip  : SV_Position; };
            struct GsOut { float4 gsOutClip : SV_Position; };

            [shader("geometry")]
            [maxvertexcount(3)]
            void gsGeometry(triangle GsIn input[3], inout TriangleStream<GsOut> stream)
            {
                GsOut o;
                for (int i = 0; i < 3; i++) { o.gsOutClip = input[i].gsInClip; stream.Append(o); }
            }
            """, ["gsGeometry"]),

        new("mesh", """
            struct MsVertex { float4 msClip : SV_Position; };

            [shader("mesh")]
            [outputtopology("triangle")]
            [numthreads(32, 1, 1)]
            void msMesh(out vertices MsVertex verts[3], out indices uint3 tris[1])
            {
                SetMeshOutputCounts(3, 1);
                verts[0].msClip = float4(0, 0, 0, 1);
                verts[1].msClip = float4(1, 0, 0, 1);
                verts[2].msClip = float4(0, 1, 0, 1);
                tris[0] = uint3(0, 1, 2);
            }
            """, ["msMesh"]),

        new("amplification", """
            struct AsPayload { float asScale; };
            groupshared AsPayload asShared;

            [shader("amplification")]
            [numthreads(16, 1, 1)]
            void asAmplification() { asShared.asScale = 1; DispatchMesh(1, 1, 1, asShared); }
            """, ["asAmplification"]),

        new("ray-tracing", """
            struct RtPayload { float4 rtColour; };
            struct RtAttribs { float2 rtBary; };

            RaytracingAccelerationStructure rtScene;
            RWTexture2D<float4> rtOutput;

            [shader("raygeneration")]
            void rtRayGen()
            {
                RayDesc ray;
                ray.Origin = 0; ray.Direction = float3(0, 0, 1); ray.TMin = 0; ray.TMax = 1000;
                RtPayload p; p.rtColour = 0;
                TraceRay(rtScene, 0, 0xff, 0, 1, 0, ray, p);
                rtOutput[DispatchRaysIndex().xy] = p.rtColour;
            }

            [shader("closesthit")]
            void rtClosestHit(inout RtPayload p, in RtAttribs a) { p.rtColour = float4(a.rtBary, 0, 1); }

            [shader("anyhit")]
            void rtAnyHit(inout RtPayload p, in RtAttribs a) { AcceptHitAndEndSearch(); }

            [shader("intersection")]
            void rtIntersection() { RtAttribs a; a.rtBary = 0; ReportHit(1.0, 0, a); }

            [shader("miss")]
            void rtMiss(inout RtPayload p) { p.rtColour = float4(0, 0, 0, 1); }

            [shader("callable")]
            void rtCallable(inout RtPayload p) { p.rtColour = 1; }
            """, ["rtRayGen", "rtClosestHit", "rtAnyHit", "rtIntersection", "rtMiss", "rtCallable"]),

        // ------------------------------------------------------------ odds and ends
        new("entry-point-uniforms", """
            Texture2D    euAlbedo;
            SamplerState euSampler;

            [shader("fragment")]
            float4 euFragment(uniform float4 euTint, uniform Texture2D euExtra) : SV_Target
            {
                return euAlbedo.Sample(euSampler, 0) * euTint + euExtra.Sample(euSampler, 0);
            }
            """, ["euFragment"]),

        new("global-uniforms", """
            uniform float4 guTint;
            uniform float  guExposure;
            Texture2D guTexture;

            [shader("fragment")]
            float4 guFragment() : SV_Target { return guTint * guExposure + guTexture.Load(0); }
            """, ["guFragment"],
            [
                new ShaderCase.Gap(
                    "descriptor globalParams",
                    "loose global uniforms become a buffer nobody declared. The module calls it " +
                    "globalParams; reflection attributes it to no variable, so its range has a " +
                    "null name. Closing it would mean putting a name chosen by Slang's SPIR-V " +
                    "emitter into the API-neutral layer, which is worse than the gap. " +
                    "ProgramLayout.GlobalConstantBufferBinding identifies it by index. Docs §9.7."),
                new ShaderCase.Gap(
                    "descriptor (unnamed)",
                    "the layout half of the same gap: the binding is at the right set and " +
                    "binding, it simply has no name to report."),
            ]),

        new("scalar-widths", """
            struct SwBlock
            {
                float  swF32;
                double swF64;
                int    swI32;
                uint   swU32;
                bool   swBool;
            };
            ConstantBuffer<SwBlock> swBuffer;

            [shader("fragment")]
            float4 swFragment() : SV_Target
            {
                return float4(swBuffer.swF32 + (float)swBuffer.swF64, swBuffer.swI32,
                              swBuffer.swU32, swBuffer.swBool ? 1 : 0);
            }
            """, ["swFragment"]),
    ];

    /// <summary>The corpus flattened to one case per entry point, for a per-module test.</summary>
    public static TheoryData<string, string> Entries()
    {
        var data = new TheoryData<string, string>();
        foreach (var shader in All)
            foreach (var entryPoint in shader.EntryPoints)
                data.Add(shader.Name, entryPoint);
        return data;
    }

    /// <summary>Finds a case by name.</summary>
    public static ShaderCase Case(string name) =>
        All.FirstOrDefault(c => c.Name == name)
        ?? throw new ArgumentException($"no corpus case named '{name}'", nameof(name));
}
