// The cooker's reading of SharpSlang's objects: each returns SharpSlang.Pipeline's managed
// snapshot, built from the binding's public L2 reflection (AGENTS.md#layers). Extension methods,
// because the binding knows nothing of the snapshot types; they read as members of the objects
// they ask.

using SharpSlang.Reflection;
using SharpSlang.Spirv;

namespace SharpSlang;

/// <summary>Snapshots of SharpSlang's objects: layouts, declarations, signatures and specializations.</summary>
public static class LinkedProgramReflectionExtensions
{
    /// <summary>
    /// Walks this program's reflection and returns an immutable snapshot. Safe to keep after the
    /// program is disposed.
    /// </summary>
    public static ProgramLayout Reflect(this LinkedProgram program, int targetIndex) =>
        ReflectionWalker.Walk(program, targetIndex);

    /// <summary>
    /// Walks this module's declaration tree. Independent of linking, so it works on a module that
    /// still has unbound generic parameters.
    /// </summary>
    public static DeclarationNode ReflectDeclarations(this SlangModule module) =>
        ReflectionWalker.WalkDeclarations(module);

    /// <summary>
    /// Supplies concrete arguments to a generic declaration and reports the resulting type and its
    /// layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "What would <c>Wrapper&lt;Lambert&gt;</c> lay out as", asked without writing a shader that
    /// declares one. A material system that has just chosen an implementation needs the field
    /// offsets before it can fill a buffer.
    /// </para>
    /// <para>
    /// Distinct from <c>LinkOptions.SpecializationArguments</c>, which specializes the
    /// <em>program</em> at link time and changes the code that gets generated. This asks a
    /// question about a type and generates nothing.
    /// </para>
    /// <para>
    /// The route is not the obvious one. <c>spReflection_specializeType</c> takes a type and
    /// argument types and looks like exactly this call; against a generic reached by name it
    /// returns null, with no diagnostics. What works is to take the type's <em>generic
    /// container</em> and specialize that (<see cref="SlangShaderReflection.SpecializeGeneric"/>,
    /// which also refuses an argument list that would end the process; U3).
    /// </para>
    /// </remarks>
    /// <param name="program">The linked program whose reflection is asked.</param>
    /// <param name="targetIndex">Index into the session's target list: the layout is the target's.</param>
    /// <param name="genericName">The generic's declared name, unqualified — <c>Wrapper</c>.</param>
    /// <param name="arguments">One per generic parameter, in declaration order.</param>
    /// <returns>The specialized type, or null if the program has no such generic.</returns>
    /// <exception cref="ArgumentException">An argument was default-constructed, or none were given, or their kinds do not match the generic's.</exception>
    /// <exception cref="Interop.SlangException">Slang refused the specialization; its diagnostics are attached.</exception>
    public static SpecializedType? SpecializeGeneric(
        this LinkedProgram program, int targetIndex, string genericName, params ReadOnlySpan<GenericArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(genericName);
        if (arguments.Length == 0)
            throw new ArgumentException("a generic needs at least one argument", nameof(arguments));

        var shader = program.GetLayout(targetIndex);

        // Both are asked, because a generic is a declaration of some kind with parameters attached
        // and Slang looks the two up through different tables. `struct Wrapper<T>` is only findable
        // as a type; `float3 apply<T>(…)` is only findable as a function, and looking for the
        // second among types is how this returned null for every generic function.
        //
        // The generic *container*, not the declaration. FindTypeByName on `Wrapper` hands back a
        // struct whose full name is `Wrapper<T>` — the declaration with its parameter still
        // symbolic — and that is not what specializeGeneric takes.
        var declaredType = shader.FindTypeByName(genericName);
        var declaredFunction = shader.FindFunctionByName(genericName);

        var generic = declaredType?.GenericContainer ?? declaredFunction?.GenericContainer;

        if (declaredType is null && declaredFunction is null) return null;

        if (generic is null)
        {
            throw new ArgumentException(
                $"'{genericName}' is a {(declaredType is not null ? "type" : "function")} in this "
                + "program but not a generic one, so there is nothing to specialize.",
                nameof(genericName));
        }

        var specialized = shader.SpecializeGeneric(generic, arguments);
        return ReflectionWalker.ReadSpecialization(shader, specialized);
    }

