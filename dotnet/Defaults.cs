// HAND-WRITTEN. Not generated — and deliberately so. See docs/design.md §4.5.
//
// The descriptor structs in slang.h carry C++ default member initializers:
//
//     struct TargetDesc {
//         size_t           structureSize = sizeof(TargetDesc);
//         SlangTargetFlags flags         = kDefaultTargetFlags;   // = GENERATE_SPIRV_DIRECTLY
//         ...
//     };
//
// ClangSharp drops them. It does not warn, and the result compiles: you get a C# struct whose
// fields all zero-initialize. That makes this the most dangerous class of generator gap in the
// binding — every other one is a build error, this one is a behaviour change.
//
// Two concrete examples, both of which would silently mis-compile every shader in the engine:
//
//   SessionDesc.defaultMatrixLayoutMode   C++ default ROW_MAJOR (1);  zero-init = UNKNOWN (0)
//   TargetDesc.flags                      C++ default GENERATE_SPIRV_DIRECTLY; zero-init = 0,
//                                         i.e. SPIR-V emitted *via glslang* instead of directly
//
// `structureSize` is worse than it looks: Slang uses it to version the struct. Leave it 0 and the
// compiler cannot tell which layout you compiled against.
//
// Rule for this file: never construct a Slang descriptor with `new`. Always start from Create().
// SharpSlangDefaultsTests re-parses slang.h and fails if any value here drifts from upstream.

using SharpSlang.Native;

namespace SharpSlang;

/// <summary>
/// Descriptor factories that restore the C++ default member initializers ClangSharp discards.
/// </summary>
internal static unsafe class Defaults
{
    /// <summary>Mirrors <c>SlangGlobalSessionDesc</c>'s in-class initializers.</summary>
    internal static SlangGlobalSessionDesc GlobalSessionDesc() => new()
    {
        structureSize = (uint)sizeof(SlangGlobalSessionDesc),
        // #define SLANG_API_VERSION 0 — a preprocessor macro, so it is not in the generated layer.
        apiVersion = 0,
        minLanguageVersion = (uint)SlangLanguageVersion.SLANG_LANGUAGE_VERSION_2025,
        enableGLSL = 0, // C++ bool -> byte
        // `reserved` is a fixed buffer and already zeroed.
    };

    /// <summary>Mirrors <c>slang::SessionDesc</c>'s in-class initializers.</summary>
    internal static SessionDesc SessionDesc() => new()
    {
        structureSize = (nuint)sizeof(SessionDesc),
        targets = null,
        targetCount = 0,
        flags = 0, // kSessionFlags_None
        defaultMatrixLayoutMode = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
        searchPaths = null,
        searchPathCount = 0,
        preprocessorMacros = null,
        preprocessorMacroCount = 0,
        fileSystem = null,
        enableEffectAnnotations = 0,
        allowGLSLSyntax = 0,
        compilerOptionEntries = null,
        compilerOptionEntryCount = 0,
        skipSPIRVValidation = 0,
    };

    /// <summary>Mirrors <c>slang::TargetDesc</c>'s in-class initializers.</summary>
    internal static TargetDesc TargetDesc() => new()
    {
        structureSize = (nuint)sizeof(TargetDesc),
        format = SlangCompileTarget.SLANG_TARGET_UNKNOWN,
        profile = SlangProfileID.SLANG_PROFILE_UNKNOWN,
        flags = SlangApi.kDefaultTargetFlags, // GENERATE_SPIRV_DIRECTLY — do not drop this
        floatingPointMode = SlangFloatingPointMode.SLANG_FLOATING_POINT_MODE_DEFAULT,
        lineDirectiveMode = SlangLineDirectiveMode.SLANG_LINE_DIRECTIVE_MODE_DEFAULT,
        forceGLSLScalarBufferLayout = 0,
        compilerOptionEntries = null,
        compilerOptionEntryCount = 0,
    };

    /// <summary>
    /// Mirrors <c>slang::TargetDesc</c> configured for the default target: Vulkan SPIR-V.
    /// </summary>
    internal static TargetDesc SpirvTargetDesc()
    {
        var d = TargetDesc();
        d.format = SlangCompileTarget.SLANG_SPIRV;
        return d;
    }

    /// <summary>Mirrors <c>slang::CompilerOptionValue</c>'s in-class initializers.</summary>
    internal static CompilerOptionValue CompilerOptionValue() => new()
    {
        kind = CompilerOptionValueKind.Int,
        intValue0 = 0,
        intValue1 = 0,
        stringValue0 = null,
        stringValue1 = null,
    };

    /// <summary>Mirrors <c>slang::SourceLocation</c>'s in-class initializers (line/column are -1).</summary>
    internal static SourceLocation SourceLocation() => new()
    {
        filePath = null,
        line = -1,
        column = -1,
    };
}
