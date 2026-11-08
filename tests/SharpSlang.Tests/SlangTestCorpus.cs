// Slang's own test suite, at the pinned release, as data this repository can run.
//
// Every oracle in this suite so far is either Slang describing itself, an invariant with no oracle,
// a specification read by a person, or the Khronos validation layers. None of them is *the Slang
// team stating what the right answer is*, and that exists: shader-slang/slang carries a `tests/`
// tree where each shader has a `//TEST:` directive and a checked-in expected result beside it.
//
//   tests/reflection   39 REFLECTION directives across 35 shaders, each with the reflection JSON
//                      the Slang team blessed. Ground truth for what a program's layout *is*.
//   tests/compute      shaders with `//TEST_INPUT:` buffer declarations and the exact output words
//                      the shader must produce. Ground truth for whether a binding actually works.
//
// Fetched at the pinned tag and cached under obj/, the same arrangement SlangHeader uses for
// slang.h: a normal dev loop downloads once, and a version bump misses the cache by design.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpSlang.Native;

namespace SharpSlang.Tests;

/// <summary>The pinned checkout of one directory of Slang's test suite.</summary>
public static partial class SlangTestCorpus
{
    /// <summary>One <c>//TEST(...):REFLECTION:&lt;args&gt;</c> directive and the output it must produce.</summary>
    /// <param name="Source">Shader file name, e.g. <c>parameter-block.slang</c>.</param>
    /// <param name="Index">Directive position in the file. Decides the .expected suffix.</param>
    /// <param name="Arguments">The slangc command line after the directive.</param>
    /// <param name="Text">The shader source.</param>
    /// <param name="ExpectedJson">The reflection JSON from the matching .expected file.</param>
    public sealed record ReflectionCase(
        string Source, int Index, string Arguments, string Text, string ExpectedJson)
    {
        public override string ToString() => Index == 0 ? Source : $"{Source}#{Index}";

        /// <summary>The slangc arguments, split.</summary>
        public string[] Argv => Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The value following a flag, or null if the flag is absent.</summary>
        public string? Value(string flag)
        {
            var argv = Argv;
            for (var i = 0; i < argv.Length - 1; i++)
                if (argv[i] == flag) return argv[i + 1];
            return null;
        }

        /// <summary>Every value following a repeated flag, in order.</summary>
        public IReadOnlyList<string> Values(string flag)
        {
            var argv = Argv;
            var found = new List<string>();
            for (var i = 0; i < argv.Length - 1; i++)
                if (argv[i] == flag) found.Add(argv[i + 1]);
            return found;
        }

        public bool Has(string flag) => Argv.Contains(flag, StringComparer.Ordinal);

        /// <summary>Flags that take a value, so a positional argument can be told from one.</summary>
        private static readonly HashSet<string> ValuedFlags = new(StringComparer.Ordinal)
        {
            "-target", "-profile", "-entry", "-stage", "-o", "-lang",
            "-warnings-disable", "-capability", "-I", "-g",
        };

        /// <summary>Input files named on the command line beyond the shader carrying the directive.</summary>
        /// <remarks>
        /// A directive may list further translation units — <c>multi-file.hlsl</c> compiles itself
        /// and <c>multi-file-extra.hlsl</c> into one program with an entry point from each. That is
        /// a composite of two modules, not one module with two entry points, and the shape of the
        /// resulting reflection differs.
        /// </remarks>
        public IReadOnlyList<string> ExtraInputs
        {
            get
            {
                var argv = Argv;
                var found = new List<string>();
                for (var i = 0; i < argv.Length; i++)
                {
                    if (argv[i].StartsWith('-')) { if (ValuedFlags.Contains(argv[i])) i++; continue; }
                    found.Add(argv[i]);
                }
                return found;
            }
        }

        /// <summary>
        /// The entry points requested, each with the stage that applies to it.
        /// </summary>
        /// <remarks>
        /// slangc's rule, quoted from its own help: "each -stage associated with the first -entry
        /// to its left". The same goes for -profile, which is where most of this corpus gets its
        /// stage from. Applying one stage to every entry is wrong for
        /// `-entry mainVS -profile vs_4_0 ... -entry mainFS -profile ps_4_0`, which asks for a
        /// vertex shader and a fragment shader in one program.
        /// </remarks>
        public IReadOnlyList<(string Name, SlangStage? Stage)> EntryPointRequests()
        {
            var argv = Argv;
            var requests = new List<(string Name, SlangStage? Stage)>();

            for (var i = 0; i < argv.Length - 1; i++)
            {
                switch (argv[i])
                {
                    case "-entry":
                        requests.Add((argv[++i], null));
                        break;

                    case "-stage" when requests.Count > 0:
                        requests[^1] = (requests[^1].Name, Stage(argv[++i]));
                        break;

                    case "-profile" when requests.Count > 0 && requests[^1].Stage is null:
                        requests[^1] = (requests[^1].Name, StageFromProfile(argv[++i]));
                        break;
                }
            }

            return requests;
        }
    }

