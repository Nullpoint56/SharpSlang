// Slang's bytecode VM: Slang code compiled for SLANG_HOST_VM and run on the CPU. The oracle is the
// program's own output: what its printf prints and what its arithmetic returns.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class ByteCodeRunnerTests(ITestOutputHelper output)
{
    private const string Program = """
        void main()
        {
            int total = 0;
            for (int i = 1; i <= 6; i++)
                total += i * 7;
            printf("total %d\n", total);
        }
        """;

    private static byte[] Compile(string source)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Format = SlangCompileTarget.SLANG_HOST_VM }],
        });
        using var module = session.LoadModuleFromSource("vm", "vm.slang", source);
        using var entry = module.FindEntryPoint("main", SlangStage.SLANG_STAGE_DISPATCH);
        using var program = session.Link(module, entry);
        using var code = program.GetTargetCode(0);
        return code.ToArray();
    }

    [NativeFact]
    public void A_program_runs_and_prints_through_the_callback()
    {
        var byteCode = Compile(Program);

        using var runner = SlangByteCodeRunner.Create();
        var printed = new List<string>();
        runner.SetPrintCallback(printed.Add);
        runner.LoadModule(byteCode);

        var main = runner.FindFunctionByName("main");
        Assert.True(main >= 0);
        Assert.Equal(-1, runner.FindFunctionByName("noSuchFunction"));

        var info = runner.GetFunctionInfo(main);
        output.WriteLine($"main: {info.parameterCount} parameters, {info.returnValueSize}-byte return");
        Assert.Equal(0u, info.parameterCount);

        runner.SelectFunction(main);
        runner.Execute();

        // 7 * (1 + ... + 6)
        Assert.Equal("total 147\n", string.Concat(printed));
        Assert.NotEqual(0, runner.CurrentWorkingSet);
    }

    [NativeFact]
    public void Bytecode_disassembles_to_text_naming_its_function()
    {
        var text = SlangByteCodeRunner.DisassembleByteCode(Compile(Program));
        output.WriteLine(text.Length > 600 ? text[..600] : text);
        Assert.Contains("main", text, StringComparison.Ordinal);

        Assert.Throws<SlangException>(() => SlangByteCodeRunner.DisassembleByteCode(new byte[64]));
    }

    [NativeFact]
    public void Bytes_that_are_not_bytecode_are_refused_with_the_runners_error()
    {
        using var runner = SlangByteCodeRunner.Create();
        var e = Assert.Throws<SlangException>(() => runner.LoadModule(new byte[64]));
        output.WriteLine(e.Message);
        Assert.Throws<SlangException>(() => runner.SelectFunction(0));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void CountCall(IByteCodeRunner* runner, VMExecInstHeader* inst, void* userData) =>
        Interlocked.Increment(ref *(int*)userData);

    [NativeFact]
    public unsafe void An_external_call_is_registered_with_its_user_data()
    {
        using var runner = SlangByteCodeRunner.Create();
        runner.RegisterExtCall("sharpslangCount", &CountCall);

        var counter = (int*)NativeMemory.AllocZeroed(sizeof(int));
        try
        {
            runner.SetExtInstHandlerUserData((nint)counter);
        }
        finally
        {
            runner.SetExtInstHandlerUserData(0);
            NativeMemory.Free(counter);
        }

        Assert.Throws<ArgumentNullException>(() => runner.RegisterExtCall("nothing", null));
    }
}
