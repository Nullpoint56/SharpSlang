// Which adapter the suite runs on. This test executable started with
// SHARPSLANG_TEST_CHILD=test-adapters prints, for each GPU backend, the adapter the NRI device
// tests create their devices on, or why the backend is unavailable here, and exits before xunit
// starts. tools/coverage.ps1 records it, so the coverage report names the GPU its figures were
// measured on rather than every GPU in the machine.
//
//   ADAPTER VK <the adapter's name>
//   UNAVAILABLE D3D12 <the reason the D3D12 tests skip>

using System.Runtime.CompilerServices;
using SharpNRI.Testing;

namespace SharpSlang.Tests;

internal static class TestAdapters
{
    private const string ChildVariable = "SHARPSLANG_TEST_CHILD";
    private const string ChildName = "test-adapters";

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "the child mode must run before xunit's entry point")]
    internal static void RunChildIfAsked()
    {
        if (Environment.GetEnvironmentVariable(ChildVariable) != ChildName) return;
        var code = 0;
        try
        {
            foreach (var api in NriTestDevice.GpuBackends)
            {
                if (NriNative.Unavailable(api) is { } reason) Console.WriteLine($"UNAVAILABLE {api} {reason.ReplaceLineEndings(" ")}");
                else Console.WriteLine($"ADAPTER {api} {NriTestDevice.AdapterFor(api)?.Name ?? "(none)"}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL " + e.ToString().ReplaceLineEndings(" / "));
            code = 1;
        }
        Console.Out.Flush();
        Environment.Exit(code);
    }
}
