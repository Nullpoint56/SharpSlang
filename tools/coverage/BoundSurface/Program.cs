// Bound surface: which of Slang's functions the binding's own code calls, and which fields of
// Slang's native structs its API reaches. Read from the built assemblies (IL) and their PDBs.
//
// A Slang function is one of two things:
//
//   - a flat export: a [DllImport] of the one DllImport class (--api, SlangApi). It is bound when a
//     method outside the generated layer calls it.
//   - a vtable slot of a COM-style interface: the generated layer has one struct per interface
//     (IGlobalSession, ISession, IModule, ...), its first field the vtable pointer (--vtable-field,
//     lpVtbl), and one instance method per slot that loads lpVtbl[n] and calls through it. The
//     index is read from that method's IL (VtblIndexAttribute is [Conditional("DEBUG")], so a
//     Release build has none). A struct repeats its base interface's slots, so each slot is
//     counted once, on the interface that declares it: the base of an interface is the interface
//     whose slots are the longest proper prefix of its own, by index and name. A slot is bound
//     when a method outside the generated layer calls its method through any of the structs that
//     carry it, or loads lpVtbl[n] itself.
//
// Generated helpers are followed: a call from binding code to a generated method that is not a
// slot (ClangSharp's QueryInterface/AddRef/Release forwarders, union accessors) counts as the
// calls and field accesses that method makes, transitively. Interfaces the host implements and
// Slang calls (--host-implemented: file systems, writers, library loaders) are not Slang's
// functions to bind, and are listed apart, as SharpNRI leaves out its CallbackInterface.
//
// "Outside the generated layer" is by source path: a method whose PDB document lies under
// --generated (dotnet/Native) is generated; one with no document is generated when its outermost
// type is in the generated namespace. That holds whether the generated layer shares the binding's
// assembly (one assembly) or lives in a Descs assembly of its own (the NativeSplit layout).
//
// Struct fields: a native struct that the idiomatic API takes or returns (public and protected
// members of public types outside the generated namespace) is exposed with every field, and so is
// every struct reachable through its fields. A struct that a Slang function signature reaches but
// the API does not expose is built or read by the binding, and a field of it is bound only when
// binding code reads or writes it. A struct no Slang signature reaches is a data layout, counted
// through its public type.
//
// Each call site carries the source line its IL offset maps to in the PDB, so tools/coverage.ps1
// can ask the test coverage whether that line ran.
//
//   BoundSurface --assembly <dll> [--assembly <dll> ...] --out <bound.json>
//                [--namespace SharpSlang.Native] [--api SlangApi] [--generated dotnet/Native]
//                [--root <repository root>] [--vtable-field lpVtbl]
//                [--host-implemented ISlangFileSystem,ISlangWriter,...]

using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.Loader;
using System.Text.Json;

var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("usage: BoundSurface --assembly <dll> [--assembly <dll> ...] --out <bound.json> [--namespace <ns>] [--api <DllImport class>]");
    Console.Error.WriteLine("                    [--generated <path under --root>] [--root <dir>] [--vtable-field <name>] [--host-implemented <I1,I2,...>]");
    return 2;
}

// Loaded for inspection only: nothing is invoked, no static constructor runs. Dependencies resolve
// from each assembly's own directory, then from the shared framework.
var directories = options.Assemblies.Select(a => Path.GetDirectoryName(Path.GetFullPath(a))!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
var context = new AssemblyLoadContext("BoundSurface", isCollectible: false);
context.Resolving += (alc, name) =>
{
    foreach (var dir in directories)
    {
        var candidate = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(candidate)) return alc.LoadFromAssemblyPath(candidate);
    }
    return null;
};
var assemblies = options.Assemblies.Select(a => context.LoadFromAssemblyPath(Path.GetFullPath(a))).ToList();
var pdbs = new Dictionary<Module, MetadataReader?>();
var pdbProviders = new List<MetadataReaderProvider>();
foreach (var assembly in assemblies)
{
    var pdbPath = Path.ChangeExtension(assembly.Location, ".pdb");
    MetadataReader? reader = null;
    if (File.Exists(pdbPath))
    {
        var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
        pdbProviders.Add(provider);
        reader = provider.GetMetadataReader();
    }
    else
    {
        Console.Error.WriteLine($"warning: no PDB beside {assembly.Location}: call sites carry no file or line, and every method counts by namespace");
    }
    pdbs[assembly.ManifestModule] = reader;
}

