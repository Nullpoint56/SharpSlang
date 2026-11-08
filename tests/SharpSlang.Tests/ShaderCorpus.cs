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

namespace SharpSlang.Tests;

/// <summary>One shader in the corpus.</summary>
/// <param name="Name">Identifies the case in test output.</param>
/// <param name="Source">Slang source.</param>
/// <param name="EntryPoints">Entry points to link and compile, each producing its own module.</param>
/// <param name="KnownGaps">
/// Facts the module states that the layout is known not to report, or the reverse — each with the
/// reason. See <see cref="ShaderCase.Gap"/>; the agreement test requires the difference to be
/// <em>exactly</em> this set, so a gap can neither grow nor be quietly fixed.
/// </param>
/// <param name="ValidationVertex">
/// A vertex entry point to pair with a fragment-only shader, for
/// <see cref="NriDeviceTests"/>. Sixteen of these shaders declare a fragment stage and nothing else,
/// which reflects perfectly well and is not a graphics pipeline — neither Vulkan nor D3D12 has such
/// a thing without a vertex stage. The driver supplies a trivial one that writes only
/// <c>SV_Position</c>; a fragment stage that reads a varying needs the vertex stage to write it,
/// and says so here. Getting it wrong is not silent: an unwritten fragment input is
/// <c>VUID-RuntimeSpirv-OpEntryPoint-08743</c>.
/// </param>
public sealed record ShaderCase(
    string Name,
    string Source,
    IReadOnlyList<string> EntryPoints,
    IReadOnlyList<ShaderCase.Gap>? KnownGaps = null,
    string? ValidationVertex = null)
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
            """, ["dkFragment"], ValidationVertex: """
            struct DkVertexOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
            [shader("vertex")]
            DkVertexOut dkValidationVertex()
            { DkVertexOut o; o.pos = float4(0, 0, 0, 1); o.uv = 0; return o; }
            """),

        new("descriptor-arrays", """
            Texture2D    daFixed[4];
            Texture2D    daUnbounded[];
            SamplerState daSampler;

            [shader("fragment")]
            float4 daFragment(uint i : INDEX) : SV_Target
            {
                return daFixed[2].Sample(daSampler, 0) + daUnbounded[i].Sample(daSampler, 0);
            }
            """, ["daFragment"], ValidationVertex: """
            struct DaVertexOut { float4 pos : SV_Position; nointerpolation uint i : INDEX; };
            [shader("vertex")]
            DaVertexOut daValidationVertex()
            { DaVertexOut o; o.pos = float4(0, 0, 0, 1); o.i = 0; return o; }
            """),

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

        // A constant buffer with an explicit register space, which the descriptor-set view used to
        // put in space 0 while the parameter, the module and Slang's own overlap warning all said
        // space 1 — on SPIR-V and DXIL alike. explicit-spaces above covers textures and samplers,
        // which the view places correctly; the buffer is the case it gets wrong, and it is what
        // the D3D12 leg refused on its first run. Both the Vulkan attribute and the D3D register
        // are given, so the same shader pins both targets and Slang has no shift to warn about.
        new("explicit-buffer-spaces", """
            struct EbFrame { float4 ebTint; };
            struct EbLight { float4 ebDir; };
            [[vk::binding(0, 1)]] ConstantBuffer<EbFrame> ebFrame   : register(b0, space1);
            [[vk::binding(2, 0)]] ConstantBuffer<EbLight> ebLight   : register(b2, space0);
            [[vk::binding(1, 1)]] Texture2D               ebTexture : register(t1, space1);

            [shader("fragment")]
            float4 ebFragment() : SV_Target
            {
                return ebFrame.ebTint + ebLight.ebDir + ebTexture.Load(0);
            }
            """, ["ebFragment"]),

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

        // Added on the back of the VkFormat transcription bug. Only the one-location 64-bit types
        // are here: a double3 or double4 takes two locations, Slang assigns it one, and the
        // resulting overlap has no valid pipeline layout — NriValidationTests puts that overlap in
        // front of the validation layers.
        new("wide-vertex-inputs", """
            struct WvIn
            {
                float3  wvPosition : POSITION;
                double  wvScalar   : TEXCOORD0;
                double2 wvPair     : TEXCOORD1;
                float2  wvAfter    : TEXCOORD2;
            };

            [shader("vertex")]
            float4 wvVertex(WvIn v) : SV_Position
            {
                return float4(v.wvPosition, 1) + (float)v.wvScalar + (float)v.wvPair.x
                     + float4(v.wvAfter, 0, 0);
            }
            """, ["wvVertex"]),

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
                    "ProgramLayout.GlobalConstantBufferBinding identifies it by index. Docs ."),
                new ShaderCase.Gap(
                    "descriptor (unnamed)",
                    "the layout half of the same gap: the binding is at the right set and " +
                    "binding, it simply has no name to report."),
            ]),

        // ------------------------------------------------------------ round four
        new("matrix-orientation-overrides", """
            struct MoBlock
            {
                             float4x4 moDefault;
                 row_major   float4x4 moRow;
                 column_major float4x4 moColumn;
                 row_major   float3x2 moRowTall;
                 column_major float3x2 moColumnTall;
            };
            ConstantBuffer<MoBlock> moBuffer;

            [shader("fragment")]
            float4 moFragment() : SV_Target
            {
                return mul(moBuffer.moDefault, float4(1)) + mul(moBuffer.moRow, float4(1))
                     + mul(moBuffer.moColumn, float4(1))
                     + float4(mul(moBuffer.moRowTall, float2(1)), 0)
                     + float4(mul(moBuffer.moColumnTall, float2(1)), 0);
            }
            """, ["moFragment"]),

        new("append-buffers", """
            struct AbParticle { float3 abPosition; float abLife; };

            AppendStructuredBuffer<AbParticle>  abAppend;
            ConsumeStructuredBuffer<AbParticle> abConsume;
            RWStructuredBuffer<AbParticle>      abPlain;

            [shader("compute")]
            [numthreads(64, 1, 1)]
            void abCompute(uint id : SV_DispatchThreadID)
            {
                AbParticle p = abConsume.Consume();
                p.abLife -= 1;
                abPlain[id] = p;
                abAppend.Append(p);
            }
            """, ["abCompute"],
            [
                new ShaderCase.Gap(
                    "descriptor elements",
                    "the module names both the append and the consume buffer `elements`, after the " +
                    "payload field inside the struct Slang wraps them in, so the two are " +
                    "indistinguishable by name in the SPIR-V. The layout reports the declared " +
                    "names. The set and binding agree; only the name differs, and SharpSlang's is " +
                    "the useful one."),
                new ShaderCase.Gap("descriptor abAppend", "the layout half of the same gap."),
                new ShaderCase.Gap("descriptor abConsume", "the layout half of the same gap."),
            ]),

        new("buffer-arrays", """
            // No ParameterBlock array here: Slang rejects it outright with
            // "arrays of non-addressable type not allowed", so there is nothing to reflect.
            struct BaFrame  { float4 baTint; };
            struct BaObject { float4x4 baModel; };

            ConstantBuffer<BaFrame>   baBuffers[3];
            ParameterBlock<BaObject>  baBlock;
            SamplerState              baSamplers[4];
            SamplerState              baUnbounded[];
            Texture2D                 baTexture;

            [shader("fragment")]
            float4 baFragment(uint i : INDEX) : SV_Target
            {
                return baBuffers[1].baTint
                     + mul(baBlock.baModel, float4(1))
                     + baTexture.Sample(baSamplers[2], 0)
                     + baTexture.Sample(baUnbounded[i], 0);
            }
            """, ["baFragment"], ValidationVertex: """
            struct BaVertexOut { float4 pos : SV_Position; nointerpolation uint i : INDEX; };
            [shader("vertex")]
            BaVertexOut baValidationVertex()
            { BaVertexOut o; o.pos = float4(0, 0, 0, 1); o.i = 0; return o; }
            """),

        new("nested-uniform-structs", """
            struct NuInner  { float3 nuOffset; float nuWeight; };
            struct NuMiddle { NuInner nuInner; float2x2 nuWarp; };
            struct NuOuter
            {
                NuMiddle nuMiddle;
                NuInner  nuArray[2];
                float    nuTrailing;
            };
            ConstantBuffer<NuOuter> nuBuffer;

            [shader("fragment")]
            float4 nuFragment() : SV_Target
            {
                return float4(nuBuffer.nuMiddle.nuInner.nuOffset, nuBuffer.nuMiddle.nuInner.nuWeight)
                     + float4(mul(nuBuffer.nuMiddle.nuWarp, float2(1)), 0, 0)
                     + float4(nuBuffer.nuArray[1].nuOffset, nuBuffer.nuTrailing);
            }
            """, ["nuFragment"]),

        new("wide-scalars", """
            struct WsBlock
            {
                float    wsLead;
                double   wsWide;
                double2  wsWidePair;
                float    wsBetween;
                double   wsWideArray[2];
                uint64_t wsBig;
                int64_t  wsBigSigned;
            };
            ConstantBuffer<WsBlock> wsBuffer;

            [shader("fragment")]
            float4 wsFragment() : SV_Target
            {
                return float4((float)(wsBuffer.wsWide + wsBuffer.wsWidePair.x + wsBuffer.wsWideArray[1]),
                              wsBuffer.wsLead + wsBuffer.wsBetween,
                              (float)wsBuffer.wsBig, (float)wsBuffer.wsBigSigned);
            }
            """, ["wsFragment"]),

        new("system-values", """
            struct SvVertexIn
            {
                float3 svPosition : POSITION;
                uint   svVertex   : SV_VertexID;
                uint   svInstance : SV_InstanceID;
            };
            struct SvVertexOut
            {
                float4 svClip     : SV_Position;
                float2 svUv       : TEXCOORD0;
                uint   svLayer    : SV_RenderTargetArrayIndex;
            };

            [shader("vertex")]
            SvVertexOut svVertex(SvVertexIn v)
            {
                SvVertexOut o;
                o.svClip = float4(v.svPosition, 1) + v.svVertex + v.svInstance;
                o.svUv = 0;
                o.svLayer = 0;
                return o;
            }

            [shader("fragment")]
            float4 svFragment(SvVertexOut i, bool svFront : SV_IsFrontFace) : SV_Target
            {
                return float4(i.svUv, svFront ? 1 : 0, i.svClip.z);
            }
            """, ["svVertex", "svFragment"]),

        new("interpolation-modifiers", """
            struct ImVaryings
            {
                float4                  imClip   : SV_Position;
                nointerpolation float3  imFlat   : COLOR0;
                noperspective   float2  imScreen : TEXCOORD0;
                sample          float   imPerSample : TEXCOORD1;
                                float2  imSmooth : TEXCOORD2;
            };

            [shader("vertex")]
            ImVaryings imVertex(float3 imPosition : POSITION)
            {
                ImVaryings o;
                o.imClip = float4(imPosition, 1);
                o.imFlat = 1; o.imScreen = 0; o.imPerSample = 0; o.imSmooth = 0;
                return o;
            }

            [shader("fragment")]
            float4 imFragment(ImVaryings v) : SV_Target
            {
                return float4(v.imFlat, v.imPerSample) + float4(v.imScreen, v.imSmooth);
            }
            """, ["imVertex", "imFragment"]),

        // Arrays sized by a specialization constant are not here, and cannot be: a buffer holding
        // one is dropped by Slang from both the layout and the module, so it has no pipeline layout
        // to agree about, and a *resource* array sized that way terminates the process inside
        // getBindingRangeCount. UnknownSizeTests covers what can be covered. Docs .

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

        // ------------------------------------------------------------ round five
        //
        // Four paths nothing in the corpus reached. Checked before adding rather than assumed:
        // unbounded descriptor arrays and storage-image formats were already covered by
        // `descriptor-arrays` and `descriptor-kinds`, so they are not repeated here.

        // The stage-narrowing path, which is the one place the Vulkan projection has had a real
        // defect ( — InputAttachment was reported visible to every stage). Every other
        // multi-entry-point case in the corpus shares its resources between the stages, so the
        // narrowing has never been exercised: here the texture is read only by the fragment stage
        // and the structured buffer only by the vertex stage, so a projection that widens either
        // back to both is wrong in a way the validation layers will judge.
        new("stage-narrowing", """
            struct SnRow { float4 snOffset; };

            StructuredBuffer<SnRow> snVertexOnly;
            Texture2D               snFragmentOnly;
            SamplerState            snSampler;
            ConstantBuffer<SnRow>   snBoth;

            struct SnVertexOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

            [shader("vertex")]
            SnVertexOut snVertex(uint id : SV_VertexID)
            {
                SnVertexOut o;
                o.pos = snVertexOnly[id].snOffset + snBoth.snOffset;
                o.uv = 0;
                return o;
            }

            [shader("fragment")]
            float4 snFragment(SnVertexOut i) : SV_Target
            {
                return snFragmentOnly.Sample(snSampler, i.uv) + snBoth.snOffset;
            }
            """, ["snVertex", "snFragment"]),

        // Existential types: a value whose concrete type is not known until it is bound. This is
        // what sub-object ranges exist for, and the corpus has never contained an `interface` at
        // all — SubObjectRangeTests reaches them through hand-written sources, so neither the
        // agreement test nor the validation layers have ever seen one.
        new("existential-types", """
            interface IEtShade
            {
                float4 etEvaluate(float2 uv);
            }

            struct EtFlat : IEtShade
            {
                float4 etColour;
                float4 etEvaluate(float2 uv) { return etColour; }
            }

            ConstantBuffer<EtFlat> etConcrete;

            [shader("fragment")]
            float4 etFragment(float2 uv : TEXCOORD0) : SV_Target
            {
                EtFlat f = etConcrete;
                return f.etEvaluate(uv);
            }
            """, ["etFragment"], ValidationVertex: """
            struct EtVertexOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
            [shader("vertex")]
            EtVertexOut etValidationVertex()
            { EtVertexOut o; o.pos = float4(0, 0, 0, 1); o.uv = 0; return o; }
            """),

        // Subgroup operations. Nothing in the corpus contains a wave intrinsic, and they carry
        // SPIR-V capabilities of their own — the profile-upgrade warning `descriptor-kinds`
        // produces for ray query is the same mechanism, so this is the second instance of a shader
        // whose declared profile is not the one it ends up needing.
        new("wave-ops", """
            RWStructuredBuffer<float> woOut;

            [shader("compute")]
            [numthreads(64, 1, 1)]
            void woCompute(uint id : SV_DispatchThreadID)
            {
                float v = (float)id;
                woOut[id] = WaveActiveSum(v)
                          + WaveActiveMax(v)
                          + (WaveIsFirstLane() ? 1.0 : 0.0)
                          + (float)WaveGetLaneIndex();
            }
            """, ["woCompute"]),

        // Atomics on both buffer shapes. Not a new descriptor kind, which is the point: it is the
        // access pattern a compute cooker actually emits, and the module decorates the buffer
        // differently for it (Coherent/NonWritable and friends) — so this is a check that the
        // layout still agrees when the module says more about a binding than usual.
        new("atomics", """
            RWStructuredBuffer<uint> atCounters;
            RWByteAddressBuffer      atRaw;

            [shader("compute")]
            [numthreads(64, 1, 1)]
            void atCompute(uint id : SV_DispatchThreadID)
            {
                uint previous;
                InterlockedAdd(atCounters[0], 1, previous);
                atRaw.InterlockedAdd(0, previous);
            }
            """, ["atCompute"]),
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

    // ---------------------------------------------------------------- pairing a vertex stage

    /// <summary>The entry point name of the vertex stage the device tests supply.</summary>
    private const string SuppliedVertex = "pvValidationVertex";

    /// <summary>
    /// A vertex stage that writes only <c>SV_Position</c> — enough to make a pipeline out of a
    /// fragment stage that reads no varyings.
    /// </summary>
    private const string TrivialVertex = $$"""

        [shader("vertex")]
        float4 {{SuppliedVertex}}() : SV_Position { return float4(0, 0, 0, 1); }
        """;

    /// <summary>
    /// The case's source and entry points, with a vertex stage added if it needs one.
    /// </summary>
    /// <remarks>
    /// Sixteen corpus shaders declare a fragment stage and nothing else. That is a perfectly good
    /// thing to reflect and it is not a graphics pipeline, so without this the device tests would
    /// build nothing for most of the corpus and report success — the shape of failure this project
    /// has hit before, where a gate that excludes everything is indistinguishable from a gate that
    /// excludes nothing. The added stage is checked for consequence by the Vulkan theory in
    /// <see cref="NriDeviceTests"/>.
    /// </remarks>
    public static (string Source, List<string> EntryPoints) PairedWithAVertexStage(ShaderCase c)
    {
        var stages = c.Source;
        var needsVertex =
            stages.Contains("[shader(\"fragment\")]", StringComparison.Ordinal)
            && !stages.Contains("[shader(\"vertex\")]", StringComparison.Ordinal);

        if (!needsVertex) return (c.Source, [.. c.EntryPoints]);

        var vertex = c.ValidationVertex is null
            ? TrivialVertex
            : $"\n{c.ValidationVertex}\n";

        // The case's own name for it when it supplied one, so the declaration reads as ordinary
        // Slang rather than as something with a magic name.
        var name = c.ValidationVertex is null
            ? SuppliedVertex
            : VertexEntryPointNameIn(c.ValidationVertex);

        return (c.Source + vertex, [.. c.EntryPoints, name]);
    }

    /// <summary>The function name following the <c>[shader("vertex")]</c> attribute.</summary>
    private static string VertexEntryPointNameIn(string slang)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            slang, @"\[shader\(""vertex""\)\]\s*\S+\s+(\w+)\s*\(");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                "a ShaderCase.ValidationVertex must declare exactly one [shader(\"vertex\")] entry "
                + "point on the line after the attribute; this one does not:\n" + slang);
        }

        return match.Groups[1].Value;
    }

    /// <summary>Finds a case by name.</summary>
    public static ShaderCase Case(string name) =>
        All.FirstOrDefault(c => c.Name == name)
        ?? throw new ArgumentException($"no corpus case named '{name}'", nameof(name));
}
