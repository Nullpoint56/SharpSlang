// Every call a game can make after load is measured for allocations, and a new one cannot slip past
// (AGENTS.md#pipeline-baking-packages-allocation; the rule is the owner's, 2026-11-08).
//
// AllocationTests proves what its measured loops call. This test reads their IL and holds the two
// packages a game ships with to it, default-deny: every public method and computed property of
// every public type in SharpSlang.Pipeline (the reader) and SharpSlang.Pipeline.NRI (the
// projection) counts as per-frame, and must be called from a measured loop, unless it creates an
// object (Creates) or is named in NotPerFrame with the reason. A member added to either package
// fails here until it is measured or excluded on purpose, and a name in either list that no longer
// exists fails too. Overloads count separately. Compiler-generated members (record equality,
// ToString, Deconstruct, auto-property getters) are stored values or value equality, not calls,
// and are not counted; a hand-written ToString is.
//
// The measured loops are the local functions whose names start with Frame in AllocationTests, and
// ProjectedFrame.Run, the whole NRI frame. A call made in a test's setup or in an assertion is not
// measured and does not count.
//
// The binding (SharpSlang) and the cooker (SharpSlang.Pipeline.Bake) are not held to this: neither
// has a per-frame API. Every SharpSlang call is a Slang call that compiles, links, reflects or hands
// out code, once per shader variant, and the reflection objects are read once by the walker into
// the snapshot (AGENTS.md#decisions: walked once, never proxied); the cooker runs in the asset
// build. A game that compiles at run time, for a hot reload, still compiles once per change.

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public sealed class AllocationCoverageTests(ITestOutputHelper output)
{
    /// <summary>The packages a game ships: the reader and the NRI projection.</summary>
    private static readonly Assembly[] PerFrameAssemblies = [typeof(ProgramLayout).Assembly, typeof(NriPipelineProjection).Assembly];

    /// <summary>Every public type of those packages.</summary>
    private static readonly Type[] PerFrameTypes =
        [.. PerFrameAssemblies.SelectMany(a => a.GetExportedTypes()).OrderBy(t => t.FullName, StringComparer.Ordinal)];

    /// <summary>Members that create an object, with what they create: load or bake work, once per shader.</summary>
    private static readonly Dictionary<string, string> Creates = new()
    {
        ["ShaderPackage.ReadFrom"] = "a package, read at load",
        ["ShaderPackage.ReadFromFile"] = "a package, read at load",
        ["ShaderPackage.WithSection"] = "a new package with the section, at bake time",
        ["ProgramLayout.FromJson"] = "a layout, read at load",
        ["SpirvModule.Read"] = "the module's header and entry points, read once per module",
        ["SpirvModule.SingleEntryPoint"] = "the module's one entry point as a new record, read once per module",
        ["NriPackage.ReadNriProjection"] = "the stored projection, read at load (AllocationTests says how much)",
        ["NriPackage.WithNriProjection"] = "a new package carrying the projection, at bake time",
        ["NriProjection.ToNri"] = "the projection, once per shader (AllocationTests says how much)",
        ["NriProjection.ToNriShaderDesc"] = "the NriShaderDesc a pipeline is created from, at load",
    };

    /// <summary>
    /// Members (by name, by name with parameter types, or a whole type by its name) that are not
    /// per-frame calls, each with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> NotPerFrame = new()
    {
        // Cook and load time: the package's file, its staleness, its format.
        ["ShaderPackage.WriteTo"] = "writes the package, at bake time",
        ["ShaderPackage.WriteToFile"] = "writes the package, at bake time",
        ["ShaderPackage.HashSource"] = "hashes a shader source to decide a rebake, at bake time",
        ["ShaderPackage.IsUpToDate"] = "the staleness check before a rebake, at bake time",
        ["ShaderPackage.StaleDependencies"] = "the staleness check before a rebake, at bake time; a new list by design",
        ["ShaderPackage.IsCurrent"] = "whether this Slang baked the package, asked once per package at load",
        ["ShaderPackage.ContainerFormatVersion"] = "the format version, a constant read once",
        ["ProgramLayout.ToJson"] = "serializes the layout, at bake time",

        // The pipeline interface read off a layout: a new collection by design, once per pipeline,
        // which the NRI projection holds in its per-frame form (its records and arrays).
        ["PipelineInterface.DescriptorNames"] = "a new dictionary by design, built once per pipeline at load",
        ["PipelineInterface.FragmentOutputs"] = "a new list by design, built once per pipeline at load",
        ["PipelineInterface.PushConstantBlocks"] = "a new list by design, built once per pipeline at load",
        ["PipelineInterface.VertexInputs"] = "a new list by design, built once per pipeline at load",
        ["PipelineInterface.RequireEveryParameterIsPlaced"] = "a check at load that throws on an unplaced parameter",

        // Tooling and diagnostics.
        ["DeclarationNode.Descendants"] = "an iterator over the declaration tree, which is for tooling and material authoring (ProgramLayout.Declarations); a renderer reads none of it",
        ["FunctionSignature.ToString"] = "text for diagnostics, a new string by design",
        ["GenericArgumentBinding.ToString"] = "text for diagnostics, a new string by design",
        ["SpecializedType.ToString"] = "text for diagnostics, a new string by design",
        ["ThreadGroupSize.ToString"] = "text for diagnostics, a new string by design",
        ["SpirvWorkgroupDimension.ToString"] = "text for diagnostics, a new string by design",
        ["SpirvWorkgroupSize.ToString"] = "text for diagnostics, a new string by design",
    };

    [Fact]
    public void Every_per_frame_call_is_measured_by_a_zero_allocation_loop()
    {
        var called = MeasuredCalls();

        var missing = new List<string>();
        var measured = new List<string>();
        foreach (var type in PerFrameTypes)
        {
            foreach (var member in PublicSurface(type))
            {
                var name = $"{type.Name}.{NameOf(member)}";
                var full = $"{name}({Parameters(member)})";
                if (Creates.ContainsKey(name) || Creates.ContainsKey(full)) continue;
                if (NotPerFrame.ContainsKey(type.Name) || NotPerFrame.ContainsKey(name) || NotPerFrame.ContainsKey(full)) continue;
                if (called.Contains(member)) measured.Add(full);
                else missing.Add(full);
            }
        }

        output.WriteLine($"{measured.Count} per-frame calls measured; {Creates.Count} creating calls and {NotPerFrame.Count} members named as not per-frame:");
        foreach (var m in measured) output.WriteLine($"  {m}");
        Assert.True(missing.Count == 0,
            $"{missing.Count} per-frame call(s) are in no measured loop (call them from a Frame local function in AllocationTests, or name them in Creates or NotPerFrame with the reason):\n  " +
            string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_name_in_the_exclusion_lists_exists()
    {
        var names = PerFrameTypes
            .SelectMany(t => PublicSurface(t).SelectMany(m => new[] { $"{t.Name}.{NameOf(m)}", $"{t.Name}.{NameOf(m)}({Parameters(m)})" }).Append(t.Name))
            .ToHashSet(StringComparer.Ordinal);
        var stale = Creates.Keys.Concat(NotPerFrame.Keys).Where(k => !names.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Creates or NotPerFrame names members that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void The_measured_loops_call_something_from_each_package()
    {
        // A refactor that renamed the frames would leave the coverage test measuring nothing and
        // reporting every member missing, which fails; this names the cause instead.
        var called = MeasuredCalls();
        foreach (var assembly in PerFrameAssemblies)
            Assert.True(called.Any(m => m.DeclaringType?.Assembly == assembly), $"no measured loop calls into {assembly.GetName().Name}");
    }

    [Fact]
    public void The_packages_a_game_ships_declare_no_native_call()
    {
        // The template's measured loops count host allocations as well as managed bytes. These two
        // assemblies cannot make one: no P/Invoke (ImplMap) and no module reference, read from
        // metadata. The host allocations of the whole frame are SharpNRI's, and AllocationTests
        // counts those with NRI's allocator.
        foreach (var assembly in PerFrameAssemblies)
        {
            using var file = File.OpenRead(assembly.Location);
            using var pe = new PEReader(file);
            var md = pe.GetMetadataReader();
            Assert.Equal(0, md.GetTableRowCount(TableIndex.ImplMap));
            Assert.Equal(0, md.GetTableRowCount(TableIndex.ModuleRef));
        }
    }

    /// <summary>What the measured loop bodies call, generic instances reduced to their definitions.</summary>
    private static HashSet<MethodBase> MeasuredCalls()
    {
        var called = new HashSet<MethodBase>();
        foreach (var method in MethodsOf(typeof(AllocationTests)).Where(IsMeasuredBody)) called.UnionWith(CallsIn(method));
        return called;
    }

    /// <summary>
    /// The local functions named Frame... (the C# compiler names one &lt;Outer&gt;g__FrameX|n_m), and
    /// the whole NRI frame, ProjectedFrame.Run.
    /// </summary>
    private static bool IsMeasuredBody(MethodBase method) =>
        method.Name.Contains("g__Frame", StringComparison.Ordinal)
        || (method.Name == "Run" && method.DeclaringType?.Name == "ProjectedFrame");

    /// <summary>Public methods, and getters of public properties that compute (an auto-property's stored value is not a call).</summary>
    private static IEnumerable<MethodBase> PublicSurface(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(Declared))
        {
            if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)) continue; // accessors come from properties
            if (method.IsDefined(typeof(CompilerGeneratedAttribute))) continue;
            yield return method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;
        }
        foreach (var property in type.GetProperties(Declared))
        {
            var getter = property.GetGetMethod();
            if (getter is null || getter.IsDefined(typeof(CompilerGeneratedAttribute))) continue;
            yield return getter;
        }
    }

    private static string NameOf(MethodBase member) => member.Name.StartsWith("get_", StringComparison.Ordinal) ? member.Name[4..] : member.Name;

    private static string Parameters(MethodBase member) => string.Join(", ", member.GetParameters().Select(p => p.ParameterType.Name.TrimEnd('&')));

    /// <summary>The type's methods and those of its nested types, which hold the loops' closures and ProjectedFrame.</summary>
    private static IEnumerable<MethodBase> MethodsOf(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(All)) yield return method;
        foreach (var constructor in type.GetConstructors(All)) yield return constructor;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var method in MethodsOf(nested)) yield return method;
        }
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>The methods a method body calls or takes the address of, generic instances reduced to their definitions.</summary>
    private static IEnumerable<MethodBase> CallsIn(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;

        for (var i = 0; i < il.Length;)
        {
            var value = il[i] == 0xFE ? unchecked((short)(0xFE00 | il[i + 1])) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            var op = OpCodesByValue[value];
            if (op.OperandType == OperandType.InlineMethod)
            {
                var resolved = Resolve(method, BitConverter.ToInt32(il, i));
                if (resolved is not null) yield return resolved;
            }
            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };
        }
    }

    private static MethodBase? Resolve(MethodBase caller, int token)
    {
        MethodBase resolved;
        try
        {
            resolved = caller.Module.ResolveMethod(token,
                caller.DeclaringType is { IsGenericType: true } t ? t.GetGenericArguments() : null,
                caller.IsGenericMethod ? caller.GetGenericArguments() : null)!;
        }
        catch (ArgumentException)
        {
            return null;
        }
        return resolved is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericMethodDefinition() : resolved;
    }
}