const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
    .Select(f => (OpCode)f.GetValue(null)!)
    .ToDictionary(o => unchecked((ushort)o.Value));

var allTypes = assemblies.SelectMany(LoadableTypes).ToList();
bool InGenerated(Type t)
{
    while (t.DeclaringType is { } outer) t = outer;
    return t.Namespace == options.Namespace;
}
string NativeName(Type t) => t.IsNested ? $"{NativeName(t.DeclaringType!)}.{t.Name}" : t.Name;

// ------------------------------------------------------------------------------- the methods
// Every method with a body, classified as generated or binding by its source document.
var methods = new List<(MethodBase Method, bool Generated)>();
foreach (var type in allTypes)
{
    foreach (var m in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
    {
        if (m.GetMethodBody() is null) continue;
        var document = Document(m);
        var generated = document is not null
            ? IsUnder(document, options.Root, options.Generated)
            : InGenerated(type);
        methods.Add((m, generated));
    }
}

// ------------------------------------------------------------------------------- the interfaces
// A vtable struct: a value type of the generated namespace with the vtable field. Its slot
// methods: instance methods that load the vtable field and make an indirect call.
var interfaces = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
var slotOf = new Dictionary<MethodBase, (string Interface, int Index)>();
foreach (var type in allTypes.Where(t => t.IsValueType && InGenerated(t) && t.GetField(options.VtableField, Declared) is not null))
{
    var slots = new SortedDictionary<int, string>();
    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        if (SlotIndex(m) is not { } index) continue;
        if (slots.TryGetValue(index, out var other))
        {
            Console.Error.WriteLine($"warning: {type.Name} has two methods on slot {index}: {other} and {m.Name}; keeping the first");
            continue;
        }
        slots[index] = m.Name;
        slotOf[m] = (NativeName(type), index);
    }
    interfaces[NativeName(type)] = slots;
}

// The base of each interface: the one whose slots are the longest proper prefix of its own. An
// interface that adds no slot (ITypeConformance : IComponentType) has the same slots as its base,
// which the IL cannot tell apart from the other way round: --base names those (Derived=Base), and
// an unnamed tie is reported.
var bases = new Dictionary<string, string?>(StringComparer.Ordinal);
bool Prefix(SortedDictionary<int, string> shorter, SortedDictionary<int, string> longer) =>
    shorter.All(s => longer.TryGetValue(s.Key, out var n) && n == s.Value);
foreach (var (name, slots) in interfaces)
{
    if (options.Bases.TryGetValue(name, out var named))
    {
        if (!interfaces.TryGetValue(named, out var namedSlots) || !Prefix(namedSlots, slots))
            throw new InvalidOperationException($"--base {name}={named}: {named} is not an interface whose slots begin {name}'s");
        bases[name] = named;
        continue;
    }
    string? best = null;
    foreach (var (other, otherSlots) in interfaces)
    {
        if (other == name || otherSlots.Count == 0 || otherSlots.Count > slots.Count || !Prefix(otherSlots, slots)) continue;
        if (otherSlots.Count == slots.Count)
        {
            if (options.Bases.GetValueOrDefault(other) != name)
                Console.Error.WriteLine($"warning: {name} and {other} have the same {slots.Count} slots; name the derived one with --base <Derived>=<Base>");
            continue;
        }
        if (best is null || otherSlots.Count > interfaces[best].Count
            || (otherSlots.Count == interfaces[best].Count && options.Bases.GetValueOrDefault(best) == other)) best = other;
    }
    bases[name] = best;
}
string Declaring(string iface, int index)
{
    while (bases[iface] is { } b && interfaces[b].ContainsKey(index)) iface = b;
    return iface;
}

