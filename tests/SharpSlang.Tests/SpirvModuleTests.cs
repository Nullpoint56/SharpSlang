// The SPIR-V reader, on its own.
//
// Half of these need no compiler: a hand-built module is the only way to test the malformed cases,
// because Slang will not emit one. The other half check the reader against real Slang output, which
// is the part that would rot silently if Slang changed what it emits.

using SharpSlang.Spirv;

namespace SharpSlang.Tests;

public class SpirvModuleTests
{
    // ---------------------------------------------------------------- hand-built modules

    /// <summary>A minimal well-formed module: header plus one OpEntryPoint.</summary>
    private static byte[] Build(uint versionWord, params (uint Model, string Name)[] entryPoints)
    {
        var words = new List<uint> { 0x07230203, versionWord, 0x000D_0007, 32, 0 };

        foreach (var (model, name) in entryPoints)
        {
            var literal = EncodeString(name);
            // OpEntryPoint = 15; operands are model, id, name literal, then interface ids (none).
            var count = 3 + literal.Count;
            words.Add(((uint)count << 16) | 15);
            words.Add(model);
            words.Add(1);
            words.AddRange(literal);
        }

        return words.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private static List<uint> EncodeString(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s).ToList();
        bytes.Add(0);
        while (bytes.Count % 4 != 0) bytes.Add(0);

        var words = new List<uint>();
        for (var i = 0; i < bytes.Count; i += 4)
            words.Add(BitConverter.ToUInt32(bytes.ToArray(), i));
        return words;
    }

    [Fact]
    public void Reads_the_header_and_entry_points()
    {
        var info = SpirvModule.Read(Build(0x0001_0500, (4, "fragmentMain")));

        Assert.Equal(SpirvVersion.V1_5, info.Version);
        Assert.Equal(32u, info.Bound);

        var ep = Assert.Single(info.EntryPoints);
        Assert.Equal(SpirvExecutionModel.Fragment, ep.ExecutionModel);
        Assert.Equal("fragmentMain", ep.Name);
    }

    [Theory]
    [InlineData("main")]              // 5 bytes -> 2 words, with a spare NUL
    [InlineData("vs")]                // 3 bytes -> 1 word exactly
    [InlineData("abc")]               // 4 bytes -> 1 word, terminator lands on the boundary
    [InlineData("aVeryLongEntryPointNameThatSpansSeveralWords")]
    public void Names_decode_at_every_word_boundary(string name)
    {
        // SPIR-V packs strings four bytes to a word, NUL-terminated and zero-padded. The name whose
        // length is an exact multiple of four is the one an off-by-one gets wrong.
        var info = SpirvModule.Read(Build(0x0001_0500, (0, name)));
        Assert.Equal(name, Assert.Single(info.EntryPoints).Name);
    }

    [Fact]
    public void Reads_several_entry_points_in_module_order()
    {
        var info = SpirvModule.Read(Build(0x0001_0600, (0, "vertexMain"), (4, "fragmentMain")));

        Assert.Equal(SpirvVersion.V1_6, info.Version);
        Assert.Equal(["vertexMain", "fragmentMain"], info.EntryPoints.Select(e => e.Name));
        Assert.Equal(
            [SpirvExecutionModel.Vertex, SpirvExecutionModel.Fragment],
            info.EntryPoints.Select(e => e.ExecutionModel));
    }

    [Fact]
    public void Single_entry_point_refuses_to_guess()
    {
        // Called on a whole-program module, this must not silently return the first of several:
        // the caller is about to put the answer in VkPipelineShaderStageCreateInfo::pName.
        var two = Build(0x0001_0500, (0, "vertexMain"), (4, "fragmentMain"));
        var ex = Assert.Throws<InvalidDataException>(() => SpirvModule.SingleEntryPoint(two));
        Assert.Contains("vertexMain", ex.Message);
        Assert.Contains("fragmentMain", ex.Message);

        var none = Build(0x0001_0500);
        Assert.Throws<InvalidDataException>(() => SpirvModule.SingleEntryPoint(none));
    }

