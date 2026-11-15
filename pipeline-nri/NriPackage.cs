// The NRI projection as a section of a baked package: written by the cooker at bake time, read
// back by the application at load (the projection is made at bake time; AGENTS.md#nri-projection).
//
// The section stores what ToNri produced, not the options to produce it again: the descs a game
// hands SharpNRI are the bytes the cooker wrote, so the game never projects and never needs the
// rules ToNri applies. Reading builds the same records and the same desc arrays ToNri builds, once
// per shader; every lookup on the result afterwards is the projection's own and allocates nothing
// (AllocationTests).
//
//   section  "SharpSlang.Pipeline.NRI/projection"
//   format   uint32                         this section's layout, 1
//   nri      int32                          NriHeaders.Version the values were written against
//   layout   rootRegisterSpace u32, shaderStages u64, flags u64
//            rootConstants  u32 n + [ registerIndex u32, size u32, shaderStages u64 ] * n
//            rootDescriptors u32 n + [ registerIndex u32, descriptorType u64, shaderStages u64 ] * n
//            rootSamplers   u32 0 (the projection emits none)
//   sets     u32 n + [ setIndex u32, registerSpace u32, u32 m + range * m ] * n
//   range    rangeIndex u32, baseRegisterIndex u32, descriptorNum u32, descriptorType u64,
//            shaderStages u64, flags u64, name?, isUnbounded bool, inputAttachment u32?,
//            imageFormat u64, counterRange u32?
//   blocks   u32 n + [ index u32, registerIndex u32, size u32, shaderStages u64, name ] * n
//   vertex   u32 n + [ name, semanticName?, semanticIndex u32, location u32, offset u32, format u64, stream u16 ] * n
//   outputs  u32 n + [ location u32, format u64, name, semantic? ] * n
//   stages   u32 n + [ name, stage u64, threadGroup (bool + x, y, z u64)? ] * n
//
// `?` is a presence byte before the value. Every enum is written as its value widened to 64 bits,
// so the layout does not move with an enum's underlying type; the NRI version decides what the
// values mean, and a section written against another one is refused, never reinterpreted.

using System.Text;
using SharpNRI;
using SharpNRI.Native;
using SharpSlang.Baking;
using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Pipeline.NRI;

/// <summary>The NRI projection stored in a <see cref="ShaderPackage"/> at bake time, and read back at load.</summary>
public static class NriPackage
{
    /// <summary>The package section the projection is stored under.</summary>
    public const string SectionName = "SharpSlang.Pipeline.NRI/projection";

    private const uint SectionFormat = 1;

    /// <summary>
    /// This package with its layout projected onto NRI and stored as a section: what the cooker
    /// calls at bake time, after <c>ShaderPackageBaker.Bake</c>.
    /// </summary>
    /// <remarks>
    /// The section is read back before it is returned and must equal the projection it came from,
    /// as <c>ShaderPackageBaker</c> reads its own layout back: a section that would not load is a
    /// defect found at bake time rather than in a shipped game.
    /// </remarks>
    /// <param name="package">A baked package.</param>
    /// <param name="options">What the host decided for the projection; stored as its result.</param>
    /// <exception cref="NotSupportedException">The projection refuses the shader (<see cref="NriProjection.ToNri"/>).</exception>
    public static ShaderPackage WithNriProjection(this ShaderPackage package, NriProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        var projection = package.Layout.ToNri(options);
        var data = Write(projection);

        // The read-back check: the same bytes again from what was read.
        var reread = Write(Read(data));
        if (!reread.AsSpan().SequenceEqual(data))
        {
            throw new InvalidOperationException(
                "The stored NRI projection does not read back as it was written. This is a defect " +
                "in SharpSlang.Pipeline.NRI's section, not in the shader; please report it with the " +
                "shader that triggered it.");
        }

        return package.WithSection(new ShaderPackageSection(SectionName, data));
    }

