// ISession's queries beyond loading and linking, IModule::disassemble, renaming an entry point and
// specializing by expression. Each answer is held to something outside the call: the source text,
// the emitted module, the program layout's own answer, or Slang's documented RTTI format.

using SharpSlang.Interop;
using SharpSlang.Native;
using SharpSlang.Spirv;

namespace SharpSlang.Tests;

public class SessionQueryTests(ITestOutputHelper output)
{
    private const string Library = """
        struct QSurface { float3 albedo; float roughness; float weights[2]; };

        float qShade(QSurface s) { return s.roughness; }
        """;

    private const string Dispatch = """
        interface IQShape { float area(); }
        struct QSquare : IQShape { float side; float area() { return side * side; } }
        struct QCircle : IQShape { float radius; float area() { return 3.14159 * radius * radius; } }
        """;

    private const string Compute = """
        RWStructuredBuffer<float> result;

        [shader("compute")]
        [numthreads(4, 1, 1)]
        void computeMain(uint3 id : SV_DispatchThreadID)
        {
            result[id.x] = id.x * 2.0;
        }

        [shader("compute")]
        [numthreads(4, 1, 1)]
        void otherMain(uint3 id : SV_DispatchThreadID)
        {
            result[id.x] = id.x * 3.0;
        }
        """;

    private const string Generic = """
        interface IMaterial { float3 shade(float3 a); }
        struct Lambert  : IMaterial { float3 shade(float3 a) { return a * 0.5; } }
        struct Emissive : IMaterial { float3 shade(float3 a) { return a * 4.0; } }

        type_param Material : IMaterial;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            Material m;
            return float4(m.shade(float3(1, 1, 1)), 1.0);
        }
        """;

    private static SlangSession NewSession(SlangGlobalSession global, params string[] searchPaths) =>
        global.CreateSession(new SessionOptions { Targets = [new TargetOptions()], SearchPaths = searchPaths });

    private static unsafe int RefCount(SlangObject o)
    {
        var p = (ISlangUnknown*)o.NativeHandle;
        p->addRef();
        return (int)p->release();
    }

    [NativeFact]
    public void The_loaded_modules_include_the_ones_an_import_pulled_in()
    {
        var directory = Directory.CreateTempSubdirectory("sharpslang-loaded-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "qlib.slang"), Library);

            using var global = SlangGlobalSession.Create();
            using var session = NewSession(global, directory.FullName);
            using var main = session.LoadModuleFromSource("qmain", "qmain.slang", """
                import qlib;
                float use(QSurface s) { return qShade(s); }
                """);

            var names = new List<string?>();
            for (var i = 0; i < session.LoadedModuleCount; i++)
            {
                using var m = session.GetLoadedModule(i);
                names.Add(m.DeclaredName);
            }

            output.WriteLine(string.Join(", ", names));
            Assert.Contains("qlib", names);
            Assert.Contains("qmain", names);
            Assert.Throws<ArgumentOutOfRangeException>(() => session.GetLoadedModule(session.LoadedModuleCount));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [NativeFact]
    public void A_loaded_module_is_borrowed_and_the_wrapper_takes_its_own_reference()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qref", "qref.slang", Library);

        var before = RefCount(module);
        using (var again = session.GetLoadedModule(0))
        {
            Assert.Equal(module.NativeHandle, again.NativeHandle);
            Assert.Equal(before + 1, RefCount(module));
        }

        Assert.Equal(before, RefCount(module));

        // Twice more, disposed each time: an over-release would show here, or end the process on glibc.
        for (var i = 0; i < 2; i++) session.GetLoadedModule(0).Dispose();
        Assert.Equal(before, RefCount(module));
        Assert.Equal("qref", module.DeclaredName);
    }

    [NativeFact]
    public void A_type_layout_from_the_session_agrees_with_the_programs()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qlay", "qlay.slang", Library);
        var type = module.RequireType(0, "QSurface");

        var fromSession = session.GetTypeLayout(type, 0);
        var fromModule = module.GetLayout(0).GetTypeLayout(type)!;
        output.WriteLine($"session {fromSession.GetSize()} bytes, module layout {fromModule.GetSize()} bytes");
        Assert.Equal(fromModule.GetSize(), fromSession.GetSize());

        // The rules decide the array stride: 16 in a constant buffer (std140), 4 in a structured
        // buffer (std430). float3 + float is 16 bytes either way; std430 still rounds the struct up
        // to its 16-byte alignment, so 16 + 8 becomes 32.
        var constant = session.GetTypeLayout(type, 0, LayoutRules.DefaultConstantBuffer);
        var structured = session.GetTypeLayout(type, 0, LayoutRules.DefaultStructuredBuffer);
        output.WriteLine($"constant buffer {constant.GetSize()}, structured buffer {structured.GetSize()}");
        Assert.Equal(16ul, constant.GetFieldByIndex(2)!.TypeLayout!.GetElementStride());
        Assert.Equal(4ul, structured.GetFieldByIndex(2)!.TypeLayout!.GetElementStride());
        Assert.Equal(16u + 2 * 16u, constant.GetSize());
        Assert.Equal(32u, structured.GetSize());

        Assert.Throws<ArgumentOutOfRangeException>(() => session.GetTypeLayout(type, 1));
    }

