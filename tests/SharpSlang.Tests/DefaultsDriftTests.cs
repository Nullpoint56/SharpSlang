// Guards dotnet/Defaults.cs against upstream drift.
//
// This is the highest-value test in the repository. ClangSharp silently discards C++ default
// member initializers, so a zero-initialized descriptor compiles cleanly and misbehaves at
// runtime — e.g. TargetDesc.flags defaulting to 0 emits SPIR-V via glslang instead of directly,
// and SessionDesc.defaultMatrixLayoutMode defaulting to UNKNOWN rather than ROW_MAJOR changes how
// every matrix in every shader is laid out. Neither shows up in a build log.
//
// The test walks each defaulted field in the pinned slang.h, evaluates the C++ initializer, and
// compares it against the corresponding field on the struct our factory returns. Any Slang release
// that adds, removes, or changes a default fails here.

using System.Reflection;
using System.Runtime.CompilerServices;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class DefaultsDriftTests
{
    private static readonly SlangHeader Header = SlangHeader.Pinned;

    /// <summary>Each mirrored struct: the C++ name, and a boxed instance from our factory.</summary>
    public static TheoryData<string> MirroredStructs =>
    [
        "SlangGlobalSessionDesc",
        "SessionDesc",
        "TargetDesc",
        "CompilerOptionValue",
        "SourceLocation",
    ];

    private static object Produce(string structName) => structName switch
    {
        "SlangGlobalSessionDesc" => Defaults.GlobalSessionDesc(),
        "SessionDesc" => Defaults.SessionDesc(),
        "TargetDesc" => Defaults.TargetDesc(),
        "CompilerOptionValue" => Defaults.CompilerOptionValue(),
        "SourceLocation" => Defaults.SourceLocation(),
        _ => throw new InvalidOperationException($"no factory mapped for {structName}"),
    };

    [Theory]
    [MemberData(nameof(MirroredStructs))]
    public void Factory_reproduces_every_cpp_default(string structName)
    {
        var instance = Produce(structName);
        var type = instance.GetType();
        var failures = new List<string>();

        foreach (var field in Header.DefaultedFields(structName))
        {
            // `reserved = {}` and friends: aggregate initializers, already zero in C#.
            if (field.Initializer.StartsWith('{')) continue;

            var managed = type.GetField(field.Name, BindingFlags.Public | BindingFlags.Instance);
            if (managed is null)
            {
                failures.Add($"  {field.Name}: present in slang.h but not on the generated {structName}");
                continue;
            }

            if (!TryExpected(field, structName, out var expected))
            {
                failures.Add($"  {field.Name}: could not evaluate C++ initializer '{field.Initializer}'");
                continue;
            }

            var actual = ToInt64(managed.GetValue(instance)!);
            if (actual != expected)
            {
                failures.Add(
                    $"  {field.Name}: Defaults gives {actual}, slang.h says {expected} " +
                    $"(from '{field.Initializer}')");
            }
        }

        Assert.True(failures.Count == 0,
            $"Defaults.{structName} has drifted from slang.h {Header.Version}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures) + Environment.NewLine +
            "Update dotnet/Defaults.cs.");
    }

    [Theory]
    [MemberData(nameof(MirroredStructs))]
    public void Factory_covers_every_defaulted_field(string structName)
    {
        // Guards against additions: a new field with a non-zero C++ default would otherwise be
        // silently zero in every descriptor we build.
        var declared = Header.DefaultedFields(structName)
            .Where(f => !f.Initializer.StartsWith('{'))
            .Select(f => f.Name)
            .ToList();

        var type = Produce(structName).GetType();
        var missing = declared
            .Where(n => type.GetField(n, BindingFlags.Public | BindingFlags.Instance) is null)
            .ToList();

        Assert.True(missing.Count == 0,
            $"slang.h {Header.Version} defaults fields absent from the generated {structName}: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void Never_trust_a_zero_initialized_descriptor()
    {
        // The two concrete cases from docs/design.md §4.5, pinned as regressions. If
        // someone "simplifies" Defaults away, these fail loudly instead of shipping broken shaders.
        var zeroed = default(SessionDesc);
        Assert.Equal(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_MODE_UNKNOWN, zeroed.defaultMatrixLayoutMode);
        Assert.Equal(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR, Defaults.SessionDesc().defaultMatrixLayoutMode);

        Assert.Equal(0u, default(TargetDesc).flags);
        Assert.Equal(SlangApi.kDefaultTargetFlags, Defaults.TargetDesc().flags);
        Assert.NotEqual(0u, SlangApi.kDefaultTargetFlags);
    }

    [Fact]
    public void StructureSize_is_set_and_matches_the_managed_layout()
    {
        // Slang versions its descriptors by structureSize; zero means "cannot tell".
        unsafe
        {
            Assert.Equal((uint)sizeof(SlangGlobalSessionDesc), Defaults.GlobalSessionDesc().structureSize);
            Assert.Equal((nuint)sizeof(SessionDesc), Defaults.SessionDesc().structureSize);
            Assert.Equal((nuint)sizeof(TargetDesc), Defaults.TargetDesc().structureSize);
        }
    }

    [Fact]
    public void Spirv_target_selects_the_direct_spirv_backend()
    {
        var d = Defaults.SpirvTargetDesc();
        Assert.Equal(SlangCompileTarget.SLANG_SPIRV, d.format);
        Assert.Equal(SlangApi.SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY,
            (int)(d.flags & SlangApi.SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY));
    }

    // ---------------------------------------------------------------- helpers

    private static bool TryExpected(SlangHeader.StructField field, string structName, out long expected)
    {
        expected = 0;
        var init = field.Initializer.Trim();

        // sizeof(Self) cannot be evaluated from the header; compare against the managed layout,
        // which is the property that actually matters.
        if (init.StartsWith("sizeof(", StringComparison.Ordinal))
        {
            var arg = init[7..].TrimEnd(')').Trim();
            if (arg != structName) return false;
            expected = ManagedSizeOf(structName);
            return true;
        }

        if (init is "nullptr" or "NULL") { expected = 0; return true; }
        if (init is "false") { expected = 0; return true; }
        if (init is "true") { expected = 1; return true; }

        // Slang qualifies some initializers (CompilerOptionValueKind::Int, LayoutRules::Default).
        // The evaluator works on unqualified names, which are unique in this header.
        var unqualified = init.Contains("::", StringComparison.Ordinal)
            ? init[(init.LastIndexOf("::", StringComparison.Ordinal) + 2)..]
            : init;

        return Header.TryEvaluate(unqualified, out expected);
    }

    private static unsafe long ManagedSizeOf(string structName) => structName switch
    {
        "SlangGlobalSessionDesc" => sizeof(SlangGlobalSessionDesc),
        "SessionDesc" => sizeof(SessionDesc),
        "TargetDesc" => sizeof(TargetDesc),
        "CompilerOptionValue" => sizeof(CompilerOptionValue),
        "SourceLocation" => sizeof(SourceLocation),
        _ => throw new InvalidOperationException($"no sizeof mapping for {structName}"),
    };

    private static unsafe long ToInt64(object value) => value switch
    {
        byte b => b,
        sbyte b => b,
        short s => s,
        ushort s => s,
        int i => i,
        uint u => u,
        long l => l,
        ulong u => (long)u,
        nint n => n,
        nuint n => (long)n,
        bool b => b ? 1 : 0,
        Enum e => Convert.ToInt64(e),
        // Reflection boxes a pointer field as System.Reflection.Pointer, whose type is NOT
        // IsPointer, so this must be matched by type rather than by GetType().IsPointer.
        Pointer p => (long)Pointer.Unbox(p),
        _ => throw new InvalidOperationException($"unhandled field type {value.GetType()}"),
    };
}
