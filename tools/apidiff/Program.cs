// API changes between two sets of packages (ApiDiff.csproj): every public or protected type and
// member of the lib/ assemblies of each package, as one line each with its signature (and a
// constant's or enum member's value), compared by identity. A changed value, return type, field
// type or base list is "changed"; an overload with other parameters is a removal plus an addition.
// What changed is stated, not why. A package on one side only is new or removed.
//
//   dotnet run --project tools/apidiff -- <old nupkg dir> <new nupkg dir> <out.md> <baseline label>
//       [--package Old=New ...]   a package renamed or split: compare Old's surface with New's
//       [--rename Old=New ...]    a namespace or type-name prefix renamed: read Old as New on the
//                                 old side, so a rename is not every member removed and re-added
//       [--reference <dir> ...]   more assemblies to resolve signatures against
//
// Assemblies a package's signatures name from its dependencies (SharpNRI.Descs for the NRI
// projection) are resolved from the other packages of the same side, then from the NuGet global
// packages folder at the version the nuspec names, then from --reference.

using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var positional = new List<string>();
var packageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var renames = new List<(string Old, string New)>();
var references = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--package" when i + 1 < args.Length:
            var (po, pn) = Pair(args[++i]);
            packageMap[po] = pn;
            break;
        case "--rename" when i + 1 < args.Length:
            renames.Add(Pair(args[++i]));
            break;
        case "--reference" when i + 1 < args.Length:
            references.Add(args[++i]);
            break;
        default:
            positional.Add(args[i]);
            break;
    }
}
if (positional.Count != 4)
{
    Console.Error.WriteLine("usage: ApiDiff <old nupkg dir> <new nupkg dir> <out.md> <baseline label> [--package Old=New] [--rename Old=New] [--reference <dir>]");
    return 2;
}
var (oldDir, newDir, outFile, baseline) = (positional[0], positional[1], positional[2], positional[3]);
var work = Directory.CreateTempSubdirectory("apidiff");
try
{
    var oldSide = Extract(oldDir, Path.Combine(work.FullName, "old"));
    var newSide = Extract(newDir, Path.Combine(work.FullName, "new"));
    var md = new StringBuilder();
    md.AppendLine("## API changes");
    md.AppendLine(CultureInfo.InvariantCulture, $"Public surface against {baseline}, per package.");
    var any = false;
    var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (id, newPackage) in newSide.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        if (newPackage.Assemblies.Count == 0) continue;
        var oldId = packageMap.FirstOrDefault(m => string.Equals(m.Value, id, StringComparison.OrdinalIgnoreCase)).Key ?? id;
        if (!oldSide.TryGetValue(oldId, out var oldPackage))
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- {id} {newPackage.Version}: new package");
            any = true;
            continue;
        }
        matched.Add(oldId);
        var before = Renamed(Surface(oldPackage, oldSide, references), renames);
        var after = Surface(newPackage, newSide, references);
        var added = after.Keys.Except(before.Keys).Order(StringComparer.Ordinal).Select(k => $"`{after[k]}`").ToList();
        var removed = before.Keys.Except(after.Keys).Order(StringComparer.Ordinal).Select(k => $"`{before[k]}`").ToList();
        var changed = after.Keys.Intersect(before.Keys).Where(k => after[k] != before[k]).Order(StringComparer.Ordinal)
            .Select(k => $"`{before[k]}` → `{after[k]}`").ToList();
        var name = oldId == id ? id : $"{oldId} → {id}";
        var version = oldPackage.Version == newPackage.Version ? newPackage.Version : $"{oldPackage.Version} → {newPackage.Version}";
        if (added.Count + removed.Count + changed.Count == 0)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- {name} {version}: unchanged");
            if (oldId != id) any = true;
            continue;
        }
        any = true;
        md.AppendLine(CultureInfo.InvariantCulture, $"- {name} {version}: {removed.Count} removed, {changed.Count} changed, {added.Count} added");
        List("Removed", removed);
        List("Changed", changed);
        List("Added", added);
    }
    foreach (var (id, oldPackage) in oldSide.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        if (oldPackage.Assemblies.Count == 0 || matched.Contains(id) || newSide.ContainsKey(id)) continue;
        md.AppendLine(CultureInfo.InvariantCulture, $"- {id} {oldPackage.Version}: removed package");
        any = true;
    }
    if (!any) md.AppendLine("- No public API changed");
    md.AppendLine();
    File.WriteAllText(outFile, md.ToString());
    Console.Write(md.ToString());
    return 0;

    void List(string heading, List<string> items)
    {
        if (items.Count == 0) return;
        md.AppendLine(CultureInfo.InvariantCulture, $"  <details><summary>{heading} ({items.Count})</summary>");
        md.AppendLine();
        foreach (var i in items) md.AppendLine(CultureInfo.InvariantCulture, $"  - {i}");
        md.AppendLine();
        md.AppendLine("  </details>");
    }
}
finally
{
    work.Delete(recursive: true);
}