// ------------------------------------------------------------------------------- the exports
var apiType = allTypes.FirstOrDefault(t => t.Namespace == options.Namespace && t.Name == options.Api)
    ?? throw new InvalidOperationException($"no {options.Namespace}.{options.Api} in the given assemblies");
var exportOf = new Dictionary<MethodBase, string>();
var exports = new List<string>();
foreach (var m in apiType.GetMethods(Declared).Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0))
{
    var entry = m.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType == typeof(System.Runtime.InteropServices.DllImportAttribute))?
        .NamedArguments.FirstOrDefault(n => n.MemberName == "EntryPoint").TypedValue.Value as string ?? m.Name;
    exportOf[m] = entry;
    if (!exports.Contains(entry)) exports.Add(entry);
}

// ------------------------------------------------------------------------------- IL
// What each generated method reaches (slots, exports, struct fields), transitively; then every
// binding method's calls, with their sites.
var vtableStructs = allTypes.Where(t => t.IsValueType && InGenerated(t) && t.GetField(options.VtableField, Declared) is not null).ToHashSet();
var direct = new Dictionary<MethodBase, Reach>();
var closure = new Dictionary<MethodBase, Reach>();
foreach (var (m, _) in methods.Where(x => x.Generated)) direct[m] = Scan(m, null);
Reach Close(MethodBase m, HashSet<MethodBase> visiting)
{
    if (closure.TryGetValue(m, out var done)) return done;
    var own = direct[m];
    var result = new Reach();
    result.Functions.UnionWith(own.Functions);
    result.Fields.UnionWith(own.Fields);
    if (visiting.Add(m))
    {
        foreach (var callee in own.Calls.Where(direct.ContainsKey))
        {
            var r = Close(callee, visiting);
            result.Functions.UnionWith(r.Functions);
            result.Fields.UnionWith(r.Fields);
        }
        visiting.Remove(m);
    }
    return closure[m] = result;
}
foreach (var m in direct.Keys) Close(m, []);

var callers = new Dictionary<string, List<Caller>>(StringComparer.Ordinal); // "Interface.slot" or export -> sites
var accessed = new HashSet<string>(StringComparer.Ordinal);                   // "Struct.Nested.field"
foreach (var (m, _) in methods.Where(x => !x.Generated))
{
    var sites = new List<(string Key, int Offset, string? Via)>();
    var reach = Scan(m, sites);
    accessed.UnionWith(reach.Fields);
    foreach (var (key, offset, via) in sites)
        (callers.TryGetValue(key, out var list) ? list : callers[key] = []).Add(Site(m, offset, via));
}

// ------------------------------------------------------------------------------- struct fields
bool IsNativeStruct(Type t) => InGenerated(t) && t.IsValueType && !t.IsEnum && !t.IsPrimitive && !vtableStructs.Contains(t)
    && !t.Name.EndsWith("_e__FixedBuffer", StringComparison.Ordinal);

var exposed = new HashSet<Type>();
void Expose(Type? t)
{
    if (t is null) return;
    if (t.HasElementType) { Expose(t.GetElementType()); return; }
    if (t.IsGenericType) { foreach (var a in t.GetGenericArguments()) Expose(a); return; }
    if (!IsNativeStruct(t) || !exposed.Add(t)) return;
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) Expose(f.FieldType);
}
foreach (var type in assemblies.SelectMany(a => a.GetExportedTypes()))
{
    if (InGenerated(type)) continue; // the generated layer and the DllImport class: the raw surface, not the idiomatic one
    foreach (var member in type.GetMembers(Declared))
    {
        switch (member)
        {
            case MethodBase mb when mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly:
                if (mb is MethodInfo mi) Expose(mi.ReturnType);
                foreach (var p in mb.GetParameters()) Expose(p.ParameterType);
                break;
            case PropertyInfo p when (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false):
                Expose(p.PropertyType);
                break;
            case FieldInfo f when f.IsPublic || f.IsFamily:
                Expose(f.FieldType);
                break;
            default:
                break;
        }
    }
}