    /// <summary>
    /// The NRI projection stored in this package at bake time, or null when the cooker stored none.
    /// Reads; does not project.
    /// </summary>
    /// <remarks>Once per shader, at load; the result's lookups allocate nothing.</remarks>
    /// <exception cref="InvalidDataException">
    /// The section is truncated or corrupt, of another section format, or was written against
    /// another <c>SharpNRI.Descs</c> (<see cref="NriHeaders.Version"/>): rebake.
    /// </exception>
    public static NriPipelineProjection? ReadNriProjection(this ShaderPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var section = package.Section(SectionName);
        if (section is null) return null;

        try
        {
            return Read(section.Data);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
        {
            throw new InvalidDataException($"not a readable stored NRI projection: {e.Message}", e);
        }
    }

    // ---------------------------------------------------------------- writing

    private static byte[] Write(NriPipelineProjection p)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(SectionFormat);
            w.Write(NriHeaders.Version);

            var layout = p.Layout;
            w.Write(layout.RootRegisterSpace);
            w.Write((ulong)layout.ShaderStages);
            w.Write((ulong)layout.Flags);

            w.Write((uint)layout.RootConstants.Length);
            foreach (var c in layout.RootConstants) WriteRootConstant(w, c);

            w.Write((uint)layout.RootDescriptors.Length);
            foreach (var d in layout.RootDescriptors)
            {
                w.Write(d.registerIndex);
                w.Write((ulong)d.descriptorType);
                w.Write((ulong)d.shaderStages);
            }

            if (layout.RootSamplers.Length != 0)
            {
                throw new NotSupportedException(
                    "the layout has root samplers, which ToNri never emits and the stored projection does not carry");
            }
            w.Write(0u);

            // The sets the layout holds are the records' descs, in the same order (ToNri builds
            // them so), so they are written once, from the records.
            if (layout.DescriptorSets.Length != p.DescriptorSets.Count)
                throw new InvalidOperationException("the layout's descriptor sets are not the projection's");

            w.Write((uint)p.DescriptorSets.Count);
            for (var s = 0; s < p.DescriptorSets.Count; s++)
            {
                var set = p.DescriptorSets[s];
                if (layout.DescriptorSets[s].RegisterSpace != set.RegisterSpace || layout.DescriptorSets[s].Ranges.Length != set.Ranges.Count)
                    throw new InvalidOperationException($"descriptor set {s} of the layout is not the projection's");

                w.Write(set.SetIndex);
                w.Write(set.RegisterSpace);
                w.Write((uint)set.Ranges.Count);
                for (var r = 0; r < set.Ranges.Count; r++)
                {
                    var range = set.Ranges[r];
                    if (!layout.DescriptorSets[s].Ranges[r].Equals(range.Desc))
                        throw new InvalidOperationException($"range {r} of descriptor set {s} of the layout is not the projection's");

                    w.Write(range.RangeIndex);
                    w.Write(range.Desc.baseRegisterIndex);
                    w.Write(range.Desc.descriptorNum);
                    w.Write((ulong)range.Desc.descriptorType);
                    w.Write((ulong)range.Desc.shaderStages);
                    w.Write((ulong)range.Desc.flags);
                    WriteOptional(w, range.Name);
                    w.Write(range.IsUnbounded);
                    WriteOptional(w, range.InputAttachmentIndex);
                    w.Write((ulong)range.ImageFormat);
                    WriteOptional(w, range.CounterRangeIndex);
                }
            }

            if (layout.RootConstants.Length != p.RootConstants.Count)
                throw new InvalidOperationException("the layout's root constants are not the projection's");

            w.Write((uint)p.RootConstants.Count);
            foreach (var block in p.RootConstants)
            {
                w.Write(block.Index);
                WriteRootConstant(w, block.Desc);
                w.Write(block.Name);
            }

            w.Write((uint)p.VertexAttributes.Count);
            foreach (var a in p.VertexAttributes)
            {
                w.Write(a.Name);
                WriteOptional(w, a.Desc.SemanticName);
                w.Write(a.Desc.SemanticIndex);
                w.Write(a.Desc.Location);
                w.Write(a.Desc.Offset);
                w.Write((ulong)a.Desc.Format);
                w.Write(a.Desc.StreamIndex);
            }

            w.Write((uint)p.FragmentOutputs.Count);
            foreach (var o in p.FragmentOutputs)
            {
                w.Write(o.Location);
                w.Write((ulong)o.Format);
                w.Write(o.Name);
                WriteOptional(w, o.Semantic);
            }

            w.Write((uint)p.Stages.Count);
            foreach (var stage in p.Stages)
            {
                w.Write(stage.Name);
                w.Write((ulong)stage.Stage);
                w.Write(stage.ThreadGroupSize is not null);
                if (stage.ThreadGroupSize is { } t)
                {
                    w.Write(t.X);
                    w.Write(t.Y);
                    w.Write(t.Z);
                }
            }
        }

