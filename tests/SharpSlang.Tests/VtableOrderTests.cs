// Is slot N the function slang.h puts at slot N?
//
// Every call into a Slang COM interface is an index into a vtable: `lpVtbl[7]` reinterpreted as a
// function pointer of the signature ClangSharp inferred, and invoked. Nothing checks the index. If
// it is wrong the code still compiles, still resolves, and calls a different function with the
// wrong arguments — corruption, not an exception, and on a slot nothing exercises it would not
// surface until a consumer reached it.
//
// The `regen` CI job proves the committed bindings equal what the generator produces from the
// pinned header. That is reproducibility. It cannot notice a generator that miscounts, because it
// compares the generator against itself.
//
// So the numbers are re-derived from the header independently. Slang's interfaces are
// single-inheritance chains of pure virtual methods, which makes a vtable exactly the base's vtable
// followed by this class's own declarations in source order — derivable by reading slang.h, which
// SlangHeader already does for the constant drift tests.
//
// Read out of dotnet/Native/*.cs rather than by reflecting over the assembly, for two reasons.
// [VtblIndex] is [Conditional("DEBUG")], so in the Release build CI runs the attributes are not in
// the metadata at all and reflection would find nothing to check. And the attribute is a comment:
// `lpVtbl[N]` in the emitted call is what actually executes. Both are read here and both are
// checked, so an annotation that disagrees with its own dispatch is also a failure.

using System.Text.RegularExpressions;

namespace SharpSlang.Tests;

