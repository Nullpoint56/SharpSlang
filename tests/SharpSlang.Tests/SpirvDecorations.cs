// Ground truth for the reflection tests.
//
// Every defect in the descriptor and vertex-input projection so far has been the same shape: what
// SharpSlang reported and what the emitted module decorated disagreed, and nothing compared them.
// Reflection is a claim about the module; the decorations are what the driver reads.
//
// So this reads the answer out of the SPIR-V. It is not a general disassembler — it wants exactly
// the DescriptorSet, Binding and Location decorations, keyed to the OpName of what they decorate.

using System.Runtime.InteropServices;
using System.Text;

namespace SharpSlang.Tests;

/// <summary>What the emitted module says about one decorated variable.</summary>
/// <param name="Name">Its <c>OpName</c>, which Slang emits as the source-level name.</param>
/// <param name="Set">The <c>DescriptorSet</c> decoration, if it has one.</param>
/// <param name="Binding">The <c>Binding</c> decoration, if it has one.</param>
/// <param name="Location">The <c>Location</c> decoration, if it has one.</param>
/// <param name="StorageClass">Input, Output, UniformConstant, PushConstant, and so on.</param>
public sealed record SpirvVariable(
    string Name,
    uint? Set,
    uint? Binding,
    uint? Location,
    uint StorageClass)
{
    public const uint Input = 1;
    public const uint Output = 3;

    /// <summary>
    /// The last dotted segment of the name — Slang names an entry-point uniform
    /// <c>entryPointParams.extra</c> and a vertex struct field <c>v.position</c>, but reflection
    /// reports each as its declared leaf name.
    /// </summary>
    public string LeafName => Name[(Name.LastIndexOf('.') + 1)..];
}

/// <summary>How the module lays out one member of a struct.</summary>
/// <param name="Offset">Byte offset within the enclosing struct.</param>
/// <param name="MatrixStride">
/// Bytes between one row/column and the next, for a matrix member. Null for anything else.
/// </param>
/// <param name="IsRowMajor">
/// The module's own word for the storage order — which is the opposite of Slang's, because HLSL
/// counts rows where SPIR-V counts columns. Null for a non-matrix member.
/// </param>
public sealed record SpirvMember(ulong Offset, ulong? MatrixStride, bool? IsRowMajor);

/// <summary>Reads decorations out of an emitted module.</summary>
public static class SpirvDecorations
{
    private const uint OpName = 5;
    private const uint OpMemberName = 6;
    private const uint OpExecutionMode = 16;
    private const uint OpTypeImage = 25;
    private const uint OpDecorate = 71;
    private const uint OpMemberDecorate = 72;
    private const uint OpVariable = 59;
    private const uint OpExecutionModeId = 331;

    private const uint DecorationRowMajor = 4;
    private const uint DecorationColMajor = 5;
    private const uint DecorationArrayStride = 6;
    private const uint DecorationMatrixStride = 7;
    private const uint DecorationLocation = 30;
    private const uint DecorationBinding = 33;
    private const uint DecorationDescriptorSet = 34;
    private const uint DecorationOffset = 35;
    private const uint DecorationInputAttachmentIndex = 43;

    /// <summary>Every variable carrying a set/binding or location decoration, in module order.</summary>
    public static List<SpirvVariable> Read(ReadOnlySpan<byte> spirv)
    {
        var words = MemoryMarshal.Cast<byte, uint>(spirv);

        var names = new Dictionary<uint, string>();
        var storage = new Dictionary<uint, uint>();
        var decorations = new Dictionary<uint, Dictionary<uint, uint>>();
        var order = new List<uint>();

        var i = 5;
        while (i < words.Length)
        {
            var count = (int)(words[i] >> 16);
            var op = words[i] & 0xFFFF;
            if (count < 1 || i + count > words.Length) break;

            switch (op)
            {
                case OpName when count >= 3:
                    names[words[i + 1]] = ReadString(words.Slice(i + 2, count - 2));
                    break;

                case OpDecorate when count >= 4:
                    {
                        var kind = words[i + 2];
                        if (kind is DecorationLocation or DecorationBinding or DecorationDescriptorSet)
                        {
                            var target = words[i + 1];
                            if (!decorations.TryGetValue(target, out var d))
                            {
                                decorations[target] = d = [];
                                order.Add(target);
                            }
                            d[kind] = words[i + 3];
                        }
                        break;
                    }

                case OpVariable when count >= 4:
                    storage[words[i + 2]] = words[i + 3];
                    break;
            }

            i += count;
        }

        return order
            .Select(id =>
            {
                var d = decorations[id];
                return new SpirvVariable(
                    names.GetValueOrDefault(id, $"%{id}"),
                    d.TryGetValue(DecorationDescriptorSet, out var s) ? s : null,
                    d.TryGetValue(DecorationBinding, out var b) ? b : null,
                    d.TryGetValue(DecorationLocation, out var l) ? l : null,
                    storage.GetValueOrDefault(id, uint.MaxValue));
            })
            .ToList();
    }

