// Does the C# layout match what a C++ compiler makes of the same header?
//
// Everything else in this suite checks the binding against Slang's behaviour or against the text of
// slang.h. Neither can see a layout error. If ClangSharp put a field at the wrong offset, or sized a
// struct wrongly, or packed a bitfield the other way round, the code compiles, the exports resolve,
// the calls dispatch, and the compiler reads a different field than the one that was written. The
// `regen` job cannot notice: it compares the generator against itself.
//
// The only party that can settle it is a C++ compiler reading the same header. So this generates a
// probe from the C# metadata, compiles it against the pinned slang.h, runs it, and compares:
//
//   * sizeof and alignof for every generated struct
//   * offsetof for every named field
//   * the exact bits a bitfield property writes, byte for byte
//   * sizeof(interface) == sizeof(void*), which is what `void** lpVtbl` assumes
//   * every signature — 279 DllImports and 231 vtable methods — as a static_assert that fails to
//     compile if it is wrong, naming the function and printing both types
//
// Vtable slot *order* is deliberately not here. clang only lays out a vtable it is going to emit,
// and Slang's interfaces are pure-abstract with no key function, so nothing in a probe translation
// unit causes one to exist and -fforce-emit-vtables does not change that. VtableOrderTests derives
// all 374 slots from the header instead. Their signatures are reachable regardless, because a
// pointer-to-member type needs no vtable to exist.
//
// Skips with a reason where no C++ compiler is installed, in the shape NativeFact and
// NriVulkanFact already use — a machine without one still runs everything else.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SharpSlang.Native;

namespace SharpSlang.Tests;

/// <summary>A fact needing a C++ compiler; says where to get one when there is none.</summary>
public sealed class AbiFactAttribute : FactAttribute
{
    public AbiFactAttribute()
    {
        if (NativeAbi.Compiler is null) Skip = NativeAbi.Unavailable;
    }
}

/// <summary>Finds a C++ compiler and runs the generated probe through it.</summary>
public static class NativeAbi
{
    /// <summary>Path to a usable C++ compiler, or null.</summary>
    public static string? Compiler => LazyCompiler.Value;

    public static string Unavailable =>
        "no C++ compiler found. Tried clang++, g++ and c++ on PATH plus the default LLVM install "
        + "directory. Install one (winget install LLVM.LLVM, apt install clang, or Xcode command "
        + "line tools) to check the binding's struct layout against the header.";

    private static readonly Lazy<string?> LazyCompiler = new(Find);

    private static string? Find()
    {
        var candidates = new List<string> { "clang++", "g++", "c++" };

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(@"C:\Program Files\LLVM\bin\clang++.exe");
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "LLVM", "bin", "clang++.exe"));
        }

        // By running it, not by looking for the file. A name on PATH that is a broken shim and a
        // name that is absent are the same thing to this test, and only one of them is visible to
        // File.Exists.
        foreach (var candidate in candidates)
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                if (probe is null) continue;
                probe.WaitForExit(20_000);
                if (probe.ExitCode == 0) return candidate;
            }
            catch (Exception)
            {
                // Not present, or not executable. Try the next one.
            }
        }

        return null;
    }

    /// <summary>Compile and run one probe source; returns its stdout.</summary>
    /// <param name="source">The C++ to build.</param>
    /// <param name="name">Base name for the generated files, inside the test's own directory.</param>
    /// <param name="diagnostics">The compiler's output, whether or not it succeeded.</param>
    /// <param name="extraArguments">Further compiler arguments, after the defaults.</param>
    public static string? Build(string source, string name, out string diagnostics,
        params string[] extraArguments)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sharpslang-abi");
        Directory.CreateDirectory(directory);

        var cpp = Path.Combine(directory, $"{name}.cpp");
        var exe = Path.Combine(directory, OperatingSystem.IsWindows() ? $"{name}.exe" : name);
        File.WriteAllText(cpp, source);
        if (File.Exists(exe)) File.Delete(exe);

        var include = Path.Combine(
            SlangHeader.RepoRoot(), "obj", "slang-headers", SlangHeader.Pinned.Version);

        var compile = Run(Compiler!, ["-std=c++17", "-I", include, .. extraArguments, cpp, "-o", exe]);
        diagnostics = compile.Output;
        if (compile.ExitCode != 0 || !File.Exists(exe)) return null;

        var run = Run(exe, []);
        if (run.ExitCode != 0)
        {
            diagnostics = $"the probe compiled but exited {run.ExitCode}:\n{run.Output}";
            return null;
        }

        return run.Output;
    }

    private static (int ExitCode, string Output) Run(string file, string[] arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) info.ArgumentList.Add(a);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"could not start {file}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            return (-1, "timed out after 180s");
        }

        return (process.ExitCode, stdout + stderr);
    }
}

