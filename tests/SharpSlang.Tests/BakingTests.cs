// Offline baking: compile once at build time, load at run time with no compiler present.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharpSlang.Baking;
using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class BakingTests
{
    private const string Shader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D    albedo;
        SamplerState albedoSampler;

        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position, float2 uv)
        {
            VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv; return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        {
            return float4(albedo.Sample(albedoSampler, i.uv).rgb * u.tint * u.exposure, 1.0);
        }
        """;

    private static ShaderPackage BakeShader(string source = Shader)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", source);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        return ShaderPackageBaker.Bake(program, source, 0);
    }

    [NativeFact]
    public void Bake_captures_code_and_layout_for_every_entry_point()
    {
        var package = BakeShader();

        Assert.Equal(["vertexMain", "fragmentMain"], package.EntryPoints.Select(e => e.Name));
        Assert.Equal(SlangStage.SLANG_STAGE_VERTEX, package.EntryPoint("vertexMain")!.Stage);
        Assert.Equal(SlangStage.SLANG_STAGE_PIXEL, package.EntryPoint("fragmentMain")!.Stage);
        Assert.All(package.EntryPoints, e => Assert.NotEmpty(e.Code));

        Assert.Equal(SlangGlobalSession.BuildTag, package.SlangVersion);
        Assert.Equal(32, package.SourceHash.Length);
        Assert.NotEmpty(package.Layout.Parameters);
    }

    [NativeFact]
    public void Package_round_trips_through_a_stream()
    {
        var original = BakeShader();

        using var buffer = new MemoryStream();
        original.WriteTo(buffer);
        buffer.Position = 0;
        var restored = ShaderPackage.ReadFrom(buffer);

        Assert.Equal(original.SlangVersion, restored.SlangVersion);
        Assert.Equal(original.SourceHash, restored.SourceHash);
        Assert.Equal(
            original.EntryPoints.Select(e => (e.Name, e.Stage, e.Code.Length)),
            restored.EntryPoints.Select(e => (e.Name, e.Stage, e.Code.Length)));

        // The bytes must survive exactly — this is what goes to vkCreateShaderModule.
        foreach (var e in original.EntryPoints)
            Assert.Equal(e.Code, restored.EntryPoint(e.Name)!.Code);

        // And the layout a renderer binds against.
        var before = (StructLayout)((ContainerLayout)original.Layout.Parameter("u")!.Type).ElementType;
        var after = (StructLayout)((ContainerLayout)restored.Layout.Parameter("u")!.Type).ElementType;
        Assert.Equal(before.Fields.Select(f => (f.Name, f.UniformOffset)),
                     after.Fields.Select(f => (f.Name, f.UniformOffset)));
    }

    [NativeFact]
    public void Sections_round_trip_by_name_whole_and_in_order()
    {
        // Format 5's optional sections: what another package wrote at bake time, kept whole by a
        // reader that does not know what is in it.
        var original = BakeShader()
            .WithSection(new ShaderPackageSection("first", [1, 2, 3]))
            .WithSection(new ShaderPackageSection("second", []))
            .WithSection(new ShaderPackageSection("first", [4, 5]));

        Assert.Equal(["second", "first"], original.Sections.Select(x => x.Name));

        using var buffer = new MemoryStream();
        original.WriteTo(buffer);
        buffer.Position = 0;
        var restored = ShaderPackage.ReadFrom(buffer);

        Assert.Equal(["second", "first"], restored.Sections.Select(x => x.Name));
        Assert.Equal(new byte[] { 4, 5 }, restored.Section("first")!.Data);
        Assert.Empty(restored.Section("second")!.Data);
        Assert.Null(restored.Section("absent"));

        // Everything else is the same package.
        Assert.Equal(original.EntryPoints.Select(e => e.Code), restored.EntryPoints.Select(e => e.Code));
        Assert.Equal(original.Layout.ToJson(), restored.Layout.ToJson());
    }

    [NativeFact]
    public void A_package_without_sections_writes_an_empty_list_and_reads_none()
    {
        var original = BakeShader();
        Assert.Empty(original.Sections);

        using var buffer = new MemoryStream();
        original.WriteTo(buffer);

        // The last four bytes are the section count, zero.
        Assert.Equal(0u, BitConverter.ToUInt32(buffer.ToArray(), (int)buffer.Length - 4));
        buffer.Position = 0;
        Assert.Empty(ShaderPackage.ReadFrom(buffer).Sections);
    }

    [NativeFact]
    public void Two_sections_of_one_name_are_refused_on_write_and_on_read()
    {
        var original = BakeShader();

        // Built around WithSection, which replaces: the writer refuses a list that names one twice.
        var doubled = new ShaderPackage
        {
            SlangVersion = original.SlangVersion,
            SourceHash = original.SourceHash,
            Layout = original.Layout,
            EntryPoints = original.EntryPoints,
            Sections = [new("twice", [1]), new("twice", [2])],
        };
        Assert.Throws<InvalidOperationException>(() => doubled.WriteTo(new MemoryStream()));

        // And a file that carries two anyway is not a package.
        var single = original.WithSection(new ShaderPackageSection("twice", [1]));
        using var buffer = new MemoryStream();
        single.WriteTo(buffer);
        var bytes = buffer.ToArray();

        // Rewrite the count to 2 and append the same section again: name (7-bit length + text),
        // length, data.
        var tail = new byte[] { 5, (byte)'t', (byte)'w', (byte)'i', (byte)'c', (byte)'e', 1, 0, 0, 0, 1 };
        var countAt = bytes.Length - tail.Length - 4;
        Assert.Equal(1u, BitConverter.ToUInt32(bytes, countAt));
        BitConverter.TryWriteBytes(bytes.AsSpan(countAt, 4), 2u);
        var forged = bytes.Concat(tail).ToArray();

        var e = Assert.Throws<InvalidDataException>(() => ShaderPackage.ReadFrom(new MemoryStream(forged)));
        Assert.Contains("two sections named 'twice'", e.Message, StringComparison.Ordinal);
    }

    [NativeFact]
    public void Round_tripped_package_carries_usable_descriptor_sets()
    {
        // The point of baking: everything the renderer needs, with no compiler in the process.
        var original = BakeShader();
        using var buffer = new MemoryStream();
        original.WriteTo(buffer);
        buffer.Position = 0;

        var restored = ShaderPackage.ReadFrom(buffer);
        var set = Assert.Single(restored.Layout.DescriptorSets);
        Assert.Equal(3, set.Ranges.Count);
    }

    [NativeFact]
    public void Staleness_is_detectable_without_recompiling()
    {
        var package = BakeShader();

        Assert.True(package.IsUpToDate(Shader, SlangGlobalSession.BuildTag));

        // Changed source.
        Assert.False(package.IsUpToDate(Shader + "\n// touched", SlangGlobalSession.BuildTag));

        // Changed compiler: Slang's layout rules are not stable across versions, so a bake from a
        // different build must be rejected even if the source is byte-identical.
        Assert.False(package.IsUpToDate(Shader, "2025.13.2"));
    }

    [NativeFact]
    public void Round_trips_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpslang-{Guid.NewGuid():N}.pslb");
        try
        {
            BakeShader().WriteToFile(path);
            var restored = ShaderPackage.ReadFromFile(path);
            Assert.Equal(2, restored.EntryPoints.Count);
            Assert.NotEmpty(restored.EntryPoint("vertexMain")!.Code);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Reading_something_that_is_not_a_package_fails_clearly()
    {
        // A truncated or wrong file must not be interpreted as a shader — the bytes end up at
        // vkCreateShaderModule, where garbage is a device lost rather than an exception.
        using var junk = new MemoryStream([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);
        var ex = Assert.Throws<InvalidDataException>(() => ShaderPackage.ReadFrom(junk));
        Assert.Contains("magic", ex.Message);
    }

    [Fact]
    public void Future_format_version_is_rejected_rather_than_misread()
    {
        using var buffer = new MemoryStream();
        using (var w = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0x424C5350u);  // correct magic
            w.Write(99u);          // a version from the future
        }
        buffer.Position = 0;

        var ex = Assert.Throws<InvalidDataException>(() => ShaderPackage.ReadFrom(buffer));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void Hashing_is_stable_and_distinguishes_sources()
    {
        Assert.Equal(ShaderPackage.HashSource("abc"), ShaderPackage.HashSource("abc"));
        Assert.NotEqual(ShaderPackage.HashSource("abc"), ShaderPackage.HashSource("abd"));
        Assert.Equal(32, ShaderPackage.HashSource("abc").Length);
    }

    [NativeFact]
    public void A_baked_package_does_not_depend_on_which_platform_baked_it()
    {
        // A package is a build artifact, so its bytes are part of its contract. `WriteIndented`
        // newlines default to Environment.NewLine, and the layout JSON goes straight into the
        // package — so the same shader baked on Windows and on Linux produced packages differing by
        // one byte per line, 214 of them on an ordinary layout. Both were valid and both loaded;
        // what broke was equality, which is what a content-addressed asset cache runs on.
        //
        // Found by digesting the corpus on two platforms and diffing. Asserted here instead of
        // there, because a property that only a four-runner comparison can catch is a property
        // nobody checks before pushing — and this one is visible from a single machine.
        var package = BakeShader();
        var json = package.Layout.ToJson();

        Assert.DoesNotContain('\r', json);

        // The whole container, not just the JSON: the same package written twice must be the same
        // bytes, which is the property the cache actually depends on.
        Assert.Equal(Written(package), Written(package));

        static byte[] Written(ShaderPackage package)
        {
            using var buffer = new MemoryStream();
            package.WriteTo(buffer);
            return buffer.ToArray();
        }
    }

    public static TheoryData<string> CorpusCases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    private static ShaderPackage BakeCorpusCase(ShaderCase c)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("t", "t.slang", c.Source);

        var eps = c.EntryPoints.Select(module.FindEntryPoint).ToArray();
        try
        {
            using var program = session.Link(module, eps);
            return ShaderPackageBaker.Bake(program, c.Source, 0);
        }
        finally
        {
            foreach (var e in eps) e.Dispose();
        }
    }

    private static ShaderPackage RoundTrip(ShaderPackage package)
    {
        using var buffer = new MemoryStream();
        package.WriteTo(buffer);
        buffer.Position = 0;
        return ShaderPackage.ReadFrom(buffer);
    }

    /// <summary>The first line two JSON texts differ on, for a failure message worth reading.</summary>
    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var left = i < e.Length ? e[i].Trim() : "<end of text>";
            var right = i < a.Length ? a[i].Trim() : "<end of text>";
            if (left != right) return $"first difference on line {i + 1}: wrote {left}, read back {right}";
        }
        return "no difference";
    }

    [NativeTheory]
    [MemberData(nameof(CorpusCases))]
    public void Every_corpus_layout_survives_the_package_round_trip(string caseName)
    {
        // The package is the contract between the cook and the application, and the layout JSON in it is
        // the one part that is converted rather than copied. Package_round_trips_through_a_stream
        // covers one ordinary shader, which is how an unbounded array, present in this corpus twice,
        // baked and wrote fine and could not be read back. So: every shape the corpus has, through
        // the real container, not just ToJson and FromJson.
        var c = ShaderCorpus.Case(caseName);
        var original = BakeCorpusCase(c);
        var restored = RoundTrip(original);

        var before = original.Layout.ToJson();
        var after = restored.Layout.ToJson();
        Assert.True(before == after, $"{caseName}: the layout changed in the round trip, {FirstDifference(before, after)}");

        Assert.Equal(original.EntryPoints.Select(e => e.Name), restored.EntryPoints.Select(e => e.Name));
        foreach (var e in original.EntryPoints)
            Assert.Equal(e.Code, restored.EntryPoint(e.Name)!.Code);
    }

    [NativeFact]
    public void An_unbounded_array_survives_the_package_round_trip()
    {
        // ArrayLayout.ElementCount was `required int?`, null for an unbounded array, and ToJson
        // drops nulls (WhenWritingNull). So the member was absent on write and refused on read:
        // every package containing a `Texture2D t[]` baked and wrote fine, and ReadFrom threw
        //
        //   InvalidDataException: not a readable SharpSlang shader package: JSON deserialization
        //   for type 'SharpSlang.Reflection.ArrayLayout' was missing required properties
        //   including: 'ElementCount'.
        //
        // Measured before the fix, on this test and on the two corpus cases with an unbounded
        // array, descriptor-arrays and buffer-arrays; the other corpus cases passed.
        //
        // Reported by a consumer. Its cook caught that in its up-to-date check and
        // silently re-cooked, so it surfaced only at pipeline creation.
        var original = BakeCorpusCase(ShaderCorpus.Case("descriptor-arrays"));
        var restored = RoundTrip(original);

        var unbounded = Assert.IsType<ArrayLayout>(restored.Layout.Parameter("daUnbounded")!.Type);
        Assert.Null(unbounded.ElementCount);
        Assert.False(unbounded.IsSpecializationSized);

        var fixedSize = Assert.IsType<ArrayLayout>(restored.Layout.Parameter("daFixed")!.Type);
        Assert.Equal(4, fixedSize.ElementCount);

        // Absence is the encoding of null, and stays so: the fix changed the reader, not the bytes,
        // which is what makes packages cooked before it readable without a rebake.
        using var json = JsonDocument.Parse(original.Layout.ToJson());
        var parameters = json.RootElement.GetProperty("Parameters").EnumerateArray().ToList();
        var unboundedType = parameters.Single(p => p.GetProperty("Name").GetString() == "daUnbounded").GetProperty("Type");
        var fixedType = parameters.Single(p => p.GetProperty("Name").GetString() == "daFixed").GetProperty("Type");
        Assert.False(unboundedType.TryGetProperty("ElementCount", out _));
        Assert.Equal(4, fixedType.GetProperty("ElementCount").GetInt32());
    }

    [Fact]
    public void No_serialized_layout_type_requires_a_nullable_property()
    {
        // The fault class behind the unbounded-array defect, not just its one instance: ToJson
        // writes with WhenWritingNull, so a null value is absent from the JSON, and a `required`
        // member that is absent is refused on read. A `required` nullable property therefore makes
        // every null value of it unreadable, and nothing fails until a package is loaded.
        var context = new NullabilityInfoContext();

        // Not vacuous: the context does see the assembly's annotations, on a reference type and on
        // a value type.
        Assert.Equal(NullabilityState.Nullable,
            context.Create(typeof(ResourceLayout).GetProperty(nameof(ResourceLayout.ResultType))!).ReadState);
        Assert.Equal(NullabilityState.Nullable,
            context.Create(typeof(ArrayLayout).GetProperty(nameof(ArrayLayout.ElementCount))!).ReadState);

        var offenders = typeof(ProgramLayout).Assembly.GetTypes()
            .Where(t => t.Namespace == "SharpSlang.Reflection")
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                                             | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.IsDefined(typeof(RequiredMemberAttribute), inherit: false))
            .Where(p => Nullable.GetUnderlyingType(p.PropertyType) is not null
                        || context.Create(p).ReadState == NullabilityState.Nullable)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "required nullable properties, which ToJson omits when null (WhenWritingNull) and FromJson " +
            $"then refuses as missing, so a package holding a null one cannot be read: {string.Join(", ", offenders)}");
    }

    [NativeFact]
    public void Bake_refuses_a_layout_that_does_not_read_back()
    {
        // Bake reads its own layout back before returning, so a serializer defect like the
        // unbounded-array one fails the cook, naming the line, instead of failing the load.
        var json = BakeCorpusCase(ShaderCorpus.Case("descriptor-arrays")).Layout.ToJson();

        // The real layout passes.
        ShaderPackageBaker.VerifyLayoutRoundTrips(json);

        // Delete a line the reader tolerates losing and the writer reinstates: a non-null default,
        // which still parses (IsSpecializationSized is not required) and re-serializes as written.
        // A line that does not end the object, so the JSON stays valid without its comma.
        var lines = json.Split('\n').ToList();
        var removed = lines.FindIndex(l => l.Trim() == "\"IsSpecializationSized\": false,");
        Assert.True(removed >= 0, "the layout has no array, so there is nothing to mutate");
        lines.RemoveAt(removed);
        var mutated = string.Join('\n', lines);

        var ex = Assert.Throws<InvalidOperationException>(() => ShaderPackageBaker.VerifyLayoutRoundTrips(mutated));
        Assert.Contains("SharpSlang", ex.Message);
        Assert.Contains($"line {removed + 1}", ex.Message);
        Assert.Contains("read back as '\"IsSpecializationSized\": false,'", ex.Message);
    }
}