var inSignatures = new HashSet<Type>();
void Reach(Type? t)
{
    if (t is null) return;
    if (t.HasElementType) { Reach(t.GetElementType()); return; }
    if (t.IsFunctionPointer)
    {
        Reach(t.GetFunctionPointerReturnType());
        foreach (var p in t.GetFunctionPointerParameterTypes()) Reach(p);
        return;
    }
    if (!IsNativeStruct(t) || !inSignatures.Add(t)) return;
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) Reach(f.FieldType);
}
foreach (var m in slotOf.Keys.Cast<MethodInfo>().Concat(exportOf.Keys.Cast<MethodInfo>()))
{
    if (slotOf.TryGetValue(m, out var slot) && options.HostImplemented.Contains(Declaring(slot.Interface, slot.Index))) continue;
    Reach(m.ReturnType);
    foreach (var p in m.GetParameters()) Reach(p.ParameterType);
}

var structs = allTypes.Where(IsNativeStruct).OrderBy(NativeName, StringComparer.Ordinal).Select(t => new StructReport(
    NativeName(t),
    exposed.Contains(t) ? "exposed" : inSignatures.Contains(t) ? "built" : "layout",
    [.. t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Select(f => new FieldReport(f.Name, exposed.Contains(t) || !inSignatures.Contains(t) || accessed.Contains($"{NativeName(t)}.{f.Name}")))]))
    .Where(s => s.Fields.Count > 0).ToList();

// ------------------------------------------------------------------------------- report
var interfaceReports = interfaces.Keys.Order(StringComparer.Ordinal).Select(name => new InterfaceReport(
    name,
    bases[name],
    options.HostImplemented.Contains(name),
    [.. interfaces[name].Where(s => Declaring(name, s.Key) == name)
        .Select(s => new SlotReport(s.Key, s.Value, Distinct(callers.GetValueOrDefault($"{name}.{s.Value}"))))]))
    .ToList();
var exportReports = exports.Select(e => new ExportReport(e, Distinct(callers.GetValueOrDefault(e)))).ToList();

var counted = interfaceReports.Where(i => !i.HostImplemented).ToList();
var slotTotal = counted.Sum(i => i.Slots.Count);
var slotBound = counted.Sum(i => i.Slots.Count(s => s.Callers.Count > 0));
var exportBound = exportReports.Count(e => e.Callers.Count > 0);
var fieldTotal = structs.Sum(s => s.Fields.Count);
var fieldBound = structs.Sum(s => s.Fields.Count(f => f.Bound));
var summary = new Summary(
    counted.Count, slotTotal, slotBound,
    exportReports.Count, exportBound,
    slotTotal + exportReports.Count, slotBound + exportBound,
    structs.Count, structs.Count(s => s.Kind == "exposed"), structs.Count(s => s.Kind == "built"), structs.Count(s => s.Kind == "layout"),
    fieldTotal, fieldBound,
    [.. interfaceReports.Where(i => i.HostImplemented).Select(i => i.Name)]);