    /// <summary>
    /// Finds a function by name and returns its signature, or null if the program has no such
    /// function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complement to walking <see cref="ReflectDeclarations"/>: that enumerates what exists,
    /// this answers about one name. Both return the same <see cref="FunctionSignature"/> snapshot.
    /// </para>
    /// <para>
    /// This is also the only way to see an <em>overload set</em>. The declaration tree reports two
    /// declarations of the same name as two sibling nodes, each with its own signature, and
    /// <c>IsOverloaded</c> is false on both. Resolved by name, Slang returns one function that says
    /// it is overloaded and carries the real declarations underneath — so
    /// <see cref="FunctionSignature.Overloads"/> is populated here and never there.
    /// </para>
    /// </remarks>
    public static FunctionSignature? FindFunction(this LinkedProgram program, int targetIndex, string name)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var function = program.GetLayout(targetIndex).FindFunctionByName(name);
        return function is null ? null : ReflectionWalker.ReadFunctionSignature(function);
    }

    /// <summary>
    /// Finds a method on a type by name and returns its signature, or null.
    /// </summary>
    /// <remarks>
    /// What a material system asks when it has resolved a concrete type and needs to know whether
    /// that type's <c>shade</c> takes the arguments it is about to pass.
    /// </remarks>
    public static FunctionSignature? FindFunction(this LinkedProgram program, int targetIndex, SlangType type, string name)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var function = program.GetLayout(targetIndex).FindFunctionByNameInType(type, name);
        return function is null ? null : ReflectionWalker.ReadFunctionSignature(function);
    }

    /// <summary>
    /// Picks the overload of <paramref name="name"/> that a call with these argument types would
    /// resolve to, or null if none matches.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="FindFunction(LinkedProgram, int, string)"/>, which reports that a
    /// name is overloaded and hands back the candidates. This answers which one a call site gets.
    ///
    /// Two routes exist and both are used, because neither alone is enough. Slang's
    /// <c>specializeWithArgTypes</c> resolves against a candidate set on the function itself;
    /// <c>TryResolveOverloadedFunction</c> resolves across a list the caller assembles. The first is
    /// tried, and the second picks up the case where an overloaded name does not narrow to one.
    /// </remarks>
    public static FunctionSignature? ResolveOverload(
        this LinkedProgram program, int targetIndex, string name, params ReadOnlySpan<SlangType> argumentTypes)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var shader = program.GetLayout(targetIndex);
        var function = shader.FindFunctionByName(name);
        if (function is null) return null;

        var resolved = function.SpecializeWithArgTypes(argumentTypes);

        // Where the name is an overload set and the call did not narrow it, the candidates can be
        // handed back for Slang to arbitrate. Only reached when the direct route declined.
        if (resolved is null && function.IsOverloaded)
        {
            var count = function.OverloadCount;
            var candidates = new List<SlangFunction>((int)count);
            for (uint i = 0; i < count; i++)
            {
                if (function.GetOverload(i) is { } overload) candidates.Add(overload);
            }

            resolved = shader.TryResolveOverloadedFunction([.. candidates]);
        }

        return resolved is null ? null : ReflectionWalker.ReadFunctionSignature(resolved);
    }

    /// <summary>
    /// A user attribute on this type's declaration, looked up by name, or null.
    /// </summary>
    /// <remarks>
    /// <c>TypeLayout.Attributes</c> enumerates the same set and is the one to reach for when the
    /// type has a layout. This exists for a type that does not, and because Slang's own by-name
    /// lookup (<see cref="SlangType.FindUserAttributeByName"/>) is the second opinion the
    /// enumeration is checked against.
    /// </remarks>
    public static UserAttribute? FindAttribute(this SlangType type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        var attribute = type.FindUserAttributeByName(name);
        return attribute is null ? null : ReflectionWalker.ReadAttribute(attribute);
    }

    /// <summary>
    /// The bindings in <paramref name="layout"/> that the generated code never touches.
    /// </summary>
    /// <remarks>
    /// The question a cooker asks. Reflection cannot answer it and the emitted module cannot
    /// either — a declared-but-unread binding is present in both.
    /// </remarks>
    public static IReadOnlyList<ParameterBinding> UnusedBindings(this SlangMetadata metadata, ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layout);

        var unused = new List<ParameterBinding>();
        foreach (var parameter in layout.Parameters)
        {
            foreach (var binding in parameter.Bindings)
            {
                if (!metadata.IsUsed(binding.Category, binding.Space, binding.Index)) unused.Add(binding);
            }
        }

        return unused;
    }

    /// <summary>
    /// The entry point's name and stage <em>in the emitted module</em> — what a pipeline builder
    /// puts in <c>VkPipelineShaderStageCreateInfo::pName</c>.
    /// </summary>
    /// <remarks>
    /// Reads it out of the SPIR-V rather than deriving it. Slang names every per-entry-point module
    /// <c>main</c> by default whatever the function was called, and
    /// <see cref="TargetOptions.UseEntryPointNameInSpirv"/> changes that — so neither answer is
    /// safe to hardcode, on either side of the artifact.
    /// </remarks>
    /// <exception cref="InvalidDataException">The target does not emit SPIR-V, or the module is malformed.</exception>
    public static SpirvEntryPoint GetSpirvEntryPoint(this LinkedProgram program, int entryPointIndex, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(program);
        using var code = program.GetEntryPointCode(entryPointIndex, targetIndex);
        return SpirvModule.SingleEntryPoint(code.Span);
    }
}
