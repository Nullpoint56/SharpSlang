// The NRI projection stored in a package at bake time and read back at load
// (AGENTS.md#nri-projection).
//
// The oracle is the projection itself: what a game loads from a package must be, field by field,
// what ToNri computes from the same layout, on every corpus shader and on both targets a device
// here runs (SPIR-V and DXIL). The comparison walks every public field of every generated desc
// struct by reflection, so a field SharpNRI.Descs adds and the section does not store fails here.

using System.Reflection;
using SharpNRI;
using SharpSlang.Baking;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class NriPackageTests(ITestOutputHelper output)
{
    private static readonly NriProjectionOptions Capped = new() { UnboundedArrayCap = 64 };

    [NativeTheory]
    [MemberData(nameof(NriDeviceTests.Cases), MemberType = typeof(NriDeviceTests))]
    public void The_stored_projection_of_a_spirv_layout_reads_back_equal_to_a_fresh_one(string caseName) =>
        RoundTrip(caseName, new TargetOptions());

    [DxcTheory]
    [MemberData(nameof(NriDeviceTests.Cases), MemberType = typeof(NriDeviceTests))]
    public void The_stored_projection_of_a_dxil_layout_reads_back_equal_to_a_fresh_one(string caseName) =>
        RoundTrip(caseName, NriDeviceTests.Dxil);

    private void RoundTrip(string caseName, TargetOptions target)
    {
        var c = ShaderCorpus.Case(caseName);
        var layout = Reflect(c.Source, [.. c.EntryPoints], target);
        var package = Package(layout);

        NriPipelineProjection fresh;
        try
        {
            fresh = layout.ToNri(Capped);
        }
        catch (NotSupportedException refused)
        {
            // A shader the projection refuses is refused at bake time with the same words, and
            // the package is left without a section rather than with a partial one.
            var atBake = Assert.Throws<NotSupportedException>(() => package.WithNriProjection(Capped));
            Assert.Equal(refused.Message, atBake.Message);
            Assert.Null(package.Section(NriPackage.SectionName));
            output.WriteLine($"refused at bake time: {refused.Message}");
            return;
        }

        var loaded = ThroughBytes(package.WithNriProjection(Capped)).ReadNriProjection();
        Assert.NotNull(loaded);
        AssertSameProjection(fresh, loaded);
    }

    [DxcFact]
    public void Root_constants_promoted_by_name_are_stored_with_the_options_they_were_projected_with()
    {
        // The projection depends on the host's options, and the section stores their result: a
        // DXIL constant buffer promoted to root constants reads back as root constants.
        const string Source = """
            struct Pc { float4 tint; uint index; };
            ConstantBuffer<Pc> pc;
            RWStructuredBuffer<float4> output;

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void main() { output[pc.index] = pc.tint; }
            """;

        var layout = Reflect(Source, ["main"], NriDeviceTests.Dxil);
        var options = new NriProjectionOptions { RootConstantParameters = ["pc"] };
        var fresh = layout.ToNri(options);
        Assert.Single(fresh.RootConstants);

        var loaded = ThroughBytes(Package(layout).WithNriProjection(options)).ReadNriProjection()!;
        AssertSameProjection(fresh, loaded);
        Assert.Equal("pc", loaded.RootConstants[0].Name);

        // Without the options the stored projection is a different one, and says so.
        var plain = ThroughBytes(Package(layout).WithNriProjection()).ReadNriProjection()!;
        Assert.Empty(plain.RootConstants);
    }

    [NativeFact]
    public void A_package_without_the_section_reads_as_no_stored_projection()
    {
        var layout = NriProjectionTests.Reflect(ShaderCorpus.Case("compute-fixed").Source, [.. ShaderCorpus.Case("compute-fixed").EntryPoints]);
        Assert.Null(ThroughBytes(Package(layout)).ReadNriProjection());
    }

    [NativeFact]
    public void The_section_is_one_named_section_the_reader_keeps_whole()
    {
        var c = ShaderCorpus.Case("compute-fixed");
        var package = Package(NriProjectionTests.Reflect(c.Source, [.. c.EntryPoints]))
            .WithSection(new ShaderPackageSection("someone-elses", [1, 2, 3]))
            .WithNriProjection(Capped);

        var read = ThroughBytes(package);
        Assert.Equal(2, read.Sections.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, read.Section("someone-elses")!.Data);
        Assert.Equal(package.Section(NriPackage.SectionName)!.Data, read.Section(NriPackage.SectionName)!.Data);

        // Projected again, the section is replaced, not doubled.
        var twice = read.WithNriProjection(Capped);
        Assert.Equal(2, twice.Sections.Count);
    }

    [NativeFact]
    public void A_section_written_against_other_nri_descs_is_refused_with_rebake()
    {
        // The section stores NRI's enum values and struct fields as SharpNRI.Descs numbers them,
        // so a section from another NriHeaders.Version is refused, never misread.
        var c = ShaderCorpus.Case("compute-fixed");
        var package = Package(NriProjectionTests.Reflect(c.Source, [.. c.EntryPoints])).WithNriProjection(Capped);
        var data = package.Section(NriPackage.SectionName)!.Data;

        // After the section's own format word: the NRI version, as NriHeaders.Version.
        Assert.Equal(NriHeaders.Version, BitConverter.ToInt32(data, 4));
        var other = (byte[])data.Clone();
        BitConverter.TryWriteBytes(other.AsSpan(4, 4), NriHeaders.Version - 1);

        var tampered = package.WithSection(new ShaderPackageSection(NriPackage.SectionName, other));
        var e = Assert.Throws<InvalidDataException>(() => ThroughBytes(tampered).ReadNriProjection());
        Assert.Contains("Rebake", e.Message, StringComparison.Ordinal);
        Assert.Contains($"NRI {NriHeaders.Version}'s", e.Message, StringComparison.Ordinal);
    }

    [NativeFact]
    public void A_truncated_section_is_refused_as_data()
    {
        var c = ShaderCorpus.Case("parameter-blocks");
        var package = Package(NriProjectionTests.Reflect(c.Source, [.. c.EntryPoints])).WithNriProjection(Capped);
        var data = package.Section(NriPackage.SectionName)!.Data;

        // Every prefix: one failure mode, InvalidDataException, as ShaderPackage.ReadFrom has.
        for (var length = 0; length < data.Length; length++)
        {
            var cut = package.WithSection(new ShaderPackageSection(NriPackage.SectionName, data[..length]));
            Assert.Throws<InvalidDataException>(cut.ReadNriProjection);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Links and reflects for one target; no code is generated, so DXIL needs no dxc.</summary>
    internal static ProgramLayout Reflect(string source, string[] entryPoints, TargetOptions target)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [target] });
        using var module = session.LoadModuleFromSource("np", "np.slang", source);
        var eps = entryPoints.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. eps]);
            return program.Reflect(0);
        }
        finally
        {
            foreach (var e in eps) e.Dispose();
        }
    }

    /// <summary>A package around a layout, with no code: the section is about the layout alone.</summary>
    internal static ShaderPackage Package(ProgramLayout layout) => new()
    {
        SlangVersion = SlangGlobalSession.PinnedBuildTag,
        SourceHash = new byte[32],
        Layout = layout,
        EntryPoints = [],
    };

    /// <summary>Written to bytes and read back, as a game loads it.</summary>
    internal static ShaderPackage ThroughBytes(ShaderPackage package)
    {
        using var stream = new MemoryStream();
        package.WriteTo(stream);
        stream.Position = 0;
        return ShaderPackage.ReadFrom(stream);
    }

    /// <summary>Every member of two projections, and every field of every desc struct in them.</summary>
    internal static void AssertSameProjection(NriPipelineProjection expected, NriPipelineProjection actual)
    {
        var e = expected.Layout;
        var a = actual.Layout;
        Assert.Equal(e.RootRegisterSpace, a.RootRegisterSpace);
        Assert.Equal(e.ShaderStages, a.ShaderStages);
        Assert.Equal(e.Flags, a.Flags);
        SameStructs("Layout.RootConstants", e.RootConstants, a.RootConstants);
        SameStructs("Layout.RootDescriptors", e.RootDescriptors, a.RootDescriptors);
        Assert.Equal(e.RootSamplers.Length, a.RootSamplers.Length);
        Assert.Equal(e.DescriptorSets.Length, a.DescriptorSets.Length);
        for (var s = 0; s < e.DescriptorSets.Length; s++)
        {
            Assert.Equal(e.DescriptorSets[s].RegisterSpace, a.DescriptorSets[s].RegisterSpace);
            SameStructs($"Layout.DescriptorSets[{s}].Ranges", e.DescriptorSets[s].Ranges, a.DescriptorSets[s].Ranges);
        }

        Assert.Equal(expected.DescriptorSets.Count, actual.DescriptorSets.Count);
        for (var s = 0; s < expected.DescriptorSets.Count; s++)
        {
            var es = expected.DescriptorSets[s];
            var @as = actual.DescriptorSets[s];
            Assert.Equal((es.SetIndex, es.RegisterSpace, es.Ranges.Count), (@as.SetIndex, @as.RegisterSpace, @as.Ranges.Count));
            for (var r = 0; r < es.Ranges.Count; r++)
            {
                var er = es.Ranges[r];
                var ar = @as.Ranges[r];
                Assert.Equal(
                    (er.RangeIndex, er.Name, er.IsUnbounded, er.InputAttachmentIndex, er.ImageFormat, er.CounterRangeIndex),
                    (ar.RangeIndex, ar.Name, ar.IsUnbounded, ar.InputAttachmentIndex, ar.ImageFormat, ar.CounterRangeIndex));
                SameStruct($"DescriptorSets[{s}].Ranges[{r}].Desc", er.Desc, ar.Desc);

                // The layout's arrays are the records' descs, as ToNri builds them.
                SameStruct($"Layout.DescriptorSets[{s}].Ranges[{r}] against the record", ar.Desc, a.DescriptorSets[s].Ranges[r]);
            }
        }

        Assert.Equal(expected.RootConstants.Count, actual.RootConstants.Count);
        for (var i = 0; i < expected.RootConstants.Count; i++)
        {
            Assert.Equal((expected.RootConstants[i].Index, expected.RootConstants[i].Name), (actual.RootConstants[i].Index, actual.RootConstants[i].Name));
            SameStruct($"RootConstants[{i}].Desc", expected.RootConstants[i].Desc, actual.RootConstants[i].Desc);
            SameStruct($"Layout.RootConstants[{i}] against the record", actual.RootConstants[i].Desc, a.RootConstants[i]);
        }

        Assert.Equal(expected.VertexAttributes, actual.VertexAttributes);
        Assert.Equal(expected.FragmentOutputs, actual.FragmentOutputs);
        Assert.Equal(expected.Stages, actual.Stages);
    }

    private static void SameStructs<T>(string what, T[] expected, T[] actual) where T : struct
    {
        Assert.True(expected.Length == actual.Length, $"{what}: {expected.Length} expected, {actual.Length} read");
        for (var i = 0; i < expected.Length; i++) SameStruct($"{what}[{i}]", expected[i], actual[i]);
    }

    private static void SameStruct<T>(string what, T expected, T actual) where T : struct
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var e = field.GetValue(expected);
            var a = field.GetValue(actual);
            Assert.True(Equals(e, a), $"{what}.{field.Name}: {e} expected, {a} read");
        }
    }
}
