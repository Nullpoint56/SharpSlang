// DXIL: what Slang emits for D3D12, and what the NRI projection makes of its layout.
//
// Everything else in this suite compiles SPIR-V. A DXIL target differs in three ways that matter to
// a binding, and each is a question with its own test here rather than an assumption carried over:
//
//   1. It needs a second compiler. Slang emits DXIL by handing HLSL to dxc, ships no dxc, and
//      fails inside the first compile when there is none — so CreateSession asks first, and the
//      first tests below pin both sides of that.
//   2. The layout speaks registers, not bindings. b0, t0, s0 and u0 are four bindings at index 0
//      in four categories, where SPIR-V numbers one set's descriptors consecutively in one
//      category. The projection is the same code for both; whether it gives the same answers is
//      the corpus theory below.
//   3. [vk::push_constant] means nothing. The block is an ordinary constant buffer at a b
//      register, and root constants on D3D12 come from the host naming it.
//
// What a D3D12 device does with all of it is NriDeviceTests' section 5; this file needs only the
// compiler and dxc, so it runs wherever dxc is staged and skips by absence everywhere else.
//
// Every list here is exact and checked against the corpus, like the lists in the other corpus
// tests: an entry that stops being true is a failing test, so none of them can quietly grow to
// excuse a regression or outlive a fix.

using PrismSlang.Baking;
using PrismSlang.Interop;
using PrismSlang.NRI;
using PrismSlang.Native;
using PrismSlang.Reflection;
using SharpNRI.Native;

namespace PrismSlang.Tests;

