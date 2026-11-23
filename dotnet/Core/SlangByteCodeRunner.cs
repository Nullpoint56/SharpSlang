// L2 — slang::IByteCodeRunner: Slang's interpreter for its own bytecode (the SLANG_HOST_VM target),
// which runs Slang code on the CPU without a GPU or a C++ compiler.
//
// Created by slang_createByteCodeRunner with no session behind it, so it owns no Slang parent. The
// print callback is a [UnmanagedCallersOnly] thunk with a GCHandle as its user data; the runner
// holds the handle until it is replaced or the runner is disposed, and no managed exception crosses
// back into Slang.

using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>
/// <c>slang::IByteCodeRunner</c>: runs code compiled for <c>SLANG_HOST_VM</c> on the CPU, for
/// tools that evaluate shader functions without a device.
/// </summary>
/// <remarks>
/// Compile with a <c>SLANG_HOST_VM</c> target, take <see cref="LinkedProgram.GetTargetCode"/>,
/// <see cref="LoadModule"/> it, select a function and <see cref="Execute"/>. Calls made from
/// <c>printf</c> in the code arrive at <see cref="SetPrintCallback"/>.
/// </remarks>
public sealed unsafe class SlangByteCodeRunner : SlangObject<IByteCodeRunner>
{
    private GCHandle _print;

    private SlangByteCodeRunner(IByteCodeRunner* owned) : base(owned, nameof(SlangByteCodeRunner)) { }

    /// <summary><c>slang_createByteCodeRunner</c>.</summary>
    /// <exception cref="SlangException">Slang could not create one.</exception>
    public static SlangByteCodeRunner Create() => NativeLibraryHint.Explained(() =>
    {
        var desc = Defaults.ByteCodeRunnerDesc();
        IByteCodeRunner* runner = null;
        var hr = SlangApi.slang_createByteCodeRunner(&desc, &runner);
        if (SlangResult.Failed(hr) || runner is null)
            throw new SlangException(hr, "slang_createByteCodeRunner", Diagnostics.NewInternalError());
        return new SlangByteCodeRunner(runner);
    });

    /// <summary><c>slang_disassembleByteCode</c>: bytecode as readable text.</summary>
    /// <exception cref="SlangException">The bytes are not Slang bytecode.</exception>
    public static string DisassembleByteCode(ReadOnlySpan<byte> byteCode)
    {
        if (byteCode.IsEmpty) throw new ArgumentException("no bytecode", nameof(byteCode));

        var bytes = byteCode.ToArray();
        return NativeLibraryHint.Explained(() =>
        {
            fixed (byte* p = bytes)
            {
                var blob = SlangApi.slang_createBlob(p, (nuint)bytes.Length);
                Diagnostics.ThrowIfNull(blob, "slang_createBlob", null);
                using var input = new SlangBlob(blob);

                ISlangBlob* text = null;
                var hr = SlangApi.slang_disassembleByteCode(input.Handle, &text);
                if (SlangResult.Failed(hr) || text is null)
                {
                    if (text is not null) text->release();
                    throw new SlangException(SlangResult.Failed(hr) ? hr : SlangResult.Fail, "slang_disassembleByteCode", Diagnostics.NewInternalError());
                }

                using var owned = new SlangBlob(text);
                return owned.AsText().TrimEnd('\0');
            }
        });
    }

    /// <summary><c>loadModule</c>: a bytecode module to run functions from.</summary>
    /// <exception cref="SlangException">Slang refused it; its error string is attached.</exception>
    public void LoadModule(ReadOnlySpan<byte> byteCode)
    {
        if (byteCode.IsEmpty) throw new ArgumentException("no bytecode", nameof(byteCode));

        fixed (byte* p = byteCode)
        {
            var blob = SlangApi.slang_createBlob(p, (nuint)byteCode.Length);
            Diagnostics.ThrowIfNull(blob, "slang_createBlob", null);
            using var owned = new SlangBlob(blob);
            Check(Ptr->loadModule(owned.Handle), "IByteCodeRunner::loadModule");
        }
    }