var report = new Report(summary, interfaceReports, exportReports, structs);
File.WriteAllText(options.Out, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

var c = CultureInfo.InvariantCulture;
Console.WriteLine(string.Create(c, $"{counted.Count} interfaces, {slotTotal} vtable slots: {slotBound} bound; {exportReports.Count} flat exports: {exportBound} bound; functions {summary.FunctionsBound} of {summary.Functions}"));
if (summary.HostImplemented.Count > 0)
    Console.WriteLine($"host-implemented interfaces, not counted: {string.Join(", ", summary.HostImplemented)}");
Console.WriteLine(string.Create(c, $"{structs.Count} native structs ({summary.StructsExposed} exposed, {summary.StructsBuilt} built by the binding, {summary.StructsLayout} data layouts), {fieldTotal} fields: {fieldBound} bound"));
foreach (var p in pdbProviders) p.Dispose();
return 0;

// ===============================================================================================

// The slot index of a generated vtable method: the constant between `ldfld lpVtbl` and the
// `ldind.i` that reads the entry (none: 0; a bare `sizeof`: 1), when an indirect call follows.
int? SlotIndex(MethodBase m)
{
    var il = m.GetMethodBody()?.GetILAsByteArray();
    if (il is null) return null;
    int? index = null;
    var state = 0; // 0: before lpVtbl, 1: after it, reading the index, 2: entry read
    var sawSizeof = false;
    var calls = false;
    foreach (var ins in Instructions(il))
    {
        switch (state)
        {
            case 0 when ins.Op.OperandType == OperandType.InlineField:
                var f = Resolve(() => m.Module.ResolveField(ins.Token, GenericArgs(m.DeclaringType), GenericArgs(m)));
                if (f?.Name == options.VtableField && f.DeclaringType is { } d && InGenerated(d)) state = 1;
                break;
            case 1:
                if (ins.Op == OpCodes.Ldind_I) { state = 2; index ??= sawSizeof ? 1 : 0; }
                else if (ins.Op == OpCodes.Sizeof) sawSizeof = true;
                else if (ins.Constant is { } k) index = k;
                break;
            case 2 when ins.Op == OpCodes.Calli:
                calls = true;
                break;
            default:
                break;
        }
    }
    return calls ? index : null;
}

// What a method's IL reaches. With `sites`, each Slang function reached (directly or through a
// generated helper) is recorded with the IL offset of the instruction that reached it.
Reach Scan(MethodBase m, List<(string Key, int Offset, string? Via)>? sites)
{
    var reach = new Reach();
    var il = m.GetMethodBody()!.GetILAsByteArray()!;
    var typeArgs = GenericArgs(m.DeclaringType);
    var methodArgs = GenericArgs(m);
    var pendingVtable = (Type?)null; // binding code reading lpVtbl itself: the next index is a slot
    int? pendingIndex = null;
    var pendingOffset = 0;
    var pendingSizeof = false;
    foreach (var ins in Instructions(il))
    {
        if (pendingVtable is not null)
        {
            if (ins.Op == OpCodes.Ldind_I)
            {
                var index = pendingIndex ?? (pendingSizeof ? 1 : 0);
                var iface = NativeName(pendingVtable);
                if (interfaces.TryGetValue(iface, out var slots) && slots.ContainsKey(index))
                {
                    var declaring = Declaring(iface, index);
                    var key = $"{declaring}.{interfaces[declaring][index]}";
                    reach.Functions.Add(key);
                    sites?.Add((key, pendingOffset, iface == declaring ? null : iface));
                }
                pendingVtable = null;
            }
            else if (ins.Op == OpCodes.Sizeof) pendingSizeof = true;
            else if (ins.Constant is { } k) pendingIndex = k;
            else if (ins.Op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Call or FlowControl.Return) pendingVtable = null;
        }

        switch (ins.Op.OperandType)
        {
            case OperandType.InlineField:
                var field = Resolve(() => m.Module.ResolveField(ins.Token, typeArgs, methodArgs));
                if (field is null || field.DeclaringType is not { } owner || !InGenerated(owner)) break;
                if (sites is not null && field.Name == options.VtableField && vtableStructs.Contains(owner))
                {
                    pendingVtable = owner; pendingIndex = null; pendingSizeof = false; pendingOffset = ins.Offset;
                }
                if (IsStructField(owner)) reach.Fields.Add($"{NativeName(owner)}.{field.Name}");
                break;
            case OperandType.InlineMethod:
                var target = Resolve(() => m.Module.ResolveMethod(ins.Token, typeArgs, methodArgs));
                if (target is null) break;
                if (target is MethodInfo { IsGenericMethod: true } g && !g.IsGenericMethodDefinition) target = g.GetGenericMethodDefinition();
                reach.Calls.Add(target);
                if (slotOf.TryGetValue(target, out var slot))
                {
                    var declaring = Declaring(slot.Interface, slot.Index);
                    var key = $"{declaring}.{interfaces[declaring][slot.Index]}";
                    reach.Functions.Add(key);
                    sites?.Add((key, ins.Offset, declaring == slot.Interface ? null : slot.Interface));
                }
                else if (exportOf.TryGetValue(target, out var export))
                {
                    reach.Functions.Add(export);
                    sites?.Add((export, ins.Offset, null));
                }
                else if (sites is not null && closure.TryGetValue(target, out var helper))
                {
                    reach.Fields.UnionWith(helper.Fields);
                    foreach (var key in helper.Functions)
                    {
                        reach.Functions.Add(key);
                        sites.Add((key, ins.Offset, $"{NativeName(target.DeclaringType!)}.{target.Name}"));
                    }
                }
                break;
            default:
                break;
        }
    }
    return reach;
}

bool IsStructField(Type owner) => owner.IsValueType && !owner.IsEnum && !vtableStructs.Contains(owner);

static Type[]? GenericArgs(MemberInfo? member) => member switch
{
    Type { IsGenericType: true } t => t.GetGenericArguments(),
    MethodBase { IsGenericMethod: true } mb => mb.GetGenericArguments(),
    _ => null,
};

static T? Resolve<T>(Func<T?> resolve) where T : class
{
    try { return resolve(); }
    catch (ArgumentException) { return null; }
    catch (BadImageFormatException) { return null; }
    catch (FileNotFoundException) { return null; }
    catch (TypeLoadException) { return null; }
}

IEnumerable<Instruction> Instructions(byte[] il)
{
    var offset = 0;
    while (offset < il.Length)
    {
        var start = offset;
        ushort code = il[offset++];
        if (code == 0xFE) code = (ushort)(0xFE00 | il[offset++]);
        var op = opcodes[code];
        var token = 0;
        int? constant = op.Value switch
        {
            >= 0x16 and <= 0x1E => op.Value - 0x16, // ldc.i4.0 .. ldc.i4.8
            0x15 => -1,                             // ldc.i4.m1
            _ => null,
        };
        switch (op.OperandType)
        {
            case OperandType.InlineSwitch:
                var n = BitConverter.ToInt32(il, offset);
                offset += 4 + (4 * n);
                break;
            case OperandType.ShortInlineI when op == OpCodes.Ldc_I4_S:
                constant = (sbyte)il[offset];
                offset += 1;
                break;
            case OperandType.InlineI when op == OpCodes.Ldc_I4:
                constant = BitConverter.ToInt32(il, offset);
                offset += 4;
                break;
            case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType or OperandType.InlineSig or OperandType.InlineString:
                token = BitConverter.ToInt32(il, offset);
                offset += 4;
                break;
            default:
                offset += OperandSize(op.OperandType);
                break;
        }
        yield return new Instruction(start, op, token, constant);
    }
}

static int OperandSize(OperandType type) => type switch
{
    OperandType.InlineNone => 0,
    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
    OperandType.InlineVar => 2,
    OperandType.InlineI8 or OperandType.InlineR => 8,
    _ => 4,
};

static IEnumerable<Type> LoadableTypes(Assembly a)
{
    try { return a.GetTypes(); }
    catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
}

// The first non-hidden sequence point's document of a method, from its PDB
string? Document(MethodBase m)
{
    if (!pdbs.TryGetValue(m.Module, out var pdb) || pdb is null) return null;
    var debug = pdb.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(m.MetadataToken).ToDebugInformationHandle());
    foreach (var point in debug.GetSequencePoints())
        if (!point.IsHidden) return pdb.GetString(pdb.GetDocument(point.Document).Name);
    return debug.Document.IsNil ? null : pdb.GetString(pdb.GetDocument(debug.Document).Name);
}