static (string, string) Pair(string value)
{
    var parts = value.Split('=', 2, StringSplitOptions.TrimEntries);
    return parts.Length == 2 ? (parts[0], parts[1]) : throw new ArgumentException($"'{value}' is not Old=New");
}

// The old side read with the renames applied: an identifier prefix (a namespace, or a type name)
// replaced where it starts a dotted name, in keys and display lines alike
static Dictionary<string, string> Renamed(Dictionary<string, string> surface, List<(string Old, string New)> renames)
{
    if (renames.Count == 0) return surface;
    string Apply(string s)
    {
        foreach (var (o, n) in renames) s = Regex.Replace(s, $@"(?<![\w.]){Regex.Escape(o)}(?=[.\s,(<>\[\]*]|$)", n);
        return s;
    }
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (k, v) in surface) result[Apply(k)] = Apply(v);
    return result;
}

// Every package in the directory: id, version, dependencies and its lib/ assemblies, extracted.
static Dictionary<string, Package> Extract(string dir, string into)
{
    var packages = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
    foreach (var nupkg in Directory.EnumerateFiles(dir, "*.nupkg"))
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var nuspec = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/', StringComparison.Ordinal));
        using var reader = new StreamReader(nuspec.Open());
        var xml = XDocument.Parse(reader.ReadToEnd());
        var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        var id = metadata.Elements().Single(e => e.Name.LocalName == "id").Value;
        var version = metadata.Elements().Single(e => e.Name.LocalName == "version").Value;
        var dependencies = metadata.Descendants().Where(e => e.Name.LocalName == "dependency")
            .Select(e => (Id: (string?)e.Attribute("id") ?? "", Version: ((string?)e.Attribute("version") ?? "").Trim('[', ']', '(', ')').Split(',')[0].Trim()))
            .Where(d => d.Id.Length > 0).Distinct().ToList();
        var target = Path.Combine(into, id);
        Directory.CreateDirectory(target);
        var assemblies = new List<string>();
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(target, e.Name);
            e.ExtractToFile(path, overwrite: true);
            assemblies.Add(path);
        }
        packages[id] = new Package(id, version, assemblies, dependencies);
    }
    return packages;
}

// The assemblies a package's dependencies bring, from the NuGet global packages folder
static IEnumerable<string> DependencyAssemblies(Package package, Dictionary<string, Package> side)
{
    var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    foreach (var (id, version) in package.Dependencies)
    {
        if (side.ContainsKey(id)) continue;
        var lib = Path.Combine(cache, id.ToLowerInvariant(), version.ToLowerInvariant(), "lib");
        if (!Directory.Exists(lib)) continue;
        var tfm = Directory.GetDirectories(lib).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
        if (tfm is null) continue;
        foreach (var dll in Directory.EnumerateFiles(tfm, "*.dll")) yield return dll;
    }
}

// identity -> display line, for every public or protected type and member of the package's assemblies
static Dictionary<string, string> Surface(Package package, Dictionary<string, Package> side, List<string> references)
{
    var paths = Directory.GetFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
        .Concat(side.Values.SelectMany(p => p.Assemblies))
        .Concat(side.Values.SelectMany(p => DependencyAssemblies(p, side)))
        .Concat(references.SelectMany(r => Directory.EnumerateFiles(r, "*.dll")))
        .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(g => g.First());
    using var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
    var surface = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in package.Assemblies)
    {
        foreach (var type in context.LoadFromAssemblyPath(path).GetTypes().Where(Visible))
        {
            var name = Name(type);
            surface[$"T {name}"] = TypeLine(type, name);
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var f in type.GetFields(all).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
            {
                if (type.IsEnum && f.IsSpecialName) continue;   // value__
                var key = $"F {name}.{f.Name}";
                surface[key] = type.IsEnum ? $"{name}.{f.Name} = {f.GetRawConstantValue()}"
                    : f.IsLiteral ? $"const {Name(f.FieldType)} {name}.{f.Name} = {Literal(f.GetRawConstantValue())}"
                    : $"{(f.IsStatic ? "static " : "")}{(f.IsInitOnly ? "readonly " : "")}{Name(f.FieldType)} {name}.{f.Name}";
            }
            foreach (var p in type.GetProperties(all))
            {
                var get = p.GetMethod is { } g && (g.IsPublic || g.IsFamily || g.IsFamilyOrAssembly);
                var set = p.SetMethod is { } s && (s.IsPublic || s.IsFamily || s.IsFamilyOrAssembly);
                if (!get && !set) continue;
                var index = p.GetIndexParameters();
                var key = $"P {name}.{p.Name}({string.Join(",", index.Select(i => Name(i.ParameterType)))})";
                var isStatic = (p.GetMethod ?? p.SetMethod)!.IsStatic;
                surface[key] = $"{(isStatic ? "static " : "")}{Name(p.PropertyType)} {name}.{p.Name}{(index.Length > 0 ? $"[{Parameters(index)}]" : "")} {{ {(get ? "get; " : "")}{(set ? (IsInit(p.SetMethod!) ? "init; " : "set; ") : "")}}}";
            }
            foreach (var m in type.GetMethods(all).Where(m => !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal)).Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
            {
                var generic = m.IsGenericMethodDefinition ? $"<{string.Join(",", m.GetGenericArguments().Select(a => a.Name))}>" : "";
                var key = $"M {name}.{m.Name}{generic}({string.Join(",", m.GetParameters().Select(ParameterKey))})";
                surface[key] = $"{(m.IsStatic ? "static " : "")}{(m.IsAbstract && !type.IsInterface ? "abstract " : "")}{Name(m.ReturnType)} {name}.{m.Name}{generic}({Parameters(m.GetParameters())})";
            }
            foreach (var c in type.GetConstructors(all).Where(c => !c.IsStatic && (c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly)))
            {
                var key = $"C {name}({string.Join(",", c.GetParameters().Select(ParameterKey))})";
                surface[key] = $"{name}({Parameters(c.GetParameters())})";
            }
            foreach (var e in type.GetEvents(all).Where(e => e.AddMethod is { } a && (a.IsPublic || a.IsFamily)))
                surface[$"E {name}.{e.Name}"] = $"event {Name(e.EventHandlerType!)} {name}.{e.Name}";
        }
    }
    return surface;
}

