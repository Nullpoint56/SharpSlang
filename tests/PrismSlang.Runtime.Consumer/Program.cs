// A game that loads baked shaders and never compiles one.
//
// Every call below is one the engine team named in the acceptance criterion: read a package off
// disk, project its layout to Vulkan and to NRI, pack specialization values, check the target
// environment, and walk a uniform offset. If any of it needed the compiler this would not compile,
// and if the compiler still came along for the ride the payload would be in bin/ — which is the
// other half of the test, asserted from PrismSlang.Tests. The NRI projection adds a second
// payload that must not come along: NRI.dll, which PrismNRI.Descs does not carry and PrismNRI does.

using PrismSlang.Baking;
using PrismSlang.Native;
using PrismSlang.NRI;
using PrismSlang.Reflection;
using PrismSlang.Spirv;
using PrismSlang.Vulkan;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: PrismSlang.Runtime.Consumer <package.pslb>");
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

    // The device says which SPIR-V versions it will take; the package says what it holds.
    VulkanEnvironment.Require(VulkanApiVersion.V1_4, entryPoint.SpirvVersion, entryPoint.Name);
}

var layout = package.Layout.ToVulkan();
Console.WriteLine($"  {layout.DescriptorSets.Count} descriptor set(s), " +
                  $"{layout.PushConstants.Count} push range(s), " +
                  $"{layout.VertexInputs.Count} vertex input(s), " +
                  $"{layout.Stages.Count} stage(s)");

foreach (var set in layout.DescriptorSets)
    foreach (var binding in set.Bindings)
        Console.WriteLine($"    set {set.Set} binding {binding.Binding} {binding.DescriptorType} '{binding.Name}'");

// The same snapshot read for NRI: a PrismNRI pipeline layout desc, built with no device and no
// NRI binary in this process. A renderer hands nri.Layout to NriDevice.CreatePipelineLayout and
// binds each descriptor by the set and range index the projection reports.
var nri = package.Layout.ToNri(new NriProjectionOptions { UnboundedArrayCap = 64 });
Console.WriteLine($"  NRI: {nri.Layout.DescriptorSets.Length} descriptor set(s), " +
                  $"{nri.RootConstants.Count} root constant block(s), " +
                  $"{nri.VertexAttributes.Count} vertex attribute(s), " +
                  $"shader stages {nri.Layout.ShaderStages}");

foreach (var set in nri.DescriptorSets)
    foreach (var range in set.Ranges)
        Console.WriteLine($"    set {set.SetIndex} (space {set.RegisterSpace}) range {range.RangeIndex} " +
                          $"{range.Desc.descriptorType} x{range.Desc.descriptorNum} '{range.Name}'");

// Specialization values, packed the way VkSpecializationInfo wants them.
//
// The type has to match the constant's: a ternary that unifies an int default and a float default
// hands Pack a double for an int constant, and Pack refuses it by design rather than truncating.
// Written as statements on purpose: a conditional expression unifies its two branches to one
// numeric type before boxing, so `c.IsFloatingPoint ? c.DefaultFloat : c.DefaultInt` hands Pack a
// double for an integer constant no matter which side is taken.
static object DefaultOf(SpecializationConstant c)
{
    if (c.IsFloatingPoint) return c.DefaultFloat ?? 0d;
    return c.DefaultInt ?? 0L;
}

var values = package.Layout.SpecializationConstants.ToDictionary(c => c.Name, DefaultOf);

var specialization = package.Layout.Pack(values, includeDefaults: true);
Console.WriteLine($"  specialization: {specialization.MapEntries.Count} entries, " +
                  $"{specialization.Data.Length} bytes");

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
Console.WriteLine("OK - read, projected and packed with no compiler in sight.");
return 0;
