// ReflectionWalker against the Slang team's own answer.
//
// SlangReflectionCorpusTests compares `spReflection_ToJson` with the same .expected files, and that
// proves the *program* was assembled correctly — but the JSON there is written by Slang, so the
// 1,555 lines of projection in ReflectionWalker are not in the comparison at all. This file puts
// them there: the same programs, walked into ProgramLayout, checked field by field against the
// JSON the Slang team recorded.
//
// It is the only test in the suite where an expected value was written by someone who was not
// reading this repository's output. Everything else either derives the answer from Slang at run
// time or asserts an invariant; both are satisfied by a projection that is consistently wrong.
//
// Two things keep it honest, and neither is optional:
//
//   * The comparison counts. A walker that silently checked three fields per shader would pass, so
//     the number of value comparisons made is asserted, not just their agreement.
//   * Keys the comparer does not understand are inventoried and ratcheted, not ignored. "We compare
//     everything Slang says" and "we compare the eight keys someone got round to" look identical
//     from a green run, and the second is what this file would decay into.

using System.Globalization;
using System.Text.Json;
using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public sealed class SlangWalkerCorpusTests(ITestOutputHelper output)
{
    /// <summary>One place the projection disagrees with Slang's recorded reflection.</summary>
    private sealed record Diff(string Path, string Expected, string Actual)
    {
        public override string ToString() => $"{Path}: slang {Expected}, walker {Actual}";
    }

    /// <summary>What one directive produced when walked and compared.</summary>
    private sealed record Report(
        SlangTestCorpus.ReflectionCase Case,
        string? OutOfReach,
        int Comparisons,
        IReadOnlyList<Diff> Diffs,
        IReadOnlyList<string> UncheckedKeys);

    // ---------------------------------------------------------------- vocabulary

    /// <summary>
    /// Whether a name in Slang's JSON is the same symbol as an enum value of the binding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slang's writer camel-cases what the header screams: <c>SLANG_PARAMETER_CATEGORY_
    /// DESCRIPTOR_TABLE_SLOT</c> becomes <c>descriptorTableSlot</c>, <c>SLANG_RESOURCE_ACCESS_
    /// READ_WRITE</c> becomes <c>readWrite</c>. Comparing with the separators and the case removed
    /// derives that rule rather than restating it as a table, which also absorbs the handful of
    /// names the writer emits in PascalCase (<c>Interface</c>) instead of camel.
    /// </para>
    /// <para>
    /// Every name sharing the value, not <c>ToString()</c>. slang.h aliases freely — stage 5 is
    /// both <c>SLANG_STAGE_FRAGMENT</c> and <c>SLANG_STAGE_PIXEL</c>, and the constant-buffer
    /// category is also <c>SLANG_PARAMETER_CATEGORY_METAL_BUFFER</c> — and which of the two
    /// <c>ToString()</c> returns is not something a comparison should depend on. The first draft of
    /// this did depend on it and reported 40 disagreements that were all the same alias.
    /// </para>
    /// </remarks>
    private static bool Same<T>(string json, T value, string prefix) where T : struct, Enum =>
        Aliases(value, prefix).Contains(Canon(json));

    private static string Canon(string s) => s.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    private static readonly Dictionary<Type, Dictionary<long, HashSet<string>>> AliasCache = [];

    /// <summary>Every spelling of one enum value, canonicalised, with the prefix removed.</summary>
    private static HashSet<string> Aliases<T>(T value, string prefix) where T : struct, Enum
    {
        if (!AliasCache.TryGetValue(typeof(T), out var byValue))
        {
            byValue = [];
            foreach (var name in Enum.GetNames<T>())
            {
                var key = Convert.ToInt64(Enum.Parse<T>(name), CultureInfo.InvariantCulture);
                var bare = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;

                if (!byValue.TryGetValue(key, out var names)) byValue[key] = names = [];
                names.Add(Canon(bare));
            }
            AliasCache[typeof(T)] = byValue;
        }

        return byValue.TryGetValue(Convert.ToInt64(value, CultureInfo.InvariantCulture), out var found)
            ? found
            : [];
    }

    /// <summary>How to print an enum value when it did not match, aliases and all.</summary>
    private static string Spell<T>(T value, string prefix) where T : struct, Enum =>
        string.Join("|", Aliases(value, prefix).Order(StringComparer.Ordinal));

    // ---------------------------------------------------------------- the comparison

    private sealed class Walk(string source)
    {
        private readonly List<Diff> _diffs = [];
        private readonly SortedSet<string> _unchecked = new(StringComparer.Ordinal);

        public int Comparisons { get; private set; }

        public IReadOnlyList<Diff> Diffs => _diffs;

        public IReadOnlyList<string> UncheckedKeys => [.. _unchecked];

        private void Check(string path, bool ok, string expected, string actual)
        {
            Comparisons++;
            if (!ok) _diffs.Add(new Diff($"{source}{path}", expected, actual));
        }

        private void Equal(string path, string expected, string actual) =>
            Check(path, string.Equals(expected, actual, StringComparison.Ordinal), expected, actual);

        private void Equal(string path, long expected, long actual) =>
            Check(path, expected == actual,
                expected.ToString(CultureInfo.InvariantCulture),
                actual.ToString(CultureInfo.InvariantCulture));

        private void Symbol<T>(string path, string json, T value, string prefix) where T : struct, Enum =>
            Check(path, Same(json, value, prefix), json, Spell(value, prefix));

        /// <summary>The category-keyed lookup the three per-category sections all need.</summary>
        private bool Category<T>(string path, string kind, IReadOnlyList<T> candidates,
            Func<T, SlangParameterCategory> category)
        {
            var ok = candidates.Any(c => Same(kind, category(c), CategoryPrefix));

            Check($"{path}.kind", ok, kind,
                candidates.Count == 0
                    ? "(none)"
                    : string.Join("|", candidates.Select(c => Spell(category(c), CategoryPrefix))));

            return ok;
        }

        private const string CategoryPrefix = "SLANG_PARAMETER_CATEGORY_";

        /// <summary>A key present in Slang's output that nothing here compares.</summary>
        private void Unchecked(string path, string key) => _unchecked.Add($"{path}.{key}");

        // ------------------------------------------------------------ program

        public void Program(JsonElement root, ProgramLayout layout)
        {
            foreach (var key in Keys(root))
            {
                switch (key)
                {
                    case "parameters":
                        Parameters("", root.GetProperty("parameters"), layout.Parameters);
                        break;

                    case "entryPoints":
                        EntryPoints(root.GetProperty("entryPoints"), layout.EntryPoints);
                        break;

                    case "bindlessSpaceIndex":
                        Equal(".bindlessSpaceIndex",
                            root.GetProperty("bindlessSpaceIndex").GetInt64(),
                            layout.BindlessSpaceIndex is { } space ? space : -1);
                        break;

                    default:
                        Unchecked("", key);
                        break;
                }
            }
        }

        private void Parameters(string path, JsonElement json, IReadOnlyList<ShaderParameter> actual)
        {
            var expected = json.GetArrayLength();
            Equal($"{path}.parameters.count", expected, actual.Count);
            if (expected != actual.Count) return;

            for (var i = 0; i < expected; i++)
                Parameter($"{path}.parameters[{i}]", json[i], actual[i]);
        }

        private void Parameter(string path, JsonElement json, ShaderParameter actual)
        {
            foreach (var key in Keys(json))
            {
                switch (key)
                {
                    case "name":
                        Equal($"{path}.name", json.GetProperty("name").GetString() ?? "", actual.Name);
                        break;

                    case "type":
                        Type($"{path}.type", json.GetProperty("type"), actual.Type);
                        break;

                    case "binding":
                        Binding($"{path}.binding", json.GetProperty("binding"), actual.Bindings);
                        break;

                    case "semanticName":
                        Equal($"{path}.semanticName",
                            json.GetProperty("semanticName").GetString() ?? "", actual.Semantic ?? "");
                        break;

                    case "semanticIndex":
                        Equal($"{path}.semanticIndex",
                            json.GetProperty("semanticIndex").GetInt64(), actual.SemanticIndex);
                        break;

                    case "stage":
                        Symbol($"{path}.stage", json.GetProperty("stage").GetString() ?? "", actual.Stage, "SLANG_STAGE_");
                        break;

                    default:
                        Unchecked(path, key);
                        break;
                }
            }
        }

        private void Binding(string path, JsonElement json, IReadOnlyList<ParameterBinding> bindings)
        {
            // Slang writes one binding per parameter here; the projection keeps the list because a
            // struct mixing a texture and uniform data occupies two categories at once. The one
            // Slang names is the one to compare against.
            if (!json.TryGetProperty("kind", out var kindJson)) { Unchecked(path, "(no kind)"); return; }

            var kind = kindJson.GetString() ?? "";
            if (!Category(path, kind, bindings, b => b.Category)) return;

            var match = bindings.First(b => Same(kind, b.Category, CategoryPrefix));

            foreach (var key in Keys(json))
            {
                switch (key)
                {
                    case "kind":
                        break; // done above

                    // A uniform binding's position is a byte offset; everything else is a slot
                    // index. The projection carries both in ParameterBinding.Index, because Slang
                    // reports them through the same call.
                    case "index" or "offset":
                        Equal($"{path}.{key}", json.GetProperty(key).GetInt64(), match.Index);
                        break;

                    case "space":
                        Equal($"{path}.space", json.GetProperty("space").GetInt64(), match.Space);
                        break;

                    default:
                        Unchecked(path, key);
                        break;
                }
            }
        }

        // ------------------------------------------------------------ types

        private void Type(string path, JsonElement json, TypeLayout actual)
        {
            foreach (var key in Keys(json))
            {
                switch (key)
                {
                    case "kind":
                        Symbol($"{path}.kind", json.GetProperty("kind").GetString() ?? "", actual.Kind, prefix: "");
                        break;

                    case "name":
                        Equal($"{path}.name", json.GetProperty("name").GetString() ?? "", actual.Name ?? "");
                        break;

                    case "scalarType" when actual is ScalarLayout scalar:
                        Symbol($"{path}.scalarType", json.GetProperty("scalarType").GetString() ?? "", scalar.ScalarType, "SLANG_SCALAR_TYPE_");
                        break;

                    case "scalarType" when actual is VectorLayout vector:
                        Symbol($"{path}.scalarType", json.GetProperty("scalarType").GetString() ?? "", vector.ElementType, "SLANG_SCALAR_TYPE_");
                        break;

                    case "scalarType" when actual is MatrixLayout matrix:
                        Symbol($"{path}.scalarType", json.GetProperty("scalarType").GetString() ?? "", matrix.ElementType, "SLANG_SCALAR_TYPE_");
                        break;

                    case "elementCount" when actual is VectorLayout vector:
                        Equal($"{path}.elementCount",
                            json.GetProperty("elementCount").GetInt64(), vector.ElementCount);
                        break;

                    // Slang spells "no fixed length" as 0; the projection spells it as null, which
                    // is the distinction ArrayLayout.IsSpecializationSized then refines. Zero and
                    // null are the same claim here, and mapping null to anything else would report
                    // every unbounded array as a disagreement.
                    case "elementCount" when actual is ArrayLayout array:
                        Equal($"{path}.elementCount",
                            json.GetProperty("elementCount").GetInt64(), array.ElementCount ?? 0);
                        break;

                    case "rowCount" when actual is MatrixLayout matrix:
                        Equal($"{path}.rowCount", json.GetProperty("rowCount").GetInt64(), matrix.RowCount);
                        break;

                    case "columnCount" when actual is MatrixLayout matrix:
                        Equal($"{path}.columnCount",
                            json.GetProperty("columnCount").GetInt64(), matrix.ColumnCount);
                        break;

                    case "baseShape" when actual is ResourceLayout resource:
                        Symbol($"{path}.baseShape", json.GetProperty("baseShape").GetString() ?? "", (resource.Shape & SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK),
                            "SLANG_");
                        break;

                    case "access" when actual is ResourceLayout resource:
                        Symbol($"{path}.access", json.GetProperty("access").GetString() ?? "", resource.Access, "SLANG_RESOURCE_ACCESS_");
                        break;

                    // Slang's `resultType` is the laid-out element: it carries sizes and field
                    // offsets. The projection splits that in two — ResultType *names* the type and
                    // ElementType *measures* it — because spReflectionType_GetResourceResultType
                    // hands back a type rather than a type layout, and layout questions can only
                    // be asked of a layout. ElementType is therefore the one to compare against
                    // wherever there is one; ResultType alone would report every StructuredBuffer
                    // element as having no size, which it does, correctly, and pointlessly.
                    case "resultType" when actual is ResourceLayout resource
                        && (resource.ElementType ?? resource.ResultType) is { } element:
                        Type($"{path}.resultType", json.GetProperty("resultType"), element);
                        break;

                    case "elementType" when actual is ArrayLayout array:
                        Type($"{path}.elementType", json.GetProperty("elementType"), array.ElementType);
                        break;

                    case "elementType" when actual is ContainerLayout container:
                        Type($"{path}.elementType", json.GetProperty("elementType"), container.ElementType);
                        break;

                    case "elementType" when actual is ResourceLayout { ElementType: { } buffered }:
                        Type($"{path}.elementType", json.GetProperty("elementType"), buffered);
                        break;

                    case "elementStride" when actual is ResourceLayout resource:
                        Equal($"{path}.elementStride",
                            json.GetProperty("elementStride").GetInt64(), (long)resource.ElementStride);
                        break;

                    case "fields" when actual is StructLayout structure:
                        Fields($"{path}.fields", json.GetProperty("fields"), structure.Fields);
                        break;

                    case "sizes":
                        Sizes($"{path}.sizes", json.GetProperty("sizes"), actual);
                        break;

                    default:
                        Unchecked(path, key);
                        break;
                }
            }
        }

        private void Fields(string path, JsonElement json, IReadOnlyList<FieldLayout> actual)
        {
            var expected = json.GetArrayLength();
            Equal($"{path}.count", expected, actual.Count);
            if (expected != actual.Count) return;

            for (var i = 0; i < expected; i++)
            {
                var element = json[i];
                var field = actual[i];

                foreach (var key in Keys(element))
                {
                    switch (key)
                    {
                        case "name":
                            Equal($"{path}[{i}].name",
                                element.GetProperty("name").GetString() ?? "", field.Name);
                            break;

                        case "type":
                            Type($"{path}[{i}].type", element.GetProperty("type"), field.Type);
                            break;

                        case "binding":
                            FieldOffset($"{path}[{i}].binding", element.GetProperty("binding"), field);
                            break;

                        case "semanticName":
                            Equal($"{path}[{i}].semanticName",
                                element.GetProperty("semanticName").GetString() ?? "", field.Semantic ?? "");
                            break;

                        case "semanticIndex":
                            Equal($"{path}[{i}].semanticIndex",
                                element.GetProperty("semanticIndex").GetInt64(), field.SemanticIndex);
                            break;

                        default:
                            Unchecked($"{path}[]", key);
                            break;
                    }
                }
            }
        }

        /// <summary>A field's position, which the projection keeps per category as an offset.</summary>
        private void FieldOffset(string path, JsonElement json, FieldLayout field)
        {
            if (!json.TryGetProperty("kind", out var kindJson)) return;

            var kind = kindJson.GetString() ?? "";
            if (!Category(path, kind, field.Offsets, o => o.Category)) return;

            var match = field.Offsets.First(o => Same(kind, o.Category, CategoryPrefix));

            foreach (var key in Keys(json))
            {
                switch (key)
                {
                    case "kind":
                        break;

                    case "index" or "offset":
                        Equal($"{path}.{key}", json.GetProperty(key).GetInt64(), (long)match.Offset);
                        break;

                    case "space":
                        Equal($"{path}.space", json.GetProperty("space").GetInt64(), match.Space);
                        break;

                    default:
                        Unchecked(path, key);
                        break;
                }
            }
        }

        private void Sizes(string path, JsonElement json, TypeLayout actual)
        {
            for (var i = 0; i < json.GetArrayLength(); i++)
            {
                var element = json[i];
                if (!element.TryGetProperty("kind", out var kindJson)) continue;

                var kind = kindJson.GetString() ?? "";
                if (!Category($"{path}[{i}]", kind, actual.Sizes, s => s.Category)) continue;

                var match = actual.Sizes.First(s => Same(kind, s.Category, CategoryPrefix));

                foreach (var key in Keys(element))
                {
                    switch (key)
                    {
                        case "kind":
                            break;

                        // Slang writes the markers as strings ("unbounded"); the projection keeps
                        // them as the sentinel numbers CategorySize documents.
                        case "value" when element.GetProperty("value").ValueKind == JsonValueKind.Number:
                            Equal($"{path}[{i}].value",
                                element.GetProperty("value").GetInt64(), (long)match.Size);
                            break;

                        // One alignment per type in the projection, and it is the uniform one —
                        // the only category measured in bytes.
                        case "alignment" when Same(kind,
                            SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM, CategoryPrefix):
                            Equal($"{path}[{i}].alignment",
                                element.GetProperty("alignment").GetInt64(), actual.Alignment);
                            break;

                        default:
                            Unchecked($"{path}[]", key);
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------ entry points

        private void EntryPoints(JsonElement json, IReadOnlyList<EntryPointLayout> actual)
        {
            var expected = json.GetArrayLength();
            Equal(".entryPoints.count", expected, actual.Count);
            if (expected != actual.Count) return;

            for (var i = 0; i < expected; i++)
            {
                var element = json[i];
                var entry = actual[i];
                var path = $".entryPoints[{i}]";

                foreach (var key in Keys(element))
                {
                    switch (key)
                    {
                        case "name":
                            Equal($"{path}.name", element.GetProperty("name").GetString() ?? "", entry.Name);
                            break;

                        case "stage":
                            Symbol($"{path}.stage", element.GetProperty("stage").GetString() ?? "", entry.Stage, "SLANG_STAGE_");
                            break;

                        case "parameters":
                            Parameters(path, element.GetProperty("parameters"), entry.Parameters);
                            break;

                        case "threadGroupSize":
                            ThreadGroup($"{path}.threadGroupSize",
                                element.GetProperty("threadGroupSize"), entry);
                            break;

                        case "usesAnySampleRateInput":
                            {
                                // Written as a JSON boolean, unlike the `used` flags next door, which
                                // are 0/1 numbers. Both spellings are accepted rather than assumed.
                                var flag = element.GetProperty("usesAnySampleRateInput");
                                var expectedFlag = flag.ValueKind switch
                                {
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    JsonValueKind.Number => flag.GetInt64() != 0,
                                    _ => (bool?)null,
                                };

                                if (expectedFlag is { } wanted)
                                {
                                    Check($"{path}.usesAnySampleRateInput",
                                        wanted == entry.UsesAnySampleRateInput,
                                        wanted.ToString(), entry.UsesAnySampleRateInput.ToString());
                                }
                                else
                                {
                                    Unchecked(path, key);
                                }

                                break;
                            }

                        default:
                            Unchecked(path, key);
                            break;
                    }
                }
            }
        }

        private void ThreadGroup(string path, JsonElement json, EntryPointLayout entry)
        {
            if (entry.ThreadGroupSize is not { } size)
            {
                Check(path, false, json.ToString(), "(none)");
                return;
            }

            long[] actual = [(long)size.X, (long)size.Y, (long)size.Z];
            for (var i = 0; i < Math.Min(3, json.GetArrayLength()); i++)
                Equal($"{path}[{i}]", json[i].GetInt64(), actual[i]);
        }

        private static IEnumerable<string> Keys(JsonElement element) =>
            element.ValueKind != JsonValueKind.Object
                ? []
                : element.EnumerateObject().Select(p => p.Name);
    }

    // ---------------------------------------------------------------- running

    private static Report Run(SlangTestCorpus.ReflectionCase test)
    {
        try
        {
            using var built = SlangTestCorpus.Build(test);

            var walk = new Walk(test.ToString());
            using var expected = JsonDocument.Parse(test.ExpectedJson);
            walk.Program(expected.RootElement, built.Program.Reflect(0));

            return new Report(test, null, walk.Comparisons, walk.Diffs, walk.UncheckedKeys);
        }
        catch (SlangTestCorpus.OutOfReachException e)
        {
            return new Report(test, e.Message, 0, [], []);
        }
    }

    private static readonly Lazy<IReadOnlyList<Report>> LazyReports =
        new(() => [.. SlangTestCorpus.Reflection.Select(Run)]);

    private static IReadOnlyList<Report> Reports => LazyReports.Value;

    // ---------------------------------------------------------------- the tests

    /// <summary>Places the projection knowingly cannot match Slang, and why.</summary>
    /// <remarks>
    /// Keyed on what Slang said, because that is the stable half — the projection's answer is the
    /// thing that would change if the gap were closed. Checked in both directions below.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownGaps = new(StringComparer.Ordinal)
    {
        // Empty, and the assertion below fails if it stops being empty without a reason being
        // written here. The one entry it used to hold — GenericTypeParameter, which the walker
        // reported as LayoutKind.Unknown — was a gap this corpus found and it has been closed
        // rather than declared.
    };

    [NativeFact]
    public void The_projection_agrees_with_the_reflection_the_Slang_team_recorded()
    {
        var ran = Reports.Where(r => r.OutOfReach is null).ToList();
        var all = ran.SelectMany(r => r.Diffs).ToList();

        var unexplained = all.Where(d => !KnownGaps.ContainsKey(d.Expected)).ToList();
        var explained = all.Where(d => KnownGaps.ContainsKey(d.Expected)).ToList();

        output.WriteLine($"{ran.Count} directives walked, "
            + $"{ran.Sum(r => r.Comparisons)} value comparisons, "
            + $"{unexplained.Count} disagreements, {explained.Count} declared gap(s)");

        foreach (var d in explained) output.WriteLine($"  declared gap: {d}");

        Assert.True(unexplained.Count == 0,
            $"ReflectionWalker disagrees with Slang's own reflection in {unexplained.Count} "
            + $"place(s), across {unexplained.Select(d => d.Path.Split('.')[0]).Distinct().Count()} "
            + $"shader(s):\n"
            + string.Join("", unexplained.Take(20).Select(d => $"    {d}\n")));

        // The other direction. A gap that stops happening is a gap that has been closed, and the
        // declaration outliving it turns this list into folklore.
        var stale = KnownGaps.Keys
            .Where(k => !all.Any(d => d.Expected == k))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            $"{stale.Count} declared gap(s) no longer occur. Remove them:\n"
            + string.Join("", stale.Select(k => $"    {k} — was: {KnownGaps[k]}\n")));
    }

    [NativeFact]
    public void Enough_was_actually_compared_for_agreement_to_mean_anything()
    {
        // A walker that compared the parameter count and stopped would satisfy the test above on
        // every shader in the corpus. The floor is what makes agreement a claim rather than a
        // formality, and it is a floor rather than an equality so that adding a comparison is not
        // a failing test.
        var total = Reports.Sum(r => r.Comparisons);
        var walked = Reports.Count(r => r.OutOfReach is null);

        foreach (var r in Reports.Where(r => r.OutOfReach is null).OrderBy(r => r.Comparisons))
            output.WriteLine($"  {r.Comparisons,5} comparisons  {r.Case}");

        // 2,414 at the time of writing. The floor sits below that with room for the corpus to
        // shift, and above the point where the comparer would have to have lost whole sections.
        Assert.True(walked >= 25, $"only {walked} directives were walked");
        Assert.True(total >= 2000,
            $"only {total} value comparisons across {walked} directives — the comparer stopped "
            + "understanding Slang's output and is now agreeing about almost nothing.");
    }

    [NativeFact]
    public void Nothing_in_Slangs_output_goes_unchecked_but_what_is_declared()
    {
        // The coverage ledger. Every key Slang writes that this comparer has no handler for lands
        // here, so the answer to "how much of the ground truth do we actually check" is a list
        // rather than an impression — and it cannot grow without saying so.
        var seen = Reports.SelectMany(r => r.UncheckedKeys)
            .Select(k => k[(k.LastIndexOf('.') + 1)..])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in seen.Order(StringComparer.Ordinal)) output.WriteLine($"unchecked: {key}");

        var undeclared = seen.Where(k => !UncheckedKeys.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var stale = UncheckedKeys.Keys.Where(k => !seen.Contains(k)).Order(StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0,
            $"{undeclared.Count} key(s) in Slang's reflection output are not compared and not "
            + $"declared:\n" + string.Join("", undeclared.Select(k => $"    {k}\n"))
            + "Add a case to the comparer, or list it below with the reason it is not checked.");

        Assert.True(stale.Count == 0,
            $"{stale.Count} key(s) are declared unchecked but the comparer now handles them, or the "
            + $"corpus no longer produces them. Remove them:\n"
            + string.Join("", stale.Select(k => $"    {k} — was: {UncheckedKeys[k]}\n")));
    }

    /// <summary>Keys Slang writes that this comparer does not check, and why.</summary>
    /// <remarks>
    /// The coverage ledger. Roughly 2,400 value comparisons are made across the corpus; these are
    /// what is left, and each is here because the projection genuinely does not carry it or because
    /// checking it would need a second walk of a different shape — never because nobody looked.
    /// </remarks>
    private static readonly Dictionary<string, string> UncheckedKeys = new(StringComparer.Ordinal)
    {
        ["bindings"] =
            "an entry point's post-emit binding list, carrying the `used` flags. Needs an "
            + "ICompileRequest; SlangMetadata.IsUsed is the modern route and is a different shape",

        ["count"] =
            "the length of a `typeParams` or `constraints` list, neither of which is projected",

        ["typeParams"] =
            "global generic type parameters of an unspecialised program. LinkedProgram.Reflect "
            + "walks a linked program, where those are resolved",

        // The two halves Slang splits a ConstantBuffer<T> into: the buffer's own binding, and the
        // element's offsets within it. ContainerLayout collapses them into ElementType plus the
        // parameter's own binding, which is the shape a host binds from — comparing these would
        // need a walk that un-collapses it.
        ["containerVarLayout"] = "half of Slang's ConstantBuffer split; ContainerLayout collapses it",
        ["elementVarLayout"] = "the other half; see containerVarLayout",

        ["result"] =
            "an entry point's return type layout. EntryPointLayout.ResultType carries it, but "
            + "Slang writes it as a var-layout with the same container/element split as above",

        ["shared"] =
            "the HLSL `shared` modifier on a cbuffer. Not projected: it has no Vulkan meaning and "
            + "no consumer here has asked for it",

        ["format"] =
            "the image format of a `[format(...)]`-decorated resource. Projected onto the "
            + "descriptor range rather than onto the type, so it is reached from DescriptorSets "
            + "and not from the parameter walk this comparer follows",

        ["userAttribs"] =
            "user attributes. Projected as TypeLayout.Attributes and ShaderParameter.Attributes, "
            + "and covered by ReflectionTests against a shader written for it; the JSON spells "
            + "argument values in a form that needs its own comparison",

        ["uniformStride"] =
            "the byte stride between array elements in a uniform block. ArrayLayout does not "
            + "carry it separately — UniformLayoutTests asserts the stride against the emitted "
            + "module's ArrayStride decoration instead, which is a stronger oracle",

        ["size"] =
            "how many units of a category a binding spans. ParameterBinding is (category, index, "
            + "space); the span lives on the type's Sizes, which is compared",

        ["elementStride"] =
            "on a *binding* rather than on a resource type. The resource case is compared against "
            + "ResourceLayout.ElementStride; a binding's stride has no counterpart on the "
            + "(category, index, space) triple ParameterBinding is",

        ["elementType"] =
            "on a type the projection models without one — a resource with no element layout "
            + "(a texture, a byte-address buffer), where ResourceLayout.ElementType is null by "
            + "design and ResultType names the type instead",

        ["value"] =
            "a size reported as a string marker rather than a number — Slang's spelling of "
            + "unbounded. The numeric case is compared; CategorySize.IsKnown is the projection's "
            + "equivalent and is asserted by UnknownSizeTests",

        ["stage"] =
            "the stage on a varying's binding, which repeats what the entry point already says. "
            + "Compared there",

        // The next four are absent from slang-compiler 2026.14.1's corpus and present in 2026.21's,
        // where the JSON writer starts saying "version": "1.1".
        ["version"] =
            "the reflection JSON format's own version. A property of Slang's writer, not of the "
            + "program, so there is nothing in the projection to compare it with",

        ["globalScope"] =
            "the global scope as one variable layout: the container Slang wraps the globals in, "
            + "and under it the top-level `parameters` again. Measured across the 2026.21 corpus, "
            + "identical to the top-level list in 32 of 33 files, the other differing only in "
            + "writing a pointer's valueType as a name; the top-level list is compared",

        ["scope"] =
            "an entry point's parameters again, inside the container Slang would put its uniform "
            + "parameters in. Measured across the 2026.21 corpus, identical to the entry point's "
            + "own `parameters` in all 34 entry points, which are compared",

        ["valueType"] =
            "the pointee of a `pointer` type. The projection reports a pointer as "
            + "LayoutKind.Pointer and carries no pointee, so there is nothing to compare it with",
    };
}
