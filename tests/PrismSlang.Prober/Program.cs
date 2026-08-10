// One reflection operation per run, in a process nobody minds losing.
//
// Exit codes are the whole interface:
//
//   0    the operation completed
//   1    it threw, and the message is on stderr — a normal failure, which is the interesting
//        alternative to the one below: an exception is something a cooker can catch and report
//   2    the prober was misused (unknown case, bad arguments)
//   any other exit, or none at all
//        the operation ended the process. On Linux that is a signal; on Windows a status code
//        like 0xC0000005. Either way there is no managed stack and nothing to catch.
//
// Every case here must be *deliberately* one of those. A case that crashes for an incidental
// reason — a missing payload, a typo in the shader — would look exactly like the finding it is
// meant to demonstrate, which is why `healthy` exists and why the test runs it first.

using PrismSlang;
using PrismSlang.Interop;
using PrismSlang.Native;
using PrismSlang.Reflection;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: PrismSlang.Prober <case>");
    Console.Error.WriteLine("cases: " + string.Join(", ", Cases.Keys));
    return 2;
}

if (!Cases.TryGetValue(args[0], out var run))
{
    Console.Error.WriteLine($"unknown case '{args[0]}'. Known: {string.Join(", ", Cases.Keys)}");
    return 2;
}

try
{
    run();
    Console.Out.WriteLine("completed");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
    return 1;
}

internal static class Cases
{
    public static IEnumerable<string> Keys => All.Keys;

    public static bool TryGetValue(string name, out Action run) => All.TryGetValue(name, out run!);

    private static readonly Dictionary<string, Action> All = new(StringComparer.Ordinal)
    {
        // The control. If this does not exit 0, the prober cannot resolve the native payload or
        // the compiler is broken, and every other case's exit code means nothing. The test asserts
        // this one first for exactly that reason: a prober that always dies would make "it died"
        // pass for the wrong reason.
        ["healthy"] = () => Reflect("""
            Texture2D    hFixed[4];
            SamplerState hSampler;

            [shader("fragment")]
            float4 hFragment() : SV_Target { return hFixed[0].Sample(hSampler, 0); }
            """),

        // §9.12. A resource array sized by a specialization constant. Every query that does not
        // crash gives the same answers as a fixed-size array, so there is nothing to test for and
        // nothing to guard — the only observable difference is that reflecting it ends the process
        // inside spReflectionTypeLayout_getBindingRangeCount.
        ["specialization-sized-resource-array"] = () => Reflect("""
            [vk::constant_id(0)] const int kCascades = 4;

            Texture2D    scArray[kCascades];
            SamplerState scSampler;

            [shader("fragment")]
            float4 scFragment() : SV_Target { return scArray[0].Sample(scSampler, 0); }
            """),

        // §9.20. An integer argument where a type parameter belongs. LinkedProgram.SpecializeGeneric
        // refuses this before Slang sees it, because Slang does not refuse it — it access-violates.
        // Reaching past the guard is the point, so this calls the flat API directly.
        ["generic-argument-kind-mismatch"] = () => MismatchedGenericArgument(wrongKind: true),

        // The other half of the same guard: right kind, wrong count. Separated because the two
        // are one refusal in the managed API and may be two different behaviours underneath, and
        // guarding both on the strength of measuring one is how a guard ends up wider than its
        // evidence.
        ["generic-argument-count-mismatch"] = () => MismatchedGenericArgument(wrongKind: false),

        // §9.25. Does creating a child actually retain its parent? LifetimeTests asserts that
        // disposal order does not matter, on the stated premise that "each object holds its own
        // reference to what it needs". That premise has never been measured — it was read off COM
        // convention — and the macOS leg crashes in exactly the test that depends on it. addRef
        // followed by release reports the count without changing it.
        ["refcount-ownership"] = RefcountOwnership,

        // §9.26. The same teardown LifetimeTests does, outermost first, one release at a time with
        // the name of each printed before it happens. Taking a native reference on the owner made
        // the refcounts balance and did not stop the macOS host dying here, so the remaining
        // suspect is not *whether* the objects survive but the order Slang destroys them in when
        // the last release collapses the whole hierarchy at once. The last line of output is the
        // release that did it.
        ["dispose-outermost-first"] = DisposeOutermostFirst,

        // §9.24. Two reflection questions that were being asked in-process on the strength of
        // Windows and Linux tolerating them, and that the first macOS run turned into a dead test
        // host. Both are caller mistakes rather than obvious Slang defects, which is exactly why
        // the answer belongs somewhere a crash is the result rather than the end of the run.
        ["specialized-type-arg-out-of-range"] = SpecializedTypeArgOutOfRange,
        ["apply-generic-to-itself"] = ApplyGenericToItself,

        // §9.22. spReflection_specializeType handed a type that is not generic. Returns null on
        // Windows, aborts the process on Linux — the same platform split as §9.12, and found the
        // same way: by a test that passed on one platform and killed the runner on the other.
        ["specialize-a-non-generic-type"] = SpecializeNonGenericType,

        // §9.12 / §9.21. GetSpecializedElementCount is the documented resolving counterpart of
        // GetElementCount — "returns SLANG_UNKNOWN_SIZE when size depends on unresolved generic
        // parameters or link-time constants" — and it access-violates on exactly that case. The
        // walk reads the size marker first and never asks; this is the only place it is called,
        // because calling it anywhere else would end the caller.
        ["specialized-element-count"] = SpecializedElementCount,

        // §9.21. getBindingRangeImageFormat on a ParameterBlock's binding range. The receiver and
        // the index are the ones every other binding-range query in the same loop is answered for,
        // which is the whole point: this prints the range's type — proving the pair is valid — and
        // then dies asking the next question about it.
        ["image-format-on-parameter-block"] = ImageFormatOnParameterBlock,
    };