    [NativeFact]
    public void A_container_type_wraps_its_element()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qbuf", "qbuf.slang", Library);
        var element = module.RequireType(0, "QSurface");

        var buffer = session.GetContainerType(element, ContainerType.StructuredBuffer);
        output.WriteLine($"{buffer.FullName}: {buffer.Kind}");

        Assert.Equal(SlangTypeKind.SLANG_TYPE_KIND_RESOURCE, buffer.Kind);
        Assert.StartsWith("StructuredBuffer<QSurface", buffer.FullName, StringComparison.Ordinal);
        Assert.Equal("QSurface", buffer.ResourceResultType?.Name);
    }

    [NativeFact]
    public void A_declarations_source_location_is_where_its_name_is_written()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qloc", "qloc.slang", Library);

        var decl = Children(module.GetModuleReflection()!).Single(d => d.Name == "qShade");
        var location = session.GetDeclSourceLocation(decl);
        output.WriteLine($"{location}");

        // The oracle is the source text: the line holding the name, and the column it starts at.
        var lines = Library.Split('\n');
        var line = Array.FindIndex(lines, l => l.Contains("qShade(", StringComparison.Ordinal));
        Assert.NotNull(location);
        Assert.Equal(line + 1, location.Value.Line);
        Assert.Equal(lines[line].IndexOf("qShade", StringComparison.Ordinal) + 1, location.Value.Column);
        Assert.EndsWith("qloc.slang", location.Value.FilePath, StringComparison.Ordinal);

        static IEnumerable<SlangDecl> Children(SlangDecl d)
        {
            for (var i = 0u; i < d.ChildrenCount; i++)
                if (d.GetChild(i) is { } c) yield return c;
        }
    }

    [NativeFact]
    public void Witness_ids_are_allocated_per_interface_in_the_order_asked()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qdyn", "qdyn.slang", Dispatch);
        var shape = module.RequireType(0, "IQShape");
        var square = module.RequireType(0, "QSquare");
        var circle = module.RequireType(0, "QCircle");

        Assert.Equal(0u, session.GetTypeConformanceWitnessSequentialId(square, shape));
        Assert.Equal(1u, session.GetTypeConformanceWitnessSequentialId(circle, shape));
        Assert.Equal(0u, session.GetTypeConformanceWitnessSequentialId(square, shape));

        // slang.h's header format: a non-zero RTTI word, then the witness id at byte 8.
        var rtti = session.GetDynamicObjectRttiBytes(circle, shape);
        output.WriteLine(Convert.ToHexString(rtti));
        Assert.Equal(16, rtti.Length);
        Assert.Equal(1u, BitConverter.ToUInt32(rtti, 0));
        Assert.Equal(1u, BitConverter.ToUInt32(rtti, 8));

        var squareWitness = session.GetTypeConformanceWitnessMangledName(square, shape);
        var circleWitness = session.GetTypeConformanceWitnessMangledName(circle, shape);
        var squareRtti = session.GetTypeRttiMangledName(square);
        output.WriteLine($"{squareWitness} {circleWitness} {squareRtti}");
        Assert.NotEmpty(squareWitness);
        Assert.NotEqual(squareWitness, circleWitness);
        Assert.Contains("QSquare", squareRtti, StringComparison.Ordinal);

        var dynamic = session.DynamicType;
        output.WriteLine($"{dynamic.FullName}: {dynamic.Kind}");
        Assert.NotEqual(0, dynamic.NativeHandle);
    }

    [NativeFact]
    public void A_disassembled_module_names_what_it_declares()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qdis", "qdis.slang", Library);

        var text = module.Disassemble();
        output.WriteLine(text.Length > 400 ? text[..400] : text);
        Assert.Contains("qShade", text, StringComparison.Ordinal);
    }

    [NativeFact]
    public void A_renamed_entry_point_is_emitted_under_its_new_name()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });
        using var module = session.LoadModuleFromSource("qren", "qren.slang", Compute);
        using var entry = module.FindEntryPoint("computeMain");

        using var renamed = entry.RenameEntryPoint("renamedMain");
        Assert.Equal(["renamedMain"], renamed.EntryPointNames);

        using var composite = session.CreateCompositeComponentType(module, renamed);
        using var program = composite.Link();
        var code = program.GetEntryPointCodeBytes(0, 0);
        Assert.Equal("renamedMain", SpirvModule.SingleEntryPoint(code).Name);

        // The same from a composite holding one entry point.
        using var single = session.CreateCompositeComponentType(entry);
        using var renamedComposite = single.RenameEntryPoint("alsoRenamed");
        using var composite2 = session.CreateCompositeComponentType(module, renamedComposite);
        using var program2 = composite2.Link();
        Assert.Equal("alsoRenamed", SpirvModule.SingleEntryPoint(program2.GetEntryPointCodeBytes(0, 0)).Name);

        // Two entry points: slang.h requires one, and Slang itself would wrap them and succeed.
        using var other = module.FindEntryPoint("otherMain");
        using var both = session.CreateCompositeComponentType(entry, other);
        var e = Assert.Throws<InvalidOperationException>(() => both.RenameEntryPoint("ambiguous"));
        output.WriteLine(e.Message);
    }

    [NativeFact]
    public void The_result_files_hold_the_code_getEntryPointCode_returns()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qres", "qres.slang", Compute);
        using var entry = module.FindEntryPoint("computeMain");
        using var program = session.Link(module, entry);

        var files = program.GetResultFiles(0, 0);
        foreach (var (path, bytes) in files) output.WriteLine($"{path}: {bytes.Length} bytes");

        var code = program.GetEntryPointCodeBytes(0, 0);
        Assert.Contains(files.Values, f => f.AsSpan().SequenceEqual(code));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.GetResultFiles(0, 1));
    }

    [NativeFact]
    public void Specializing_by_expression_emits_what_specializing_by_type_emits()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("qspec", "qspec.slang", Generic);
        using var entry = module.FindEntryPoint("fragmentMain");
        using var composite = session.CreateCompositeComponentType(module, entry);

        static byte[] Emit(SlangComponentType specialized)
        {
            using (specialized)
            using (var program = specialized.Link())
                return program.GetEntryPointCodeBytes(0, 0);
        }

        var byType = Emit(composite.Specialize(module.RequireType(0, "Emissive")));
        var byExpression = Emit(composite.Specialize(SpecializationArgument.OfExpression("Emissive")));
        var other = Emit(composite.Specialize(SpecializationArgument.OfExpression("Lambert")));

        Assert.Equal(byType, byExpression);
        Assert.NotEqual(byType, other);

        var e = Assert.Throws<SlangException>(() => composite.Specialize(SpecializationArgument.OfExpression("NoSuchType")));
        output.WriteLine(e.Message);
        Assert.Throws<ArgumentException>(() => composite.Specialize(default(SpecializationArgument)));
    }
}