    /// <summary><c>findFunctionByName</c>: the function's index, or -1.</summary>
    public int FindFunctionByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using Utf8.Scoped n = new(name);
        return Ptr->findFunctionByName(n);
    }

    /// <summary><c>selectFunctionByIndex</c>: the function <see cref="Execute"/> runs.</summary>
    /// <exception cref="SlangException">No function at <paramref name="index"/>.</exception>
    public void SelectFunction(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Check(Ptr->selectFunctionByIndex((uint)index), $"IByteCodeRunner::selectFunctionByIndex({index})");
    }

    /// <summary><c>getFunctionInfo</c>: the function's parameter count and return value size.</summary>
    /// <exception cref="SlangException">No function at <paramref name="index"/>.</exception>
    public ByteCodeFuncInfo GetFunctionInfo(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ByteCodeFuncInfo info;
        Check(Ptr->getFunctionInfo((uint)index, &info), $"IByteCodeRunner::getFunctionInfo({index})");
        return info;
    }

    /// <summary><c>getCurrentWorkingSet</c>: the selected function's working memory, owned by the runner.</summary>
    public nint CurrentWorkingSet => (nint)Ptr->getCurrentWorkingSet();

    /// <summary><c>execute</c>: runs the selected function with <paramref name="arguments"/> as its argument data.</summary>
    /// <exception cref="SlangException">The run failed; the runner's error string is attached.</exception>
    public void Execute(ReadOnlySpan<byte> arguments = default)
    {
        fixed (byte* p = arguments)
        {
            Check(Ptr->execute(arguments.IsEmpty ? null : p, (nuint)arguments.Length), "IByteCodeRunner::execute");
        }
    }

    /// <summary><c>getErrorString</c>: the runner's last error, or null.</summary>
    public string? ErrorString
    {
        get
        {
            ISlangBlob* blob = null;
            Ptr->getErrorString(&blob);
            var text = Diagnostics.Consume(blob);
            return text?.TrimEnd('\0');
        }
    }

    /// <summary><c>getReturnValue</c>: a copy of the last executed function's return value.</summary>
    public byte[] GetReturnValue()
    {
        nuint size;
        var p = Ptr->getReturnValue(&size);
        return p is null || size == 0 ? [] : new ReadOnlySpan<byte>(p, checked((int)size)).ToArray();
    }

    /// <summary>
    /// <c>setExtInstHandlerUserData</c>: the user data every function registered with
    /// <see cref="RegisterExtCall"/> receives.
    /// </summary>
    public void SetExtInstHandlerUserData(nint userData) => Ptr->setExtInstHandlerUserData((void*)userData);

    /// <summary>
    /// <c>registerExtCall</c>: a native function the bytecode calls by <paramref name="name"/>. Write
    /// it as an <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c> static method, and
    /// let no exception escape it.
    /// </summary>
    /// <exception cref="SlangException">Slang refused the registration.</exception>
    public void RegisterExtCall(string name, delegate* unmanaged[Cdecl]<IByteCodeRunner*, VMExecInstHeader*, void*, void> function)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (function is null) throw new ArgumentNullException(nameof(function));

        using Utf8.Scoped n = new(name);
        Check(Ptr->registerExtCall(n, function), $"IByteCodeRunner::registerExtCall('{name}')");
    }

    /// <summary>
    /// <c>setPrintCallback</c>: where the code's <c>printf</c> output goes; null stops it. An
    /// exception the callback throws is dropped, since it cannot cross into Slang.
    /// </summary>
    /// <exception cref="SlangException">Slang refused the callback.</exception>
    public void SetPrintCallback(Action<string>? callback)
    {
        var previous = _print;
        if (callback is null)
        {
            Check(Ptr->setPrintCallback(null, null), "IByteCodeRunner::setPrintCallback");
            _print = default;
        }
        else
        {
            var handle = GCHandle.Alloc(callback);
            try
            {
                Check(Ptr->setPrintCallback(&Print, (void*)GCHandle.ToIntPtr(handle)), "IByteCodeRunner::setPrintCallback");
            }
            catch
            {
                handle.Free();
                throw;
            }
            _print = handle;
        }

        if (previous.IsAllocated) previous.Free();
    }

    // After the runner's release: Slang can no longer call the callback.
    private protected override void AfterRelease()
    {
        base.AfterRelease();
        if (_print.IsAllocated) _print.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void Print(sbyte* message, void* userData)
    {
        try
        {
            if (GCHandle.FromIntPtr((nint)userData).Target is Action<string> callback)
                callback(Utf8.Read(message) ?? "");
        }
        catch
        {
            // Nothing may unwind into Slang (AGENTS.md#layers).
        }
    }

    private void Check(int hr, string operation)
    {
        if (SlangResult.Succeeded(hr)) return;
        throw new SlangException(hr, operation, ErrorString ?? Diagnostics.NewInternalError());
    }
}
