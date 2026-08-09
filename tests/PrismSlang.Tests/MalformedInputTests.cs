// Two readers take bytes that came from somewhere else.
//
// SpirvModule parses a module, and ShaderPackage parses a container — both from disk, in a runtime
// that has no compiler and no way to re-derive what it is reading. A truncated file, a partial
// write, a corrupted download or a package from a different build all arrive as bytes, and the only
// acceptable answers are "here is the data" and a diagnosable exception. Not a hang, not an
// OutOfMemoryException from a length prefix, and not an unhandled index out of range that reads
// like a bug in the caller.
//
// Everything here is deterministic. A seeded sequence that finds a defect on CI has to find it
// again on the machine that fixes it.

using PrismSlang.Baking;
using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Spirv;

namespace PrismSlang.Tests;

public class MalformedInputTests
{
    private static byte[] ValidModule()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", """
            struct Uniforms { float4 tint; float4x4 transform; };
            ConstantBuffer<Uniforms> uniforms;
            Texture2D    albedo;
            SamplerState albedoSampler;

            [shader("fragment")]
            float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
            { return mul(uniforms.transform, albedo.Sample(albedoSampler, uv) * uniforms.tint); }
            """);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);
        return program.GetEntryPointCodeBytes(0);
    }

    /// <summary>A tiny deterministic generator. Not cryptographic and not meant to be.</summary>
    private static IEnumerable<uint> Sequence(uint seed)
    {
        var state = seed;
        while (true)
        {
            state = (state * 1664525) + 1013904223;
            yield return state;
        }
    }

    /// <summary>Reading may fail, but only in ways a caller can act on.</summary>
    private static void MustFailCleanly(Action read, string what)
    {
        try
        {
            read();
        }
        catch (InvalidDataException)
        {
            // The documented failure.
        }
        catch (Exception e)
        {
            Assert.Fail($"{what} threw {e.GetType().Name} rather than InvalidDataException: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- SPIR-V

    [NativeFact]
    public void Truncating_a_module_at_every_word_boundary_never_does_worse_than_throw()
    {
        var valid = ValidModule();

        for (var length = 0; length < valid.Length; length += 4)
        {
            var truncated = valid[..length];
            var at = length;

            Assert.False(SpirvModule.IsSpirv(truncated) && truncated.Length < 20);
            MustFailCleanly(() => SpirvModule.Read(truncated), $"Read of {at} bytes");
            MustFailCleanly(() => SpirvModule.ReadVersion(truncated), $"ReadVersion of {at} bytes");
            MustFailCleanly(() => SpirvModule.SingleEntryPoint(truncated), $"SingleEntryPoint of {at} bytes");
        }
    }

    [NativeFact]
    public void Cutting_a_module_at_a_non_word_boundary_is_rejected()
    {
        var valid = ValidModule();

        for (var trim = 1; trim <= 3; trim++)
        {
            var ragged = valid[..(valid.Length - trim)];
            Assert.False(SpirvModule.IsSpirv(ragged));
            Assert.Throws<InvalidDataException>(() => SpirvModule.Read(ragged));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Nothing_that_is_not_spirv_is_read_as_spirv(int shape)
    {
        byte[] bytes = shape switch
        {
            0 => [],
            1 => [0, 0, 0, 0],
            2 => "not a shader, just some text that happens to be here"u8.ToArray(),
            _ => new byte[4096],
        };

        Assert.False(SpirvModule.IsSpirv(bytes));
        Assert.Throws<InvalidDataException>(() => SpirvModule.Read(bytes));
    }

    [NativeFact]
    public void A_corrupted_module_never_hangs_crashes_or_allocates_wildly()
    {
        // Word-granular corruption, which is what reaches an instruction-stream parser: a bad
        // length word is the one that loops forever or indexes past the end.
        var valid = ValidModule();
        var words = valid.Length / 4;
        var random = Sequence(seed: 0x5EED).GetEnumerator();

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            random.MoveNext();
            var word = (int)(random.Current % (uint)words);
            random.MoveNext();
            var value = random.Current;

            var corrupted = valid.ToArray();
            BitConverter.TryWriteBytes(corrupted.AsSpan(word * 4, 4), value);

            var at = word;
            var to = value;
            MustFailCleanly(
                () => SpirvModule.Read(corrupted),
                $"Read with word {at} set to 0x{to:X8} (seed 0x5EED, iteration {iteration})");
        }
    }

    [NativeFact]
    public void An_instruction_claiming_to_run_past_the_end_is_rejected_rather_than_trusted()
    {
        // The specific shape worth naming: word count 0 loops forever, and a huge word count reads
        // off the end. Both are one bad word in an otherwise valid module.
        var valid = ValidModule();

        foreach (var count in new uint[] { 0, 1, 0xFFFF, 0x7FFF })
        {
            var corrupted = valid.ToArray();
            var header = BitConverter.ToUInt32(corrupted, 5 * 4);
            BitConverter.TryWriteBytes(corrupted.AsSpan(5 * 4, 4), (count << 16) | (header & 0xFFFF));

            MustFailCleanly(() => SpirvModule.Read(corrupted), $"Read with a length word of {count}");
        }
    }

    // ---------------------------------------------------------------- packages

    private static byte[] ValidPackage()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        const string Source = """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return 1; }
            """;
        using var module = session.LoadModuleFromSource("m", "m.slang", Source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        using var buffer = new MemoryStream();
        ShaderPackageBaker.Bake(program, Source).WriteTo(buffer);
        return buffer.ToArray();
    }

    [NativeFact]
    public void Truncating_a_package_never_does_worse_than_throw()
    {
        var valid = ValidPackage();

        // Every length, not every fourth: the container is byte-oriented, with 7-bit-encoded string
        // lengths that can be cut mid-prefix.
        for (var length = 0; length < valid.Length; length++)
        {
            var at = length;
            MustFailCleanly(
                () => ShaderPackage.ReadFrom(new MemoryStream(valid[..at])),
                $"ReadFrom of {at} of {valid.Length} bytes");
        }
    }

    [NativeFact]
    public void A_corrupted_package_never_hangs_crashes_or_allocates_wildly()
    {
        // A length prefix is the dangerous field: a corrupted one asks the reader to allocate an
        // array sized by whatever the corruption happened to be.
        var valid = ValidPackage();
        var random = Sequence(seed: 0xC0FFEE).GetEnumerator();

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            random.MoveNext();
            var index = (int)(random.Current % (uint)valid.Length);
            random.MoveNext();
            var value = (byte)(random.Current & 0xFF);

            var corrupted = valid.ToArray();
            corrupted[index] = value;

            var at = index;
            var to = value;
            MustFailCleanly(
                () => ShaderPackage.ReadFrom(new MemoryStream(corrupted)),
                $"ReadFrom with byte {at} set to 0x{to:X2} (seed 0xC0FFEE, iteration {iteration})");
        }
    }

    [NativeFact]
    public void A_package_from_another_format_version_says_to_rebake()
    {
        var valid = ValidPackage();
        BitConverter.TryWriteBytes(valid.AsSpan(4, 4), 2u);

        var e = Assert.Throws<InvalidDataException>(() => ShaderPackage.ReadFrom(new MemoryStream(valid)));
        Assert.Contains("Rebake", e.Message);
    }

    [Fact]
    public void Something_that_is_not_a_package_at_all_is_rejected_by_its_magic()
    {
        var e = Assert.Throws<InvalidDataException>(
            () => ShaderPackage.ReadFrom(new MemoryStream(new byte[64])));

        Assert.Contains("magic", e.Message);
    }
}
