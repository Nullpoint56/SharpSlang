// Guards dotnet/SlangResult.cs against upstream drift.
//
// Those constants exist only because ClangSharp cannot evaluate function-like macros, so nothing
// in the build would notice if Slang renumbered a facility or added an error code. Every value is
// re-derived from the pinned slang.h here.

using System.Reflection;

namespace SharpSlang.Tests;

public class SlangResultDriftTests
{
    private static readonly SlangHeader Header = SlangHeader.Pinned;

    /// <summary>Maps each C# member of <see cref="SlangResult"/> to the macro it transcribes.</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        [nameof(SlangResult.Ok)] = "SLANG_OK",
        [nameof(SlangResult.Fail)] = "SLANG_FAIL",
        [nameof(SlangResult.NotImplemented)] = "SLANG_E_NOT_IMPLEMENTED",
        [nameof(SlangResult.NoInterface)] = "SLANG_E_NO_INTERFACE",
        [nameof(SlangResult.Abort)] = "SLANG_E_ABORT",
        [nameof(SlangResult.InvalidHandle)] = "SLANG_E_INVALID_HANDLE",
        [nameof(SlangResult.InvalidArg)] = "SLANG_E_INVALID_ARG",
        [nameof(SlangResult.OutOfMemory)] = "SLANG_E_OUT_OF_MEMORY",
        [nameof(SlangResult.BufferTooSmall)] = "SLANG_E_BUFFER_TOO_SMALL",
        [nameof(SlangResult.Uninitialized)] = "SLANG_E_UNINITIALIZED",
        [nameof(SlangResult.Pending)] = "SLANG_E_PENDING",
        [nameof(SlangResult.CannotOpen)] = "SLANG_E_CANNOT_OPEN",
        [nameof(SlangResult.NotFound)] = "SLANG_E_NOT_FOUND",
        [nameof(SlangResult.InternalFail)] = "SLANG_E_INTERNAL_FAIL",
        [nameof(SlangResult.NotAvailable)] = "SLANG_E_NOT_AVAILABLE",
        [nameof(SlangResult.TimeOut)] = "SLANG_E_TIME_OUT",
    };

    public static TheoryData<string, string> CodeMap
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (member, macro) in Map) data.Add(member, macro);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CodeMap))]
    public void Code_matches_the_macro_in_slang_h(string member, string macro)
    {
        var expected = unchecked((int)Header.Evaluate(macro));
        var actual = ReadIntMember(member);

        Assert.True(expected == actual,
            $"{member} is 0x{actual:X8} but slang.h {Header.Version} defines {macro} as 0x{expected:X8}. " +
            $"Update dotnet/SlangResult.cs.");
    }

    [Fact]
    public void Every_error_macro_in_slang_h_is_represented()
    {
        // Catches additions, which the per-member theory above cannot: a new SLANG_E_* would
        // otherwise sit unbound and surface as an opaque hex code at runtime.
        var declared = Header.DefinedNames
            .Where(n => n.StartsWith("SLANG_E_", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var covered = Map.Values.ToHashSet(StringComparer.Ordinal);
        var missing = declared.Where(n => !covered.Contains(n)).ToList();

        Assert.True(missing.Count == 0,
            $"slang.h {Header.Version} defines error codes with no SlangResult member: " +
            $"{string.Join(", ", missing)}. Add them to dotnet/SlangResult.cs and to CodeMap.");
    }

    [Fact]
    public void Failure_is_defined_as_negative()
    {
        // SLANG_FAILED(status) ((status) < 0) -- the whole error model rests on this.
        Assert.Contains("< 0", Header.MacroBody("SLANG_FAILED"));

        Assert.True(SlangResult.Failed(SlangResult.NotFound));
        Assert.True(SlangResult.Failed(int.MinValue));
        Assert.False(SlangResult.Failed(SlangResult.Ok));
        Assert.True(SlangResult.Succeeded(SlangResult.Ok));
        Assert.True(SlangResult.Succeeded(1), "non-zero success codes exist; Succeeded must not test == 0");
    }

    [Theory]
    [InlineData(0x200, 5)]
    [InlineData(7, 0x57)]
    [InlineData(0, 0x4001)]
    public void Facility_and_code_round_trip(int facility, int code)
    {
        var r = unchecked((int)Header.Evaluate($"SLANG_MAKE_ERROR({facility}, {code})"));
        Assert.Equal(facility, SlangResult.Facility(r));
        Assert.Equal(code, SlangResult.Code(r));
        Assert.True(SlangResult.Failed(r));
    }

    [Fact]
    public void Describe_names_known_codes_and_falls_back_for_unknown()
    {
        Assert.Equal("SLANG_E_NOT_FOUND", SlangResult.Describe(SlangResult.NotFound));
        Assert.Equal("SLANG_OK", SlangResult.Describe(SlangResult.Ok));
        Assert.Contains("facility", SlangResult.Describe(unchecked((int)0x8FFF1234)));
    }

    private static int ReadIntMember(string name)
    {
        var t = typeof(SlangResult);
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"SlangResult.{name} not found");
        return (int)f.GetValue(null)!;
    }
}
