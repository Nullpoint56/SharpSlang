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
    };

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
