namespace SharpSlang.Native;

/// <summary>Generic metadata for compiler-synthesized bindable resources.</summary>
/// <remarks>
/// <para>This metadata is intended for features that inject hidden resources into the compiled program interface without surfacing them through the ordinary public reflection model. Shader coverage is the first consumer: its synthesized <c>__slang_coverage</c> buffer is reported here so hosts can discover how to bind it even though it is not part of normal <c>ProgramLayout</c> reflection.</para>
/// <para>The metadata is retrieved by calling <c>castAs</c> / <c>queryInterface</c> on the artifact-associated <c>IMetadata</c> object. Like coverage metadata, this interface is ABI-stable by design:</para>
/// <para>- <c>SyntheticResourceInfo</c> may grow by tail extension guarded by its leading <c>structSize</c>, or - a future <c>ISyntheticResourceMetadataN</c> may be added with a new UUID.</para>
/// <para>Intended use: - use <c>ISyntheticResourceMetadata</c> to discover and bind hidden compiler-synthesized resources - descriptor-backed hosts read <c>(space, binding)</c> from <c>SyntheticResourceInfo</c> - CPU/CUDA-style marshaling hosts read <c>uniformOffset</c> and <c>uniformStride</c> from <c>SyntheticResourceInfo</c></para>
/// <para>Lifetime and ownership: - the metadata object is owned by the compiled artifact / <c>IMetadata</c> container it is queried from - pointers returned through <c>SyntheticResourceInfo</c> remain valid for the lifetime of that metadata object - callers do not own returned strings and must not free them</para>
/// <para>Thread safety: - metadata objects are immutable once returned to the host; concurrent read-only queries are allowed as long as callers keep the owning COM object alive</para>
/// </remarks>
[NativeTypeName("uint32_t")]
public enum SyntheticResourceScope : uint
{
    /// <summary>One shared resource bound at program/global scope.</summary>
    Global = 0,
    /// <summary>A resource scoped to one linked entry point.</summary>
    EntryPoint = 1,
}