    /// <summary>
    /// Whether each Slang object retains the one above it, measured rather than assumed.
    /// </summary>
    private static unsafe void RefcountOwnership()
    {
        const string Source = """
            Texture2D    rcTexture;
            SamplerState rcSampler;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return rcTexture.Sample(rcSampler, 0); }
            """;

        using var global = SlangGlobalSession.Create();
        Report("global, alone", global);

        using var session = global.CreateSession(
            new SessionOptions { Targets = [new TargetOptions()] });
        Report("global, after CreateSession", global);
        Report("session, alone", session);

        using var module = session.LoadModuleFromSource("rc", "rc.slang", Source);
        Report("global, after LoadModule", global);
        Report("session, after LoadModule", session);
        Report("module, alone", module);

        using var entry = module.FindEntryPoint("fragmentMain");
        Report("module, after FindEntryPoint", module);
        Report("entry, alone", entry);

        using var program = session.Link(module, entry);
        Report("global, after Link", global);
        Report("session, after Link", session);
        Report("module, after Link", module);

        // The one that was never asked. A composite that does not retain its components is safe
        // in the innermost-first order every other test uses and a use-after-free in the one
        // LifetimeTests deliberately inverts: disposing the entry point would destroy it, and the
        // program's own release would then walk a dangling component pointer.
        Report("entry, after Link", entry);
        Report("program, alone", program);

        // addRef then release leaves the count where it started and reports it on the way back
        // down, so this observes without disturbing.
        static void Report(string what, SlangObject o)
        {
            var p = (ISlangUnknown*)o.RawForTests;
            p->addRef();
            Console.Out.WriteLine($"    {what,-32} refcount={p->release()}");
        }
    }

    /// <summary>
    /// LifetimeTests's out-of-order teardown, narrated so a fault names the release that caused it.
    /// </summary>
    /// <remarks>
    /// Deliberately not a test. The thing being measured is whether a release ends the process, and
    /// an in-process assertion cannot survive to report it — the whole macOS investigation has cost
    /// three CI runs whose only output was the name of a test. Here the last line on stdout is the
    /// answer, and it arrives whether the process exits or dies.
    /// </remarks>
    private static unsafe void DisposeOutermostFirst()
    {
        const string Source = """
            struct Uniforms { float4 tint; };
            ConstantBuffer<Uniforms> uniforms;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return uniforms.tint; }
            """;

        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        var module = session.LoadModuleFromSource("m", "m.slang", Source);
        var entry = module.FindEntryPoint("fragmentMain");
        var program = session.Link(module, entry);

        // Captured while everything is alive, because the wrappers start throwing from Raw as soon
        // as they are disposed and these two have to be readable right up to the last release.
        var globalPtr = (ISlangUnknown*)global.RawForTests;
        var sessionPtr = (ISlangUnknown*)session.RawForTests;

        Release("global", global);
        Release("session", session);
        Release("module", module);
        Release("entry", entry);
        Release("program", program);

        void Release(string what, SlangObject o)
        {
            // The invariant, printed on the line before the release that has to preserve it. The
            // last release destroys the Linkage, and if the Linkage is the only thing left holding
            // the global session then Slang destroys it from inside ~Linkage() and double-frees a
            // witness table between the two AST arenas (§9.26). global >= 2 here is what says
            // somebody other than the Linkage is still holding it.
            Console.Out.WriteLine(
                $"    global={Count(globalPtr)} session={Count(sessionPtr)}  releasing {what} ...");
            Console.Out.Flush();
            o.Dispose();
            Console.Out.WriteLine($"    releasing {what} ok");
            Console.Out.Flush();
        }

        static uint Count(ISlangUnknown* p)
        {
            p->addRef();
            return p->release();
        }
    }