public sealed class DxilTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>Why a corpus case does not compile to DXIL: the reason, and a fragment of dxc's own words.</summary>
    internal sealed record NotDxil(string Reason, string Diagnostic);

    /// <summary>Corpus cases Slang cannot compile to DXIL, measured with dxc 1.9 at sm_6_5.</summary>
    /// <remarks>
    /// <para>
    /// Two of the three refusals come from dxc, not from Slang: Slang emits HLSL for them without
    /// complaint and dxc rejects it, so the failure surfaces from <c>GetEntryPointCodeBytes</c> with
    /// dxc's diagnostic in the message. The third, <c>compute-specialized</c>, is Slang's own
    /// E55205, raised from the same call. The diagnostic fragment is asserted, so a case that
    /// starts failing for some other reason is not excused by this list.
    /// </para>
    /// <para>
    /// Specialization constants as such are not here, and that is a measurement rather than the
    /// expectation it was: <c>specialization-constants</c> compiles. Slang lowers each
    /// <c>[vk::constant_id]</c> constant to a member of a constant buffer at <c>b0</c> that nobody
    /// declared, so the DXIL is legal and the "constant" is uniform data the host would have to
    /// fill. It is not specializable on D3D12, which has no such thing; it compiles, and the
    /// projection describes the buffer. A specialization constant in <c>numthreads</c> is the
    /// exception, and the one that changed: slang-compiler 2026.14.1 resolved
    /// <c>numthreads(csGroupX)</c> to the declared default at compile time and compiled it, and
    /// 2026.21 refuses it.
    /// </para>
    /// </remarks>
    internal static readonly Dictionary<string, NotDxil> CannotCompile = new(StringComparer.Ordinal)
    {
        ["subpass-inputs"] = new(
            "an input attachment read has no D3D meaning: D3D12 reads a previous pass's output as an "
            + "ordinary texture, and dxc refuses SubpassInput outright rather than lowering it.",
            "SubpassInput is a Vulkan specific feature"),
        ["wide-vertex-inputs"] = new(
            "a double vertex input. D3D12's input assembler has no 64-bit formats and dxc refuses a "
            + "double in a stage signature, so the shader never reaches the projection — where on "
            + "SPIR-V it is refused for the same underlying reason (NRI has no 64-bit vertex format).",
            "cannot be used as shader inputs or outputs"),
        ["compute-specialized"] = new(
            "a specialization constant as a workgroup size. D3D12 fixes the thread group size in "
            + "the shader, and Slang refuses to choose one for it: 2026.14.1 compiled the declared "
            + "default in, 2026.21 says so instead.",
            "specialization constants not supported for numthreads"),
    };

    /// <summary>Corpus cases the NRI projection refuses on a DXIL layout, and the construct each message must name.</summary>
    /// <remarks>
    /// The SPIR-V list less <c>wide-vertex-inputs</c>, which dxc refuses before the projection can
    /// (see <see cref="CannotCompile"/>). The combined sampler is the same refusal on both targets,
    /// for the same reason: NRI has no descriptor type for one.
    /// </remarks>
    private static readonly Dictionary<string, string> Refused = new(StringComparer.Ordinal)
    {
        ["descriptor-kinds"] = "combined texture sampler",
    };

    /// <summary>
    /// Corpus cases where, on a DXIL layout, a projected range carries the name of a parameter in a
    /// different register class: a known defect, pinned so its fix is noticed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each value is <c>"&lt;name&gt; on &lt;descriptor type&gt; &lt;register&gt;"</c>. The range is one
    /// Slang's binding-range walk attributes to no variable — the buffer of loose global uniforms,
    /// the buffer Slang makes of specialization constants, an entry point's <c>uniform</c> values —
    /// so the projection falls back to <c>PipelineInterface.DescriptorNames</c>, which is keyed by
    /// register index alone. On SPIR-V an index is unique within a set and the fallback finds
    /// nothing or the right thing. On DXIL <c>b0</c> and <c>t0</c> share index 0, and the fallback
    /// lends the constant buffer the name of whatever <c>t0</c> or <c>u0</c> parameter it finds,
    /// which <c>NriPipelineProjection.Range(name)</c> then returns first for that name.
    /// </para>
    /// <para>
    /// The descriptors themselves are right — each range has the right type, register and space,
    /// and the D3D12 corpus theory builds the pipeline of <c>global-uniforms</c>
    /// (<c>entry-point-uniforms</c> is refused there for a different reason,
    /// pinned in NriDeviceTests' D3D12ProjectionDefects). Only the name is wrong,
    /// which is worse than the null it is on SPIR-V: a host binding by name would put a texture
    /// where a constant buffer is read. The fix is the class-aware lookup PromotedConstantBuffers
    /// already does, in <c>DescriptorNames</c>; when it lands this list empties and the test says so.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> BorrowedNames = new(StringComparer.Ordinal)
    {
        ["entry-point-uniforms"] = "euAlbedo on CONSTANT_BUFFER b0",
        ["global-uniforms"] = "guTexture on CONSTANT_BUFFER b0",
    };

    private static readonly NriProjectionOptions Capped = new() { UnboundedArrayCap = 64 };

    /// <summary>The four categories a DXIL layout places a descriptor in.</summary>
    private static readonly SlangParameterCategory[] RegisterClasses =
    [
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER,
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE,
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SAMPLER_STATE,
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS,
    ];

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    [Fact]
    public void Every_declared_exception_names_a_shader_that_exists()
    {
        var unknown = CannotCompile.Keys.Concat(Refused.Keys).Concat(BorrowedNames.Keys)
            .Where(name => ShaderCorpus.All.All(c => c.Name != name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            "CannotCompile, Refused or BorrowedNames names shaders the corpus does not have: "
            + string.Join(", ", unknown));

        // A case that does not compile has no layout to refuse or misname.
        Assert.Empty(CannotCompile.Keys.Intersect(Refused.Keys));
        Assert.Empty(CannotCompile.Keys.Intersect(BorrowedNames.Keys));
    }

    // ---------------------------------------------------------------- 1. dxc, or a refusal naming it

    [DxcFact]
    public void A_dxil_session_is_created_when_dxc_is_loadable()
    {
        // The positive half of the refusal below, and the one this workstation can measure: dxc
        // copied beside slang-compiler from Microsoft.Direct3D.DXC by the targets file, found by Slang's
        // own by-name search, and so RequireDxc is silent and CreateSession goes through.
        using var global = SlangGlobalSession.Create();

        Assert.True(global.IsDownstreamCompilerAvailable(SlangPassThrough.SLANG_PASS_THROUGH_DXC));
        global.RequireDxc();

        using var session = global.CreateSession(new SessionOptions { Targets = [NriDeviceTests.Dxil] });
        Assert.NotNull(session);
    }

    [NativeFact]
    public void Without_dxc_a_dxil_session_is_refused_naming_the_package()
    {
        // The negative half can only be measured where dxc is absent, because "absent" is decided
        // by the process's library search and nothing in a running test host can take a loaded
        // dxcompiler away again — Slang caches the pass-through once it has found it, and the
        // DLL sits in the same directory as the compiler under test. So on a machine that has it
        // (every win-x64 run with the package restored) this test records that and returns, and
        // the other CI legs are the measurement: linux-x64 and osx-arm64 load slang-compiler and
        // stage no dxc, so there this is the assertion that CreateSession refuses with the fix in
        // its message rather than failing inside the first compile.
        //
        // A plain NativeFact, not a DxcFact, for exactly that reason: the DxcFact gate would skip
        // it on the only machines where it can say anything.
        using var global = SlangGlobalSession.Create();

        if (global.IsDownstreamCompilerAvailable(SlangPassThrough.SLANG_PASS_THROUGH_DXC))
        {
            output.WriteLine("dxc is loadable here, so there is no refusal to observe; the DXIL session "
                           + "is A_dxil_session_is_created_when_dxc_is_loadable's subject.");
            return;
        }

        var e = Assert.Throws<SlangException>(
            () => global.CreateSession(new SessionOptions { Targets = [NriDeviceTests.Dxil] }));

        // The two things a reader needs to act: what to reference, and where it comes from.
        Assert.Contains("PrismSlang.Native.Dxc", e.Message);
        Assert.Contains("Microsoft.Direct3D.DXC", e.Message);
        Assert.Contains("dxcompiler", e.Message);

        // And RequireDxc says the same thing on its own, before any session is asked for.
        var direct = Assert.Throws<SlangException>(global.RequireDxc);
        Assert.Equal(e.Message, direct.Message);

        // A SPIR-V session on the same global session is untouched: the check is per target.
        using var spirv = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
    }

    // ---------------------------------------------------------------- 2. the corpus compiles

    [DxcTheory]
    [MemberData(nameof(Cases))]
    public void Every_corpus_entry_point_compiles_to_a_signed_dxil_container(string caseName)
    {
        // Every entry point of every case, at the profile a host gets by not naming one (sm_6_5).
        // The corpus is the one in every other corpus test — mesh, task and the six ray tracing
        // stages included — so what does not compile is a property of DXIL, recorded above.
        var c = ShaderCorpus.Case(caseName);

        if (CannotCompile.TryGetValue(caseName, out var notDxil))
        {
            var e = Assert.Throws<SlangException>(
                () => NriDeviceTests.Compile(caseName, c.Source, c.EntryPoints, NriDeviceTests.Dxil));
            Assert.Contains(notDxil.Diagnostic, e.Message);
            return;
        }

        var compiled = NriDeviceTests.Compile(caseName, c.Source, c.EntryPoints, NriDeviceTests.Dxil);
        Assert.Equal(c.EntryPoints.Count, compiled.Code.Length);

        for (var i = 0; i < compiled.Code.Length; i++)
        {
            var code = compiled.Code[i];

            // A DXIL module is a DXBC container: "DXBC", a 16-byte digest, then the parts. SPIR-V
            // would start 03 02 23 07, so this is also the check that the target took effect.
            Assert.True(code.Length > 20 && code.AsSpan(0, 4).SequenceEqual("DXBC"u8),
                $"{c.EntryPoints[i]}: the code does not start with a DXBC header");

            // The digest is the validator's hash of the rest of the container, written when it
            // signs; a container nothing signed has zeros there. D3D12 refuses that unless
            // experimental shader models are enabled, which Windows' Developer Mode alone does not
            // do — measured with it on, on an RTX 4070 SUPER and on WARP: "Compute Shader is
            // unsigned or a preview shader and experimental shader models are not enabled, or
            // shader is otherwise corrupt." Nonzero is all this asserts. Whether it is the right
            // hash is left to D3D12, which checks it in every pipeline NriDeviceTests builds from
            // DXIL: a digest with one bit changed was refused as "corrupt or in an unrecognized
            // format". Not every wrong digest is refused, though: with Developer Mode on, sixteen
            // 0x01 bytes were accepted and the shader ran.
            //
            // Which validator signed is not visible here, and it is not dxil.dll. The pinned dxc
            // signs with the validator built into dxcompiler.dll and never loads dxil.dll. Measured
            // on compute-fixed: the same 2,908 bytes, digest F7387757C1E54D1675BD3B9EE9C5CB8E
            // included, with dxil.dll present and with it deleted so that no module of that name
            // was in the process; dxil.dll's own validator, handed a copy with the digest zeroed,
            // wrote the same 16 bytes back; and D3D12 built a pipeline from the container made
            // without it and dispatched it correctly. Measured: nonzero for every case.
            Assert.True(code.AsSpan(4, 16).ContainsAnyExcept((byte)0),
                $"{c.EntryPoints[i]}: the DXBC digest is all zeros, so no validator signed the container");
        }

        output.WriteLine($"{caseName}: " + string.Join(", ",
            compiled.Code.Select((code, i) => $"{c.EntryPoints[i]} {code.Length} bytes")));
    }

    // ---------------------------------------------------------------- 3. the projection reads registers

    [DxcTheory]
    [MemberData(nameof(Cases))]
    public void The_dxil_projection_accepts_what_the_spirv_one_does_and_reads_registers(string caseName)
    {
        // The same ToNri over a DXIL layout. What can differ from SPIR-V is the input: per-class
        // register numbering, and categories the projection has never been handed on that target.
        // So: the same cases succeed and the same construct is refused, every range is in one of
        // the four D3D register classes (and never in DESCRIPTOR_TABLE_SLOT, the SPIR-V category,
        // which PromotedConstantBuffers reads as "this layout was compiled for SPIR-V"), and the
        // projection emits a range for every one of them.
        var c = ShaderCorpus.Case(caseName);

        if (CannotCompile.ContainsKey(caseName))
        {
            output.WriteLine("not compiled to DXIL; see CannotCompile");
            return;
        }

        var layout = NriDeviceTests.Compile(caseName, c.Source, c.EntryPoints, NriDeviceTests.Dxil).Layout;

        var ranges = layout.DescriptorSets.SelectMany(s => s.Ranges).ToList();
        foreach (var r in ranges)
        {
            Assert.True(RegisterClasses.Contains(r.Category),
                $"range '{r.Name}' at index {r.IndexOffset} is in {r.Category}, which is not a D3D register class");
        }

        if (Refused.TryGetValue(caseName, out var construct))
        {
            var e = Assert.Throws<NotSupportedException>(() => layout.ToNri(Capped));
            Assert.Contains(construct, e.Message);
            return;
        }

        var projection = layout.ToNri(Capped);

        // No root constants without being asked: on DXIL there is no push-constant category to
        // make them from. That includes an entry point's `uniform` values, which Slang places in a
        // push-constant buffer on SPIR-V and in an ordinary constant buffer at a b register on
        // DXIL; the projection used to report them as root constants on both, which put the same
        // register in the layout twice, and D3D12 refused the root signature of
        // entry-point-uniforms. PipelineInterface.PushConstantBlocks now reads the entry point's
        // base offsets to tell the two apart, and this asserts the DXIL half of that.
        Assert.Empty(projection.RootConstants);

        // Every reflected range becomes exactly one projected range, at its own register, and the
        // register class survives as the descriptor type's class — a b register is a constant
        // buffer, a t register is something read-only, and so on.
        Assert.Equal(ranges.Count, projection.DescriptorSets.Sum(s => s.Ranges.Count));
        foreach (var set in projection.DescriptorSets)
        {
            foreach (var r in set.Ranges)
            {
                var reflected = layout.DescriptorSets.Single(s => s.SpaceOffset == set.RegisterSpace).Ranges
                    .Where(x => x.IndexOffset == r.Desc.baseRegisterIndex)
                    .Select(x => x.Category)
                    .ToList();
                Assert.Contains(RegisterClassOf(r.Desc.descriptorType), reflected);
            }
        }

        output.WriteLine($"{caseName}: " + string.Join(", ", projection.DescriptorSets.SelectMany(s =>
            s.Ranges.Select(r => $"{Register(r.Desc.descriptorType)}{r.Desc.baseRegisterIndex} space{s.RegisterSpace} '{r.Name}'"))));
    }

    [DxcFact]
    public void Range_names_borrowed_across_register_classes_are_exactly_the_known_ones()
    {
        // See BorrowedNames. Asked of the whole corpus at once, so the list is checked in both
        // directions: a new case that borrows a name fails here, and so does the fix.
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var c in ShaderCorpus.All)
        {
            if (CannotCompile.ContainsKey(c.Name) || Refused.ContainsKey(c.Name)) continue;

            var layout = NriDeviceTests.Compile(c.Name, c.Source, c.EntryPoints, NriDeviceTests.Dxil).Layout;
            var parameters = layout.Parameters
                .Concat(layout.EntryPoints.SelectMany(e => e.Parameters))
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var r in layout.ToNri(Capped).DescriptorSets.SelectMany(s => s.Ranges))
            {
                // Only names that are top-level parameters can be judged by their category; a name
                // the walk found inside a ParameterBlock is a field, and is attributed correctly.
                if (r.Name is null || !parameters.TryGetValue(r.Name, out var p)) continue;
                if (p.PrimaryBinding is not { } b) continue;

                // A ParameterBlock introduces a space rather than occupying a register, and its
                // implicit constant buffer is rightly named after it — pbBlock at b0 of space 1 is
                // the buffer the module calls pbBlock. Not a borrowed name.
                if (b.Category is SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUB_ELEMENT_REGISTER_SPACE
                               or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_REGISTER_SPACE) continue;

                if (b.Category == RegisterClassOf(r.Desc.descriptorType)) continue;

                found[c.Name] = $"{r.Name} on {r.Desc.descriptorType} {Register(r.Desc.descriptorType)}{r.Desc.baseRegisterIndex}";
            }
        }

        foreach (var (name, what) in found) output.WriteLine($"{name}: {what}");

        Assert.True(found.Count == 0,
            "a projected range carries the name of a parameter in another register class:\n"
            + string.Join("\n", found.Select(kv => $"    {kv.Key}: {kv.Value}")));
    }

    [DxcFact]
    public void A_push_constant_block_is_a_constant_buffer_on_dxil_until_the_host_names_it()
    {
        // The corpus's push-constants case: a [[vk::push_constant]] block and a texture. On SPIR-V
        // the block is root constants by Slang's own placement. On DXIL the attribute is gone —
        // the block is at b0 in the CONSTANT_BUFFER category, beside the texture at t0 — so by
        // default the projection describes it as a descriptor and emits no root constants, and
        // only RootConstantParameters turns it back into what the shader author meant.
        var c = ShaderCorpus.Case("push-constants");
        var layout = NriDeviceTests.Compile(c.Name, c.Source, c.EntryPoints, NriDeviceTests.Dxil).Layout;

        var push = layout.Parameters.Single(p => p.Name == "pcPush").PrimaryBinding
            ?? throw new InvalidOperationException("pcPush has no binding");
        Assert.Equal(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER, push.Category);

        var plain = layout.ToNri(Capped);
        Assert.Empty(plain.RootConstants);
        var (_, asDescriptor) = plain.Range("pcPush")
            ?? throw new InvalidOperationException("unpromoted, pcPush should be a descriptor range");
        Assert.Equal(DescriptorType.CONSTANT_BUFFER, asDescriptor.Desc.descriptorType);
        Assert.Equal(push.Index, asDescriptor.Desc.baseRegisterIndex);

        var promoted = layout.ToNri(Capped with { RootConstantParameters = ["pcPush"] });
        var block = Assert.Single(promoted.RootConstants);

        Assert.Equal("pcPush", block.Name);
        Assert.Equal(0u, block.Index);
        Assert.Equal(push.Index, block.Desc.registerIndex);
        Assert.Equal(push.Space, promoted.Layout.RootRegisterSpace);
        Assert.Equal(StageBits.FRAGMENT_SHADER, block.Desc.shaderStages);
        Assert.Single(promoted.Layout.RootConstants);

        // Measured: 20 bytes — a float4 and a float, with no padding after the float, because the
        // size is the block's uniform footprint rather than its 16-byte-rounded register count.
        Assert.Equal(20u, block.Desc.size);

        // The block leaves the set, so the root signature does not also bind b0 through a table;
        // the texture stays, and so does nothing else.
        Assert.Null(promoted.Range("pcPush"));
        Assert.NotNull(promoted.Range("pcTexture"));
        Assert.Equal(
            plain.DescriptorSets.Sum(s => s.Ranges.Count) - 1,
            promoted.DescriptorSets.Sum(s => s.Ranges.Count));
    }

    // ---------------------------------------------------------------- 4. promotion refusals

    [DxcFact]
    public void Promoting_a_texture_is_refused_because_it_has_no_b_register()
    {
        // The DXIL-only refusal: on SPIR-V the same name is refused earlier, as a descriptor of a
        // SPIR-V layout, so only a DXIL layout reaches the category check.
        var c = ShaderCorpus.Case("push-constants");
        var layout = NriDeviceTests.Compile(c.Name, c.Source, c.EntryPoints, NriDeviceTests.Dxil).Layout;

        var e = Assert.Throws<NotSupportedException>(
            () => layout.ToNri(Capped with { RootConstantParameters = ["pcTexture"] }));

        Assert.Contains("'pcTexture'", e.Message);
        Assert.Contains("not as a constant buffer", e.Message);
        Assert.Contains("SHADER_RESOURCE", e.Message);
    }

    [DxcFact]
    public void Promoting_blocks_in_two_register_spaces_is_refused_because_nri_has_one_root_space()
    {
        // The first version of this test pinned a walker defect instead: Slang honours
        // `register(b0, space1)`, the parameter said space 1, and PrismSlang's descriptor-set view
        // put the constant buffer's range in space 0 while a texture declared the same way landed
        // in space 1 — on SPIR-V as well as DXIL, it turned out. The walker now moves a top-level
        // parameter's range to the space the parameter reports (ReflectionWalker
        // .RelocateExplicitlySpacedParameters), the corpus's explicit-buffer-spaces case holds the
        // same shape in front of every oracle, and this test is the refusal it was meant to be.
        const string Source = """
            struct SpA { float4 a; };
            struct SpB { float4 b; };
            [[vk::binding(0, 1)]] ConstantBuffer<SpA> spFirst  : register(b0, space1);
            [[vk::binding(2, 0)]] ConstantBuffer<SpB> spSecond : register(b2, space0);
            [[vk::binding(3, 1)]] Texture2D           spTex    : register(t3, space1);

            [shader("fragment")]
            float4 spFragment() : SV_Target { return spFirst.a + spSecond.b + spTex.Load(0); }
            """;

        var layout = NriDeviceTests.Compile("spaces", Source, ["spFragment"], NriDeviceTests.Dxil).Layout;

        uint SpaceOfRange(string name) =>
            layout.DescriptorSets.Single(s => s.Ranges.Any(r => r.Name == name)).SpaceOffset;

        // The walk places each buffer where its parameter says.
        Assert.Equal(1u, SpaceOfRange("spFirst"));
        Assert.Equal(0u, SpaceOfRange("spSecond"));
        Assert.Equal(1u, SpaceOfRange("spTex"));

        // One promoted block carries its space into the root register space and its register
        // into the root constant, which is where NRI's D3D12 backend binds it.
        var one = layout.ToNri(Capped with { RootConstantParameters = ["spFirst"] });
        var block = Assert.Single(one.RootConstants);
        Assert.Equal(1u, one.Layout.RootRegisterSpace);
        Assert.Equal(0u, block.Desc.registerIndex);
        Assert.DoesNotContain(one.DescriptorSets.SelectMany(s => s.Ranges), r => r.Name == "spFirst");

        // Two blocks in two spaces have no single root space to share, and the refusal names both.
        var e = Assert.Throws<NotSupportedException>(
            () => layout.ToNri(Capped with { RootConstantParameters = ["spFirst", "spSecond"] }));
        Assert.Contains("spFirst", e.Message);
        Assert.Contains("spSecond", e.Message);
        Assert.Contains("one root register space", e.Message);
    }

    // ---------------------------------------------------------------- 5. a baked DXIL package

    [DxcFact]
    public void A_baked_dxil_package_round_trips_through_a_file_and_projects_a_shader_desc()
    {
        // The shipping path: bake at build time, write the package, read it at run time with no
        // compiler, project, hand NRI the desc. What a DXIL package carries is different from a
        // SPIR-V one in exactly the ways guide.md's "Targeting D3D12" lists, and each is asserted
        // on the side that was read back, because that is the side a game sees.
        var c = ShaderCorpus.Case("compute-fixed");
        var baked = NriDeviceTests.Bake(c.Source, c.EntryPoints[0], NriDeviceTests.Dxil);

        var path = Path.Combine(Path.GetTempPath(), $"prismslang-dxil-{Guid.NewGuid():N}.pslb");
        ShaderPackage restored;
        try
        {
            baked.WriteToFile(path);
            restored = ShaderPackage.ReadFromFile(path);
        }
        finally
        {
            File.Delete(path);
        }

        var entry = Assert.Single(restored.EntryPoints);
        Assert.Equal("cfCompute", entry.Name);
        Assert.Equal(SlangStage.SLANG_STAGE_COMPUTE, entry.Stage);
        Assert.Equal(baked.EntryPoints[0].Code, entry.Code);
        Assert.True(entry.Code.AsSpan(0, 4).SequenceEqual("DXBC"u8), "the baked code is not a DXBC container");

        // Nothing to read these from: there is no SPIR-V module in the package. The thread-group
        // size is still in the layout, which is where the projection reads it from.
        Assert.Equal(PrismSlang.Spirv.SpirvVersion.Unspecified, entry.SpirvVersion);
        Assert.Null(entry.WorkgroupSize);
        Assert.Equal("main", entry.ModuleEntryPointName);

        var desc = entry.ToNriShaderDesc();
        Assert.Equal(StageBits.COMPUTE_SHADER, desc.Stage);
        Assert.Equal("main", desc.EntryPointName);
        Assert.Same(entry.Code, desc.Bytecode);

        var projection = restored.Layout.ToNri(Capped);
        var stage = Assert.Single(projection.Stages);
        Assert.Equal(new ThreadGroupSize(8, 4, 2), stage.ThreadGroupSize);
        var (_, range) = projection.Range("cfOutput")
            ?? throw new InvalidOperationException("the restored projection has no range named cfOutput");
        Assert.Equal(DescriptorType.STORAGE_STRUCTURED_BUFFER, range.Desc.descriptorType);
        Assert.Equal(0u, range.Desc.baseRegisterIndex);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The register class an NRI descriptor type is bound in on D3D12.</summary>
    private static SlangParameterCategory RegisterClassOf(DescriptorType type) => type switch
    {
        DescriptorType.CONSTANT_BUFFER => SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER,
        DescriptorType.SAMPLER => SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SAMPLER_STATE,
        DescriptorType.TEXTURE or DescriptorType.BUFFER or DescriptorType.STRUCTURED_BUFFER
            or DescriptorType.ACCELERATION_STRUCTURE => SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE,
        DescriptorType.STORAGE_TEXTURE or DescriptorType.STORAGE_BUFFER
            or DescriptorType.STORAGE_STRUCTURED_BUFFER => SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS,

        // INPUT_ATTACHMENT and MUTABLE have no register class; the first is refused by dxc before a
        // DXIL layout exists, and the projection never emits the second.
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "no D3D register class"),
    };

    private static char Register(DescriptorType type) => RegisterClassOf(type) switch
    {
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER => 'b',
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SAMPLER_STATE => 's',
        SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS => 'u',
        _ => 't',
    };
}