    // ---------------------------------------------------------------- the cases

    private static readonly Lazy<IReadOnlyList<ReflectionCase>> LazyReflection = new(LoadReflection);

    /// <summary>Every REFLECTION directive in tests/reflection that has an expected result.</summary>
    public static IReadOnlyList<ReflectionCase> Reflection => LazyReflection.Value;

    private static List<ReflectionCase> LoadReflection()
    {
        var directory = Fetch("reflection");
        var cases = new List<ReflectionCase>();

        foreach (var path in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.Contains(".expected", StringComparison.Ordinal) || name.StartsWith('_')) continue;

            var text = File.ReadAllText(path);

            // slang-test's own opt-out. The four files carrying it are not run upstream, so their
            // .expected is whatever it was when someone last regenerated it and is not maintained
            // against the compiler. global-uniforms.hlsl is one, and comparing against it was
            // reporting a difference in this binding that upstream does not have either.
            if (text.Contains("//TEST_IGNORE_FILE", StringComparison.Ordinal)) continue;

            // The suffix counts *every* directive in the file, not only the REFLECTION ones:
            // unbounded-arrays.hlsl leads with COMPARE_HLSL and its reflection result is therefore
            // .1.expected. Enumerating all of them and keeping the index is what gets that right.
            var directives = TestDirective().Matches(text);
            for (var i = 0; i < directives.Count; i++)
            {
                if (directives[i].Groups["kind"].Value != "REFLECTION") continue;

                var expected = Path.Combine(directory, i == 0 ? $"{name}.expected" : $"{name}.{i}.expected");
                if (!File.Exists(expected)) continue;

                if (StandardOutput(File.ReadAllText(expected)) is not { } json) continue;

                cases.Add(new ReflectionCase(
                    name, i, directives[i].Groups["args"].Value.Trim(), text, json));
            }
        }