    /// <summary>Argument 0 of a specialization argument list of length 0.</summary>
    private static unsafe void SpecializedTypeArgOutOfRange()
    {
        using var loaded = LoadGenericProgram();

        SlangReflectionType* block;
        using (Utf8.Scoped n = new("MgWrapper"))
        {
            block = SlangApi.spReflection_FindTypeByName(loaded.Shader, n);
        }

        if (block is null) throw new InvalidOperationException("MgWrapper not found");

        var count = SlangApi.spReflectionType_getSpecializedTypeArgCount(block);
        Console.Out.WriteLine($"specializedTypeArgCount={count}");
        Console.Out.Flush();

        var arg = SlangApi.spReflectionType_getSpecializedTypeArgType(block, 0);
        Console.Out.WriteLine($"arg[0] of {count} -> {(arg is null ? "null" : "non-null")}");
    }

    /// <summary>An unspecialized generic applied as its own substitution.</summary>
    private static unsafe void ApplyGenericToItself()
    {
        using var loaded = LoadGenericProgram();

        SlangReflectionType* declared;
        using (Utf8.Scoped n = new("MgWrapper"))
        {
            declared = SlangApi.spReflection_FindTypeByName(loaded.Shader, n);
        }

        var generic = declared is null
            ? null
            : SlangApi.spReflectionType_GetGenericContainer(declared);

        if (generic is null) throw new InvalidOperationException("MgWrapper is not generic");

        Console.Out.WriteLine("applying the generic to itself");
        Console.Out.Flush();

        var applied = SlangApi.spReflectionGeneric_applySpecializations(generic, generic);
        Console.Out.WriteLine($"applySpecializations(g, g) -> {(applied is null ? "null" : "non-null")}");
    }

