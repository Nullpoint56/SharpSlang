// The shader runs, and the numbers are the Slang team's.
//
// This is the only test in the suite that executes a corpus of shaders. Everything else stops at
// the point where a pipeline is created, which is where the validation layers stop too — and the
// layers cannot see an offset. A uniform member reported at byte 16 that really lives at 32
// produces a pipeline that is created without complaint and a shader that reads the wrong bytes.
//
// So: take a shader from Slang's own tests/compute, fill its buffers, bind them at the register
// space and range *SharpSlang's NRI projection reports*, dispatch on a validated NRI Vulkan
// device, read back, and compare with the words the Slang team checked in. Nothing about the
// binding is assumed — the pipeline layout is the projection's, and if the projection puts a buffer
// at the wrong range the shader reads whatever is bound there instead and the output is wrong.
// Every message NRI's validation or the Khronos layer reports while a case runs fails that case.
//
// That closes the loop the other device tests leave open:
//
//   NriDeviceTests             is this layout *legal*          (Khronos and D3D12 say)
//   SlangWalkerCorpusTests     is this layout *reported right* (the Slang team says)
//   this file                  does a host binding by it *work* (the driver says)

using SharpSlang.Native;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;
using SharpNRI;
using SharpNRI.Native;
using SharpNRI.Testing;

namespace SharpSlang.Tests;

