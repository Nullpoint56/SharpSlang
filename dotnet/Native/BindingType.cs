using static SharpSlang.Native.SlangBindingType;

namespace SharpSlang.Native;

/// <summary>Types of API-managed bindings that a parameter might use.</summary>
/// <remarks>
/// <para><c>SlangBindingType</c> represents the distinct types of binding ranges that might be understood by an underlying graphics API or cross-API abstraction layer. Several of the enumeration cases here correspond to cases of <c>VkDescriptorType</c> defined by the Vulkan API. Note however that the values of this enumeration are not the same as those of any particular API.</para>
/// <para>The <c>SlangBindingType</c> enumeration is distinct from <c>SlangParameterCategory</c> because <c>SlangParameterCategory</c> differentiates the types of parameters for the purposes of layout, where the layout rules of some targets will treat parameters of different types as occupying the same binding space for layout (e.g., in SPIR-V both a <c>Texture2D</c> and <c>SamplerState</c> use the same space of <c>binding</c> indices, and are not allowed to overlap), while those same types map to different types of bindings in the API (e.g., both textures and samplers use different <c>VkDescriptorType</c> values).</para>
/// <para>When you want to answer "what register/binding did this parameter use?" you should use <c>SlangParameterCategory</c>.</para>
/// <para>When you want to answer "what type of descriptor range should this parameter use?" you should use <c>SlangBindingType</c>.</para>
/// </remarks>
[NativeTypeName("SlangBindingTypeIntegral")]
public enum BindingType : uint
{
    Unknown = SLANG_BINDING_TYPE_UNKNOWN,
    Sampler = SLANG_BINDING_TYPE_SAMPLER,
    Texture = SLANG_BINDING_TYPE_TEXTURE,
    ConstantBuffer = SLANG_BINDING_TYPE_CONSTANT_BUFFER,
    ParameterBlock = SLANG_BINDING_TYPE_PARAMETER_BLOCK,
    TypedBuffer = SLANG_BINDING_TYPE_TYPED_BUFFER,
    RawBuffer = SLANG_BINDING_TYPE_RAW_BUFFER,
    CombinedTextureSampler = SLANG_BINDING_TYPE_COMBINED_TEXTURE_SAMPLER,
    InputRenderTarget = SLANG_BINDING_TYPE_INPUT_RENDER_TARGET,
    InlineUniformData = SLANG_BINDING_TYPE_INLINE_UNIFORM_DATA,
    RayTracingAccelerationStructure = SLANG_BINDING_TYPE_RAY_TRACING_ACCELERATION_STRUCTURE,
    VaryingInput = SLANG_BINDING_TYPE_VARYING_INPUT,
    VaryingOutput = SLANG_BINDING_TYPE_VARYING_OUTPUT,
    ExistentialValue = SLANG_BINDING_TYPE_EXISTENTIAL_VALUE,
    PushConstant = SLANG_BINDING_TYPE_PUSH_CONSTANT,
    MutableFlag = SLANG_BINDING_TYPE_MUTABLE_FLAG,
    MutableTexture = SLANG_BINDING_TYPE_MUTABLE_TETURE,
    MutableTypedBuffer = SLANG_BINDING_TYPE_MUTABLE_TYPED_BUFFER,
    MutableRawBuffer = SLANG_BINDING_TYPE_MUTABLE_RAW_BUFFER,
    BaseMask = SLANG_BINDING_TYPE_BASE_MASK,
    ExtMask = SLANG_BINDING_TYPE_EXT_MASK,
}