    /// <summary>Descriptors only, as (set, binding) keyed by leaf name.</summary>
    public static Dictionary<string, (uint Set, uint Binding)> Descriptors(ReadOnlySpan<byte> spirv) =>
        FirstPerKey(
            Read(spirv).Where(v => v is { Set: not null, Binding: not null }),
            v => v.LeafName,
            v => (v.Set!.Value, v.Binding!.Value));

    /// <summary>Input locations by leaf name — the vertex attributes, for a vertex module.</summary>
    public static Dictionary<string, uint> InputLocations(ReadOnlySpan<byte> spirv) =>
        FirstPerKey(
            Read(spirv).Where(v => v is { Location: not null, StorageClass: SpirvVariable.Input }),
            v => v.LeafName,
            v => v.Location!.Value);

    /// <summary>Output locations by leaf name — the colour attachments, for a fragment module.</summary>
    public static Dictionary<string, uint> OutputLocations(ReadOnlySpan<byte> spirv) =>
        FirstPerKey(
            Read(spirv).Where(v => v is { Location: not null, StorageClass: SpirvVariable.Output }),
            v => v.LeafName,
            v => v.Location!.Value);

    /// <summary>
    /// <c>ToDictionary</c> that keeps the first of a repeated key instead of throwing.
    /// </summary>
    /// <remarks>
    /// A module may name two ids the same — a geometry shader's per-vertex arrays are one case —
    /// and this reader is an oracle, so it has to survive whatever the compiler emits rather than
    /// failing the test it is meant to adjudicate.
    /// </remarks>
    private static Dictionary<TKey, TValue> FirstPerKey<T, TKey, TValue>(
        IEnumerable<T> source,
        Func<T, TKey> key,
        Func<T, TValue> value)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (var item in source) result.TryAdd(key(item), value(item));
        return result;
    }

    /// <summary>
    /// Struct members by name, for every struct the module declares.
    /// </summary>
    /// <remarks>
    /// Keyed by member name alone, not by owning type. That is enough for these tests and it keeps
    /// the reader simple; the shaders are written so member names are unique across the module.
    /// </remarks>
    public static Dictionary<string, SpirvMember> Members(ReadOnlySpan<byte> spirv)
    {
        var words = MemoryMarshal.Cast<byte, uint>(spirv);

        var names = new Dictionary<(uint Type, uint Index), string>();
        var offsets = new Dictionary<(uint, uint), ulong>();
        var strides = new Dictionary<(uint, uint), ulong>();
        var rowMajor = new Dictionary<(uint, uint), bool>();

        foreach (var (op, o) in Instructions(words))
        {
            switch (op)
            {
                case OpMemberName when o.Length >= 3:
                    names[(o[0], o[1])] = ReadString(o.AsSpan(2));
                    break;

                case OpMemberDecorate when o.Length >= 3:
                    {
                        var key = (o[0], o[1]);
                        switch (o[2])
                        {
                            case DecorationOffset when o.Length >= 4: offsets[key] = o[3]; break;
                            case DecorationMatrixStride when o.Length >= 4: strides[key] = o[3]; break;
                            case DecorationRowMajor: rowMajor[key] = true; break;
                            case DecorationColMajor: rowMajor[key] = false; break;
                        }
                        break;
                    }
            }
        }

        var result = new Dictionary<string, SpirvMember>();
        foreach (var (key, name) in names)
        {
            if (!offsets.TryGetValue(key, out var offset)) continue;
            result[name] = new SpirvMember(
                offset,
                strides.TryGetValue(key, out var stride) ? stride : null,
                rowMajor.TryGetValue(key, out var row) ? row : null);
        }
        return result;
    }

    /// <summary>Every <c>ArrayStride</c> the module declares, in declaration order.</summary>
    public static List<uint> ArrayStrides(ReadOnlySpan<byte> spirv) =>
        Decorations(spirv, DecorationArrayStride).Select(d => d.Value).ToList();

    /// <summary><c>InputAttachmentIndex</c> by variable name.</summary>
    public static Dictionary<string, uint> InputAttachmentIndices(ReadOnlySpan<byte> spirv)
    {
        var names = Names(spirv);
        return Decorations(spirv, DecorationInputAttachmentIndex)
            .ToDictionary(d => names.GetValueOrDefault(d.Target, $"%{d.Target}"), d => d.Value);
    }

    /// <summary>
    /// The <c>imageFormat</c> operand of every <c>OpTypeImage</c>, in declaration order. 0 is
    /// <c>Unknown</c>; 4 is <c>Rgba8</c>.
    /// </summary>
    public static List<uint> ImageFormats(ReadOnlySpan<byte> spirv) =>
        Instructions(MemoryMarshal.Cast<byte, uint>(spirv))
            .Where(i => i.Op == OpTypeImage && i.Operands.Length >= 8)
            .Select(i => i.Operands[7])
            .ToList();

    /// <summary>
    /// The workgroup size the module declares: literals from <c>LocalSize</c>, or result ids from
    /// <c>LocalSizeId</c> with <c>ById</c> set.
    /// </summary>
    public static (uint X, uint Y, uint Z, bool ById)? LocalSize(ReadOnlySpan<byte> spirv)
    {
        const uint LocalSize = 17;
        const uint LocalSizeId = 38;

        foreach (var (op, o) in Instructions(MemoryMarshal.Cast<byte, uint>(spirv)))
        {
            if (op is not (OpExecutionMode or OpExecutionModeId) || o.Length < 5) continue;
            if (op == OpExecutionMode && o[1] == LocalSize) return (o[2], o[3], o[4], false);
            if (op == OpExecutionModeId && o[1] == LocalSizeId) return (o[2], o[3], o[4], true);
        }
        return null;
    }

    /// <summary>Specialization constant ids by the name the module gives them.</summary>
    public static Dictionary<string, uint> SpecializationIds(ReadOnlySpan<byte> spirv)
    {
        const uint DecorationSpecId = 1;
        var names = Names(spirv);
        return Decorations(spirv, DecorationSpecId)
            .ToDictionary(d => names.GetValueOrDefault(d.Target, $"%{d.Target}"), d => d.Value);
    }

    /// <summary>Every <c>OpEntryPoint</c> as name to execution model.</summary>
    public static Dictionary<string, uint> EntryPointModels(ReadOnlySpan<byte> spirv) =>
        EntryPointIds(spirv).ToDictionary(e => e.Name, e => e.Model);

    /// <summary>
    /// The workgroup size each entry point declares, by name. <c>ById</c> means the operands are
    /// result ids of specialization constants rather than literals.
    /// </summary>
    public static Dictionary<string, (uint X, uint Y, uint Z, bool ById)> WorkgroupSizes(
        ReadOnlySpan<byte> spirv)
    {
        const uint LocalSize = 17;
        const uint LocalSizeId = 38;

        var sizes = new Dictionary<uint, (uint, uint, uint, bool)>();
        foreach (var (op, o) in Instructions(MemoryMarshal.Cast<byte, uint>(spirv)))
        {
            if (op is not (OpExecutionMode or OpExecutionModeId) || o.Length < 5) continue;
            if (op == OpExecutionMode && o[1] == LocalSize) sizes[o[0]] = (o[2], o[3], o[4], false);
            if (op == OpExecutionModeId && o[1] == LocalSizeId) sizes[o[0]] = (o[2], o[3], o[4], true);
        }

        var result = new Dictionary<string, (uint, uint, uint, bool)>();
        foreach (var e in EntryPointIds(spirv))
            if (sizes.TryGetValue(e.Id, out var size)) result[e.Name] = size;
        return result;
    }

    private static List<(uint Id, uint Model, string Name)> EntryPointIds(ReadOnlySpan<byte> spirv)
    {
        const uint OpEntryPoint = 15;
        return Instructions(MemoryMarshal.Cast<byte, uint>(spirv))
            .Where(i => i.Op == OpEntryPoint && i.Operands.Length >= 3)
            .Select(i => (i.Operands[1], i.Operands[0], ReadString(i.Operands.AsSpan(2))))
            .ToList();
    }

    /// <summary>
    /// Every struct type an <c>Input</c> variable's type reaches, through pointers, arrays and
    /// struct members, that decorates a member with <c>Offset</c>. Named by <c>OpName</c> where it
    /// has one, by result id where it does not.
    /// </summary>
    public static List<string> InputStructsWithMemberOffsets(ReadOnlySpan<byte> spirv)
    {
        const uint OpTypeArray = 28, OpTypeRuntimeArray = 29, OpTypeStruct = 30, OpTypePointer = 32;

        var instructions = Instructions(MemoryMarshal.Cast<byte, uint>(spirv));
        var names = Names(spirv);

        var children = new Dictionary<uint, uint[]>();
        var offsetStructs = new HashSet<uint>();
        var inputTypes = new List<uint>();

        foreach (var (op, operands) in instructions)
        {
            switch (op)
            {
                case OpTypePointer when operands.Length >= 3: children[operands[0]] = [operands[2]]; break;
                case OpTypeArray or OpTypeRuntimeArray when operands.Length >= 2: children[operands[0]] = [operands[1]]; break;
                case OpTypeStruct when operands.Length >= 1: children[operands[0]] = operands[1..]; break;
                case OpMemberDecorate when operands.Length >= 3 && operands[2] == DecorationOffset:
                    offsetStructs.Add(operands[0]);
                    break;
                case OpVariable when operands.Length >= 3 && operands[2] == SpirvVariable.Input:
                    inputTypes.Add(operands[0]);
                    break;
            }
        }

        var seen = new HashSet<uint>();
        var pending = new Stack<uint>(inputTypes);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!seen.Add(id) || !children.TryGetValue(id, out var next)) continue;
            foreach (var n in next) pending.Push(n);
        }

        return seen
            .Where(offsetStructs.Contains)
            .Select(id => names.TryGetValue(id, out var name) ? name : $"%{id}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary><c>OpName</c> by result id.</summary>
    public static Dictionary<uint, string> Names(ReadOnlySpan<byte> spirv) =>
        FirstPerKey(
            Instructions(MemoryMarshal.Cast<byte, uint>(spirv))
                .Where(i => i.Op == OpName && i.Operands.Length >= 2),
            i => i.Operands[0],
            i => ReadString(i.Operands.AsSpan(1)));

    private static List<(uint Target, uint Value)> Decorations(ReadOnlySpan<byte> spirv, uint decoration) =>
        Instructions(MemoryMarshal.Cast<byte, uint>(spirv))
            .Where(i => i.Op == OpDecorate && i.Operands.Length >= 3 && i.Operands[1] == decoration)
            .Select(i => (i.Operands[0], i.Operands[2]))
            .ToList();

    private static List<(uint Op, uint[] Operands)> Instructions(ReadOnlySpan<uint> words)
    {
        var result = new List<(uint, uint[])>();

        var i = 5;
        while (i < words.Length)
        {
            var count = (int)(words[i] >> 16);
            if (count < 1 || i + count > words.Length) break;
            result.Add((words[i] & 0xFFFF, words.Slice(i + 1, count - 1).ToArray()));
            i += count;
        }
        return result;
    }

    private static string ReadString(ReadOnlySpan<uint> words)
    {
        var bytes = new List<byte>(words.Length * 4);
        foreach (var word in words)
        {
            for (var b = 0; b < 4; b++)
            {
                var c = (byte)((word >> (b * 8)) & 0xFF);
                if (c == 0) return Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add(c);
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
