namespace SharpSlang.Native;

[NativeTypeName("SlangParameterCategoryIntegral")]
public enum SlangParameterCategory : uint
{
    SLANG_PARAMETER_CATEGORY_NONE = 0,
    SLANG_PARAMETER_CATEGORY_MIXED = 1,
    SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER = 2,
    SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE = 3,
    SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS = 4,
    SLANG_PARAMETER_CATEGORY_VARYING_INPUT = 5,
    SLANG_PARAMETER_CATEGORY_VARYING_OUTPUT = 6,
    SLANG_PARAMETER_CATEGORY_SAMPLER_STATE = 7,
    SLANG_PARAMETER_CATEGORY_UNIFORM = 8,
    SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT = 9,
    SLANG_PARAMETER_CATEGORY_SPECIALIZATION_CONSTANT = 10,
    SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER = 11,
    /// <summary>HLSL register <c>space</c>, Vulkan GLSL <c>set</c></summary>
    SLANG_PARAMETER_CATEGORY_REGISTER_SPACE = 12,
    /// <summary>A parameter whose type is to be specialized by a global generic type argument</summary>
    SLANG_PARAMETER_CATEGORY_GENERIC = 13,
    SLANG_PARAMETER_CATEGORY_RAY_PAYLOAD = 14,
    SLANG_PARAMETER_CATEGORY_HIT_ATTRIBUTES = 15,
    SLANG_PARAMETER_CATEGORY_CALLABLE_PAYLOAD = 16,
    SLANG_PARAMETER_CATEGORY_SHADER_RECORD = 17,
    /// <summary>An existential type parameter represents a "hole" that</summary>
    /// <remarks>
    /// <para>needs to be filled with a concrete type to enable</para>
    /// <para>generation of specialized code.</para>
    /// <para>Consider this example:</para>
    /// <para>struct MyParams</para>
    /// <para>{</para>
    /// <para>IMaterial material;</para>
    /// <para>ILight lights[3];</para>
    /// <para>};</para>
    /// <para>This <c>MyParams</c> type introduces two existential type parameters:</para>
    /// <para>one for <c>material</c> and one for <c>lights</c>. Even though <c>lights</c></para>
    /// <para>is an array, it only introduces one type parameter, because</para>
    /// <para>we need to have a *single* concrete type for all the array</para>
    /// <para>elements to be able to generate specialized code.</para>
    /// </remarks>
    SLANG_PARAMETER_CATEGORY_EXISTENTIAL_TYPE_PARAM = 18,
    /// <summary>An existential object parameter represents a value</summary>
    /// <remarks>
    /// <para>that needs to be passed in to provide data for some</para>
    /// <para>interface-type shader parameter.</para>
    /// <para>Consider this example:</para>
    /// <para>struct MyParams</para>
    /// <para>{</para>
    /// <para>IMaterial material;</para>
    /// <para>ILight lights[3];</para>
    /// <para>};</para>
    /// <para>This <c>MyParams</c> type introduces four existential object parameters:</para>
    /// <para>one for <c>material</c> and three for <c>lights</c> (one for each array</para>
    /// <para>element). This is consistent with the number of interface-type</para>
    /// <para>"objects" that are being passed through to the shader.</para>
    /// </remarks>
    SLANG_PARAMETER_CATEGORY_EXISTENTIAL_OBJECT_PARAM = 19,
    /// <summary>The register space offset for the sub-elements that occupies register spaces.</summary>
    SLANG_PARAMETER_CATEGORY_SUB_ELEMENT_REGISTER_SPACE = 20,
    /// <summary>The input_attachment_index subpass occupancy tracker</summary>
    SLANG_PARAMETER_CATEGORY_SUBPASS = 21,
    /// <summary>Metal tier-1 argument buffer element [[id]].</summary>
    SLANG_PARAMETER_CATEGORY_METAL_ARGUMENT_BUFFER_ELEMENT = 22,
    /// <summary>Metal [[attribute]] inputs.</summary>
    SLANG_PARAMETER_CATEGORY_METAL_ATTRIBUTE = 23,
    /// <summary>Metal [[payload]] inputs</summary>
    SLANG_PARAMETER_CATEGORY_METAL_PAYLOAD = 24,
    SLANG_PARAMETER_CATEGORY_COUNT,
    /// <summary>Aliases for Metal-specific categories.</summary>
    SLANG_PARAMETER_CATEGORY_METAL_BUFFER = SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER,
    SLANG_PARAMETER_CATEGORY_METAL_TEXTURE = SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE,
    SLANG_PARAMETER_CATEGORY_METAL_SAMPLER = SLANG_PARAMETER_CATEGORY_SAMPLER_STATE,
    /// <summary>DEPRECATED:</summary>
    SLANG_PARAMETER_CATEGORY_VERTEX_INPUT = SLANG_PARAMETER_CATEGORY_VARYING_INPUT,
    SLANG_PARAMETER_CATEGORY_FRAGMENT_OUTPUT = SLANG_PARAMETER_CATEGORY_VARYING_OUTPUT,
    SLANG_PARAMETER_CATEGORY_COUNT_V1 = SLANG_PARAMETER_CATEGORY_SUBPASS,
}