    [Fact]
    public void Not_spirv_is_rejected_rather_than_misread()
    {
        Assert.False(SpirvModule.IsSpirv([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]));
        Assert.False(SpirvModule.IsSpirv([]));

        // Wrong magic.
        Assert.Contains("magic", Assert.Throws<InvalidDataException>(
            () => SpirvModule.Read(new byte[20])).Message);

        // Not a whole number of words.
        Assert.Contains("word", Assert.Throws<InvalidDataException>(
            () => SpirvModule.Read(new byte[21])).Message);

        // Shorter than the header.
        Assert.Contains("header", Assert.Throws<InvalidDataException>(
            () => SpirvModule.Read(BitConverter.GetBytes(0x07230203u))).Message);
    }

    [Fact]
    public void An_instruction_running_past_the_end_is_an_error_not_a_read_overrun()
    {
        var words = new List<uint> { 0x07230203, 0x0001_0500, 0, 1, 0, (999u << 16) | 15 };
        var bytes = words.SelectMany(BitConverter.GetBytes).ToArray();

        Assert.Contains("malformed", Assert.Throws<InvalidDataException>(
            () => SpirvModule.Read(bytes)).Message);
    }

    [Fact]
    public void A_zero_length_instruction_terminates_instead_of_looping()
    {
        // Zero would advance the cursor by nothing. Getting this wrong hangs rather than fails.
        var words = new List<uint> { 0x07230203, 0x0001_0500, 0, 1, 0, 0 };
        var bytes = words.SelectMany(BitConverter.GetBytes).ToArray();

        Assert.Throws<InvalidDataException>(() => SpirvModule.Read(bytes));
    }

    // ---------------------------------------------------------------- version helpers

    [Theory]
    [InlineData(SpirvVersion.V1_0, 1, 0, "spirv_1_0")]
    [InlineData(SpirvVersion.V1_3, 1, 3, "spirv_1_3")]
    [InlineData(SpirvVersion.V1_5, 1, 5, "spirv_1_5")]
    [InlineData(SpirvVersion.V1_6, 1, 6, "spirv_1_6")]
    public void Versions_map_to_profiles_and_back(SpirvVersion v, int major, int minor, string profile)
    {
        Assert.Equal(major, v.Major());
        Assert.Equal(minor, v.Minor());
        Assert.Equal(profile, v.ToProfileName());
        Assert.Equal(v, SpirvVersions.FromProfileName(profile));
        Assert.Equal($"{major}.{minor}", v.ToDisplayString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("glsl_450")]
    [InlineData("spirv_9_9")]     // well-formed but not a version that exists
    [InlineData("spirv_")]
    [InlineData("spirv_1")]
    public void Unrecognised_profiles_report_unspecified_rather_than_a_wrong_version(string? profile) =>
        Assert.Equal(SpirvVersion.Unspecified, SpirvVersions.FromProfileName(profile));

    [Fact]
    public void Versions_are_ordered_by_value()
    {
        // a host compares them with <= against its device's limit, so the encoding has to sort correctly.
        Assert.True(SpirvVersion.V1_0 < SpirvVersion.V1_3);
        Assert.True(SpirvVersion.V1_3 < SpirvVersion.V1_5);
        Assert.True(SpirvVersion.V1_5 < SpirvVersion.V1_6);
    }

    // ---------------------------------------------------------------- against real Slang output

    [NativeFact]
    public void Reads_what_slang_actually_emits()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { SpirvVersion = SpirvVersion.V1_5 }],
        });
        using var module = session.LoadModuleFromSource("s", "s.slang", EntryPointNameTests.TwoStages);
        using var vs = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, vs);

        var code = program.GetEntryPointCodeBytes(0, 0);

        Assert.True(SpirvModule.IsSpirv(code));
        var info = SpirvModule.Read(code);
        Assert.Equal(SpirvVersion.V1_5, info.Version);
        Assert.Equal(SpirvExecutionModel.Vertex, Assert.Single(info.EntryPoints).ExecutionModel);
        Assert.NotEqual(0u, info.Bound);
    }
}
