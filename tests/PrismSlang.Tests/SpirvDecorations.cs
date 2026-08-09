// Ground truth for the reflection tests.
//
// Every defect in the descriptor and vertex-input projection so far has been the same shape: what
// PrismSlang reported and what the emitted module decorated disagreed, and nothing compared them.
// Reflection is a claim about the module; the decorations are what the driver reads.
//
// So this reads the answer out of the SPIR-V. It is not a general disassembler — it wants exactly
// the DescriptorSet, Binding and Location decorations, keyed to the OpName of what they decorate.

using System.Runtime.InteropServices;
using System.Text;

namespace PrismSlang.Tests;

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

/// <summary>Reads decorations out of an emitted module.</summary>
public static class SpirvDecorations
{
    private const uint OpName = 5;
    private const uint OpDecorate = 71;
    private const uint OpVariable = 59;

    private const uint DecorationLocation = 30;
    private const uint DecorationBinding = 33;
    private const uint DecorationDescriptorSet = 34;

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
        Read(spirv)
            .Where(v => v is { Set: not null, Binding: not null })
            .ToDictionary(v => v.LeafName, v => (v.Set!.Value, v.Binding!.Value));

    /// <summary>Input locations by leaf name — the vertex attributes, for a vertex module.</summary>
    public static Dictionary<string, uint> InputLocations(ReadOnlySpan<byte> spirv) =>
        Read(spirv)
            .Where(v => v is { Location: not null, StorageClass: SpirvVariable.Input })
            .ToDictionary(v => v.LeafName, v => v.Location!.Value);

    /// <summary>Output locations by leaf name — the colour attachments, for a fragment module.</summary>
    public static Dictionary<string, uint> OutputLocations(ReadOnlySpan<byte> spirv) =>
        Read(spirv)
            .Where(v => v is { Location: not null, StorageClass: SpirvVariable.Output })
            .ToDictionary(v => v.LeafName, v => v.Location!.Value);

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
