// The shader runs, and the numbers are the Slang team's.
//
// This is the only test in the suite that executes a shader. Everything else stops at the point
// where a pipeline is created, which is where the Khronos layers stop too — and the layers cannot
// see an offset. A uniform member reported at byte 16 that really lives at 32 produces a pipeline
// that is created without complaint and a shader that reads the wrong bytes.
//
// So: take a shader from Slang's own tests/compute, fill its buffers, bind them at the set and
// binding index *PrismSlang's reflection reports*, dispatch on a real device, read back, and
// compare with the words the Slang team checked in. Nothing about the binding is assumed — the
// descriptor set layouts come from the projection, and if the projection puts a buffer at the wrong
// binding the shader reads whatever is at that binding instead and the output is wrong.
//
// That closes the loop the other Vulkan tests leave open:
//
//   VulkanCorpusValidationTests   is this layout *legal*        (Khronos says)
//   SlangWalkerCorpusTests        is this layout *reported right* (the Slang team says)
//   this file                     does a host binding by it *work* (the driver says)

using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Vulkan;
using Vortice.Vulkan;

namespace PrismSlang.Tests;

public sealed class SlangComputeCorpusTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly VulkanTestDevice? _vulkan =
        VulkanTestDevice.Unavailable is null && SlangNative.Available ? new VulkanTestDevice() : null;

    public void Dispose() => _vulkan?.Dispose();

    /// <summary>What happened to one case.</summary>
    private enum Outcome { Matched, Differed, OutOfReach, Threw }

    private sealed record Result(string Case, Outcome Outcome, string Detail);

    // ---------------------------------------------------------------- running one shader

    private Result Run(SlangTestCorpus.ComputeCase test)
    {
        var device = _vulkan!;
        var entryPoints = new List<SlangEntryPoint>();

        try
        {
            using var global = SlangGlobalSession.Create();
            using var session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions()],
                SearchPaths = [SlangTestCorpus.ComputeDirectory],
                // `-xslang -matrix-layout-column-major` passes the flag through to the compiler,
                // and it changes every matrix element's address. Ignoring it made
                // non-square-column-major read a transposed matrix and report 1 where 11 was
                // recorded — the harness reading the wrong bytes, not the binding.
                MatrixLayout = test.Arguments.Contains("-matrix-layout-column-major", StringComparison.Ordinal)
                    ? SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR
                    : SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
            });

            using var module = session.LoadModuleFromSource(
                Path.GetFileNameWithoutExtension(test.Source), test.Source, test.Text);

            // Every case in this corpus names its entry point computeMain and none passes -entry;
            // `-compute` is what tells slang-test the stage. Only four of them carry a
            // [shader("compute")] attribute, so the stage has to be supplied.
            entryPoints.Add(module.FindEntryPoint("computeMain", SlangStage.SLANG_STAGE_COMPUTE));

            using var program = session.Link(module, [.. entryPoints]);
            var layout = program.Reflect();
            var vulkan = layout.ToVulkan();

            var spirv = program.GetEntryPointCodeBytes(0);

            var pipelines = new VulkanPipelines(device);
            using var built = pipelines.BuildLayout(vulkan);

            // Built owns the module; adding it here is what gets it destroyed.
            var shaderModule = pipelines.CreateModule(spirv);
            built.Modules.Add(shaderModule);

            pipelines.CreateComputePipeline(built, new StageModule(
                VkShaderStageFlags.Compute, shaderModule, program.GetSpirvEntryPoint(0).Name));

            return Execute(test, vulkan, built, device);
        }
        catch (SlangTestCorpus.OutOfReachException e)
        {
            return new Result(test.Source, Outcome.OutOfReach, e.Message);
        }
        catch (Exception e)
        {
            return new Result(test.Source, Outcome.Threw, $"{e.GetType().Name}: {Oneline(e.Message)}");
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    private static Result Execute(
        SlangTestCorpus.ComputeCase test,
        VulkanPipelineLayout vulkan,
        VulkanPipelines.Built built,
        VulkanTestDevice device)
    {
        using var runner = new VulkanComputeRunner(device);

        // Where each declared buffer is bound, according to the projection and nothing else. A
        // name the projection does not know is the finding, not a reason to guess an index.
        var placed = new List<(SlangTestCorpus.BufferInput Input, uint Set, VulkanBinding Binding)>();

        foreach (var input in test.Inputs)
        {
            var found = vulkan.DescriptorSets
                .SelectMany(s => s.Bindings.Select(b => (Set: s.Set, Binding: b)))
                .Where(x => string.Equals(x.Binding.Name, input.Name, StringComparison.Ordinal))
                .ToList();

            if (found.Count != 1)
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"the projection reports {found.Count} bindings named '{input.Name}'; the "
                    + "corpus declares one buffer under that name. Bound parameters are "
                    + string.Join(", ", vulkan.DescriptorSets.SelectMany(s => s.Bindings).Select(b => b.Name)));
            }

            placed.Add((input, found[0].Set, found[0].Binding));
        }

        // Images first: a format the device cannot store to is a device limit, not a finding, and
        // it has to be reported before any of the work below happens.
        var placedImages = new List<(SlangTestCorpus.ImageInput Input, uint Set, VulkanBinding Binding, VkFormat Format)>();

        foreach (var image in test.Images)
        {
            if (SlangTestCorpus.ImageFormat(image.Format) is not { } name
                || !Enum.TryParse<VkFormat>(name, out var format))
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"'{image.Name}' declares format {image.Format}, which this harness does not map");
            }

            if (!device.SupportsStorageImageFormat(format))
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"this device cannot use {format} as a storage image");
            }

            var found = vulkan.DescriptorSets
                .SelectMany(s => s.Bindings.Select(b => (Set: s.Set, Binding: b)))
                .Where(x => string.Equals(x.Binding.Name, image.Name, StringComparison.Ordinal))
                .ToList();

            if (found.Count != 1)
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"the projection reports {found.Count} bindings named '{image.Name}'");
            }

            var wanted = image.Storage
                ? VulkanDescriptorType.StorageImage
                : VulkanDescriptorType.SampledImage;

            if (found[0].Binding.DescriptorType != wanted)
            {
                return new Result(test.Source, Outcome.Differed,
                    $"'{image.Name}' is a{(image.Storage ? "n RW" : " ")}Texture{image.Dimensions}D "
                    + $"and the projection calls it {found[0].Binding.DescriptorType}, not {wanted}");
            }

            placedImages.Add((image, found[0].Set, found[0].Binding, format));
        }

        foreach (var sampler in test.Samplers)
        {
            var found = vulkan.DescriptorSets
                .SelectMany(s => s.Bindings.Select(b => (Set: s.Set, Binding: b)))
                .Where(x => string.Equals(x.Binding.Name, sampler.Name, StringComparison.Ordinal))
                .ToList();

            if (found.Count != 1)
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"the projection reports {found.Count} bindings named '{sampler.Name}'");
            }

            if (found[0].Binding.DescriptorType != VulkanDescriptorType.Sampler)
            {
                return new Result(test.Source, Outcome.Differed,
                    $"'{sampler.Name}' is a SamplerState and the projection calls it "
                    + $"{found[0].Binding.DescriptorType}, not Sampler");
            }
        }

        var sets = runner.AllocateSets(built.SetLayouts);
        var buffers = new Dictionary<string, VulkanComputeRunner.Buffer>(StringComparer.Ordinal);

        foreach (var (image, set, binding, format) in placedImages)
        {
            var created = image.Storage
                ? runner.CreateStorageImage(image.Dimensions, format, image.Size)
                : runner.CreateSampledImage(image.Dimensions, format, image.Size);

            runner.BindImage(sets[(int)set], binding.Binding, created, image.Storage);
        }

        foreach (var sampler in test.Samplers)
        {
            var placement = vulkan.DescriptorSets
                .SelectMany(s => s.Bindings.Select(b => (Set: s.Set, Binding: b)))
                .Single(x => string.Equals(x.Binding.Name, sampler.Name, StringComparison.Ordinal));

            runner.BindSampler(sets[(int)placement.Set], placement.Binding.Binding, runner.CreateSampler());
        }

        foreach (var (input, set, binding) in placed)
        {
            // Room for what the shader may write, not only for what was declared: an output buffer
            // is usually declared as four zeroes and the expected result may be longer.
            var words = Math.Max(input.Words.Length, test.ExpectedValues.Count() * 2);
            var buffer = runner.CreateStorageBuffer(words * sizeof(uint));

            runner.Write(buffer, System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>(input.Words));
            buffers[$"{input.Name}[{input.ArrayIndex}]"] = buffer;

            var type = binding.DescriptorType switch
            {
                VulkanDescriptorType.StorageBuffer => VkDescriptorType.StorageBuffer,
                VulkanDescriptorType.UniformBuffer => VkDescriptorType.UniformBuffer,
                _ => (VkDescriptorType?)null,
            };

            if (type is null)
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"'{input.Name}' is projected as {binding.DescriptorType}, which this harness "
                    + "only binds as a buffer");
            }

            runner.BindBuffer(sets[(int)set], binding.Binding, type.Value, buffer, input.ArrayIndex);
        }

        // One thread group unless the directive says otherwise. The shader's [numthreads] decides
        // how many invocations that is, and the expected output is sized for exactly that —
        // array-param declares numthreads(4,1,1) and records four words. `-compute-dispatch 3,1,1`
        // asks for three groups, and semantic.slang records twelve words for its numthreads(4,1,1)
        // precisely because of it.
        var (gx, gy, gz) = DispatchSize(test.Arguments);
        runner.Dispatch(built.Pipeline, built.Layout, sets, gx, gy, gz);

        var target = test.Inputs.First(i => i.IsOutput);
        var readback = runner.Read(buffers[$"{target.Name}[{target.ArrayIndex}]"]);
        var produced = System.Runtime.InteropServices.MemoryMarshal
            .Cast<byte, uint>(readback).ToArray();

        return Compare(test, produced);
    }

    /// <summary>Thread groups to dispatch, from <c>-compute-dispatch X,Y,Z</c> or one.</summary>
    private static (uint X, uint Y, uint Z) DispatchSize(string arguments)
    {
        var argv = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i] != "-compute-dispatch") continue;

            var parts = argv[i + 1].Split(',');
            if (parts.Length == 3
                && uint.TryParse(parts[0], out var x)
                && uint.TryParse(parts[1], out var y)
                && uint.TryParse(parts[2], out var z))
            {
                return (x, y, z);
            }
        }

        return (1, 1, 1);
    }

    // ---------------------------------------------------------------- comparing

    private static Result Compare(SlangTestCorpus.ComputeCase test, uint[] produced)
    {
        var expected = test.ExpectedValues.ToList();

        // A 64-bit element is two words, so the buffer has to be twice as long and each value is
        // read as a pair. bitcast-64bit is the only case here that does it.
        var wordsPerValue = test.OutputType is "uint64_t" or "int64_t" or "double" ? 2 : 1;

        if (expected.Count * wordsPerValue > produced.Length)
        {
            return new Result(test.Source, Outcome.Differed,
                $"expected {expected.Count} value(s) of {wordsPerValue} word(s), the buffer holds "
                + $"{produced.Length} words");
        }

        for (var i = 0; i < expected.Count; i++)
        {
            var (ok, detail) = wordsPerValue == 2
                ? MatchesWide(expected[i], produced[i * 2] | ((ulong)produced[(i * 2) + 1] << 32), test.OutputType!)
                : test.IsTyped
                    ? MatchesTyped(expected[i], produced[i], test.OutputType!)
                    : MatchesWord(expected[i], produced[i]);

            if (!ok)
                return new Result(test.Source, Outcome.Differed, $"value {i}: {detail}");
        }

        return new Result(test.Source, Outcome.Matched,
            $"{expected.Count} value(s)" + (wordsPerValue == 2 ? " (64-bit)" : ""));
    }

    private static (bool, string) MatchesWide(string expected, ulong produced, string type)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        if (type == "double")
        {
            if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, culture, out var want))
                return (false, $"could not read '{expected}' as a double");

            var got = BitConverter.UInt64BitsToDouble(produced);
            var tolerance = Math.Max(1e-9, Math.Abs(want) * 1e-12);
            return (Math.Abs(want - got) <= tolerance, $"slang {want}, shader wrote {got}");
        }

        if (type == "int64_t")
        {
            if (!long.TryParse(expected, culture, out var wanted))
                return (false, $"could not read '{expected}' as an int64_t");
            return (wanted == unchecked((long)produced), $"slang {wanted}, shader wrote {unchecked((long)produced)}");
        }

        if (!ulong.TryParse(expected, culture, out var want64))
            return (false, $"could not read '{expected}' as a uint64_t");

        return (want64 == produced, $"slang {want64}, shader wrote {produced}");
    }

    /// <summary>Raw output: bare hex, no <c>0x</c> and no leading zeros.</summary>
    private static (bool, string) MatchesWord(string expected, uint produced)
    {
        if (!uint.TryParse(expected, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var want))
        {
            return (false, $"could not read '{expected}' as a hex word");
        }

        return (want == produced, $"slang {want:X}, shader wrote {produced:X}");
    }

    /// <summary>
    /// <c>-output-using-type</c>: a <c>type:</c> header and formatted values.
    /// </summary>
    /// <remarks>
    /// Floats compared with a tolerance rather than by their printed form. slang-test writes six
    /// decimal places and the question here is whether the shader computed the right number, not
    /// whether two printers round the same way.
    /// </remarks>
    private static (bool, string) MatchesTyped(string expected, uint produced, string type)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        switch (type)
        {
            case "float":
            {
                if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, culture, out var want))
                    return (false, $"could not read '{expected}' as a float");

                var got = BitConverter.UInt32BitsToSingle(produced);
                var tolerance = Math.Max(1e-4, Math.Abs(want) * 1e-5);
                return (Math.Abs(want - got) <= tolerance, $"slang {want}, shader wrote {got}");
            }

            case "uint":
            {
                if (!uint.TryParse(expected, culture, out var want))
                    return (false, $"could not read '{expected}' as a uint");
                return (want == produced, $"slang {want}, shader wrote {produced}");
            }

            default:
            {
                if (!int.TryParse(expected, culture, out var want))
                    return (false, $"could not read '{expected}' as an int ({type})");
                return (want == unchecked((int)produced), $"slang {want}, shader wrote {unchecked((int)produced)}");
            }
        }
    }

    private static string Oneline(string s)
    {
        var text = s.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');
        return text.Length > 200 ? text[..200] + "..." : text;
    }

    // ---------------------------------------------------------------- the tests

    private static readonly Lazy<IReadOnlyList<SlangTestCorpus.ComputeCase>> LazyCases =
        new(() => SlangTestCorpus.Compute);

    // Per instance, not static: Run needs the device this instance owns, and xUnit builds a fresh
    // instance for each test method. Two runs of the corpus, about four seconds each.
    private IReadOnlyList<Result>? _results;

    private IReadOnlyList<Result> Results => _results ??= [.. LazyCases.Value.Select(Run)];

    [VulkanValidationFact]
    public void The_compute_corpus_has_cases_to_run()
    {
        var cases = LazyCases.Value;
        output.WriteLine($"tests/compute at {SlangTestCorpus.Version}: {cases.Count} case(s) with a "
            + "-vk directive, inputs this harness can build, and a recorded result");

        foreach (var c in cases)
            output.WriteLine($"  {c.Source,-46} {c.Inputs.Count} buffer(s), {c.Images.Count} image(s), "
                + $"{c.ExpectedValues.Count()} word(s){(c.IsTyped ? $", typed {c.OutputType}" : "")}");

        Assert.True(cases.Count(c => c.Images.Count > 0) >= 3,
            "no case with a storage image is being run, so image binding indices are back to being "
            + "judged only by the validation layers.");

        Assert.InRange(cases.Count, 20, 200);
    }

    /// <summary>Cases whose recorded output this configuration cannot produce, and why.</summary>
    /// <remarks>
    /// Declared and checked both ways, as everywhere else here: a case that starts passing must
    /// leave this list, or "out of reach" becomes a way to make a failure disappear.
    /// </remarks>
    private static readonly Dictionary<string, string> ExpectedToDiffer = new(StringComparer.Ordinal)
    {
        ["buffer-layout.slang"] =
            "the recorded output is the D3D constant-buffer layout, not std140, and this runs a "
            + "plain Vulkan target.\n"
            + "        The shader fills cb and sb with word i = i and packs four field offsets into "
            + "one int, so each byte of the result is a field's word offset. Expected 160E0308 "
            + "decodes to cb.b at byte 24 and cb.d at 56, which is what the shader's own comments "
            + "call the `cbuffer` column; this produces 18140308, which is 32 and 80 — the `std140` "
            + "column, and the right answer for Vulkan.\n"
            + "        One .expected.txt is shared by the dx11, dx12, vk, cpu and cuda legs, so "
            + "slang-test's Vulkan run must force D3D layout to make them agree. That is a target "
            + "flag (ICompileRequest::setTargetForceDXLayout on the legacy API) which TargetOptions "
            + "does not expose and a Vulkan-only engine would not want. Both sides are right about "
            + "their own target.",
    };

    [VulkanValidationFact]
    public void Nothing_differs_but_the_case_declared_to()
    {
        var results = Results;
        var differed = results
            .Where(r => r.Outcome == Outcome.Differed)
            .Select(r => r.Case)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = differed.Where(c => !ExpectedToDiffer.ContainsKey(c)).Order(StringComparer.Ordinal).ToList();
        var stale = ExpectedToDiffer.Keys.Where(c => !differed.Contains(c)).Order(StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0,
            $"{undeclared.Count} shader(s) produced output nobody expected them to:\n"
            + string.Join("", undeclared.Select(c =>
                $"    {c}: {results.First(r => r.Case == c).Detail}\n")));

        Assert.True(stale.Count == 0,
            $"{stale.Count} shader(s) are declared to differ and now match. Remove them:\n"
            + string.Join("", stale.Select(c => $"    {c} — was: {ExpectedToDiffer[c]}\n")));
    }

    [VulkanValidationFact]
    public void Binding_by_the_projection_produces_the_numbers_Slang_recorded()
    {
        Assert.True(_vulkan!.SupportsCompute,
            "queue family 0 on this device does not do compute, so nothing here can dispatch");

        var results = Results;

        foreach (var group in results.GroupBy(r => r.Outcome).OrderBy(g => g.Key))
            output.WriteLine($"{group.Key}: {group.Count()}");

        output.WriteLine("");
        foreach (var r in results) output.WriteLine($"  {r.Outcome,-11} {r.Case,-46} {r.Detail}");

        var reached = results.Count(r => r.Outcome == Outcome.Matched);
        Assert.True(reached >= 40,
            $"only {reached} shaders ran and produced the recorded output. This harness is the only "
            + "thing in the suite that executes a shader, and at that count it is not executing "
            + "enough of them to mean anything.");

        var wrong = results
            .Where(r => r.Outcome == Outcome.Threw
                     || (r.Outcome == Outcome.Differed && !ExpectedToDiffer.ContainsKey(r.Case)))
            .ToList();

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {results.Count} shader(s) did not produce the output Slang's own test "
            + $"suite records. A wrong word here is a binding, an offset or a stride that reflection "
            + $"reported and the hardware disagreed with:\n"
            + string.Join("", wrong.Select(r => $"    {r.Outcome} {r.Case}\n        {r.Detail}\n")));
    }
}
