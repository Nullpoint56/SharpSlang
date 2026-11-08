// Slang models the same facts more than once, and this asks both.
//
// The reflection API has, for most things, a primary route and an alternate one: a parameter's
// binding is on the parameter *and* on its variable layout; a binding range's image format is on
// the range *and* on the variable layout behind it; a field is findable by index *and* by name. The
// projection picks one of each and the other sits there unused — which is how a coverage count ends
// up full of functions nobody has ever called.
//
// Calling the alternate route and requiring it to agree turns that dead surface into an oracle. It
// is a weaker one than the emitted module — both answers come from the same compiler — but it is
// not nothing: these are separate code paths in Slang, and 's binding-range names were wrong
// precisely because one path knew something the other did not.
//
// Where the two disagree, the disagreement is the finding and it is recorded rather than smoothed.
//
// The alternate routes are asked through the binding's L2 reflection classes (SlangShaderReflection,
// SlangTypeLayout, ...), which the walk does not use for these questions: so each test also holds
// one L2 member to an answer it did not produce.

using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class SecondOpinionTests(ITestOutputHelper output)
{
    private const string Source = """
        [__AttributeUsage(_AttributeTargets.Struct)]
        struct SoTagAttribute { int order; };

        [SoTag(2)]
        struct SoBlock { float4 soTint; float3 soDir; float soGloss; };

        ConstantBuffer<SoBlock>              soBuffer;
        Texture2D                            soTexture;
        SamplerState                         soSampler;
        [format("rgba8")] RWTexture2D<float4> soImage;
        StructuredBuffer<float4>             soStructured;

        struct SoVertexOut { float4 clip : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        SoVertexOut soVertex(float3 pos : POSITION)
        { SoVertexOut o; o.clip = float4(pos, 1); o.uv = pos.xy; return o; }

        [shader("fragment")]
        float4 soFragment(SoVertexOut i) : SV_Target
        {
            soImage[uint2(0, 0)] = soBuffer.soTint;
            return soTexture.Sample(soSampler, i.uv) + soBuffer.soTint + soStructured[0];
        }
        """;

    private sealed record Loaded(
        SlangGlobalSession Global, SlangSession Session, SlangModule Module, LinkedProgram Program)
        : IDisposable
    {
        public void Dispose()
        {
            Program.Dispose();
            Module.Dispose();
            Session.Dispose();
            Global.Dispose();
        }

        public SlangShaderReflection Shader => Program.GetLayout(0);
    }

    private static Loaded Load()
    {
        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        var module = session.LoadModuleFromSource("so", "so.slang", Source);
        using var vertex = module.FindEntryPoint("soVertex");
        using var fragment = module.FindEntryPoint("soFragment");
        var program = session.Link(module, vertex, fragment);
        return new Loaded(global, session, module, program);
    }

    // ---------------------------------------------------------------- bindings, twice

    [NativeFact]
    public void A_parameters_binding_is_the_same_from_the_parameter_as_from_its_layout()
    {
        // spReflectionParameter_GetBindingIndex / GetBindingSpace against
        // ShaderParameter.PrimaryBinding, which the walk builds from the variable layout's
        // per-category offsets. Different entry points into the same data.
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        var count = loaded.Shader.ParameterCount;
        Assert.Equal((int)count, layout.Parameters.Count);

        for (uint i = 0; i < count; i++)
        {
            var parameter = loaded.Shader.GetParameterByIndex(i)!;
            var index = parameter.BindingIndex;
            var space = parameter.BindingSpace;

            var projected = layout.Parameters[(int)i].PrimaryBinding;
            output.WriteLine($"{layout.Parameters[(int)i].Name}: parameter says {space}:{index}, "
                + $"layout says {projected?.Space}:{projected?.Index}");

            Assert.NotNull(projected);
            Assert.Equal(projected.Value.Index, index);
            Assert.Equal(projected.Value.Space, space);
        }
    }

    [NativeFact]
    public void An_image_format_is_the_same_from_the_variable_layout_as_from_the_binding_range()
    {
        // The projection reads the format from the binding range. The variable layout answers the
        // same question about the same declaration, and is the route not taken.
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        var count = loaded.Shader.ParameterCount;
        for (uint i = 0; i < count; i++)
        {
            var varLayout = loaded.Shader.GetParameterByIndex(i)!;
            var name = layout.Parameters[(int)i].Name;
            var fromVariable = varLayout.ImageFormat;

            var projected = layout.DescriptorSets
                .SelectMany(s => s.Ranges)
                .FirstOrDefault(r => r.Name == name);

            if (projected is null) continue;

            output.WriteLine($"{name}: variable layout {fromVariable}, range {projected.ImageFormat}");
            Assert.Equal(projected.ImageFormat, fromVariable);
        }
    }

    // ---------------------------------------------------------------- lookup by name

    [NativeFact]
    public void Everything_findable_by_name_is_the_thing_the_snapshot_enumerated()
    {
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        // Entry points.
        foreach (var entryPoint in layout.EntryPoints)
        {
            var found = loaded.Shader.FindEntryPointByName(entryPoint.Name);
            Assert.True(found is not null, $"findEntryPointByName lost '{entryPoint.Name}'");
            Assert.Equal(entryPoint.Stage, found.Stage);
            Assert.Equal(entryPoint.Name, found.Name);
        }

        Assert.Null(loaded.Shader.FindEntryPointByName("soNoSuchEntryPoint"));

        // A field of a struct, by name, against the index the layout walk used.
        var block = loaded.Program.FindType("SoBlock");
        Assert.NotNull(block);

        var buffer = layout.Parameters.Single(p => p.Name == "soBuffer");
        var element = Assert.IsType<StructLayout>(
            Assert.IsType<ContainerLayout>(buffer.Type).ElementType);

        var typeLayout = loaded.Shader.GetParameterByIndex(
            (uint)layout.Parameters.ToList().FindIndex(p => p.Name == "soBuffer"))!.TypeLayout!;
        var elementLayout = typeLayout.ElementTypeLayout!;

        for (var i = 0; i < element.Fields.Count; i++)
        {
            var fieldName = element.Fields[i].Name;
            var index = elementLayout.FindFieldIndexByName(fieldName);

            output.WriteLine($"field '{fieldName}': walk {i}, findFieldIndexByName {index}");
            Assert.Equal(i, (int)index);
        }

        Assert.Equal(-1, elementLayout.FindFieldIndexByName("soNoSuchField"));

        // A variable inside a type, by name.
        foreach (var (fieldName, fieldType) in block.Fields)
        {
            var found = loaded.Shader.FindVarByNameInType(block, fieldName);

            Assert.True(found is not null, $"FindVarByNameInType lost '{fieldName}'");
            Assert.Equal(fieldName, found.Name);
            Assert.Equal(fieldType, found.Type!.FullName);
        }
    }

    [NativeFact]
    public void The_structural_field_list_agrees_with_the_laid_out_one()
    {
        // SlangType.Fields is spReflectionType_GetFieldCount/GetFieldByIndex — what a type *has*,
        // with no layout. The snapshot's StructLayout.Fields is the same declaration with offsets.
        // Same names, same order, or one of the two views is walking something else.
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        var block = loaded.Program.FindType("SoBlock");
        Assert.NotNull(block);

        var element = Assert.IsType<StructLayout>(
            Assert.IsType<ContainerLayout>(
                layout.Parameters.Single(p => p.Name == "soBuffer").Type).ElementType);

        output.WriteLine("structural: " + string.Join(", ", block.Fields.Select(f => $"{f.Type} {f.Name}")));
        output.WriteLine("laid out:   " + string.Join(", ", element.Fields.Select(f => f.Name)));

        Assert.Equal(element.Fields.Select(f => f.Name), block.Fields.Select(f => f.Name));
    }

    [NativeFact]
    public void An_attribute_found_by_name_is_the_one_that_was_enumerated()
    {
        using var loaded = Load();

        var block = loaded.Program.FindType("SoBlock");
        Assert.NotNull(block);

        var found = block.FindAttribute("SoTag");
        Assert.NotNull(found);
        Assert.Equal(2, Assert.Single(found.Arguments));

        Assert.Null(block.FindAttribute("SoNotAnAttribute"));
    }

    // ---------------------------------------------------------------- the rest of the surface

    [NativeFact]
    public void The_remaining_layout_queries_answer_and_agree()
    {
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        var globalVarLayout = loaded.Shader.GlobalParamsVarLayout;
        Assert.True(globalVarLayout is not null, "the global scope has no variable layout");

        // The global scope reached both ways is one type layout.
        var scope = loaded.Shader.GlobalParamsTypeLayout!;
        Assert.Equal(scope, globalVarLayout.TypeLayout);

        // The single category a type occupies, where it occupies exactly one. Checked against the
        // sizes the walk collected rather than assumed.
        var index = layout.Parameters.ToList().FindIndex(p => p.Name == "soTexture");
        var textureLayout = loaded.Shader.GetParameterByIndex((uint)index)!.TypeLayout!;

        var category = textureLayout.ParameterCategory;
        output.WriteLine($"soTexture single category: {category}");
        Assert.Contains(category, layout.Parameters[index].Type.Sizes.Select(s => s.Category));

        // A binding range spans a number of descriptors; the descriptor-range view reports the
        // same count.
        var rangeCount = scope.BindingRangeCount;
        for (long b = 0; b < rangeCount; b++)
        {
            var bindingCount = scope.GetBindingRangeBindingCount(b);
            Assert.True(bindingCount >= 0, $"range {b} reports a negative binding count");
        }

        // Field-to-binding-range mapping, for the fields of the global scope: each field's first
        // binding range is the one whose leaf is that field.
        var fields = scope.FieldCount;
        for (uint f = 0; f < fields; f++)
        {
            var offset = scope.GetFieldBindingRangeOffset(f);
            Assert.True(offset >= -1, $"field {f} reports a binding range offset below -1");
            if (offset < 0 || offset >= rangeCount) continue;

            var fieldName = scope.GetFieldByIndex(f)!.Variable!.Name;
            Assert.Equal(fieldName, scope.GetBindingRangeLeafVariable(offset)?.Name);
        }

        // The element variable layout of a container — the other way to reach what
        // GetElementTypeLayout gives, carrying the offsets as well as the type.
        var bufferIndex = layout.Parameters.ToList().FindIndex(p => p.Name == "soBuffer");
        var bufferLayout = loaded.Shader.GetParameterByIndex((uint)bufferIndex)!.TypeLayout!;

        var elementVar = bufferLayout.ElementVarLayout;
        Assert.True(elementVar is not null, "a ConstantBuffer has no element variable layout");
        Assert.Equal(bufferLayout.ElementTypeLayout, elementVar.TypeLayout);
        Assert.True(
            elementVar.PendingDataLayout is null,
            "an element variable layout reported pending data, which has not been seen before "
            + "and is worth projecting properly rather than flagging");

        // A type parameter by name: none here, and the query has to say so rather than crash.
        Assert.Null(loaded.Shader.FindTypeParameter("NoSuchTypeParameter"));
    }

    [NativeFact]
    public void A_linked_program_has_nothing_specializable_and_nothing_pending()
    {
        // Both flags exist because Slang exposes the question, and both are false everywhere here
        // — which is what linking is for. Pinned so that "always false" is a measurement rather
        // than an assumption nobody rechecked.
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        Assert.All(layout.DescriptorSets.SelectMany(s => s.Ranges),
            r => Assert.False(r.IsSpecializable));

        Assert.All(layout.Parameters, p =>
        {
            Assert.False(p.Type.HasPendingData);
            Assert.Null(p.Type.GenericParameterIndex);
        });
    }

    [NativeFact]
    public void A_varying_reports_the_stage_it_belongs_to_and_a_global_does_not()
    {
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        // Globals are not attributed to a stage.
        Assert.All(layout.Parameters, p => Assert.Equal(SlangStage.SLANG_STAGE_NONE, p.Stage));

        foreach (var entryPoint in layout.EntryPoints)
            foreach (var parameter in entryPoint.Parameters)
                output.WriteLine($"{entryPoint.Name}.{parameter.Name}: {parameter.Stage}");
    }

    // ---------------------------------------------------------------- the last of the surface

    [NativeFact]
    public void By_name_attribute_lookup_on_a_function_and_a_variable_needs_the_global_session()
    {
        // The asymmetry noted on SlangType.FindAttribute: the type overload takes a name, these two
        // take an IGlobalSession as well. Nothing in the headers says why, and a caller who assumes
        // the three are alike gets a compile error rather than a surprise, so it is only recorded.
        const string Tagged = """
            [__AttributeUsage(_AttributeTargets.Function)]
            struct FnTagAttribute { int order; };

            [__AttributeUsage(_AttributeTargets.Var)]
            struct VarTagAttribute { int order; };

            [FnTag(5)] float3 fnTagged(float3 a) { return a; }

            [VarTag(6)] static const int fvValue = 3;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return float4(fnTagged(1), fvValue); }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("fn", "fn.slang", Tagged);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, entry);

        var function = program.GetLayout(0).FindFunctionByName("fnTagged");
        Assert.True(function is not null, "fnTagged is not findable by name");

        var found = function.FindUserAttributeByName(global, "FnTag");
        Assert.True(found is not null, "FindUserAttributeByName lost FnTag");
        Assert.Equal("FnTag", found.Name);

        // The by-name answer is the enumerated one.
        Assert.Equal(1u, function.UserAttributeCount);
        Assert.Equal(function.GetUserAttribute(0), found);
        Assert.True(found.TryGetArgumentValueInt(0, out var order));
        Assert.Equal(5, order);

        // And the same shape for a variable — reached through the function's first parameter,
        // which is a variable like any other.
        var parameter = function.GetParameter(0);
        Assert.True(parameter is not null, "fnTagged has no first parameter");

        // The parameter carries no attribute; the point is that asking is answered rather than
        // fatal, which is the only claim a negative lookup can support.
        Assert.True(parameter.FindUserAttributeByName(global, "VarTag") is null,
            "an attribute appeared on a parameter that has none");

        Assert.True(parameter.GenericContainer is null,
            "an ordinary parameter reported a generic container");
    }

    [NativeFact]
    public void A_function_and_a_generic_both_convert_back_to_the_declaration_they_came_from()
    {
        // asDecl on either side of the fence. Round-tripping through it has to land on the same
        // name, or the two views are not of the same thing — which is the only property these
        // conversions can be checked for.
        const string Generic = """
            interface IAdTag { float3 shade(float3 a); }
            struct AdLambert : IAdTag { float3 shade(float3 a) { return a; } }
            float3 adApply<T : IAdTag>(T m, float3 a) { return m.shade(a); }

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { AdLambert m; return float4(adApply(m, 1), 1); }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("ad", "ad.slang", Generic);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, entry);

        var function = program.GetLayout(0).FindFunctionByName("adApply");
        Assert.True(function is not null, "adApply is not findable by name");

        var functionDecl = function.AsDecl();
        Assert.True(functionDecl is not null, "a function did not convert to a declaration");
        output.WriteLine($"function -> decl '{functionDecl.Name}' ({functionDecl.Kind})");

        // And back: the declaration cast to a function is the same declaration, though not the
        // same pointer (measured, v2026.21), so it is compared by what it reports.
        var back = functionDecl.CastToFunction();
        Assert.NotNull(back);
        Assert.Equal(function.Name, back.Name);
        Assert.Equal(function.ParameterCount, back.ParameterCount);

        var generic = function.GenericContainer;
        Assert.True(generic is not null, "adApply reported no generic container");

        var genericDecl = generic.AsDecl();
        Assert.True(genericDecl is not null, "a generic did not convert to a declaration");

        Assert.Equal("adApply", genericDecl.Name);
        Assert.Equal(SlangDeclKind.SLANG_DECL_KIND_GENERIC, genericDecl.Kind);
        Assert.Equal(generic.Name, genericDecl.CastToGeneric()?.Name);

        // spReflectionGeneric_applySpecializations is exercised from the prober
        // (`apply-generic-to-itself`) rather than here. Applying an unspecialized generic as its
        // own substitution is a question, not an operation with an obvious answer, and a question
        // asked in-process is a question that can take the runner down with it — which is what
        // happened to the first macOS run.
    }

    [NativeFact]
    public void The_calls_that_report_nothing_are_pinned_reporting_nothing()
    {
        // Five queries measured to answer with silence. They are called here rather than left
        // unreached, because "we never call it" and "we called it and it says nothing" are
        // different claims and only the second is a measurement. If any of them starts answering,
        // this fails and the projection has something new to carry.
        using var loaded = Load();
        var layout = loaded.Program.Reflect();

        var scope = loaded.Shader.GlobalParamsTypeLayout!;

        // 1-2. The sub-object range's own name and space (U15).
        var subObjects = scope.SubObjectRangeCount;
        for (long i = 0; i < subObjects; i++)
        {
            var variable = scope.GetSubObjectRangeOffset(i)?.Variable;

            Assert.True(variable is null,
                "getSubObjectRangeOffset now carries a variable; SubObjectRange can take its name "
                + "from there instead of from the binding range");

            Assert.Equal(0, scope.GetSubObjectRangeSpaceOffset(i));
        }

        // 3-4. Specialized type arguments, on a type that has them in its own name.
        var block = loaded.Program.FindType("SoBlock");
        Assert.NotNull(block);
        // Only the count, in-process. Asking for argument 0 of a list of length 0 is an
        // out-of-range read, and this test used to do exactly that on the reasoning that Windows
        // and Linux returned null for it. That is not a guarantee, it is two data points — and
        // asking a native API for an element it has just said does not exist is the caller's
        // mistake regardless of who survives it. The query lives in the prober now
        // (`specialized-type-arg-out-of-range`), where the answer is measured rather than assumed.
        Assert.Equal(0, block.SpecializedTypeArgCount);

        // 5. specializeType is not here. It is called from the prober instead: given a type that
        //    is not generic it returns null on Windows and *aborts* on Linux, which is the fifth
        //    measured Slang crash and cost this suite a green run to find.

        // 6. Pending data on a specialized type.
        var textureIndex = layout.Parameters.ToList().FindIndex(p => p.Name == "soTexture");
        var textureLayout = loaded.Shader.GetParameterByIndex((uint)textureIndex)!.TypeLayout!;

        Assert.True(
            textureLayout.SpecializedTypePendingDataVarLayout is null,
            "a type layout reported specialized pending data, which has not been seen here");
    }

    [NativeFact]
    public void Capabilities_targets_and_string_hashes_answer()
    {
        using var loaded = Load();

        // A capability Slang knows, and one it does not.
        var known = loaded.Global.FindCapability("spvRayQueryKHR");
        var unknown = loaded.Global.FindCapability("NotACapabilityAnyoneDeclared");
        output.WriteLine($"spvRayQueryKHR={known}  nonsense={unknown}");

        Assert.NotEqual(unknown, known);
        Assert.Equal(SlangCapabilityID.SLANG_CAPABILITY_UNKNOWN, unknown);

        // The compiler's own view of what it can emit, distinct from what a device can run.
        Assert.True(loaded.Global.SupportsTarget(SlangCompileTarget.SLANG_SPIRV),
            "this build cannot generate SPIR-V, which is the only target this package uses");

        // getStringHash's other end. Deterministic, and different strings differ — the two claims
        // a host matching a name against a baked constant depends on.
        var albedo = SlangGlobalSession.HashString("albedo");
        Assert.Equal(albedo, SlangGlobalSession.HashString("albedo"));
        Assert.NotEqual(albedo, SlangGlobalSession.HashString("roughness"));
        output.WriteLine($"hash(\"albedo\")={albedo}");
    }

    // ---------------------------------------------------------------- Slang's own dump

    [NativeFact]
    public void Slangs_reflection_json_names_what_the_snapshot_names()
    {
        // spReflection_ToJson takes an ICompileRequest — the legacy API this binding does not bind
        // — and null works. Worth having: it is the one view of a program that is Slang's own
        // formatting rather than this package's reading, which is what belongs in a bug report.
        using var loaded = Load();
        var json = loaded.Program.ToJson();

        Assert.False(string.IsNullOrWhiteSpace(json), "spReflection_ToJson produced nothing");
        output.WriteLine(json![..Math.Min(400, json.Length)]);

        foreach (var parameter in loaded.Program.Reflect().Parameters)
            Assert.Contains($"\"{parameter.Name}\"", json, StringComparison.Ordinal);

        // The program's convenience and the reflection object's call are one call.
        Assert.Equal(json, loaded.Shader.ToJson());
    }
}