static bool IsUnder(string document, string root, string relative)
{
    var normal = document.Replace('\\', '/');
    var prefix = relative.Replace('\\', '/').TrimEnd('/') + "/";
    if (normal.StartsWith("/_/", StringComparison.Ordinal)) return normal[3..].StartsWith(prefix, StringComparison.OrdinalIgnoreCase); // deterministic source paths
    var full = Path.GetFullPath(Path.Combine(root, relative)).Replace('\\', '/').TrimEnd('/') + "/";
    return normal.StartsWith(full, StringComparison.OrdinalIgnoreCase);
}

// The user-facing method a site belongs to (lambdas and local functions name their container),
// and the source line of the sequence point that covers the IL offset
Caller Site(MethodBase m, int offset, string? via)
{
    var methodName = m.Name;
    if (methodName.StartsWith('<') && methodName.IndexOf('>', StringComparison.Ordinal) > 1) methodName = methodName[1..methodName.IndexOf('>', StringComparison.Ordinal)];
    var type = m.DeclaringType!;
    while (type.Name.StartsWith('<') && type.DeclaringType is { } outer) type = outer;
    var typeName = type.Name;
    for (var outer = type.DeclaringType; outer is not null; outer = outer.DeclaringType) typeName = $"{outer.Name}.{typeName}";

    string? file = null;
    var line = 0;
    if (pdbs.TryGetValue(m.Module, out var pdb) && pdb is not null)
    {
        var debug = pdb.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(m.MetadataToken).ToDebugInformationHandle());
        foreach (var point in debug.GetSequencePoints())
        {
            if (point.Offset > offset) break;
            if (point.IsHidden) continue;
            file = pdb.GetString(pdb.GetDocument(point.Document).Name);
            line = point.StartLine;
        }
    }
    return new Caller($"{typeName}.{methodName}", file, line, via);
}

