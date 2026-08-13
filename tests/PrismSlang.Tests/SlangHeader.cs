// Reads the pinned slang.h and makes its compile-time constants queryable from C#.
//
// This exists so the drift tests can *derive* expected values from upstream rather than restate
// them. A test that hardcodes `SLANG_E_NOT_FOUND == unchecked((int)0x82000005)` passes forever,
// including on the day Slang renumbers it — which is precisely the failure it was written to catch.
//
// Scope is deliberately narrow: enough of the C preprocessor and C constant-expression grammar to
// evaluate the integer constants slang.h actually uses. Not a C parser, and not trying to be.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PrismSlang.Tests;

/// <summary>Parsed view of the pinned <c>slang.h</c>: macros, enum members, and struct defaults.</summary>
public sealed partial class SlangHeader
{
    /// <summary>A <c>field = initializer</c> pair lifted from a C++ struct body.</summary>
    public sealed record StructField(string Type, string Name, string Initializer);

    private readonly Dictionary<string, string> _objectMacros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string[] Parameters, string Body)> _functionMacros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _constants = new(StringComparer.Ordinal);
    // `inline constexpr SlangTargetFlags kDefaultTargetFlags = ...` and friends. Stored unevaluated
    // and resolved on demand, so a constexpr referring to a later declaration still works.
    private readonly Dictionary<string, string> _constExprs = new(StringComparer.Ordinal);
    private readonly string _text;

    // slang.h and slang-deprecated.h concatenated, used *only* by the vtable walker.
    //
    // Deliberately separate from _text. Macros, enums and constexprs are collected from slang.h
    // alone, and folding a second header into that collection would quietly change what every
    // drift test resolves against. Interface declarations are the one thing that genuinely spans
    // both files: slang.h #includes slang-deprecated.h, and ICompileRequest — 84 of the 374 vtable
    // slots — is declared there.
    private readonly string _declarations;

    /// <summary>The Slang release tag this header was taken from.</summary>
    public string Version { get; }

    // Types that appear in casts. All integral, so a cast only matters for width truncation.
    private static readonly Dictionary<string, int> CastWidths = new(StringComparer.Ordinal)
    {
        ["int"] = 32, ["int32_t"] = 32, ["unsigned"] = 32, ["uint32_t"] = 32,
        ["int16_t"] = 16, ["uint16_t"] = 16, ["int8_t"] = 8, ["uint8_t"] = 8,
        ["int64_t"] = 64, ["uint64_t"] = 64, ["size_t"] = 64, ["SlangInt"] = 64,
        ["SlangUInt"] = 64, ["long"] = 64, ["short"] = 16, ["char"] = 8,
    };

    private SlangHeader(string text, string declarations, string version)
    {
        _text = text;
        _declarations = declarations;
        Version = version;
        CollectMacros();
        CollectConstExprVariables();
        CollectEnumMembers();
    }

    // ---------------------------------------------------------------- loading

    private static readonly Lazy<SlangHeader> LazyPinned = new(Load);

    /// <summary>The header for the release pinned in Directory.Build.props.</summary>
    public static SlangHeader Pinned => LazyPinned.Value;

    private static SlangHeader Load()
    {
        var version = typeof(SlangHeader).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SlangVersion")?.Value
            ?? throw new InvalidOperationException(
                "SlangVersion assembly metadata missing - the test csproj must emit it from $(SlangVersion).");

        // The whole include closure, not a list maintained by hand. slang.h includes
        // slang-deprecated.h and slang-image-format-defs.h, and NativeAbiTests compiles this
        // directory with a real C++ compiler — so a header missing from the cache is not a
        // parsing gap, it is a probe that cannot build at all.
        //
        // A hardcoded pair passed locally and failed on all four runners, because
        // tools/generate-native.ps1 fetches all three and had already populated the cache on the
        // machine the check was written on. Deriving the list from the headers removes the
        // difference between a warm cache and a cold one.
        var fetched = Closure(version, "slang.h");

        return new SlangHeader(
            Strip(fetched["slang.h"]),
            Strip(fetched["slang.h"] + "\n" + fetched["slang-deprecated.h"]),
            version);
    }

    /// <summary>Every Slang header reachable from one, fetched and cached.</summary>
    /// <remarks>
    /// Only names beginning <c>slang</c>. The other quoted include in the tree is
    /// <c>TargetConditionals.h</c>, which is Apple's and comes from the SDK behind an
    /// <c>__APPLE__</c> guard — asking github for it would 404, and tolerating a 404 instead would
    /// make a genuinely missing Slang header look the same as a system one.
    /// </remarks>
    private static Dictionary<string, string> Closure(string version, string root)
    {
        var fetched = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (fetched.ContainsKey(name)) continue;

            var text = Cached(version, name);
            fetched[name] = text;

            foreach (Match include in QuotedInclude().Matches(text))
            {
                var next = include.Groups["file"].Value;

                if (next.StartsWith("slang", StringComparison.OrdinalIgnoreCase)
                    && !next.Contains('/')
                    && !fetched.ContainsKey(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return fetched;
    }

    /// <summary>The pinned copy of one header, downloaded on first use.</summary>
    private static string Cached(string version, string name)
    {
        // Same cache tools/generate-native.ps1 populates, so a normal dev loop needs no download.
        var cached = Path.Combine(RepoRoot(), "obj", "slang-headers", version, name);
        if (!File.Exists(cached))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            var url = $"https://raw.githubusercontent.com/shader-slang/slang/{version}/include/{name}";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            File.WriteAllText(cached, http.GetStringAsync(url).GetAwaiter().GetResult());
        }

        return File.ReadAllText(cached);
    }

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("could not locate the repo root (Directory.Build.props)");
    }

    /// <summary>Join line continuations and remove comments so macro bodies parse as one line.</summary>
    private static string Strip(string src)
    {
        src = src.Replace("\\\r\n", " ").Replace("\\\n", " ");
        src = BlockComment().Replace(src, " ");
        src = LineComment().Replace(src, "");
        return src;
    }

    // ---------------------------------------------------------------- macro + enum collection

    private void CollectMacros()
    {
        foreach (Match m in DefineDirective().Matches(_text))
        {
            var name = m.Groups["name"].Value;
            var body = m.Groups["body"].Value.Trim();
            if (m.Groups["params"].Success)
            {
                var ps = m.Groups["params"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                _functionMacros[name] = (ps, body);
            }
            else if (body.Length > 0)
            {
                _objectMacros[name] = body;
            }
        }
    }

    private void CollectConstExprVariables()
    {
        foreach (Match m in ConstExprVariable().Matches(_text))
            _constExprs.TryAdd(m.Groups["name"].Value, m.Groups["init"].Value.Trim());
    }

    private void CollectEnumMembers()
    {
        // Enum members are order-dependent (implicit +1), and later members may reference earlier
        // ones, so evaluate in declaration order and publish each as it resolves.
        // Anonymous enums count: `enum : SessionFlags { kSessionFlags_None = 0 }` is how Slang
        // declares some flag constants, and skipping them silently loses those symbols.
        foreach (Match e in EnumBlock().Matches(_text))
        {
            long next = 0;
            foreach (var raw in e.Groups["body"].Value.Split(','))
            {
                var entry = raw.Trim();
                if (entry.Length == 0) continue;

                var eq = entry.IndexOf('=');
                var name = (eq < 0 ? entry : entry[..eq]).Trim();
                if (!Identifier().IsMatch(name)) continue;

                if (eq >= 0)
                {
                    if (!TryEvaluate(entry[(eq + 1)..], out var v)) continue;
                    next = v;
                }
                _constants[name] = next;
                next++;
            }
        }
    }

    // ---------------------------------------------------------------- public queries

    /// <summary>Every <c>#define</c>d name in the header, object-like and function-like.</summary>
    public IEnumerable<string> DefinedNames => _objectMacros.Keys.Concat(_functionMacros.Keys);

    /// <summary>The raw replacement text of a macro, for asserting on its shape rather than its value.</summary>
    public string MacroBody(string name) =>
        _objectMacros.TryGetValue(name, out var body) ? body
        : _functionMacros.TryGetValue(name, out var fn) ? fn.Body
        : throw new InvalidOperationException($"macro {name} not defined in slang.h {Version}");

    /// <summary>Fields of a C++ struct that carry a default member initializer, in declaration order.</summary>
    public IReadOnlyList<StructField> DefaultedFields(string structName)
    {
        var body = StructBody(structName)
            ?? throw new InvalidOperationException($"struct {structName} not found in slang.h {Version}");

        return FieldWithInitializer().Matches(body)
            .Select(m => new StructField(
                m.Groups["type"].Value.Trim(),
                m.Groups["name"].Value,
                m.Groups["init"].Value.Trim()))
            .ToList();
    }

    private string? StructBody(string structName)
    {
        var open = Regex.Match(_text, $@"\bstruct\s+{Regex.Escape(structName)}\b[^;{{]*?\{{");
        if (!open.Success) return null;

        var depth = 0;
        var start = open.Index + open.Length;
        for (var i = start - 1; i < _text.Length; i++)
        {
            if (_text[i] == '{') depth++;
            else if (_text[i] == '}' && --depth == 0) return _text[start..i];
        }
        return null;
    }

    // ---------------------------------------------------------------- vtable order

    /// <summary>The virtual methods of a C++ interface in vtable slot order, base classes first.</summary>
    /// <remarks>
    /// <para>
    /// Slang's COM interfaces are single-inheritance chains of pure virtual methods, so a class's
    /// vtable is its base's vtable followed by its own declarations in source order. That makes
    /// slot N derivable from the header — and this is the only thing in the repository that can
    /// contradict the slot numbers in <c>dotnet/Native/</c>. Those are ClangSharp's own count of
    /// the same declarations, the <c>regen</c> CI job proves only that the committed code equals
    /// what the generator produces, and a generator agreeing with itself is not a check.
    /// </para>
    /// <para>
    /// Multiple inheritance would break the linear model outright, so it throws rather than
    /// returning an order that is quietly wrong. Every Slang interface is single-inheritance today.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> VirtualMethods(string interfaceName)
    {
        List<string> slots = [];
        CollectVirtuals(interfaceName, slots, []);
        return slots;
    }

    /// <summary>Every COM interface the headers declare, whether or not anything binds it.</summary>
    /// <remarks>
    /// A type qualifies by declaring <c>SLANG_COM_INTERFACE</c> or by having virtual methods, and
    /// both halves are load-bearing. <c>ICompileRequest</c> predates Slang's COM conversion and has
    /// no IID, so the macro alone would miss it; <c>ITypeConformance</c> is an empty derived
    /// interface that adds nothing but an IID, so virtual methods alone would miss that.
    /// </remarks>
    public IReadOnlyList<string> ComInterfaces()
    {
        var found = new List<string>();

        foreach (Match m in ClassDeclaration().Matches(_declarations))
        {
            var (_, body) = Declaration(m.Groups["name"].Value);
            var own = string.Concat(TopLevelSpans(body));

            if (own.Contains("SLANG_COM_INTERFACE", StringComparison.Ordinal)
                || VirtualMethodDeclaration().IsMatch(own))
            {
                found.Add(m.Groups["name"].Value);
            }
        }

        return [.. found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private void CollectVirtuals(string name, List<string> into, HashSet<string> seen)
    {
        if (!seen.Add(name))
            throw new InvalidOperationException($"cyclic base chain reaching {name} in slang.h {Version}");

        var (baseName, body) = Declaration(name);
        if (baseName is { Length: > 0 }) CollectVirtuals(baseName, into, seen);

        into.AddRange(OwnVirtuals(body));
    }

    /// <summary>
    /// The virtual methods an interface declares itself, excluding everything it inherits.
    /// </summary>
    /// <remarks>
    /// Inherited slots are deliberately not here. <c>&amp;ISession::queryInterface</c> in C++ has
    /// type <c>… (ISlangUnknown::*)(…)</c>, not <c>(ISession::*)</c>, so an inherited method can
    /// only be named against the class that declares it — where this returns it.
    /// </remarks>
    public IReadOnlyList<string> OwnVirtualMethods(string interfaceName) =>
        [.. OwnVirtuals(Declaration(interfaceName).Body)];

    private static IEnumerable<string> OwnVirtuals(string body)
    {
        foreach (var span in TopLevelSpans(body))
            foreach (Match m in VirtualMethodDeclaration().Matches(span))
                yield return m.Groups["name"].Value;
    }

    /// <summary>The single base a C++ class derives from, and its body.</summary>
    private (string? Base, string Body) Declaration(string name)
    {
        // Requiring the brace lands on the definition rather than on a forward declaration
        // (`struct ISession;`), without needing to know which of the two comes first.
        var open = Regex.Match(
            _declarations,
            $@"\b(?:struct|class)\s+{Regex.Escape(name)}\s*(?::(?<bases>[^{{;]*))?\{{");

        if (!open.Success)
            throw new InvalidOperationException($"no definition of {name} in slang.h {Version}");

        string? baseName = null;
        if (open.Groups["bases"].Success)
        {
            var bases = open.Groups["bases"].Value;
            if (bases.Contains(','))
                throw new InvalidOperationException(
                    $"{name} derives from more than one base ('{bases.Trim()}'). Vtable slots are no "
                    + "longer one linear sequence and they cannot be derived this way.");

            baseName = Regex.Replace(bases, @"\b(?:public|protected|private|virtual)\b", " ").Trim();
        }

        var start = open.Index + open.Length;
        var depth = 1;
        for (var i = start; i < _declarations.Length; i++)
        {
            if (_declarations[i] == '{') depth++;
            else if (_declarations[i] == '}' && --depth == 0) return (baseName, _declarations[start..i]);
        }

        throw new InvalidOperationException($"unterminated body for {name} in slang.h {Version}");
    }

    /// <summary>The runs of a class body that sit at the class's own brace depth.</summary>
    /// <remarks>
    /// Nested braces are skipped rather than flattened. An inline method body and a nested type
    /// both belong to something other than this vtable, and simply deleting a nested block would
    /// splice the text either side of it together — which can form a return type followed by the
    /// next declaration's name and invent a method that does not exist.
    /// </remarks>
    private static IEnumerable<string> TopLevelSpans(string body)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '{')
            {
                if (depth++ == 0) yield return body[start..i];
            }
            else if (body[i] == '}' && --depth == 0)
            {
                start = i + 1;
            }
        }

        if (depth == 0) yield return body[start..];
    }

    /// <summary>Evaluate a C integer constant expression against this header's symbols.</summary>
    public bool TryEvaluate(string expression, out long value)
    {
        value = 0;
        try
        {
            var p = new Parser(this, expression);
            var v = p.ParseExpression();
            if (!p.AtEnd) return false;
            value = v;
            return true;
        }
        catch (FormatException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Evaluate, or throw with the expression included for a legible test failure.</summary>
    public long Evaluate(string expression) =>
        TryEvaluate(expression, out var v)
            ? v
            : throw new InvalidOperationException($"could not evaluate '{expression}' against slang.h {Version}");

    // ---------------------------------------------------------------- expression parser

    private sealed class Parser(SlangHeader header, string text)
    {
        private readonly List<string> _tokens = Tokenize(text);
        private int _i;

        public bool AtEnd => _i >= _tokens.Count;
        private string? Peek => _i < _tokens.Count ? _tokens[_i] : null;
        private string Next() => _i < _tokens.Count ? _tokens[_i++] : throw new FormatException("unexpected end");
        private void Expect(string t)
        {
            if (Next() != t) throw new FormatException($"expected '{t}'");
        }

        private static List<string> Tokenize(string s)
        {
            var outp = new List<string>();
            for (var i = 0; i < s.Length;)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    var j = i;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    outp.Add(s[i..j]); i = j; continue;
                }
                if (char.IsDigit(c))
                {
                    var j = i;
                    if (c == '0' && j + 1 < s.Length && (s[j + 1] is 'x' or 'X')) j += 2;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]))) j++;
                    outp.Add(s[i..j]); i = j; continue;
                }
                // Two-character operators we care about.
                if (i + 1 < s.Length && (s.Substring(i, 2) is "<<" or ">>"))
                {
                    outp.Add(s.Substring(i, 2)); i += 2; continue;
                }
                outp.Add(c.ToString()); i++;
            }
            return outp;
        }

        // Precedence climbing, loosest first. Only the operators slang.h uses in constants.
        public long ParseExpression() => ParseBinary(0);

        private static readonly string[][] Levels =
        [
            ["|"], ["^"], ["&"], ["<<", ">>"], ["+", "-"], ["*", "/", "%"],
        ];

        private long ParseBinary(int level)
        {
            if (level >= Levels.Length) return ParseUnary();

            var left = ParseBinary(level + 1);
            while (Peek is { } op && Levels[level].Contains(op))
            {
                Next();
                var right = ParseBinary(level + 1);
                left = op switch
                {
                    "|" => left | right,
                    "^" => left ^ right,
                    "&" => left & right,
                    "<<" => left << (int)right,
                    ">>" => left >> (int)right,
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => right == 0 ? throw new FormatException("div by zero") : left / right,
                    "%" => right == 0 ? throw new FormatException("mod by zero") : left % right,
                    _ => throw new FormatException($"operator {op}"),
                };
            }
            return left;
        }

        private long ParseUnary()
        {
            switch (Peek)
            {
                case "-": Next(); return -ParseUnary();
                case "+": Next(); return ParseUnary();
                case "~": Next(); return ~ParseUnary();
                case "!": Next(); return ParseUnary() == 0 ? 1 : 0;
                default: return ParsePrimary();
            }
        }

        private long ParsePrimary()
        {
            var t = Next();

            if (t == "(")
            {
                // A cast is `(TYPE)` followed by an operand; anything else is a grouping.
                if (CastWidths.ContainsKey(t = Peek ?? "") && _i + 1 < _tokens.Count && _tokens[_i + 1] == ")")
                {
                    var type = Next();
                    Expect(")");
                    return Truncate(ParseUnary(), type);
                }
                var v = ParseExpression();
                Expect(")");
                return v;
            }

            if (char.IsDigit(t[0])) return ParseNumber(t);

            // Function-style cast: int32_t(0x80000000)
            if (CastWidths.ContainsKey(t) && Peek == "(")
            {
                Next();
                var v = ParseExpression();
                Expect(")");
                return Truncate(v, t);
            }

            // Function-like macro invocation: SLANG_MAKE_ERROR(fac, code)
            if (header._functionMacros.TryGetValue(t, out var fn) && Peek == "(")
            {
                Next();
                var args = new List<string>();
                var depth = 0;
                var current = new List<string>();
                while (true)
                {
                    var tok = Next();
                    if (tok == "(") depth++;
                    if (tok == ")" && depth-- == 0) { args.Add(string.Join(' ', current)); break; }
                    if (tok == "," && depth == 0) { args.Add(string.Join(' ', current)); current.Clear(); continue; }
                    current.Add(tok);
                }

                var body = fn.Body;
                for (var k = 0; k < fn.Parameters.Length && k < args.Count; k++)
                    body = Regex.Replace(body, $@"\b{Regex.Escape(fn.Parameters[k])}\b", $"({args[k]})");

                return header.Evaluate(body);
            }

            if (header._constants.TryGetValue(t, out var known)) return known;
            if (header._objectMacros.TryGetValue(t, out var macro)) return header.Evaluate(macro);
            if (header._constExprs.TryGetValue(t, out var ce)) return header.Evaluate(ce);

            throw new InvalidOperationException($"unknown symbol '{t}'");
        }

        private static long Truncate(long v, string type) => CastWidths[type] switch
        {
            32 => type[0] == 'u' || type.StartsWith("unsigned", StringComparison.Ordinal) ? (uint)v : (int)v,
            16 => type[0] == 'u' ? (ushort)v : (short)v,
            8 => type[0] == 'u' ? (byte)v : (sbyte)v,
            _ => v,
        };

        private static long ParseNumber(string t)
        {
            t = t.TrimEnd('u', 'U', 'l', 'L');
            return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.Parse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : long.Parse(t, CultureInfo.InvariantCulture);
        }
    }

    // ---------------------------------------------------------------- patterns

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"^[ \t]*#[ \t]*define[ \t]+(?<name>\w+)(?:\((?<params>[^)]*)\))?(?<body>[^\r\n]*)",
        RegexOptions.Multiline)]
    private static partial Regex DefineDirective();

    [GeneratedRegex(@"\benum\b\s*(?:class\s+)?(?:\w+\s*)?(?::\s*\w+\s*)?\{(?<body>[^{}]*)\}")]
    private static partial Regex EnumBlock();

    [GeneratedRegex(@"\b(?:inline\s+)?(?:static\s+)?constexpr\s+[\w:]+\s+(?<name>\w+)\s*=\s*(?<init>[^;]+);")]
    private static partial Regex ConstExprVariable();

    [GeneratedRegex(@"^\s*(?<type>[A-Za-z_][\w:\*&<>\s]*?)\s+(?<name>\w+)\s*=\s*(?<init>[^;]+);",
        RegexOptions.Multiline)]
    private static partial Regex FieldWithInitializer();

    [GeneratedRegex(@"^[A-Za-z_]\w*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^[ \t]*#[ \t]*include[ \t]*""(?<file>[^""]+)""", RegexOptions.Multiline)]
    private static partial Regex QuotedInclude();

    // `virtual SLANG_NO_THROW IModule* SLANG_MCALL loadModule(const char*, IBlob** = nullptr) = 0;`
    //
    // The name is the last identifier before the parameter list. Excluding '(' from the run in
    // between is what keeps a match inside one declaration: a return type never contains
    // parentheses here, so the first '(' after `virtual` is always the parameter list.
    [GeneratedRegex(@"\bvirtual\b[^;{(]*?(?<name>\w+)\s*\(")]
    private static partial Regex VirtualMethodDeclaration();

    // A class or struct *definition*. The brace is required, which skips forward declarations.
    [GeneratedRegex(@"\b(?:struct|class)\s+(?<name>[A-Za-z_]\w*)\s*(?::[^{;]*)?\{")]
    private static partial Regex ClassDeclaration();
}
