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

using System.Runtime.InteropServices;
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

        // §9.28. The three threading patterns slang.h documents, measured instead of trusted.
        // Nothing in the suite has ever used this library from two threads — AssemblyInfo disables
        // test parallelism for unrelated reasons — so the primary use case of a shader cooker is
        // the one thing with no coverage at all. Out of process because the middle case is the one
        // the header says needs external synchronization, and asking it in the runner would be
        // §9.24 all over again.
        // §9.31. Whether the allocator poisoning CI leans on is actually doing anything. Both
        // tripwires are environment variables the runner sets and nobody has ever confirmed took
        // effect — and macOS's pair is documented not to apply to its nano allocator, which is
        // where small objects live. A tripwire that is off looks exactly like one that found
        // nothing.
        ["allocator-poisoning"] = AllocatorPoisoning,

        ["threads-own-global-session"] = ThreadsOwnGlobalSession,
        ["threads-shared-global-session"] = ThreadsSharedGlobalSession,
        ["threads-parallel-backend"] = ThreadsParallelBackend,
    };

    /// <summary>Whether the allocator is really poisoning freed memory, and at which sizes.</summary>
    /// <remarks>
    /// The control for two CI tripwires that nobody had confirmed. `MALLOC_PERTURB_` and macOS's
    /// `MallocScribble`/`MallocPreScribble` are environment variables — a runner can set them, print
    /// them in the log, and have them do nothing, which is exactly what `MALLOC_CHECK_=3` turned out
    /// to be (§9.19). An unarmed tripwire is indistinguishable from one that found nothing.
    ///
    /// Two sizes on purpose. Apple's libmalloc routes allocations up to 256 bytes through the
    /// *nano* zone, and the scribble options apply to the scalable zone — so poisoning can be live
    /// for a 4 KB block and inert for a 64 B one, which is where most AST nodes live. If that is
    /// what this reports, `MallocNanoZone=0` is the missing half.
    ///
    /// The read after free is a deliberate use-after-free. That is the measurement, and this is a
    /// process built to be lost.
    /// </remarks>
    private static unsafe void AllocatorPoisoning()
    {
        foreach (var (what, size) in (( string, nuint)[])[("nano  (64 B)", 64), ("scalable (4 KB)", 4096)])
        {
            var block = (byte*)NativeMemory.Alloc(size);
            var offset = size - 16;                       // clear of any free-list header

            var fresh = Volatile.Read(ref block[offset]);

            // Our own sentinel, so "nothing scrubbed it" is distinguishable from "scrubbed with a
            // byte that happens to be zero".
            new Span<byte>(block, (int)size).Fill(0xC3);
            NativeMemory.Free(block);
            var freed = Volatile.Read(ref block[offset]);

            Console.Out.WriteLine(
                $"    {what,-16} fresh=0x{fresh:X2}  after free=0x{freed:X2}"
                + $"  {Verdict(freed)}");
        }

        foreach (var name in (string[])
                 ["MALLOC_PERTURB_", "MallocScribble", "MallocPreScribble", "MallocNanoZone"])
        {
            Console.Out.WriteLine(
                $"    env {name,-18} = {Environment.GetEnvironmentVariable(name) ?? "<unset>"}");
        }

        // 0xA5 is MALLOC_PERTURB_=165 writing the byte itself into freed memory (it writes the
        // complement into fresh allocations, so 0x5A above says the same thing). 0x55 is
        // MallocScribble. 0xC3 is our sentinel surviving, which means nothing poisoned anything.
        static string Verdict(byte freed) => freed switch
        {
            0xA5 => "<- MALLOC_PERTURB_ is live",
            0x55 => "<- MallocScribble is live",
            0xC3 => "<- NOT POISONED: the sentinel survived the free",
            _ => "<- overwritten by something else (allocator metadata, or reuse)",
        };
    }

    // A shader with enough in it that codegen is not instant: two stages, a constant buffer, a
    // matrix and a texture fetch.
    private const string ThreadSource = """
        struct TUniforms { float4x4 mvp; float4 tint; };
        ConstantBuffer<TUniforms> tUniforms;
        Texture2D    tTexture;
        SamplerState tSampler;

        [shader("vertex")]
        float4 tVertex(float3 p : POSITION) : SV_Position
        {
            return mul(tUniforms.mvp, float4(p, 1));
        }

        [shader("fragment")]
        float4 tFragment(float2 uv : TEXCOORD0) : SV_Target
        {
            return tTexture.Sample(tSampler, uv) * tUniforms.tint;
        }
        """;

    private static SlangSession NewThreadSession(SlangGlobalSession global) =>
        global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });

    /// <summary>Front end plus back end, the whole way through, on one session.</summary>
    private static byte[] CompileOnce(SlangGlobalSession global, string name)
    {
        using var session = NewThreadSession(global);
        using var module = session.LoadModuleFromSource(name, $"{name}.slang", ThreadSource);
        using var vertex = module.FindEntryPoint("tVertex");
        using var fragment = module.FindEntryPoint("tFragment");
        using var program = session.Link(module, vertex, fragment);
        using var code = program.GetTargetCode();
        return code.ToArray();
    }

    /// <summary>
    /// Distinct global sessions in parallel — the pattern slang.h says is allowed.
    /// </summary>
    /// <remarks>
    /// "Distinct global sessions may be used from different threads in parallel." Every thread pays
    /// for its own standard-library load, which is the cost this buys safety with, so the elapsed
    /// time is printed next to the serial one rather than left to be guessed at.
    /// </remarks>
    private static void ThreadsOwnGlobalSession()
    {
        using var baselineGlobal = SlangGlobalSession.Create();
        var baseline = CompileOnce(baselineGlobal, "t");

        Console.Out.WriteLine($"    baseline {baseline.Length} bytes");

        // What this pattern costs, which is the whole reason the other one exists: a global session
        // amortizes the standard-library load, and one per thread pays for it again per thread.
        // Printed rather than asserted — it is a property of the machine, not of the binding.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        SlangGlobalSession.Create().Dispose();
        Console.Out.WriteLine($"    one global session costs {clock.ElapsedMilliseconds} ms");
        Console.Out.Flush();

        var results = RunOnThreads(Threads, index =>
        {
            using var global = SlangGlobalSession.Create();
            return CompileOnce(global, $"t{index}");
        });

        ReportAgreement("own global session", baseline, results);
    }

    /// <summary>
    /// One global session shared across threads with no synchronization — what the header says
    /// requires external synchronization.
    /// </summary>
    /// <remarks>
    /// Not an accusation. The header is explicit that this needs a lock, so a failure here is the
    /// documented behaviour and the point is to record *how* it fails: silently wrong bytes, an
    /// exception, or the process. A cooker that gets this wrong should find out from this line
    /// rather than from a corrupted shader six months later.
    /// </remarks>
    private static void ThreadsSharedGlobalSession()
    {
        using var global = SlangGlobalSession.Create();
        var baseline = CompileOnce(global, "t");

        Console.Out.WriteLine($"    baseline {baseline.Length} bytes");
        Console.Out.Flush();

        var results = RunOnThreads(Threads, index => CompileOnce(global, $"t{index}"));
        ReportAgreement("shared global session, no lock", baseline, results);
    }

    /// <summary>
    /// Serialized front end, parallel back end — the pattern the header calls supported.
    /// </summary>
    /// <remarks>
    /// "After a component type has been fully specialized and linked, this method is supported for
    /// concurrent backend code generation, including from multiple threads compiling different
    /// linked IComponentType instances." That is the one a cooker wants, because the back end is
    /// where the time goes; so it is worth knowing whether it holds rather than assuming it does.
    /// </remarks>
    private static void ThreadsParallelBackend()
    {
        using var global = SlangGlobalSession.Create();
        var baseline = CompileOnce(global, "t");

        Console.Out.WriteLine($"    baseline {baseline.Length} bytes");
        Console.Out.Flush();

        // Front end, one at a time and on one thread, exactly as the header requires. Each program
        // is a *distinct* linked IComponentType, which is the case the note covers.
        var sessions = new SlangSession[Threads];
        var modules = new SlangModule[Threads];
        var programs = new LinkedProgram[Threads];
        try
        {
            for (var i = 0; i < Threads; i++)
            {
                sessions[i] = NewThreadSession(global);
                modules[i] = sessions[i].LoadModuleFromSource($"t{i}", $"t{i}.slang", ThreadSource);
                using var vertex = modules[i].FindEntryPoint("tVertex");
                using var fragment = modules[i].FindEntryPoint("tFragment");
                programs[i] = sessions[i].Link(modules[i], vertex, fragment);
            }

            var results = RunOnThreads(Threads, index =>
            {
                using var code = programs[index].GetTargetCode();
                return code.ToArray();
            });

            ReportAgreement("parallel back end", baseline, results);
        }
        finally
        {
            foreach (var p in programs) p?.Dispose();
            foreach (var m in modules) m?.Dispose();
            foreach (var s in sessions) s?.Dispose();
        }
    }

    private const int Threads = 8;

    /// <summary>
    /// Runs <paramref name="body"/> once per thread, capturing whatever each one produced.
    /// </summary>
    /// <remarks>
    /// A barrier so every thread is inside the work at the same time. Without it a fast machine can
    /// finish thread 0 before thread 7 starts, and a race that never overlaps is a test that never
    /// looked.
    /// </remarks>
    private static (byte[]? Bytes, string? Error)[] RunOnThreads(int count, Func<int, byte[]> body)
    {
        var results = new (byte[]?, string?)[count];
        using var ready = new Barrier(count);
        var threads = new Thread[count];

        for (var i = 0; i < count; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                ready.SignalAndWait();
                try
                {
                    results[index] = (body(index), null);
                }
                catch (Exception e)
                {
                    results[index] = (null, $"{e.GetType().Name}: {e.Message}");
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads) thread.Join();
        return results;
    }

    /// <summary>Whether every thread produced the same bytes the serial run did.</summary>
    private static void ReportAgreement(
        string what,
        byte[] baseline,
        (byte[]? Bytes, string? Error)[] results)
    {
        var failed = 0;
        var differed = 0;

        foreach (var (bytes, error) in results)
        {
            if (error is not null) { failed++; Console.Out.WriteLine($"    threw: {error}"); }
            else if (!bytes!.AsSpan().SequenceEqual(baseline)) differed++;
        }

        Console.Out.WriteLine(
            $"    {what}: {results.Length} threads, {failed} threw, {differed} disagreed with the "
            + "serial baseline");
        Console.Out.Flush();
    }

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
