// A game that loads baked shaders and never compiles one.
//
// Every call below is one the acceptance criterion names: read a package off disk, project its
// layout to NRI, read its specialization constants, and walk a uniform offset. If any of it needed
// the compiler this would not compile, and if the compiler still came along for the ride the
// payload would be in bin/ — which is the other half of the test, asserted from SharpSlang.Tests.
// The NRI projection adds a second payload that must not come along: NRI.dll, which
// SharpNRI.Descs does not carry and SharpNRI does.

using SharpSlang.Baking;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: SharpSlang.Pipeline.Consumer <package.pslb>");
    return 2;
}

var package = ShaderPackage.ReadFromFile(args[0]);
Console.WriteLine($"slang {package.SlangVersion}, {package.EntryPoints.Count} entry point(s)");

foreach (var entryPoint in package.EntryPoints)
{
    Console.WriteLine(
        $"  {entryPoint.Name,-16} {entryPoint.Stage,-24} pName={entryPoint.ModuleEntryPointName,-14} " +
        $"{entryPoint.Code.Length,6} bytes  {entryPoint.SpirvVersion.ToDisplayString()}" +
        (entryPoint.WorkgroupSize is { } w ? $"  workgroup {w}" : ""));
}

// The snapshot read for NRI: a SharpNRI pipeline layout desc, built with no device and no NRI
// binary in this process. A renderer hands nri.Layout to NriDevice.CreatePipelineLayout and binds
// each descriptor by the set and range index the projection reports. The cooker stored it in the
// package at bake time, so the game reads it back rather than projecting (and projects only a
// package baked without it).
var stored = package.ReadNriProjection();
var nri = stored ?? package.Layout.ToNri(new NriProjectionOptions { UnboundedArrayCap = 64 });
Console.WriteLine(stored is null ? "  NRI: projected at load (the package stores none)" : "  NRI: read back from the package (stored at bake time)");
Console.WriteLine($"  NRI: {nri.Layout.DescriptorSets.Length} descriptor set(s), " +
                  $"{nri.RootConstants.Count} root constant block(s), " +
                  $"{nri.VertexAttributes.Count} vertex attribute(s), " +
                  $"shader stages {nri.Layout.ShaderStages}");

foreach (var set in nri.DescriptorSets)
{
    foreach (var range in set.Ranges)
    {
        Console.WriteLine($"    set {set.SetIndex} (space {set.RegisterSpace}) range {range.RangeIndex} " +
                          $"{range.Desc.descriptorType} x{range.Desc.descriptorNum} '{range.Name}'");
    }
}

// The specialization constants the shader declares, with their ids and defaults. NRI has none, so
// a renderer on NRI specializes at link time; the snapshot still says what there is.
Console.WriteLine($"  specialization: {package.Layout.SpecializationConstants.Count} constant(s)");
foreach (var c in package.Layout.SpecializationConstants)
{
    Console.WriteLine($"    {c.Name} id {c.ConstantId} default " +
                      (c.IsFloatingPoint ? $"{c.DefaultFloat}" : $"{c.DefaultInt}"));
}

// And the byte layout of a uniform buffer, which is what a material system writes into.
foreach (var parameter in package.Layout.Parameters)
{
    if (parameter.Type is not ContainerLayout { ElementType: StructLayout block }) continue;
    foreach (var field in block.Fields)
    {
        Console.WriteLine($"    {parameter.Name}.{field.Name} @ {field.UniformOffset}" +
                          (field.Type is MatrixLayout m ? $" stride {m.Stride}" : ""));
    }
}

// ASCII on purpose: this line is asserted from a test that reads redirected stdout, and a
// non-ASCII character survives or does not depending on the console code page.
Console.WriteLine("OK - read and projected with no compiler in sight.");
return 0;