public partial class VtableOrderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>One generated vtable method: the slot it claims, the slot it calls, and its name.</summary>
    private sealed record Slot(int Attribute, int Dispatch, string Name);

    /// <summary>A generated COM interface and the slots it declares, in file order.</summary>
    private sealed record Generated(string Name, IReadOnlyList<Slot> Slots);

    // ---------------------------------------------------------------- what the generator emitted

    private static readonly Lazy<IReadOnlyList<Generated>> LazyEmitted = new(ReadGenerated);

    private static IReadOnlyList<Generated> Emitted => LazyEmitted.Value;

    private static List<Generated> ReadGenerated()
    {
        var directory = Path.Combine(SlangHeader.RepoRoot(), "dotnet", "Native");
        var result = new List<Generated>();

        foreach (var file in Directory.EnumerateFiles(directory, "I*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file);
            var marks = VtblIndexAttribute().Matches(text);
            if (marks.Count == 0) continue;

            var slots = new List<Slot>();
            for (var i = 0; i < marks.Count; i++)
            {
                // Everything up to the next [VtblIndex] belongs to this method. Bounding the search
                // matters: without it the "first lpVtbl[...] after here" would run on into the next
                // method whenever one of them failed to match.
                var end = i + 1 < marks.Count ? marks[i + 1].Index : text.Length;
                var chunk = text[marks[i].Index..end];

                var name = MethodName().Match(chunk);
                var dispatch = VtblDispatch().Match(chunk);

                Assert.True(name.Success, $"{Path.GetFileName(file)}: no method follows {marks[i].Value}");
                Assert.True(dispatch.Success,
                    $"{Path.GetFileName(file)}: {name.Groups["name"].Value} carries {marks[i].Value} but "
                    + "never indexes lpVtbl. The shape of the generated code changed and this test is "
                    + "reading it wrong.");

                slots.Add(new Slot(
                    int.Parse(marks[i].Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(dispatch.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    name.Groups["name"].Value));
            }

            result.Add(new Generated(Path.GetFileNameWithoutExtension(file), slots));
        }

        return result;
    }

    // ---------------------------------------------------------------- the tests

    [Fact]
    public void There_are_generated_interfaces_to_check()
    {
        // The others all iterate a collection, so an empty one passes every single check. A
        // renamed directory or a changed code shape is the way that happens.
        Assert.NotEmpty(Emitted);

        var slots = Emitted.Sum(i => i.Slots.Count);
        output.WriteLine($"{Emitted.Count} interfaces, {slots} vtable slots");
        Assert.InRange(slots, 300, 500);

        DocumentedFactsTests.Documents("AGENTS.md", $"all {slots} slot indices",
            $"the generated layer has {slots} vtable slots");
    }

    [Fact]
    public void The_attribute_and_the_call_agree_about_the_slot()
    {
        // [VtblIndex(4)] on a method whose body indexes lpVtbl[5] would be a call to the wrong
        // function with a correct-looking annotation over it. The two come from the same generator
        // pass, so this is cheap and has never been expected to fire — but "the annotation is
        // documentation and the index is what runs" is only a safe thing to say if it is checked.
        var wrong = Emitted
            .SelectMany(i => i.Slots.Select(s => (Interface: i.Name, Slot: s)))
            .Where(x => x.Slot.Attribute != x.Slot.Dispatch)
            .ToList();

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} method(s) call a slot other than the one they are annotated with:\n"
            + string.Join("", wrong.Select(x =>
                $"    {x.Interface}.{x.Slot.Name}: [VtblIndex({x.Slot.Attribute})] but lpVtbl[{x.Slot.Dispatch}]\n")));
    }

    [Fact]
    public void Every_interfaces_slots_are_a_gapless_sequence_from_zero()
    {
        // A duplicate or a hole is a miscount that the order comparison would also catch, but would
        // report as a long misaligned diff. This names it directly.
        var broken = new List<string>();

        foreach (var iface in Emitted)
        {
            var expected = Enumerable.Range(0, iface.Slots.Count).ToList();
            var actual = iface.Slots.Select(s => s.Dispatch).ToList();

            if (!actual.SequenceEqual(expected))
                broken.Add($"    {iface.Name}: {string.Join(", ", actual)}\n");
        }

        Assert.True(broken.Count == 0,
            $"{broken.Count} interface(s) do not occupy slots 0..n-1 exactly once:\n" + string.Concat(broken));
    }

    [Fact]
    public void Every_vtable_slot_holds_the_method_the_header_puts_there()
    {
        // The check this file exists for. Everything above compares the generated code with itself.
        var header = SlangHeader.Pinned;
        var mismatched = new List<string>();

        foreach (var iface in Emitted)
        {
            var expected = header.VirtualMethods(iface.Name);
            var actual = iface.Slots.OrderBy(s => s.Dispatch).Select(s => s.Name).ToList();

            if (expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                output.WriteLine($"{iface.Name}: {actual.Count} slots agree");
                continue;
            }

            mismatched.Add($"    {iface.Name}: header has {expected.Count} virtual method(s), "
                + $"the binding has {actual.Count}\n"
                + string.Concat(Enumerable
                    .Range(0, Math.Max(expected.Count, actual.Count))
                    .Select(i =>
                    {
                        var want = i < expected.Count ? expected[i] : "(none)";
                        var got = i < actual.Count ? actual[i] : "(none)";
                        var flag = want == got ? "  " : "->";
                        return $"      {flag} [{i,2}] header {want,-40} binding {got}\n";
                    })));
        }

        Assert.True(mismatched.Count == 0,
            $"{mismatched.Count} interface(s) do not match the vtable order slang.h {header.Version} "
            + $"declares. Every one of these is a call to the wrong function:\n"
            + string.Concat(mismatched));
    }

    [Fact]
    public void Every_COM_interface_in_the_headers_was_generated()
    {
        // The order checks only cover interfaces that exist in dotnet/Native. An interface
        // ClangSharp drops entirely is invisible to all of them, and to the `regen` job — the
        // committed output would match a generator that emits nothing for it.
        //
        // No exclusion list, because none is needed: as of the pinned release the two sets are
        // equal. If that stops being true the right answer is a list with reasons, in the shape
        // SurfaceCoverageTests uses.
        var header = SlangHeader.Pinned;
        var declared = header.ComInterfaces();
        var generated = Emitted.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"{declared.Count} COM interface(s) in slang.h {header.Version}");

        var missing = declared.Where(i => !generated.Contains(i)).ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} interface(s) are declared in the headers and have no binding. Every "
            + $"one is a piece of Slang's API that nothing can reach:\n"
            + string.Join("", missing.Select(i => $"    {i}\n")));
    }

    // ---------------------------------------------------------------- what has never been called

    /// <summary>
    /// How many of an interface's slots nothing outside the generated layer ever calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this number means has changed, and it is worth being exact about. It used to stand for
    /// everything unverified about a slot nobody had called: whether the index was right, whether
    /// the signature was right, whether calling it would work at all. Two of those three are now
    /// settled independently of anyone calling anything — the order check above derives all 374
    /// indices from slang.h, and <c>NativeAbiTests</c> asserts all 231 declared signatures against
    /// what a C++ compiler makes of the same header. The dispatch itself is generated uniformly
    /// and its shape is checked by the attribute-agrees-with-lpVtbl test.
    /// </para>
    /// <para>
    /// So a cold slot is no longer an unverified binding. It is a verified binding through which
    /// Slang's own behaviour has never been observed — which is a claim about Slang, not about
    /// this package, and a different kind of risk. It stays measured because a hand-written L1 or
    /// L2 wrapper added over one of these would be new untested code, and because the count is the
    /// honest answer to "how much of Slang can this package actually reach".
    /// </para>
    /// <para>
    /// Measured by name, which over-counts what is exercised: <c>release</c> appears on all 28
    /// interfaces and one call marks the slot reached on every one of them. The cold numbers below
    /// are therefore a floor — the true figure is higher — and that is the safe direction for a
    /// count whose purpose is to say how much is untested.
    /// </para>
    /// </remarks>
    private static readonly Lazy<Dictionary<string, int>> LazyColdSlots = new(CountColdSlots);

    private static Dictionary<string, int> CountColdSlots()
    {
        var root = SlangHeader.RepoRoot();

        var callers = string.Join("\n", Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Native{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
            .Select(File.ReadAllText));

        var cold = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var iface in Emitted)
        {
            // Both spellings. Every call into a vtable method goes through a pointer, so the call
            // site reads `Ptr->loadModule(...)`; matching only `.name(` finds none of them and
            // reports the entire surface as untried, which is how the first version of this
            // measurement produced a confident 374 of 374.
            var untried = iface.Slots.Count(
                s => !Regex.IsMatch(callers, $@"(?:\.|->){Regex.Escape(s.Name)}\s*\("));

            if (untried > 0) cold[iface.Name] = untried;
        }

        return cold;
    }

    /// <summary>Interfaces with slots nothing has ever called, the count, and why.</summary>
    /// <remarks>
    /// A reason per interface rather than per slot. Two hundred and thirty-five entries reading
    /// "not needed so far" would be a document nobody reads and a diff nobody
    /// reviews; the interface is the level at which the answer actually differs.
    /// </remarks>
    private static readonly Dictionary<string, (int Cold, string Why)> UntriedInterfaces = new(StringComparer.Ordinal)
    {
        // One fact accounts for a slot on all 28: neither queryInterface nor castAs is called
        // anywhere in this repository. The binding casts between interface pointers directly,
        // because Slang's own headers guarantee the layouts and a QI round trip would buy nothing
        // but a refcount to balance. SlangIID exists for a caller who wants the COM route.
        ["ISlangUnknown"] = (1, "queryInterface. Nothing here does a COM cast"),
        ["ISlangCastable"] = (2, "queryInterface and castAs, for the same reason"),
        ["ISlangBlob"] = (1, "queryInterface. getBufferPointer and getBufferSize are the surface"),

        ["ICompileRequest"] = (78,
            "the entire legacy compile-request API, deliberately unbound (AGENTS.md#unbound-surface). "
            + "It is generated only because slang.h includes slang-deprecated.h, and every "
            + "one of its 84 slots duplicates something ISession and IComponentType do better"),

        ["IGlobalSession"] = (28,
            "downstream-compiler configuration (dxc, fxc and glslang paths, preludes and version "
            + "queries), the core-module compile/save/load cycle, command-line argument parsing, "
            + "and createCompileRequest into the legacy API. A SPIR-V cook configures none of them"),

        ["ISession"] = (15,
            "the type-system queries L2 does not surface — specializeType, getTypeLayout, "
            + "getContainerType, the RTTI and witness mangled names — plus the loaded-module "
            + "enumeration and createCompileRequest. specializeType is measured rather than "
            + "unknown: it aborts on Linux for a non-generic type, and the prober records that"),

        ["ISlangMutableFileSystem"] = (14,
            "the write half of the filesystem host, and the read half it inherits. SlangFileSystem "
            + "implements the read-only ISlangFileSystemExt; nothing asks Slang to write files"),

        ["ISlangFileSystemExt"] = (10,
            "implemented, not called. SlangFileSystem builds a vtable Slang dispatches through, so "
            + "these slots are cold as call sites and live as callbacks — CallbackBoundaryTests "
            + "exercises that direction, which this measurement cannot see"),

        ["ISlangFileSystem"] = (3, "the base of the above, implemented rather than called"),

        ["IByteCodeRunner"] = (11,
            "Slang's bytecode VM: loading, executing and inspecting compiled Slang on the CPU. Not "
            + "a path a Vulkan renderer takes, and it needs slang-llvm, which the payload excludes"),

        ["ICooperativeTypesMetadata"] = (10,
            "cooperative matrix and vector type enumeration, for tensor and ML workloads. "
            + "no shader here uses them and the corpus declares neither kind"),

        ["ISlangWriter"] = (7,
            "redirecting Slang's diagnostic and dump streams to a caller-supplied sink. "
            + "Diagnostics arrive as blobs instead, which is the route that survives a callback "
            + "throwing"),

        ["ICoverageTracingMetadata"] = (6, "shader coverage counters, a slangc feature with no host use here"),

        ["IModule"] = (6,
            "getEntryPointHostCallable and disassemble, which need slang-llvm, plus the inherited "
            + "IComponentType members below"),

        ["IComponentType"] = (5,
            "getSession, getResultAsFileSystem, getEntryPointHostCallable and renameEntryPoint. "
            + "The first two are alternate routes to what L2 already holds; the third is CPU "
            + "codegen; the fourth renames an entry point in the *emitted* module, which "
            + "TargetOptions.UseEntryPointNameInSpirv does at the target level instead"),

        ["IEntryPoint"] = (5, "the same five, inherited from IComponentType"),
        ["ITypeConformance"] = (5, "the same five again — ITypeConformance adds no methods of its own"),

        ["IComponentType2"] = (4,
            "getTargetCompileResult and getEntryPointCompileResult, the multi-artifact route to "
            + "generated code. GetEntryPointCode returns the single blob a SPIR-V target produces"),

        ["ICompileResult"] = (5, "the artifact list the above would return"),

        ["IModulePrecompileService_Experimental"] = (5,
            "experimental by name, and precompiling to a target is what ShaderPackageBaker does "
            + "through the supported API"),

        ["ISlangProfiler"] = (5, "the compiler's own timing counters"),

        ["ISyntheticResourceMetadata"] = (5,
            "resources Slang synthesises rather than the shader declaring. Reported through "
            + "reflection where they reach a descriptor, which is the form a host can bind"),

        ["IBindlessResourceMetadata"] = (3,
            "usesBindlessResourceHeap. ProgramLayout.BindlessSpaceIndex answers the question a "
            + "host actually has, and is checked against Slang's own reflection JSON"),

        ["ISlangClonable"] = (3, "clone. Nothing here copies a Slang object"),

        ["ISlangSharedLibrary"] = (3, "symbol lookup in a loaded library — Slang's own plumbing for glslang"),
        ["ISlangSharedLibraryLoader"] = (2, "the loader behind it, replaceable and never replaced"),
        ["ISlangSharedLibrary_Dep1"] = (2, "the superseded version of the same, kept by upstream for ABI"),

        ["IMetadata"] = (2,
            "queryInterface and castAs. isParameterLocationUsed is exercised — SlangMetadata.IsUsed "
            + "is the post-emit usage query the reflection JSON's `used` flags come from"),
    };

    [Fact]
    public void Every_interface_with_untried_slots_says_why_and_how_many()
    {
        var cold = LazyColdSlots.Value;
        var total = cold.Values.Sum();
        var slots = Emitted.Sum(i => i.Slots.Count);

        output.WriteLine($"{total} of {slots} vtable slots have never been called from outside "
            + $"dotnet/Native, across {cold.Count} interface(s)");
        foreach (var (name, count) in cold.OrderByDescending(p => p.Value))
            output.WriteLine($"    {count,3} cold  {name}");

        var undeclared = cold.Keys.Where(k => !UntriedInterfaces.ContainsKey(k))
            .Order(StringComparer.Ordinal).ToList();
        var stale = UntriedInterfaces.Keys.Where(k => !cold.ContainsKey(k))
            .Order(StringComparer.Ordinal).ToList();
        var moved = cold.Where(p => UntriedInterfaces.TryGetValue(p.Key, out var d) && d.Cold != p.Value)
            .OrderBy(p => p.Key, StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0,
            $"{undeclared.Count} interface(s) have slots nothing calls and no entry here:\n"
            + string.Join("", undeclared.Select(k =>
                $"        [\"{k}\"] = ({cold[k]}, \"\"),\n")));

        Assert.True(stale.Count == 0,
            $"{stale.Count} interface(s) are declared untried and are now fully exercised. "
            + $"Remove them:\n"
            + string.Join("", stale.Select(k => $"    {k} — was: {UntriedInterfaces[k].Why}\n")));

        Assert.True(moved.Count == 0,
            $"{moved.Count} interface(s) have a different number of untried slots than declared. "
            + $"A fall is progress and a rise is coverage lost; either way the number here is the "
            + $"record and it has to move with them:\n"
            + string.Join("", moved.Select(p =>
                $"    {p.Key}: declared {UntriedInterfaces[p.Key].Cold}, measured {p.Value}\n")));
    }

    // ---------------------------------------------------------------- patterns

    [GeneratedRegex(@"\[VtblIndex\((?<index>\d+)\)\]")]
    private static partial Regex VtblIndexAttribute();

    // The declaration that follows the attribute, past any [return: ...] annotation on its own line.
    [GeneratedRegex(@"\bpublic\b[^\r\n]*?(?<name>\w+)\s*\(")]
    private static partial Regex MethodName();

    [GeneratedRegex(@"lpVtbl\[(?<index>\d+)\]")]
    private static partial Regex VtblDispatch();
}