        return stream.ToArray();
    }

    private static void WriteRootConstant(BinaryWriter w, RootConstantDesc c)
    {
        w.Write(c.registerIndex);
        w.Write(c.size);
        w.Write((ulong)c.shaderStages);
    }

    private static void WriteOptional(BinaryWriter w, string? value)
    {
        w.Write(value is not null);
        if (value is not null) w.Write(value);
    }

    private static void WriteOptional(BinaryWriter w, uint? value)
    {
        w.Write(value is not null);
        if (value is { } v) w.Write(v);
    }

    // ---------------------------------------------------------------- reading

    private static NriPipelineProjection Read(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var r = new BinaryReader(stream, Encoding.UTF8);

        var format = r.ReadUInt32();
        if (format != SectionFormat)
            throw new InvalidDataException($"stored NRI projection format {format}, this build reads {SectionFormat}. Rebake.");

        var nri = r.ReadInt32();
        if (nri != NriHeaders.Version)
        {
            throw new InvalidDataException(
                $"the stored NRI projection was written against NRI headers {nri}, and this " +
                $"SharpSlang.Pipeline.NRI reads NRI {NriHeaders.Version}'s values. Rebake.");
        }

        var rootRegisterSpace = r.ReadUInt32();
        var shaderStages = (StageBits)r.ReadUInt64();
        var flags = (PipelineLayoutBits)r.ReadUInt64();

        var rootConstants = new RootConstantDesc[Count(r, 16, "root constants")];
        for (var i = 0; i < rootConstants.Length; i++) rootConstants[i] = ReadRootConstant(r);

        var rootDescriptors = new RootDescriptorDesc[Count(r, 20, "root descriptors")];
        for (var i = 0; i < rootDescriptors.Length; i++)
        {
            rootDescriptors[i] = new RootDescriptorDesc
            {
                registerIndex = r.ReadUInt32(),
                descriptorType = (DescriptorType)r.ReadUInt64(),
                shaderStages = (StageBits)r.ReadUInt64(),
            };
        }

        if (r.ReadUInt32() != 0) throw new InvalidDataException("the stored NRI projection claims root samplers, which it never carries");

        var sets = new ProjectedDescriptorSet[Count(r, 12, "descriptor sets")];
        var setDescs = new NriDescriptorSetDesc[sets.Length];
        for (var s = 0; s < sets.Length; s++)
        {
            var setIndex = r.ReadUInt32();
            var registerSpace = r.ReadUInt32();
            var ranges = new ProjectedDescriptorRange[Count(r, 44, "descriptor ranges")];
            var descs = new DescriptorRangeDesc[ranges.Length];
            for (var i = 0; i < ranges.Length; i++)
            {
                var rangeIndex = r.ReadUInt32();
                var desc = new DescriptorRangeDesc
                {
                    baseRegisterIndex = r.ReadUInt32(),
                    descriptorNum = r.ReadUInt32(),
                    descriptorType = (DescriptorType)r.ReadUInt64(),
                    shaderStages = (StageBits)r.ReadUInt64(),
                    flags = (DescriptorRangeBits)r.ReadUInt64(),
                };
                var name = ReadOptionalString(r);
                var unbounded = r.ReadBoolean();
                var inputAttachment = ReadOptionalUInt(r);
                var imageFormat = (SlangImageFormat)r.ReadUInt64();
                var counter = ReadOptionalUInt(r);

                descs[i] = desc;
                ranges[i] = new ProjectedDescriptorRange(rangeIndex, desc, name, unbounded, inputAttachment, imageFormat, counter);
            }

            sets[s] = new ProjectedDescriptorSet(setIndex, registerSpace, ranges);
            setDescs[s] = new NriDescriptorSetDesc(registerSpace, descs);
        }

        var blocks = new ProjectedRootConstants[Count(r, 21, "root constant blocks")];
        if (blocks.Length != rootConstants.Length)
            throw new InvalidDataException("the stored NRI projection's root constants and their records disagree in number");
        for (var i = 0; i < blocks.Length; i++)
        {
            var index = r.ReadUInt32();
            var desc = ReadRootConstant(r);
            var name = r.ReadString();
            if (!desc.Equals(rootConstants[i]))
                throw new InvalidDataException($"the stored NRI projection's root constant {i} and its record disagree");

            // The layout's array holds the record's desc, as ToNri builds it.
            rootConstants[i] = desc;
            blocks[i] = new ProjectedRootConstants(index, desc, name);
        }

        var attributes = new ProjectedVertexAttribute[Count(r, 23, "vertex attributes")];
        for (var i = 0; i < attributes.Length; i++)
        {
            var name = r.ReadString();
            attributes[i] = new ProjectedVertexAttribute(name, new NriVertexAttributeDesc(
                SemanticName: ReadOptionalString(r),
                SemanticIndex: r.ReadUInt32(),
                Location: r.ReadUInt32(),
                Offset: r.ReadUInt32(),
                Format: (Format)r.ReadUInt64(),
                StreamIndex: r.ReadUInt16()));
        }

        var outputs = new ProjectedFragmentOutput[Count(r, 14, "fragment outputs")];
        for (var i = 0; i < outputs.Length; i++)
        {
            var location = r.ReadUInt32();
            var outputFormat = (Format)r.ReadUInt64();
            var name = r.ReadString();
            outputs[i] = new ProjectedFragmentOutput(location, outputFormat, name, ReadOptionalString(r));
        }

        var stages = new ProjectedStage[Count(r, 10, "stages")];
        for (var i = 0; i < stages.Length; i++)
        {
            var name = r.ReadString();
            var stage = (StageBits)r.ReadUInt64();
            ThreadGroupSize? threadGroup = r.ReadBoolean()
                ? new ThreadGroupSize(r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64())
                : null;
            stages[i] = new ProjectedStage(name, stage, threadGroup);
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException($"the stored NRI projection has {stream.Length - stream.Position} byte(s) after its end");

        return new NriPipelineProjection
        {
            Layout = new NriPipelineLayoutDesc
            {
                RootRegisterSpace = rootRegisterSpace,
                RootConstants = rootConstants,
                RootDescriptors = rootDescriptors,
                DescriptorSets = setDescs,
                ShaderStages = shaderStages,
                Flags = flags,
            },
            DescriptorSets = sets,
            RootConstants = blocks,
            VertexAttributes = attributes,
            FragmentOutputs = outputs,
            Stages = stages,
        };
    }

    private static RootConstantDesc ReadRootConstant(BinaryReader r) => new()
    {
        registerIndex = r.ReadUInt32(),
        size = r.ReadUInt32(),
        shaderStages = (StageBits)r.ReadUInt64(),
    };

    private static string? ReadOptionalString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    private static uint? ReadOptionalUInt(BinaryReader r) => r.ReadBoolean() ? r.ReadUInt32() : null;

    /// <summary>A count the rest of the section can hold, at <paramref name="perItem"/> bytes at least each.</summary>
    private static int Count(BinaryReader r, int perItem, string what)
    {
        var count = r.ReadUInt32();
        var remaining = r.BaseStream.Length - r.BaseStream.Position;
        if ((long)count * perItem > remaining)
        {
            throw new InvalidDataException(
                $"the stored NRI projection claims {count} {what}, which needs at least " +
                $"{(long)count * perItem} bytes, but only {remaining} remain. It is truncated or corrupt.");
        }

        return (int)count;
    }
}