        return cases;
    }

    /// <summary>
    /// The <c>standard output</c> block of a slang-test .expected file.
    /// </summary>
    /// <remarks>
    /// The file is a transcript — a result code, a stderr block, then a stdout block — and only the
    /// last carries the JSON. The closing brace is found from the end rather than by matching
    /// braces forward, because the payload is itself brace-heavy and a counter would have to know
    /// it is reading JSON to get that right.
    /// </remarks>
    private static string? StandardOutput(string expected)
    {
        const string Marker = "standard output = {";

        var start = expected.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0) return null;

        var end = expected.LastIndexOf('}');
        if (end <= start) return null;

        return expected[(start + Marker.Length)..end].Trim();
    }

    // ---------------------------------------------------------------- compute cases

    /// <summary>One buffer a <c>//TEST_INPUT:</c> line declares, as 32-bit words.</summary>
    /// <param name="Kind"><c>ubuffer</c> or <c>cbuffer</c>.</param>
    /// <param name="Name">The shader parameter it binds to.</param>
    /// <param name="ArrayIndex">
    /// Which element, where the parameter is an array of buffers — <c>name buffers[2]</c>. The
    /// projection reports one binding with a descriptor count, not three bindings, so this is the
    /// descriptor within it and not a binding of its own.
    /// </param>
    /// <param name="IsOutput">Marked <c>out</c>: this is the buffer the expected result describes.</param>
    /// <param name="Words">Initial contents, already in the shader's bit representation.</param>
    public sealed record BufferInput(string Kind, string Name, uint ArrayIndex, bool IsOutput, uint[] Words);

    /// <summary>An image a <c>//TEST_INPUT:</c> line declares.</summary>
    /// <param name="Dimensions">1, 2 or 3.</param>
    /// <param name="Format">Slang's spelling, e.g. <c>RGBA32Float</c>.</param>
    /// <param name="Size">Extent along every dimension it has.</param>
    /// <param name="Name">The shader parameter it binds to.</param>
    /// <param name="Storage">
    /// <c>RWTexture</c> rather than <c>Texture</c>: a storage image the shader reads and writes,
    /// against a sampled one it only reads through a sampler. Different descriptor type, different
    /// usage flags and a different layout, so it cannot be one code path with a flag on the end.
    /// </param>
    public sealed record ImageInput(int Dimensions, string Format, uint Size, string Name, bool Storage);

    /// <summary>A <c>Sampler:name x</c> declaration. It carries no state worth modelling.</summary>
    public sealed record SamplerInput(string Name);

    /// <summary>A shader with declared inputs and the output words the Slang team recorded.</summary>
    public sealed record ComputeCase(
        string Source,
        string Text,
        string Arguments,
        IReadOnlyList<BufferInput> Inputs,
        IReadOnlyList<ImageInput> Images,
        IReadOnlyList<SamplerInput> Samplers,
        IReadOnlyList<string> ExpectedLines)
    {
        public override string ToString() => Source;

        /// <summary>
        /// Whether the expected values are typed rather than raw words.
        /// </summary>
        /// <remarks>
        /// slang-test prints each output word as bare hex — no <c>0x</c>, no leading zeros, so
        /// <c>1.0f</c> is <c>3F800000</c> and ten is <c>A</c>. With <c>-output-using-type</c> it
        /// instead writes a <c>type:</c> header and formats the values, which is a different
        /// comparison and is why this exists rather than one parser guessing.
        /// </remarks>
        public bool IsTyped => ExpectedLines.Count > 0
            && ExpectedLines[0].StartsWith("type:", StringComparison.Ordinal);

        /// <summary>The element type named by the <c>type:</c> header, or null.</summary>
        public string? OutputType => IsTyped ? ExpectedLines[0]["type:".Length..].Trim() : null;

        /// <summary>The expected values, past any header.</summary>
        public IEnumerable<string> ExpectedValues => IsTyped ? ExpectedLines.Skip(1) : ExpectedLines;
    }

    private static readonly Lazy<IReadOnlyList<ComputeCase>> LazyCompute = new(LoadCompute);

    /// <summary>Compute cases this harness can express: a Vulkan target and buffers only.</summary>
    public static IReadOnlyList<ComputeCase> Compute => LazyCompute.Value;

    private static List<ComputeCase> LoadCompute()
    {
        var directory = Fetch("compute");
        var cases = new List<ComputeCase>();

        foreach (var path in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.Contains(".expected", StringComparison.Ordinal) || name.StartsWith('.')) continue;

            var text = File.ReadAllText(path);
            if (text.Contains("//TEST_IGNORE_FILE", StringComparison.Ordinal)) continue;

            // The Vulkan leg of the directive, not any of the others. A file typically declares
            // five — cpu, cuda, dx12, slang and vk — and only the last says anything about what
            // this package produces.
            var directive = VulkanComputeDirective().Match(text);
            if (!directive.Success) continue;

            var expected = Path.Combine(directory, $"{name}.expected.txt");
            if (!File.Exists(expected)) continue;

            var inputs = new List<BufferInput>();
            var images = new List<ImageInput>();
            var samplers = new List<SamplerInput>();
            var expressible = true;

            foreach (Match line in TestInput().Matches(text))
            {
                var body = line.Groups["body"].Value;

                if (ParseBufferInput(body) is { } input) inputs.Add(input);
                else if (ParseImageInput(body) is { } image) images.Add(image);
                else if (ParseSamplerInput(body) is { } sampler) samplers.Add(sampler);
                else { expressible = false; break; }
            }

            // No inputs means nothing to bind and nothing to read; every case here has at least an
            // output buffer. A case with an input this parser does not understand is dropped whole
            // rather than run with a buffer missing, which would fail for the wrong reason.
            if (!expressible || inputs.Count == 0) continue;
            if (!inputs.Any(i => i.IsOutput)) continue;

            cases.Add(new ComputeCase(
                name, text, directive.Groups["args"].Value.Trim(), inputs, images, samplers,
                [.. File.ReadAllLines(expected).Where(l => l.Trim().Length > 0)]));
        }

        return cases;
    }

    /// <summary>
    /// One <c>//TEST_INPUT:</c> body, or null where it declares something other than a buffer.
    /// </summary>
    /// <remarks>
    /// The shapes in this corpus are
    /// <c>ubuffer(data=[0 0 0 0], stride=4):out,name=outputBuffer</c> and the same with
    /// <c>name outputBuffer</c> — both spellings occur. Textures, samplers, acceleration
    /// structures and the <c>set x = new ...</c> existential syntax are not buffers and return
    /// null, which drops their case.
    /// </remarks>
    private static BufferInput? ParseBufferInput(string body)
    {
        var match = BufferDeclaration().Match(body.Trim());
        if (!match.Success) return null;

        var words = new List<uint>();
        foreach (var token in match.Groups["data"].Value
                     .Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            // A decimal point or an exponent means the shader reads it as a float, so the word is
            // its IEEE-754 bits rather than its value. Integers are stored as written.
            if (token.Contains('.', StringComparison.Ordinal)
                || token.Contains('e', StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return null;
                words.Add(BitConverter.SingleToUInt32Bits(f));
            }
            else
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return null;
                words.Add(unchecked((uint)i));
            }
        }

        var attributes = match.Groups["attrs"].Value;
        var named = BufferName().Match(attributes);
        if (!named.Success) return null;

        return new BufferInput(
            match.Groups["kind"].Value,
            named.Groups["name"].Value,
            named.Groups["index"].Success
                ? uint.Parse(named.Groups["index"].Value, CultureInfo.InvariantCulture)
                : 0,
            attributes.Contains("out", StringComparison.Ordinal),
            [.. words]);
    }

    // ---------------------------------------------------------------- fetching

    /// <summary>The Slang release this corpus is taken from, matching the native payload.</summary>
    public static string Version { get; } = SlangHeader.Pinned.Version;

    /// <summary>Where tests/reflection is cached. A search path, for the cases that include siblings.</summary>
    public static string ReflectionDirectory => Fetch("reflection");

    /// <summary>Where tests/compute is cached, for the same reason.</summary>
    public static string ComputeDirectory => Fetch("compute");

    /// <summary>Local path of one cached test directory, downloading it on first use.</summary>
    private static string Fetch(string suite)
    {
        var directory = Path.Combine(SlangHeader.RepoRoot(), "obj", "slang-tests", Version, suite);

        // A marker rather than "does the directory exist": an interrupted first run leaves a
        // partial directory behind, and a harness that silently runs a subset of the corpus is the
        // failure this whole file exists to avoid.
        var complete = Path.Combine(directory, ".complete");
        if (File.Exists(complete)) return directory;

        Directory.CreateDirectory(directory);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SharpSlang-tests");

        // The listing is the one call here that goes through the GitHub API, and the API allows
        // 60 unauthenticated requests an hour per IP. On a hosted runner that IP is shared with
        // whoever else is running, so the budget can be gone before this process makes its first
        // call: both legs of the first .NET 11 run failed all six corpus tests with a 403 while
        // the caches, evicted after a quiet week on main, were being rebuilt. A token raises the
        // limit to a thousand an hour per repository for the Actions job token, which is why
        // ci.yml passes GITHUB_TOKEN to the test step. Locally it is optional.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        string listing;
        try
        {
            listing = http.GetStringAsync(
                    $"https://api.github.com/repos/shader-slang/slang/contents/tests/{suite}?ref={Version}")
                .GetAwaiter().GetResult();
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"listing tests/{suite} at {Version} was refused by the GitHub API (403), which "
                + "is its rate limit: 60 requests an hour per IP without a token. Set GITHUB_TOKEN "
                + (string.IsNullOrWhiteSpace(token)
                    ? "to any token with public read access and retry."
                    : "to a token that is still valid; the one set was refused."), e);
        }

        var entries = JsonElement.Parse(listing).EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "file")
            .Select(e => (Name: e.GetProperty("name").GetString()!, Url: e.GetProperty("download_url").GetString()!))
            .ToList();

        if (entries.Count == 0)
            throw new InvalidOperationException(
                $"tests/{suite} at {Version} listed no files. The layout changed upstream, or the "
                + "listing is not what it was: a 403 is reported separately above.");

        foreach (var (fileName, url) in entries)
        {
            var destination = Path.Combine(directory, fileName);
            if (File.Exists(destination)) continue;
            File.WriteAllBytes(destination, http.GetByteArrayAsync(url).GetAwaiter().GetResult());
        }

        File.WriteAllText(complete, $"{entries.Count} files from tests/{suite} at {Version}\n");
        return directory;
    }

    /// <summary>
    /// One <c>RWTexture1D/2D/3D(...)</c> declaration, or null where it is something else.
    /// </summary>
    /// <remarks>
    /// Only the read-write forms, and only <c>content = one</c>. Every image in the Vulkan-reachable
    /// part of this corpus is that shape — a storage image filled with 1.0 that the shader reads,
    /// writes, and reports through a buffer — so a sampled texture with real contents, and the
    /// sampler that would go with it, are not modelled rather than being modelled speculatively.
    /// </remarks>
    private static ImageInput? ParseImageInput(string body)
    {
        var match = ImageDeclaration().Match(body.Trim());
        if (!match.Success) return null;

        // Anything other than a plain one-filled image is a different harness.
        if (!match.Groups["content"].Value.Trim().Equals("one", StringComparison.Ordinal)) return null;
        if (match.Groups["mips"].Success && match.Groups["mips"].Value.Trim() != "1") return null;

        var named = BufferName().Match(match.Groups["attrs"].Value);
        if (!named.Success) return null;

        return new ImageInput(
            int.Parse(match.Groups["dim"].Value, CultureInfo.InvariantCulture),
            // A sampled texture usually names no format; the shader's element type decides, and
            // every one in this corpus is a float.
            match.Groups["format"].Success && match.Groups["format"].Value.Length > 0
                ? match.Groups["format"].Value.Trim()
                : "R32Float",
            uint.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture),
            named.Groups["name"].Value,
            Storage: match.Groups["rw"].Success);
    }

    /// <summary>A <c>Sampler:name x</c> declaration, or null.</summary>
    /// <remarks>
    /// No parentheses and no arguments, unlike every other input form — which is why a pattern
    /// requiring `(` reads it as unsupported and drops the two cases that sample a texture.
    /// </remarks>
    private static SamplerInput? ParseSamplerInput(string body)
    {
        var match = SamplerDeclaration().Match(body.Trim());
        if (!match.Success) return null;

        var named = BufferName().Match(match.Groups["attrs"].Value);
        return named.Success ? new SamplerInput(named.Groups["name"].Value) : null;
    }

    /// <summary>The NRI <c>Format</c> name Slang's texture-format name means.</summary>
    public static string? ImageFormat(string slang) => slang switch
    {
        "R16Float" => "R16_SFLOAT",
        "RG16Float" => "RG16_SFLOAT",
        "RGBA16Float" => "RGBA16_SFLOAT",
        "R32Float" => "R32_SFLOAT",
        "RG32Float" => "RG32_SFLOAT",
        "RGBA32Float" => "RGBA32_SFLOAT",
        _ => null,
    };

    // ---------------------------------------------------------------- building a case

    /// <summary>Raised when a directive names something this binding cannot express.</summary>
    public sealed class OutOfReachException(string reason) : Exception(reason);

    /// <summary>A live program built from one directive. Dispose it.</summary>
    public sealed class Built : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        internal Built(SlangGlobalSession global, SlangSession session, SlangModule module,
            IReadOnlyList<SlangEntryPoint> entryPoints, LinkedProgram program)
        {
            Program = program;
            // Reverse of construction order, which is what the ownership chain expects.
            _owned.Add(program);
            _owned.AddRange(entryPoints);
            _owned.Add(module);
            _owned.Add(session);
            _owned.Add(global);
            EntryPointCount = entryPoints.Count;
        }

        public LinkedProgram Program { get; }

        /// <summary>How many entry points were composed, which decides what code can be requested.</summary>
        public int EntryPointCount { get; }

        public void Dispose()
        {
            foreach (var d in _owned) d.Dispose();
        }
    }

    /// <summary>
    /// Reproduces one directive's slangc command line through the binding's own API.
    /// </summary>
    /// <remarks>
    /// Shared by the two harnesses that read this corpus, because they differ only in what they do
    /// with the result — one compares Slang's JSON dump, the other compares the projection — and a
    /// second copy of this setup would be a second set of subtly different session options.
    /// </remarks>
    /// <exception cref="OutOfReachException">The directive is not expressible here.</exception>
    public static Built Build(ReflectionCase test)
    {
        var targetName = test.Value("-target")
            ?? throw new OutOfReachException("no -target; slangc would infer one from the file name");

        if (Target(targetName) is not { } format)
            throw new OutOfReachException($"-target {targetName} is not a format this payload can produce");

        if (test.ExtraInputs.Count > 0)
            throw new OutOfReachException(
                $"compiles {test.ExtraInputs.Count + 1} translation units into one program "
                + $"({string.Join(", ", test.ExtraInputs)}); SlangSession.Link composes entry points "
                + "into a single module, not modules into a composite");

        var stageName = test.Value("-stage");
        if (stageName is not null && Stage(stageName) is null)
            throw new OutOfReachException($"-stage {stageName} is not a stage the binding names");

        // -stage wins; otherwise the profile carries one, which is how two thirds of this corpus
        // says "pixel shader" without ever writing the word.
        var profile = test.Value("-profile");
        var defaultStage = stageName is not null ? Stage(stageName) : StageFromProfile(profile);

        // `-D__SLANG__` with no `=`; slangc gives those the value 1, as a C preprocessor does.
        var macros = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in test.Argv.Where(a => a.StartsWith("-D", StringComparison.Ordinal)))
        {
            var body = arg[2..];
            if (body.Length == 0) continue;
            var eq = body.IndexOf('=');
            macros[eq < 0 ? body : body[..eq]] = eq < 0 ? "1" : body[(eq + 1)..];
        }

        SlangGlobalSession? global = null;
        SlangSession? session = null;
        SlangModule? module = null;
        var entryPoints = new List<SlangEntryPoint>();

        try
        {
            global = SlangGlobalSession.Create();
            session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions { Format = format, Profile = profile }],
                MatrixLayout = test.Has("-matrix-layout-column-major")
                    ? SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR
                    : SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
                PreprocessorMacros = macros,
                // Several cases #include or import a sibling in the same directory. slangc resolves
                // those relative to the input file; a module compiled from a string has no
                // directory, so the corpus directory is supplied as a search path instead.
                SearchPaths = [ReflectionDirectory],
            });

            module = session.LoadModuleFromSource(
                Path.GetFileNameWithoutExtension(test.Source), test.Source, test.Text);

            // Three ways slangc arrives at an entry point, in its own order of preference.
            var requested = test.EntryPointRequests();
            if (requested.Count > 0)
            {
                // A named function still needs its stage when it carries no [shader] attribute,
                // which is the normal case here — these shaders predate the attribute.
                foreach (var (name, ownStage) in requested)
                {
                    var s = ownStage ?? defaultStage;
                    entryPoints.Add(s is { } known
                        ? module.FindEntryPoint(name, known)
                        : module.FindEntryPoint(name));
                }
            }
            else if (module.EntryPointCount > 0)
            {
                entryPoints.AddRange(module.EntryPoints());
            }
            else if (defaultStage is { } only)
            {
                // `-stage fragment` with no `-entry` is slangc's legacy default of `main`.
                entryPoints.Add(module.FindEntryPoint("main", only));
            }

            // No entry points at all is legitimate: most of this corpus reflects global scope.
            var program = session.Link(module, [.. entryPoints]);
            return new Built(global, session, module, entryPoints, program);
        }
        catch
        {
            foreach (var e in entryPoints) e.Dispose();
            module?.Dispose();
            session?.Dispose();
            global?.Dispose();
            throw;
        }
    }

    // ---------------------------------------------------------------- slangc arguments

    /// <summary>The <c>-target</c> name slangc uses, as the enum the binding takes.</summary>
    public static SlangCompileTarget? Target(string name) => name switch
    {
        "hlsl" => SlangCompileTarget.SLANG_HLSL,
        "glsl" => SlangCompileTarget.SLANG_GLSL,
        "spirv" => SlangCompileTarget.SLANG_SPIRV,
        "spirv-assembly" => SlangCompileTarget.SLANG_SPIRV_ASM,
        "cuda" => SlangCompileTarget.SLANG_CUDA_SOURCE,
        "metal" => SlangCompileTarget.SLANG_METAL,
        "wgsl" => SlangCompileTarget.SLANG_WGSL,
        // host-callable is CPU code through slang-llvm, which the shipped payload deliberately
        // excludes (tools/fetch-native.ps1). Reported as unsupported rather than attempted.
        _ => null,
    };

    /// <summary>The stage a <c>-profile</c> name implies, or null if it names no stage.</summary>
    /// <remarks>
    /// A D3D profile is a stage and a shader model welded together — <c>ps_4_0</c> is "pixel shader,
    /// model 4.0" — and slangc reads the stage out of it when no <c>-stage</c> is given. Most of
    /// this corpus relies on that: two thirds of the directives name a profile and nothing else,
    /// and without this rule they compile with no entry point at all and lose the whole
    /// <c>entryPoints</c> section of the reflection.
    ///
    /// <c>sm_5_0</c> and <c>sm_5_1</c> deliberately return null. They are shader models with no
    /// stage in them, and the directives using one really do reflect global scope alone.
    /// </remarks>
    public static SlangStage? StageFromProfile(string? profile)
    {
        if (string.IsNullOrEmpty(profile)) return null;

        var prefix = profile.Split('_')[0];
        return prefix switch
        {
            "ps" => SlangStage.SLANG_STAGE_FRAGMENT,
            "vs" => SlangStage.SLANG_STAGE_VERTEX,
            "cs" => SlangStage.SLANG_STAGE_COMPUTE,
            "gs" => SlangStage.SLANG_STAGE_GEOMETRY,
            "hs" => SlangStage.SLANG_STAGE_HULL,
            "ds" => SlangStage.SLANG_STAGE_DOMAIN,
            // glsl_fragment, glsl_vertex, glsl_compute and friends.
            "glsl" => Stage(profile["glsl_".Length..]),
            _ => null,
        };
    }

    /// <summary>The <c>-stage</c> name slangc uses, as the enum the binding takes.</summary>
    public static SlangStage? Stage(string name) => name switch
    {
        "vertex" => SlangStage.SLANG_STAGE_VERTEX,
        "fragment" or "pixel" => SlangStage.SLANG_STAGE_FRAGMENT,
        "compute" => SlangStage.SLANG_STAGE_COMPUTE,
        "geometry" => SlangStage.SLANG_STAGE_GEOMETRY,
        "hull" => SlangStage.SLANG_STAGE_HULL,
        "domain" => SlangStage.SLANG_STAGE_DOMAIN,
        "raygeneration" => SlangStage.SLANG_STAGE_RAY_GENERATION,
        "miss" => SlangStage.SLANG_STAGE_MISS,
        "closesthit" => SlangStage.SLANG_STAGE_CLOSEST_HIT,
        "anyhit" => SlangStage.SLANG_STAGE_ANY_HIT,
        "intersection" => SlangStage.SLANG_STAGE_INTERSECTION,
        "callable" => SlangStage.SLANG_STAGE_CALLABLE,
        "mesh" => SlangStage.SLANG_STAGE_MESH,
        "amplification" => SlangStage.SLANG_STAGE_AMPLIFICATION,
        _ => null,
    };

    // The Vulkan leg of a compute directive. COMPARE_COMPUTE and COMPARE_COMPUTE_EX differ in what
    // slang-test does with the result, not in what the shader is, so both count.
    [GeneratedRegex(@"^[ \t]*//TEST(?:\([^)]*\))?[ \t]*:[ \t]*COMPARE_COMPUTE[A-Z_]*[ \t]*:(?<args>[^\r\n]*-vk\b[^\r\n]*)$",
        RegexOptions.Multiline)]
    private static partial Regex VulkanComputeDirective();

    [GeneratedRegex(@"^[ \t]*//TEST_INPUT[ \t]*:[ \t]*(?<body>[^\r\n]*)$", RegexOptions.Multiline)]
    private static partial Regex TestInput();

    // `ubuffer(data=[...], stride=4):out,name=x`. The stride is not captured: it describes how the
    // shader reads the words, which is the shader's business, and the data is already words.
    [GeneratedRegex(@"^(?<kind>ubuffer|cbuffer)\s*\(\s*data\s*=\s*\[(?<data>[^\]]*)\][^)]*\)\s*:\s*(?<attrs>.*)$")]
    private static partial Regex BufferDeclaration();

    // `RWTexture2D(format=R32Float, size=4, content = one, mipMaps = 1):name rwt2D` and the sampled
    // form `Texture2D(size=4, content = one):name t2D`, which names no format.
    //
    // `arrayLength=` and `TextureCube` are deliberately unmatched: an array or cube view is a
    // different image and a different view type, and the one case needing them wants seven of them
    // at once. Not matching drops that case whole, which is the intended behaviour.
    [GeneratedRegex(
        @"^(?<rw>RW)?Texture(?<dim>[123])D\s*\(\s*(?:format\s*=\s*(?<format>\w+)\s*,\s*)?"
        + @"size\s*=\s*(?<size>\d+)\s*,\s*content\s*=\s*(?<content>\w+)\s*"
        + @"(?:,\s*mipMaps\s*=\s*(?<mips>\d+)\s*)?\)\s*:\s*(?<attrs>.*)$")]
    private static partial Regex ImageDeclaration();

    [GeneratedRegex(@"^Sampler\s*:\s*(?<attrs>.*)$")]
    private static partial Regex SamplerDeclaration();

    // Both spellings of the separator occur in the corpus, sometimes in the same file. The
    // subscript matters: `name buffers[0]` and `name buffers[1]` are two descriptors of one
    // binding, and a pattern ending at `\w+` reads them as the same buffer declared twice —
    // which binds the last one three times and leaves the shader reading zeros.
    [GeneratedRegex(@"\bname\s*=?\s*(?<name>\w+)(?:\[(?<index>\d+)\])?")]
    private static partial Regex BufferName();

    // `//TEST(compute):REFLECTION:-target hlsl` and `//TEST:REFLECTION:-target hlsl` both occur.
    [GeneratedRegex(@"^[ \t]*//TEST(?:\([^)]*\))?[ \t]*:[ \t]*(?<kind>[A-Z_]+)[ \t]*:?(?<args>[^\r\n]*)",
        RegexOptions.Multiline)]
    private static partial Regex TestDirective();
}