static bool Visible(Type t) => t.IsPublic || (t.IsNested && (t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem) && Visible(t.DeclaringType!));

static string TypeLine(Type t, string name)
{
    if (t.IsEnum) return $"enum {name} : {Name(t.GetEnumUnderlyingType())}{(t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.FlagsAttribute") ? " [Flags]" : "")}";
    var kind = t.IsInterface ? "interface" : t.IsValueType ? (t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") ? "ref struct" : "struct")
        : t.BaseType?.FullName == "System.MulticastDelegate" ? "delegate"
        : t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
    var readOnly = t.IsValueType && t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute") ? "readonly " : "";
    var bases = new List<string>();
    if (t.BaseType is { } b && !t.IsValueType && b.FullName is not ("System.Object" or "System.MulticastDelegate")) bases.Add(Name(b));
    bases.AddRange(t.GetInterfaces().Where(i => i.IsPublic || i.IsNestedPublic).Select(Name).Order(StringComparer.Ordinal));
    return $"{readOnly}{kind} {name}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")}";
}

static bool IsInit(MethodInfo set) => set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

static string ParameterKey(ParameterInfo p) => (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + Name(p.ParameterType);

static string Parameters(ParameterInfo[] ps) => string.Join(", ", ps.Select(p =>
    $"{(p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : "")}{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + Literal(p.RawDefaultValue) : "")}"));

static string Literal(object? value) => value switch
{
    null => "null",
    string s => $"\"{s}\"",
    bool b => b ? "true" : "false",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => value.ToString() ?? "",
};

static string Name(Type t)
{
    if (t.IsByRef) return Name(t.GetElementType()!);
    if (t.IsPointer) return Name(t.GetElementType()!) + "*";
    if (t.IsArray) return Name(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
    if (t.IsGenericParameter) return t.Name;
    if (t.IsFunctionPointer)
        return $"delegate* unmanaged<{string.Join(", ", t.GetFunctionPointerParameterTypes().Select(Name).Append(Name(t.GetFunctionPointerReturnType())))}>";
    var simple = t.FullName switch
    {
        "System.Void" => "void",
        "System.Boolean" => "bool",
        "System.Byte" => "byte",
        "System.SByte" => "sbyte",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        "System.Int32" => "int",
        "System.UInt32" => "uint",
        "System.Int64" => "long",
        "System.UInt64" => "ulong",
        "System.IntPtr" => "nint",
        "System.UIntPtr" => "nuint",
        "System.Single" => "float",
        "System.Double" => "double",
        "System.String" => "string",
        "System.Object" => "object",
        "System.Char" => "char",
        _ => null,
    };
    if (simple is not null) return simple;
    var name = t.IsNested ? Name(t.DeclaringType!) + "." + t.Name : (t.Namespace is { Length: > 0 } ns ? ns + "." : "") + t.Name;
    var tick = name.IndexOf('`', StringComparison.Ordinal);
    if (tick >= 0) name = name[..tick];
    if (t.IsGenericType) name += "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
    return name;
}

internal sealed record Package(string Id, string Version, List<string> Assemblies, List<(string Id, string Version)> Dependencies);