public sealed class SlangComputeCorpusTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>What happened to one case.</summary>
    private enum Outcome { Matched, Differed, OutOfReach, Threw, Invalid }

    private sealed record Result(string Case, Outcome Outcome, string Detail);

    // ---------------------------------------------------------------- running one shader

    private static Result Run(SlangTestCorpus.ComputeCase test, NriTestDevice fixture)
    {
        var entryPoints = new List<SlangEntryPoint>();
        var before = fixture.Messages.Count;

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
            var projection = program.Reflect().ToNri();
            var spirv = program.GetEntryPointCodeBytes(0);

            var device = fixture.Device;
            using var layout = device.CreatePipelineLayout(projection.Layout);
            using var pipeline = device.CreateComputePipeline(new NriComputePipelineDesc
            {
                PipelineLayout = layout,
                Shader = new NriShaderDesc(StageBits.COMPUTE_SHADER, spirv, program.GetSpirvEntryPoint(0).Name),
            });

            var result = Execute(test, projection, layout, pipeline, device);

            // A case whose calls NRI's validation or the layer objected to proves nothing about the
            // numbers, whatever came back. The one excused error is about NRI, not about anything
            // this harness or the projection said, and only where its cause is in the case.
            var errors = fixture.Messages.Skip(before).Where(m => m.IsError).ToList();
            var excused = test.Images.Any(i => i.Storage && i.Dimensions == 3)
                ? errors.Where(IsNriThreeDimensionalSliceLayout).ToList()
                : [];
            errors = [.. errors.Except(excused)];

            if (errors.Count > 0)
            {
                return new Result(test.Source, Outcome.Invalid,
                    $"[{result.Outcome}: {result.Detail}] " + string.Join(" / ", errors.Select(m => Oneline(m.Text, 1000)).Distinct()));
            }

            return excused.Count == 0
                ? result
                : result with { Detail = $"{result.Detail}; {excused.Count} NRI 3D-slice layout error(s) excused" };
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

    /// <summary>
    /// The Khronos layer reporting a slice other than the first of a 3D storage texture in the
    /// wrong layout at submit: a defect of NRI's barriers, not of the projection or this harness.
    /// </summary>
    /// <remarks>
    /// Measured on the RTX 4070 SUPER with SharpNRI 191.0.0-beta.4 (NRI v191) and the Vulkan SDK's
    /// 1.4.363 layer: for each of rw-texture-simple's three <c>RWTexture3D</c>s (4x4x4) the layer
    /// reports <c>VUID-vkCmdDraw-None-09600</c> for <c>arrayLayer = 1</c>, <c>2</c> and <c>3</c>,
    /// "expects ... to be in layout VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL--instead, current layout
    /// is VK_IMAGE_LAYOUT_UNDEFINED", nine errors, and nothing about slice 0. NRI creates a 3D
    /// texture with <c>VK_IMAGE_CREATE_2D_ARRAY_COMPATIBLE_BIT</c> (nri/Source/VK/DeviceVK.hpp,
    /// "allow 3D demotion to a set of layers"), enables VK_KHR_maintenance9 where the device has it,
    /// under which a 3D image's barrier range names slices, and gives a 3D texture's barrier
    /// <c>layerCount</c> = <c>TextureDesc.layerNum</c>, 1 (nri/Source/VK/CommandBufferVK.hpp): so its
    /// upload helper moves slice 0 alone. The readback matched the values Slang recorded anyway.
    /// lavapipe under WSL, with the same layer, reports none of the nine for the same case.
    /// A third-party defect, so excused here and recorded rather than patched; matched on the VUID
    /// and on a slice index above 0, and only for a case that declares a 3D storage texture, so
    /// the same VUID about anything else still fails.
    /// </remarks>
    private static bool IsNriThreeDimensionalSliceLayout(NriMessage m) =>
        m.Text.Contains("VUID-vkCmdDraw-None-09600", StringComparison.Ordinal)
        && !m.Text.Contains("arrayLayer = 0)", StringComparison.Ordinal)
        && m.Text.Contains("arrayLayer = ", StringComparison.Ordinal);

    /// <summary>The one range the projection names this, with its set, or the reason there is not exactly one.</summary>
    private static ((ProjectedDescriptorSet Set, ProjectedDescriptorRange Range)? Found, string? Why) Place(
        NriPipelineProjection projection, string name)
    {
        var found = projection.DescriptorSets
            .SelectMany(s => s.Ranges.Select(r => (Set: s, Range: r)))
            .Where(x => string.Equals(x.Range.Name, name, StringComparison.Ordinal))
            .ToList();

        return found.Count == 1
            ? (found[0], null)
            : (null, $"the projection reports {found.Count} ranges named '{name}'; the corpus declares one "
                   + "resource under that name. Bound parameters are "
                   + string.Join(", ", projection.DescriptorSets.SelectMany(s => s.Ranges).Select(r => r.Name)));
    }

    private static Result Execute(
        SlangTestCorpus.ComputeCase test,
        NriPipelineProjection projection,
        NriPipelineLayout layout,
        NriPipeline pipeline,
        NriDevice device)
    {
        using var runner = new NriComputeRunner(device);

        // Where each declared buffer is bound, according to the projection and nothing else. A
        // name the projection does not know is the finding, not a reason to guess an index.
        var placed = new List<(SlangTestCorpus.BufferInput Input, ProjectedDescriptorSet Set, ProjectedDescriptorRange Range)>();

        foreach (var input in test.Inputs)
        {
            var (found, why) = Place(projection, input.Name);
            if (found is not { } f) return new Result(test.Source, Outcome.OutOfReach, why!);

            if (f.Range.Desc.descriptorType is not (DescriptorType.CONSTANT_BUFFER
                or DescriptorType.STRUCTURED_BUFFER or DescriptorType.STORAGE_STRUCTURED_BUFFER))
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"'{input.Name}' is projected as {f.Range.Desc.descriptorType}, which this harness "
                    + "only binds as a buffer");
            }

            placed.Add((input, f.Set, f.Range));
        }

        // Images first: a format the device cannot store to is a device limit, not a finding, and
        // it has to be reported before any of the work below happens.
        var placedImages = new List<(SlangTestCorpus.ImageInput Input, ProjectedDescriptorSet Set, ProjectedDescriptorRange Range, Format Format)>();

        foreach (var image in test.Images)
        {
            if (SlangTestCorpus.ImageFormat(image.Format) is not { } name
                || !Enum.TryParse<Format>(name, out var format))
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"'{image.Name}' declares format {image.Format}, which this harness does not map");
            }

            var wantedSupport = image.Storage ? FormatSupportBits.STORAGE_TEXTURE : FormatSupportBits.TEXTURE;
            if ((device.GetFormatSupport(format) & wantedSupport) == 0)
            {
                return new Result(test.Source, Outcome.OutOfReach,
                    $"this device cannot use {format} as a{(image.Storage ? " storage" : " sampled")} texture");
            }

            var (found, why) = Place(projection, image.Name);
            if (found is not { } f) return new Result(test.Source, Outcome.OutOfReach, why!);

            var wanted = image.Storage ? DescriptorType.STORAGE_TEXTURE : DescriptorType.TEXTURE;
            if (f.Range.Desc.descriptorType != wanted)
            {
                return new Result(test.Source, Outcome.Differed,
                    $"'{image.Name}' is a{(image.Storage ? "n RW" : " ")}Texture{image.Dimensions}D "
                    + $"and the projection calls it {f.Range.Desc.descriptorType}, not {wanted}");
            }

            placedImages.Add((image, f.Set, f.Range, format));
        }

        var placedSamplers = new List<(ProjectedDescriptorSet Set, ProjectedDescriptorRange Range)>();
        foreach (var sampler in test.Samplers)
        {
            var (found, why) = Place(projection, sampler.Name);
            if (found is not { } f) return new Result(test.Source, Outcome.OutOfReach, why!);

            if (f.Range.Desc.descriptorType != DescriptorType.SAMPLER)
            {
                return new Result(test.Source, Outcome.Differed,
                    $"'{sampler.Name}' is a SamplerState and the projection calls it "
                    + $"{f.Range.Desc.descriptorType}, not SAMPLER");
            }

            placedSamplers.Add(f);
        }

        // Generous and uniform rather than counted per type. Getting a count wrong is refused by
        // NRI, which reads as a harness bug and is one.
        using var pool = device.CreateDescriptorPool(new DescriptorPoolDesc
        {
            descriptorSetMaxNum = (uint)Math.Max(8, projection.DescriptorSets.Count),
            samplerMaxNum = 64,
            constantBufferMaxNum = 64,
            textureMaxNum = 64,
            storageTextureMaxNum = 64,
            structuredBufferMaxNum = 64,
            storageStructuredBufferMaxNum = 64,
        });

        var sets = projection.DescriptorSets.ToDictionary(
            s => s.SetIndex,
            s => pool.AllocateDescriptorSet(layout, s.SetIndex));

        foreach (var (image, set, range, format) in placedImages)
            sets[set.SetIndex].Update(range.RangeIndex, 0, runner.CreateTexture(image.Dimensions, format, image.Size, image.Storage));

        foreach (var (set, range) in placedSamplers)
            sets[set.SetIndex].Update(range.RangeIndex, 0, runner.CreateSampler());

        var buffers = new Dictionary<string, (NriBuffer Buffer, AccessBits Access)>(StringComparer.Ordinal);
        foreach (var (input, set, range) in placed)
        {
            // Room for what the shader may write, not only for what was declared: an output buffer
            // is usually declared as four zeroes and the expected result may be longer.
            var words = Math.Max(input.Words.Length, test.ExpectedValues.Count() * 2);
            var access = NriComputeRunner.AccessFor(range.Desc.descriptorType);
            var buffer = runner.CreateBuffer(
                words * sizeof(uint), System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>(input.Words), access);

            buffers[$"{input.Name}[{input.ArrayIndex}]"] = (buffer, access);
            sets[set.SetIndex].Update(range.RangeIndex, input.ArrayIndex, runner.CreateView(buffer, range.Desc.descriptorType));
        }

        // One thread group unless the directive says otherwise. The shader's [numthreads] decides
        // how many invocations that is, and the expected output is sized for exactly that —
        // array-param declares numthreads(4,1,1) and records four words. `-compute-dispatch 3,1,1`
        // asks for three groups, and semantic.slang records twelve words for its numthreads(4,1,1)
        // precisely because of it.
        var (gx, gy, gz) = DispatchSize(test.Arguments);

        var target = test.Inputs.First(i => i.IsOutput);
        var (outputBuffer, outputAccess) = buffers[$"{target.Name}[{target.ArrayIndex}]"];
        var readback = runner.Dispatch(
            layout, pipeline, pool,
            [.. sets.Select(kv => (kv.Key, kv.Value))],
            gx, gy, gz,
            outputBuffer, outputAccess);

        var produced = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(readback).ToArray();
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

    private static string Oneline(string s, int limit = 200)
    {
        var text = s.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');
        return text.Length > limit ? text[..limit] + "..." : text;
    }

    // ---------------------------------------------------------------- the tests

    private static readonly Lazy<IReadOnlyList<SlangTestCorpus.ComputeCase>> LazyCases =
        new(() => SlangTestCorpus.Compute);

    // Once per process, on one validated Vulkan device: the corpus runs once and both facts below
    // read the same results. The device is disposed as soon as the last case has run.
    private static readonly Lazy<IReadOnlyList<Result>> LazyResults = new(() =>
    {
        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        return [.. LazyCases.Value.Select(c => Run(c, fixture))];
    });

    private static IReadOnlyList<Result> Results => LazyResults.Value;

    [NriVulkanFact]
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

        // Both documents state it, so both are pinned. A phrase, not a table cell — see the note
        // on the same pattern in NativeAbiTests.
        foreach (var file in new[] { "docs/design.md", "README.md" })
        {
            DocumentedFactsTests.Documents(file, $"dispatches {cases.Count} shaders",
                $"{cases.Count} compute cases are runnable");
        }

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

    [NriVulkanFact]
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

    [NriVulkanFact]
    public void Binding_by_the_projection_produces_the_numbers_Slang_recorded()
    {
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
                     || r.Outcome == Outcome.Invalid
                     || (r.Outcome == Outcome.Differed && !ExpectedToDiffer.ContainsKey(r.Case)))
            .ToList();

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {results.Count} shader(s) did not produce the output Slang's own test "
            + $"suite records, or ran with a validation error. A wrong word here is a binding, an offset or a "
            + $"stride that reflection reported and the hardware disagreed with:\n"
            + string.Join("", wrong.Select(r => $"    {r.Outcome} {r.Case}\n        {r.Detail}\n")));
    }
}