public sealed partial class NativeAbiTests(Xunit.Abstractions.ITestOutputHelper output)
{
    // ---------------------------------------------------------------- what to probe

    /// <summary>
    /// Generated types that have a C++ counterpart worth measuring.
    /// </summary>
    /// <remarks>
    /// A struct with no instance fields is skipped, and that rule is what excludes the twelve
    /// opaque reflection handles: they are declared but never defined in slang.h, so
    /// <c>sizeof</c> on one is not merely wrong, it does not compile.
    /// </remarks>
    private static IEnumerable<Type> Probed() =>
        typeof(SlangApi).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsValueType: true, IsEnum: false, IsGenericType: false })
            .Where(t => t.Namespace == "SharpSlang.Native")
            .Where(t => Fields(t).Count > 0)
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private static List<FieldInfo> Fields(Type t) =>
        [.. t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => !f.IsStatic)];

    /// <summary>Fields that exist under the same name in C++, so offsetof can reach them.</summary>
    /// <remarks>
    /// Two kinds are excluded and both are ClangSharp inventions rather than declarations in
    /// slang.h. <c>_bitfield</c> is the storage word it synthesises for a run of bitfields, whose
    /// bit positions are checked separately and more directly. <c>Anonymous</c> is the name it
    /// gives an anonymous union — <c>SpecializationArg</c> has one — which in C++ has no name at
    /// all, so <c>offsetof</c> cannot name it. The enclosing struct's size and alignment still
    /// cover both.
    /// </remarks>
    private static IEnumerable<FieldInfo> NamedFields(Type t) =>
        Fields(t).Where(f => !f.Name.StartsWith('_')
                          && !f.Name.StartsWith("Anonymous", StringComparison.Ordinal));

    /// <summary>A COM interface: one vtable pointer and nothing else on the C# side.</summary>
    private static bool IsInterface(Type t) =>
        Fields(t) is [{ Name: "lpVtbl" }];

    /// <summary>Bitfield properties, with the width slang.h declares.</summary>
    /// <remarks>
    /// <para>
    /// ClangSharp turns <c>uint32_t type : 8</c> into a <c>_bitfield</c> word plus a property doing
    /// its own shifting and masking, and annotates the property
    /// <c>[NativeTypeName("uint32_t : 8")]</c>. That hand-rolled shifting is the most error-prone
    /// thing in the generated layer and nothing has ever checked it — the struct stays the right
    /// size whichever way the bits go.
    /// </para>
    /// <para>
    /// Read out of the generated source rather than off the metadata, because
    /// <c>NativeTypeNameAttribute</c> is <c>[Conditional("DEBUG")]</c> — exactly like
    /// <c>VtblIndexAttribute</c> — so in the Release build CI runs the annotations are not in the
    /// assembly at all and reflection would find no bitfields to check.
    /// </para>
    /// </remarks>
    private static List<(string Name, int Width)> Bitfields(Type t) =>
        LazyBitfields.Value.TryGetValue(t.Name, out var found) ? found : [];

    private static readonly Lazy<Dictionary<string, List<(string Name, int Width)>>> LazyBitfields =
        new(ReadBitfields);

    private static Dictionary<string, List<(string, int)>> ReadBitfields()
    {
        var directory = Path.Combine(SlangHeader.RepoRoot(), "dotnet", "Native");
        var result = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            // ClangSharp's file=multi mode names each file after the type it declares.
            var type = Path.GetFileNameWithoutExtension(file);

            foreach (Match m in BitfieldProperty().Matches(File.ReadAllText(file)))
            {
                if (!result.TryGetValue(type, out var list)) result[type] = list = [];
                list.Add((m.Groups["name"].Value,
                    int.Parse(m.Groups["width"].Value, CultureInfo.InvariantCulture)));
            }
        }

        return result;
    }

    [GeneratedRegex(@"\[NativeTypeName\(""[^""]*:\s*(?<width>\d+)""\)\]\s*public\s+\w+\s+(?<name>\w+)")]
    private static partial Regex BitfieldProperty();

    // One DllImport declaration, from the attribute to the closing paren. The optional
    // `[return: NativeTypeName(...)]` sits between them when ClangSharp had a C spelling to record.
    [GeneratedRegex(
        @"\[DllImport\([^\]]*\)\]\s*"
        + @"(?:\[return:\s*NativeTypeName\(""(?<ret>[^""]*)""\)\]\s*)?"
        + @"public\s+static\s+extern\s+(?<csret>[\w\*]+)\s+(?<name>\w+)\s*\((?<args>[^;]*)\)\s*;",
        RegexOptions.Singleline)]
    private static partial Regex ExternDeclaration();

    [GeneratedRegex(@"\[NativeTypeName\(""(?<c>[^""]*)""\)\]")]
    private static partial Regex ParameterAnnotation();

    // A vtable method's declaration: the attribute, an optional [return:] annotation, then the
    // signature. It ends at `{` rather than `;` — these have bodies that do the dispatch.
    // Any number of attributes in any order between the slot marker and the declaration:
    // createCompileRequest carries [return: NativeTypeName(...)] and [Obsolete], in that order,
    // and a pattern allowing only the first silently dropped it from the check.
    [GeneratedRegex(
        @"\[VtblIndex\(\d+\)\]"
        + @"(?:\s*(?:\[return:\s*NativeTypeName\(""(?<ret>[^""]*)""\)\]|\[\w+(?:\([^)]*\))?\]))*"
        + @"\s*public\s+(?<csret>[\w\*]+)\s+(?<name>\w+)\s*\(",
        RegexOptions.Singleline)]
    private static partial Regex VtableDeclaration();

    [GeneratedRegex(@"\[VtblIndex\(\d+\)\]")]
    private static partial Regex AnySlot();

    // Whatever method follows a slot marker, however the declaration is spelled.
    [GeneratedRegex(@"public\b[^\r\n]*?(?<name>\w+)\s*\(", RegexOptions.Singleline)]
    private static partial Regex AfterSlot();

    // The C# type of a parameter: the last type token before its name, past any attributes.
    [GeneratedRegex(@"(?:\]\s*)?(?<type>[\w\*]+)\s+(?<name>\w+)\s*(?:=[^,]*)?$", RegexOptions.Singleline)]
    private static partial Regex CsParameterType();

    // ---------------------------------------------------------------- generating the probe

    private static string LayoutProbe()
    {
        var cpp = new StringBuilder();
        cpp.AppendLine("// Generated by NativeAbiTests. Ground truth for the binding's struct layout.");
        cpp.AppendLine("#include \"slang.h\"");
        cpp.AppendLine("#include <stdio.h>");
        cpp.AppendLine("#include <string.h>");
        cpp.AppendLine("#include <stddef.h>");
        cpp.AppendLine("using namespace slang;");
        cpp.AppendLine("int main() {");

        foreach (var type in Probed())
        {
            if (IsInterface(type))
            {
                // The whole dispatch mechanism assumes an interface is exactly one vtable pointer
                // with the pointer at offset zero. Slang's interfaces are single-inheritance and
                // pure-virtual, so that is true — but it is an assumption until measured.
                cpp.AppendLine(CultureInfo.InvariantCulture, $"  printf(\"S|{type.Name}|%zu|%zu\\n\", sizeof({type.Name}), alignof({type.Name}));");
                continue;
            }

            cpp.AppendLine(CultureInfo.InvariantCulture, $"  printf(\"S|{type.Name}|%zu|%zu\\n\", sizeof({type.Name}), alignof({type.Name}));");

            foreach (var field in NamedFields(type))
            {
                cpp.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  printf(\"F|{type.Name}|{field.Name}|%zu\\n\", offsetof({type.Name}, {field.Name}));");
            }

            // Bitfields: write all-ones into one, then print the struct's bytes. Where the bits
            // land is the whole question, and a byte pattern answers it without needing to know
            // how the compiler chose to name or order the storage.
            foreach (var (name, width) in Bitfields(type))
            {
                var ones = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;
                cpp.AppendLine(CultureInfo.InvariantCulture, $"  {{ {type.Name} v; memset(&v, 0, sizeof v); "
                    + $"v.{name} = ({ones}ull); "
                    + $"unsigned char b[sizeof({type.Name})]; memcpy(b, &v, sizeof v); "
                    + $"printf(\"B|{type.Name}|{name}|\"); "
                    + $"for (size_t i = 0; i < sizeof v; i++) printf(\"%02x\", b[i]); printf(\"\\n\"); }}");
            }
        }

        cpp.AppendLine("  return 0;");
        cpp.AppendLine("}");
        return cpp.ToString();
    }

    // ---------------------------------------------------------------- the measurement

    private sealed record Measured(
        Dictionary<string, (long Size, long Align)> Structs,
        Dictionary<string, long> Offsets,
        Dictionary<string, string> BitPatterns);

    private static readonly Lazy<(Measured? Truth, string Diagnostics)> LazyTruth = new(() =>
    {
        var stdout = NativeAbi.Build(LayoutProbe(), "layout", out var diagnostics);
        if (stdout is null) return (null, diagnostics);

        var structs = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        var offsets = new Dictionary<string, long>(StringComparer.Ordinal);
        var bits = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            switch (parts[0])
            {
                case "S" when parts.Length == 4:
                    structs[parts[1]] = (long.Parse(parts[2], CultureInfo.InvariantCulture),
                                         long.Parse(parts[3], CultureInfo.InvariantCulture));
                    break;
                case "F" when parts.Length == 4:
                    offsets[$"{parts[1]}.{parts[2]}"] = long.Parse(parts[3], CultureInfo.InvariantCulture);
                    break;
                case "B" when parts.Length == 4:
                    bits[$"{parts[1]}.{parts[2]}"] = parts[3];
                    break;
            }
        }

        return (new Measured(structs, offsets, bits), diagnostics);
    });

    private static Measured Truth =>
        LazyTruth.Value.Truth ?? throw new InvalidOperationException(
            "the layout probe did not build:\n" + LazyTruth.Value.Diagnostics);

    // ---------------------------------------------------------------- the tests

    [AbiFact]
    public void The_probe_builds_and_measures_the_whole_generated_surface()
    {
        var probed = Probed().ToList();
        output.WriteLine($"compiler: {NativeAbi.Compiler}");
        output.WriteLine($"{probed.Count} generated struct(s) probed, "
            + $"{Truth.Structs.Count} measured, {Truth.Offsets.Count} field offsets, "
            + $"{Truth.BitPatterns.Count} bitfield(s)");

        Assert.NotEmpty(probed);

        var missing = probed.Select(t => t.Name).Where(n => !Truth.Structs.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} struct(s) were probed and produced no measurement:\n"
            + string.Join("", missing.Select(n => $"    {n}\n")));

        // §4.3 and §7 both state these. A measured quantity in prose has a half-life, and this is
        // the test that measured them. Pinned against §7's sentence rather than §4.3's table,
        // because "| 51 |" would match any table cell in the document and pass by accident.
        DocumentedFactsTests.Documents("docs/design.md", $"{Truth.Structs.Count} struct sizes",
            $"the probe measures {Truth.Structs.Count} structs");
        DocumentedFactsTests.Documents("docs/design.md", $"{Truth.Offsets.Count} field offsets",
            $"the probe measures {Truth.Offsets.Count} field offsets");
        DocumentedFactsTests.Documents("docs/design.md", $"{Truth.BitPatterns.Count} bitfield accessors",
            $"the probe measures {Truth.BitPatterns.Count} bitfield accessors");
    }

    [AbiFact]
    public void Every_struct_is_the_size_and_alignment_the_C_plus_plus_compiler_gives_it()
    {
        var wrong = new List<string>();

        foreach (var type in Probed())
        {
            var (size, align) = Truth.Structs[type.Name];

            // Unsafe.SizeOf is the CLR's own size, which is what the binding's pointer arithmetic
            // uses; Marshal.SizeOf is the marshalled size, which is what OffsetOf below reports
            // against. They coincide for blittable sequential structs, and this is the test that
            // they do — if they ever diverge the offsets are being read off a different layout.
            var clr = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!
                .MakeGenericMethod(type).Invoke(null, null)!;
            var marshalled = Marshal.SizeOf(type);

            if (clr != size) wrong.Add($"    {type.Name}: C++ sizeof {size}, C# {clr}\n");
            if (marshalled != clr)
                wrong.Add($"    {type.Name}: CLR size {clr} but marshalled size {marshalled}, so the "
                    + "field offsets below are measured against a different layout\n");

            // Alignment is not directly observable in C#, so this checks the consequence that
            // matters: a size that is not a multiple of the alignment cannot be right for an array
            // of these, which is how TargetDesc and CompilerOptionEntry are passed.
            if (align > 0 && size % align != 0)
                wrong.Add($"    {type.Name}: C++ sizeof {size} is not a multiple of alignof {align}\n");
        }

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} struct(s) are laid out differently by the two compilers. Every one is a "
            + $"field read at the wrong address:\n" + string.Concat(wrong));
    }

    [AbiFact]
    public void Every_field_is_at_the_offset_the_C_plus_plus_compiler_puts_it()
    {
        var wrong = new List<string>();
        var checkedCount = 0;

        foreach (var type in Probed().Where(t => !IsInterface(t)))
        {
            foreach (var field in NamedFields(type))
            {
                if (!Truth.Offsets.TryGetValue($"{type.Name}.{field.Name}", out var expected)) continue;

                var actual = (long)Marshal.OffsetOf(type, field.Name);
                checkedCount++;

                if (actual != expected)
                    wrong.Add($"    {type.Name}.{field.Name}: C++ offsetof {expected}, C# {actual}\n");
            }
        }

        output.WriteLine($"{checkedCount} field offsets compared");
        Assert.True(checkedCount >= 50, $"only {checkedCount} field offsets were compared");

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} field(s) are at a different offset in the two layouts:\n"
            + string.Concat(wrong));
    }

    [AbiFact]
    public void A_bitfield_property_writes_the_bits_the_C_plus_plus_compiler_writes()
    {
        // The one thing in the generated layer that is hand-rolled arithmetic rather than a layout
        // the compiler chose. slang.h has two bitfields, both in VMExecOperand, and they share an
        // underlying type — which is the case where MSVC and gcc agree, so this is expected to
        // pass. Expected, and never measured until now.
        var wrong = new List<string>();
        var compared = 0;

        foreach (var type in Probed())
        {
            foreach (var (name, width) in Bitfields(type))
            {
                if (!Truth.BitPatterns.TryGetValue($"{type.Name}.{name}", out var expected)) continue;

                var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(property is not null,
                    $"{type.Name}.{name} is annotated as a bitfield in the source but is not a property");

                var ones = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;

                // Same operation on the C# side: a zeroed struct, the property set to all ones,
                // then the raw bytes.
                var box = Activator.CreateInstance(type)!;
                property!.SetValue(box, Convert.ChangeType(
                    ones & MaxOf(property.PropertyType), property.PropertyType, CultureInfo.InvariantCulture));

                var bytes = new byte[Marshal.SizeOf(type)];
                var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    Marshal.StructureToPtr(box, handle.AddrOfPinnedObject(), fDeleteOld: false);
                }
                finally
                {
                    handle.Free();
                }

                var actual = Convert.ToHexString(bytes).ToLowerInvariant();
                compared++;

                output.WriteLine($"{type.Name}.{name} ({width} bits)\n  C++ {expected}\n  C#  {actual}");

                if (actual != expected)
                    wrong.Add($"    {type.Name}.{name} ({width} bits): C++ wrote {expected}, "
                        + $"C# wrote {actual}\n");
            }
        }

        Assert.True(compared > 0,
            "no bitfield was compared. slang.h has two, in VMExecOperand — either they are gone "
            + "upstream or the [NativeTypeName(\"... : n\")] annotation this finds them by changed.");

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} bitfield accessor(s) put the bits somewhere else than the C++ compiler "
            + $"does. The struct is the right size and the reads are wrong:\n" + string.Concat(wrong));
    }

    // ---------------------------------------------------------------- signatures

    /// <summary>One <c>DllImport</c>, as the C signature the binding claims it has.</summary>
    private sealed record Import(string Name, string Return, IReadOnlyList<string> Parameters)
    {
        public string AsFunctionPointer =>
            $"{Return} (*)({(Parameters.Count == 0 ? "void" : string.Join(", ", Parameters))})";
    }

    /// <summary>
    /// The C signature each <c>DllImport</c> declares, read out of the generated source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>NativeExportTests</c> proves every entry point name resolves in the loaded library.
    /// Nothing proves the <em>signature</em> matches, and a name that resolves with the wrong
    /// parameter list is a corrupted stack rather than an exception — the failure mode a
    /// <c>DllImport</c> is least able to report.
    /// </para>
    /// <para>
    /// The C spelling comes from the <c>[NativeTypeName]</c> annotations where ClangSharp emitted
    /// one, and from the C# type otherwise. Only the underlying type has to agree: a typedef is
    /// transparent to <c>is_same</c>, so <c>SlangResult</c>, <c>int32_t</c> and <c>int</c> all
    /// compare equal, and only genuinely different types — a missing parameter, a lost
    /// <c>const</c>, a pointer that should be a pointer-to-pointer — fail.
    /// </para>
    /// </remarks>
    private static List<Import> Imports()
    {
        var source = File.ReadAllText(
            Path.Combine(SlangHeader.RepoRoot(), "dotnet", "Native", "SlangApi.cs"));

        var imports = new List<Import>();

        foreach (Match declaration in ExternDeclaration().Matches(source))
        {
            var returnType = declaration.Groups["ret"].Success
                ? declaration.Groups["ret"].Value
                : ToC(declaration.Groups["csret"].Value);

            if (returnType is null) continue;

            var parameters = new List<string>();
            var reconstructed = true;

            foreach (var parameter in SplitParameters(declaration.Groups["args"].Value))
            {
                var annotated = ParameterAnnotation().Match(parameter);
                var spelling = annotated.Success
                    ? annotated.Groups["c"].Value
                    : ToC(CsParameterType().Match(parameter).Groups["type"].Value);

                if (spelling is null) { reconstructed = false; break; }
                parameters.Add(spelling);
            }

            if (reconstructed) imports.Add(new Import(declaration.Groups["name"].Value, returnType, parameters));
        }

        return imports;
    }

    /// <summary>Top-level commas only: a function-pointer parameter has commas of its own.</summary>
    private static IEnumerable<string> SplitParameters(string args)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is '<' or '(' or '[') depth++;
            else if (args[i] is '>' or ')' or ']') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                yield return args[start..i];
                start = i + 1;
            }
        }

        if (args[start..].Trim().Length > 0) yield return args[start..];
    }

    /// <summary>The C type a C# type stands for, or null where the mapping is not obvious.</summary>
    /// <remarks>
    /// Deliberately incomplete. A guess here would produce a compile error that looks like a
    /// binding defect, so anything not on this list drops its whole function from the check and is
    /// counted — function pointers especially, whose C# spelling carries a calling convention that
    /// the C declaration expresses differently.
    /// </remarks>
    private static string? ToC(string cs)
    {
        var stars = cs.Length - cs.TrimEnd('*').Length;
        var bare = cs.TrimEnd('*').Trim();

        var mapped = bare switch
        {
            "void" => "void",
            "bool" => "bool",
            "sbyte" => "char",
            "byte" => "unsigned char",
            "short" => "short",
            "ushort" => "unsigned short",
            "int" => "int",
            "uint" => "unsigned int",
            // C# long is 64-bit; C++ long is 32-bit on Windows. Say what is meant.
            "long" => "long long",
            "ulong" => "unsigned long long",
            "nint" => "ptrdiff_t",
            "nuint" => "size_t",
            "float" => "float",
            "double" => "double",
            _ when bare.Contains("delegate*", StringComparison.Ordinal) => null,
            _ when bare.Contains('<', StringComparison.Ordinal) => null,
            // A generated struct or enum: same name in C++, reachable via `using namespace slang`.
            _ when bare.All(c => char.IsLetterOrDigit(c) || c == '_') => bare,
            _ => null,
        };

        return mapped is null ? null : mapped + new string('*', stars);
    }

    [AbiFact]
    public void Every_DllImport_has_the_signature_the_header_declares()
    {
        var imports = Imports();
        var declared = File.ReadAllText(
            Path.Combine(SlangHeader.RepoRoot(), "dotnet", "Native", "SlangApi.cs"))
            .Split("[DllImport(").Length - 1;

        output.WriteLine($"{imports.Count} of {declared} DllImports reconstructed and checked");

        // A mapping that quietly stopped reconstructing anything would pass this test by having
        // nothing to assert. Most of L0 is plain pointers and integers, so most of it reconstructs.
        Assert.True(imports.Count >= declared * 3 / 4,
            $"only {imports.Count} of {declared} signatures could be reconstructed; the C# to C "
            + "mapping has stopped understanding the generated code.");

        var cpp = new StringBuilder();
        cpp.AppendLine("// Generated by NativeAbiTests. One assertion per DllImport.");
        cpp.AppendLine("#include \"slang.h\"");
        cpp.AppendLine("#include <stddef.h>");
        cpp.AppendLine("using namespace slang;");
        cpp.AppendLine("template<class A, class B> struct same_ { static const bool value = false; };");
        cpp.AppendLine("template<class A> struct same_<A, A> { static const bool value = true; };");

        foreach (var import in imports)
        {
            cpp.AppendLine(CultureInfo.InvariantCulture, $"static_assert(same_<decltype(&{import.Name}), {import.AsFunctionPointer}>::value,");
            cpp.AppendLine(CultureInfo.InvariantCulture, $"    \"{import.Name} is declared with a different signature by the binding\");");
        }

        cpp.AppendLine("int main() { return 0; }");

        var result = NativeAbi.Build(cpp.ToString(), "signatures", out var diagnostics);

        Assert.True(result is not null,
            "the signature probe did not build. Each error names the function and shows the two "
            + "types — the first is what slang.h declares, the second is what the binding says:\n"
            + diagnostics);
    }

    // ---------------------------------------------------------------- vtable method signatures

    /// <summary>One vtable method, as the C++ member-function type the binding claims it has.</summary>
    private sealed record Method(string Interface, string Name, string Return, IReadOnlyList<string> Parameters)
    {
        public string AsMemberPointer =>
            $"{Return} ({Interface}::*)({(Parameters.Count == 0 ? "" : string.Join(", ", Parameters))})";
    }

    /// <summary>
    /// Every vtable method's signature, read out of the generated source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gap the rest of this file leaves. <c>VtableOrderTests</c> proves slot N holds the right
    /// <em>function</em>; the free-function probe above proves the 279 <c>DllImport</c>s have the
    /// right <em>signature</em>. Nothing until now checked the signature of a vtable method — and
    /// 246 of the 374 slots have never been called, so a wrong parameter list on one of those is a
    /// stack the first caller corrupts.
    /// </para>
    /// <para>
    /// Reachable in plain C++ because a pointer-to-member-function type needs no vtable to exist.
    /// The layout dump does — clang only lays out a vtable it is going to emit, and Slang's
    /// interfaces are pure-abstract with no key function — which is why slot order is derived from
    /// the header and this is not.
    /// </para>
    /// <para>
    /// Only methods an interface declares itself. <c>&amp;ISession::queryInterface</c> has type
    /// <c>(ISlangUnknown::*)</c> in C++, so an inherited slot can only be named against its
    /// declaring class, where it is checked once.
    /// </para>
    /// </remarks>
    private static List<Method> VtableMethods(out int declared, out List<string> skipped)
    {
        var directory = Path.Combine(SlangHeader.RepoRoot(), "dotnet", "Native");
        var header = SlangHeader.Pinned;
        var methods = new List<Method>();

        declared = 0;
        skipped = [];

        foreach (var file in Directory.EnumerateFiles(directory, "I*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            var iface = Path.GetFileNameWithoutExtension(file);
            var text = File.ReadAllText(file);

            var own = header.OwnVirtualMethods(iface).ToHashSet(StringComparer.Ordinal);
            if (own.Count == 0) continue;

            // The denominator is every slot the file declares, counted independently of whether
            // the pattern below could read it — otherwise a declaration shape this stops
            // understanding drops out of both sides and the coverage floor never notices.
            declared += SlotsDeclaredHere(text, own);

            foreach (Match declaration in VtableDeclaration().Matches(text))
            {
                var name = declaration.Groups["name"].Value;
                if (!own.Contains(name)) continue;   // inherited; checked on its declaring class

                var arguments = BalancedArguments(text, declaration.Index + declaration.Length - 1);
                if (arguments is null) { skipped.Add($"{iface}::{name} (unbalanced parameter list)"); continue; }

                var returnType = declaration.Groups["ret"].Success
                    ? declaration.Groups["ret"].Value
                    : ToC(declaration.Groups["csret"].Value);

                if (returnType is null) { skipped.Add($"{iface}::{name} (return type)"); continue; }

                var parameters = new List<string>();
                var reconstructed = true;

                foreach (var parameter in SplitParameters(arguments))
                {
                    if (parameter.Trim().Length == 0) continue;

                    var annotated = ParameterAnnotation().Match(parameter);
                    var spelling = annotated.Success
                        ? annotated.Groups["c"].Value
                        : ToC(CsParameterType().Match(parameter).Groups["type"].Value);

                    if (spelling is null) { reconstructed = false; break; }
                    parameters.Add(spelling);
                }

                if (!reconstructed) { skipped.Add($"{iface}::{name} (parameter type)"); continue; }

                methods.Add(new Method(iface, name, returnType, parameters));
            }
        }

        return methods;
    }

    /// <summary>How many of an interface's own methods its generated file carries slots for.</summary>
    private static int SlotsDeclaredHere(string text, HashSet<string> own) =>
        AnySlot().Matches(text)
            .Count(m => own.Contains(AfterSlot().Match(text[m.Index..]).Groups["name"].Value));

    /// <summary>The text inside a parameter list, given the position of its opening paren.</summary>
    /// <remarks>
    /// Counted rather than matched with a regex, because a <c>[NativeTypeName]</c> annotation can
    /// hold a function-pointer spelling with parentheses of its own.
    /// </remarks>
    private static string? BalancedArguments(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text[(open + 1)..i];
            else if (text[i] == '{' && depth == 0) return null;
        }

        return null;
    }

    [AbiFact]
    public void Every_vtable_method_has_the_signature_the_header_declares()
    {
        var methods = VtableMethods(out var declared, out var skipped);

        output.WriteLine($"{methods.Count} of {declared} vtable method signatures reconstructed");
        foreach (var s in skipped) output.WriteLine($"  skipped: {s}");

        DocumentedFactsTests.Documents("docs/design.md", $"{declared} vtable methods",
            $"the probe checks {declared} vtable method signatures");

        // Counted, not reconstructed, and no reason recorded: a declaration shape the pattern
        // does not match at all. That is the silent case — it leaves a slot unchecked while both
        // the total and the skip list look healthy — so it is an error rather than a note.
        // createCompileRequest was exactly this, hidden behind an [Obsolete] attribute.
        var unexplained = declared - methods.Count - skipped.Count;
        Assert.True(unexplained == 0,
            $"{unexplained} vtable slot(s) were counted but neither checked nor skipped for a "
            + "stated reason, so the declaration pattern no longer matches how they are generated.");

        Assert.True(methods.Count >= declared * 3 / 4,
            $"only {methods.Count} of {declared} vtable signatures could be reconstructed; the C# to "
            + "C++ mapping has stopped understanding the generated code.");

        // 128 of the 374 slots are inherited and checked on their declaring interface, so this
        // floor is well under the total on purpose. It is here so the check cannot quietly
        // collapse to nothing.
        Assert.True(methods.Count >= 150, $"only {methods.Count} vtable methods found to check");

        var cpp = new StringBuilder();
        cpp.AppendLine("// Generated by NativeAbiTests. One assertion per vtable method.");
        cpp.AppendLine("#include \"slang.h\"");
        cpp.AppendLine("#include <stddef.h>");
        cpp.AppendLine("using namespace slang;");
        cpp.AppendLine("template<class A, class B> struct same_ { static const bool value = false; };");
        cpp.AppendLine("template<class A> struct same_<A, A> { static const bool value = true; };");

        // Either spelling, and the alternative is the point rather than a weakening.
        //
        // SLANG_NO_THROW is __declspec(nothrow) on Windows and *empty* everywhere else, and C++17
        // makes noexcept part of a function's type — so the same method has two different types
        // depending on the platform. A first version derived the noexcept from the header text and
        // passed on Windows while failing 19 assertions on both Linux legs and macOS.
        //
        // Asserting it either way would be asserting how a Slang macro expands, which is not what
        // this is for: C# has no way to say noexcept, so the binding cannot be wrong about it. The
        // return type and every parameter still have to match exactly, which is what can be wrong.
        foreach (var method in methods)
        {
            cpp.AppendLine(CultureInfo.InvariantCulture, $"static_assert(same_<decltype(&{method.Interface}::{method.Name}), "
                + $"{method.AsMemberPointer}>::value");
            cpp.AppendLine(CultureInfo.InvariantCulture, $"           || same_<decltype(&{method.Interface}::{method.Name}), "
                + $"{method.AsMemberPointer} noexcept>::value,");
            cpp.AppendLine(CultureInfo.InvariantCulture, $"    \"{method.Interface}::{method.Name} is declared with a different "
                + $"signature by the binding\");");
        }

        cpp.AppendLine("int main() { return 0; }");

        // Twice, and the second run is what stops this being a Windows-only check. Forcing
        // SLANG_NO_THROW empty is precisely the declaration every non-Windows platform sees, so a
        // machine with the MSVC ABI verifies the Itanium shape and one with Itanium verifies both
        // as well. The first version of this probe was green here and red on three runners.
        foreach (var (label, arguments) in new[]
                 {
                     ("as this platform declares them", []),
                     ("with SLANG_NO_THROW empty, as every non-Windows platform sees them",
                      new[] { "-DSLANG_NO_THROW=" }),
                 })
        {
            var result = NativeAbi.Build(
                cpp.ToString(), $"vtable-signatures-{arguments.Length}", out var diagnostics, arguments);

            Assert.True(result is not null,
                $"the vtable signature probe did not build {label}. Each error names the interface "
                + "and method and shows the two types — the first is what slang.h declares, the "
                + "second is what the binding calls through:\n" + diagnostics);

            output.WriteLine($"built {label}");
        }
    }

    private static ulong MaxOf(Type t) =>
        Type.GetTypeCode(t) switch
        {
            TypeCode.Byte => byte.MaxValue,
            TypeCode.UInt16 => ushort.MaxValue,
            TypeCode.UInt32 => uint.MaxValue,
            _ => ulong.MaxValue,
        };
}