    private static unsafe void SpecializeNonGenericType()
    {
        const string Source = """
            struct SnBlock { float4 snTint; };
            ConstantBuffer<SnBlock> snBuffer;

            [shader("fragment")]
            float4 snFragment() : SV_Target { return snBuffer.snTint; }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(
            new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("sn", "sn.slang", Source);
        using var entry = module.FindEntryPoint("snFragment");
        using var program = session.Link(module, entry);

        var shader = (SlangProgramLayout*)program.GetLayout(0);

        SlangReflectionType* block;
        using (Utf8.Scoped n = new("SnBlock"))
        {
            block = SlangApi.spReflection_FindTypeByName(shader, n);
        }

        if (block is null) throw new InvalidOperationException("SnBlock not found");

        Console.Out.WriteLine($"SnBlock kind={SlangApi.spReflectionType_GetKind(block)}");
        Console.Out.Flush();

        var args = stackalloc SlangReflectionType*[1];
        args[0] = block;
        ISlangBlob* diagnostics = null;

        var specialized = SlangApi.spReflection_specializeType(shader, block, 1, args, &diagnostics);
        Console.Out.WriteLine(specialized is null ? "refused" : "specialized");
    }

    private static unsafe void SpecializedElementCount()
    {
        const string Source = """
            [vk::constant_id(0)] const int kSeCount = 3;
            struct SeBlock { float4 seItems[kSeCount]; float seTail; };
            ConstantBuffer<SeBlock> seBuffer;

            [shader("fragment")]
            float4 seFragment() : SV_Target { return seBuffer.seItems[0] + seBuffer.seTail; }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(
            new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("se", "se.slang", Source);
        using var entry = module.FindEntryPoint("seFragment");
        using var program = session.Link(module, entry);

        var shader = (SlangProgramLayout*)program.GetLayout(0);

        // Reached structurally rather than through a layout: §9.12 measured that a ConstantBuffer
        // of a block containing such an array reports no bindings at all, so there is no layout to
        // walk down. The type is still findable by name.
        SlangReflectionType* block;
        using (Utf8.Scoped n = new("SeBlock"))
        {
            block = SlangApi.spReflection_FindTypeByName(shader, n);
        }

        if (block is null) throw new InvalidOperationException("SeBlock not found");

        var arrayField = SlangApi.spReflectionType_GetFieldByIndex(block, 0);
        var arrayType = arrayField is null
            ? null
            : SlangApi.spReflectionVariable_GetType(arrayField);

        if (arrayType is null) throw new InvalidOperationException("no array type");

        Console.Out.WriteLine($"array kind={SlangApi.spReflectionType_GetKind(arrayType)}");
        Console.Out.Flush();

        Console.Out.WriteLine("GetSpecializedElementCount -> "
            + SlangApi.spReflectionType_GetSpecializedElementCount(arrayType, shader));
    }

    private static unsafe void ImageFormatOnParameterBlock()
    {
        // Every binding-range shape that could plausibly be the one: a parameter block holding
        // resources, a nested one, a constant buffer, a sampler, a structured buffer, an
        // acceleration structure, and both formatted and unformatted storage images.
        const string Source = """
            struct PbInner { Texture2D innerTex; SamplerState innerSampler; float4 innerTint; };
            struct PbBlock
            {
                ParameterBlock<PbInner> nested;
                Texture2D               blockTex;
                float4                  tint;
            };
            struct PbValues { float4 a; float4 b; };

            ParameterBlock<PbBlock>            pbBlock;
            ConstantBuffer<PbValues>           pbCbuffer;
            SamplerState                       pbSampler;
            StructuredBuffer<float4>           pbStructured;
            ByteAddressBuffer                  pbRaw;
            RaytracingAccelerationStructure    pbTlas;
            [format("rgba8")] RWTexture2D<float4> pbFormatted;
            RWTexture2D<float4>                pbPlain;

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void pbMain()
            {
                RayDesc r; r.Origin = 0; r.Direction = float3(0, 0, 1); r.TMin = 0; r.TMax = 1;
                RayQuery<RAY_FLAG_NONE> q; q.TraceRayInline(pbTlas, RAY_FLAG_NONE, 0xff, r);

                pbFormatted[uint2(0, 0)] = pbBlock.tint + pbCbuffer.a + pbStructured[0]
                    + asfloat(pbRaw.Load(0)) + pbBlock.blockTex.Sample(pbSampler, 0)
                    + pbBlock.nested.innerTex.Sample(pbBlock.nested.innerSampler, 0)
                    + pbBlock.nested.innerTint;
                pbPlain[uint2(0, 0)] = 1;
            }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(
            new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("pb", "pb.slang", Source);
        using var entry = module.FindEntryPoint("pbMain");
        using var program = session.Link(module, entry);

        var shader = (SlangProgramLayout*)program.GetLayout(0);
        var scope = SlangApi.spReflection_getGlobalParamsTypeLayout(shader);
        if (scope is null) throw new InvalidOperationException("no global scope layout");

        var count = SlangApi.spReflectionTypeLayout_getBindingRangeCount(scope);
        Console.Out.WriteLine($"{count} binding range(s)");

        for (long b = 0; b < count; b++)
        {
            // Every one of these is answered, for every range, including the one below.
            var type = SlangApi.spReflectionTypeLayout_getBindingRangeType(scope, b);
            var set = SlangApi.spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(scope, b);
            Console.Out.WriteLine($"  [{b}] type={type} set={set}");
        }

        for (long b = 0; b < count; b++)
        {
            var type = SlangApi.spReflectionTypeLayout_getBindingRangeType(scope, b);
            Console.Out.WriteLine($"  asking image format of range {b} ({type})");
            Console.Out.Flush();

            var format = SlangApi.spReflectionTypeLayout_getBindingRangeImageFormat(scope, b);
            Console.Out.WriteLine($"    -> {format}");
        }
    }

    /// <summary>The generic-bearing program several cases share, with its pieces kept alive.</summary>
    private sealed unsafe class GenericProgram(
        SlangGlobalSession global, SlangSession session, SlangModule module, LinkedProgram program)
        : IDisposable
    {
        public SlangProgramLayout* Shader => (SlangProgramLayout*)program.GetLayout(0);

        public void Dispose()
        {
            program.Dispose();
            module.Dispose();
            session.Dispose();
            global.Dispose();
        }
    }

    private static GenericProgram LoadGenericProgram()
    {
        const string Source = """
            interface IMgMaterial { float3 shade(float3 albedo); }
            struct MgLambert : IMgMaterial { float3 shade(float3 a) { return a; } }
            struct MgWrapper<T : IMgMaterial> { T inner; float4 tint; };

            [shader("fragment")]
            float4 mgFragment() : SV_Target { return 1; }
            """;

        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        var module = session.LoadModuleFromSource("mg", "mg.slang", Source);
        using var entry = module.FindEntryPoint("mgFragment");
        var program = session.Link(module, entry);
        return new GenericProgram(global, session, module, program);
    }

    private static unsafe void MismatchedGenericArgument(bool wrongKind)
    {
        const string Source = """
            interface IMgMaterial { float3 shade(float3 albedo); }
            struct MgLambert : IMgMaterial { float3 shade(float3 a) { return a; } }
            struct MgWrapper<T : IMgMaterial> { T inner; float4 tint; };

            [shader("fragment")]
            float4 mgFragment() : SV_Target { return 1; }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(
            new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("mg", "mg.slang", Source);
        using var entry = module.FindEntryPoint("mgFragment");
        using var program = session.Link(module, entry);

        var shader = (SlangProgramLayout*)program.GetLayout(0);

        SlangReflectionType* declared;
        using (Utf8.Scoped n = new("MgWrapper"))
        {
            declared = SlangApi.spReflection_FindTypeByName(shader, n);
        }

        if (declared is null) throw new InvalidOperationException("MgWrapper not found");

        var generic = SlangApi.spReflectionType_GetGenericContainer(declared);
        if (generic is null) throw new InvalidOperationException("MgWrapper is not generic");

        ISlangBlob* diagnostics = null;
        SlangReflectionGeneric* specialized;

        if (wrongKind)
        {
            // An integer where the single type parameter belongs.
            var kind = SlangReflectionGenericArgType.SLANG_GENERIC_ARG_INT;
            var arg = new SlangReflectionGenericArg { intVal = 3 };
            specialized = SlangApi.spReflection_specializeGeneric(
                shader, generic, 1, &kind, &arg, &diagnostics);
        }
        else
        {
            // Two type arguments for one type parameter. The right kind, the wrong number — and
            // the second is a real type, so nothing here is a dangling or mistyped pointer.
            SlangReflectionType* lambert;
            using (Utf8.Scoped n = new("MgLambert"))
            {
                lambert = SlangApi.spReflection_FindTypeByName(shader, n);
            }

            if (lambert is null) throw new InvalidOperationException("MgLambert not found");

            var kinds = stackalloc SlangReflectionGenericArgType[2];
            kinds[0] = SlangReflectionGenericArgType.SLANG_GENERIC_ARG_TYPE;
            kinds[1] = SlangReflectionGenericArgType.SLANG_GENERIC_ARG_TYPE;

            var args = stackalloc SlangReflectionGenericArg[2];
            args[0] = new SlangReflectionGenericArg { typeVal = lambert };
            args[1] = new SlangReflectionGenericArg { typeVal = lambert };

            specialized = SlangApi.spReflection_specializeGeneric(
                shader, generic, 2, kinds, args, &diagnostics);
        }

        Console.Out.WriteLine(specialized is null ? "refused" : "specialized");
        if (diagnostics is not null)
        {
            using var blob = new SlangBlob(diagnostics);
            Console.Out.WriteLine($"diagnostics: {blob.AsText()}");
        }
    }

    /// <summary>Compiles a shader and walks its layout, which is where the process is lost.</summary>
    private static void Reflect(string source)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(
            new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("probe", "probe.slang", source);
        using var entry = module.FindEntryPoint(EntryPointNameIn(source));
        using var program = session.Link(module, entry);

        // Reflect() is what walks the binding ranges. Touching the result keeps a future
        // lazier implementation from making this case pass by never asking the question.
        var layout = program.Reflect();
        Console.Out.WriteLine(
            $"{layout.DescriptorSets.Count} set(s), "
            + $"{layout.DescriptorSets.Sum(s => s.Ranges.Count)} range(s)");
    }

    private static string EntryPointNameIn(string source) =>
        System.Text.RegularExpressions.Regex.Match(source, @"float4 (\w+)\(\) : SV_Target").Groups[1].Value;
}