static List<Caller> Distinct(List<Caller>? sites) =>
    sites is null ? [] : [.. sites.DistinctBy(s => (s.Method, s.File, s.Line, s.Via)).OrderBy(s => s.Method, StringComparer.Ordinal).ThenBy(s => s.Line)];

internal sealed class Reach
{
    public HashSet<string> Functions { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Fields { get; } = new(StringComparer.Ordinal);
    public HashSet<MethodBase> Calls { get; } = [];
}

internal readonly record struct Instruction(int Offset, OpCode Op, int Token, int? Constant);

// Via: the generated struct the call went through when it is not the declaring interface (IModule
// for an IComponentType slot), or the generated helper that made the call.
internal sealed record Caller(string Method, string? File, int Line, string? Via);
internal sealed record SlotReport(int Index, string Name, List<Caller> Callers);
internal sealed record InterfaceReport(string Name, string? Base, bool HostImplemented, List<SlotReport> Slots);
internal sealed record ExportReport(string Name, List<Caller> Callers);
internal sealed record FieldReport(string Name, bool Bound);
internal sealed record StructReport(string Name, string Kind, List<FieldReport> Fields);
internal sealed record Summary(
    int Interfaces, int Slots, int SlotsBound, int Exports, int ExportsBound, int Functions, int FunctionsBound,
    int Structs, int StructsExposed, int StructsBuilt, int StructsLayout, int Fields, int FieldsBound, List<string> HostImplemented);
internal sealed record Report(Summary Summary, List<InterfaceReport> Interfaces, List<ExportReport> Exports, List<StructReport> Structs);

internal sealed class Options
{
    public List<string> Assemblies { get; } = [];
    public string Out { get; private set; } = "";
    public string Namespace { get; private set; } = "SharpSlang.Native";
    public string Api { get; private set; } = "SlangApi";
    public string Generated { get; private set; } = "dotnet/Native";
    public string Root { get; private set; } = Directory.GetCurrentDirectory();
    public string VtableField { get; private set; } = "lpVtbl";
    public HashSet<string> HostImplemented { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Bases { get; } = new(StringComparer.Ordinal);

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            var value = args[i + 1];
            switch (args[i])
            {
                case "--assembly": o.Assemblies.Add(value); break;
                case "--out": o.Out = value; break;
                case "--namespace": o.Namespace = value; break;
                case "--api": o.Api = value; break;
                case "--generated": o.Generated = value; break;
                case "--root": o.Root = value; break;
                case "--vtable-field": o.VtableField = value; break;
                case "--host-implemented": o.HostImplemented.UnionWith(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--base":
                    foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                        if (parts.Length != 2) return null;
                        o.Bases[parts[0]] = parts[1];
                    }
                    break;
                default: return null;
            }
        }
        return args.Length % 2 == 0 && o.Assemblies.Count > 0 && o.Out.Length > 0 ? o : null;
    }
}
