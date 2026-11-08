namespace SharpSlang.Native;

public enum CompilerOptionName
{
    /// <summary>stringValue0: macro name; stringValue1: macro value</summary>
    MacroDefine = 0,
    DepFile = 1,
    EntryPointName = 2,
    Specialize = 3,
    Help = 4,
    HelpStyle = 5,
    /// <summary>stringValue: additional include path.</summary>
    Include = 6,
    Language = 7,
    /// <summary>bool</summary>
    MatrixLayoutColumn = 8,
    /// <summary>bool</summary>
    MatrixLayoutRow = 9,
    /// <summary>bool</summary>
    ZeroInitialize = 10,
    /// <summary>bool</summary>
    IgnoreCapabilities = 11,
    /// <summary>bool</summary>
    RestrictiveCapabilityCheck = 12,
    /// <summary>stringValue0: module name.</summary>
    ModuleName = 13,
    Output = 14,
    /// <summary>intValue0: profile</summary>
    Profile = 15,
    /// <summary>intValue0: stage</summary>
    Stage = 16,
    /// <summary>intValue0: CodeGenTarget</summary>
    Target = 17,
    Version = 18,
    /// <summary>stringValue0: "all" or comma-separated list of</summary>
    /// <remarks>
    /// <para>warning codes or names.</para>
    /// </remarks>
    WarningsAsErrors = 19,
    /// <summary>stringValue0: comma separated list of warning codes or names.</summary>
    DisableWarnings = 20,
    /// <summary>stringValue0: warning code or name.</summary>
    EnableWarning = 21,
    /// <summary>stringValue0: warning code or name.</summary>
    DisableWarning = 22,
    DumpWarningDiagnostics = 23,
    InputFilesRemain = 24,
    /// <summary>bool</summary>
    EmitIr = 25,
    /// <summary>bool</summary>
    ReportDownstreamTime = 26,
    /// <summary>bool</summary>
    ReportPerfBenchmark = 27,
    /// <summary>bool</summary>
    ReportCheckpointIntermediates = 28,
    /// <summary>bool</summary>
    SkipSPIRVValidation = 29,
    SourceEmbedStyle = 30,
    SourceEmbedName = 31,
    SourceEmbedLanguage = 32,
    /// <summary>bool</summary>
    DisableShortCircuit = 33,
    /// <summary>bool</summary>
    MinimumSlangOptimization = 34,
    /// <summary>bool</summary>
    DisableNonEssentialValidations = 35,
    /// <summary>bool</summary>
    DisableSourceMap = 36,
    /// <summary>bool</summary>
    UnscopedEnum = 37,
    /// <summary>bool: preserve all resource parameters in the output code.</summary>
    PreserveParameters = 38,
    /// <summary>Target</summary>
    /// <remarks>
    /// <para>intValue0: CapabilityName</para>
    /// </remarks>
    Capability = 39,
    /// <summary>bool</summary>
    DefaultImageFormatUnknown = 40,
    /// <summary>bool</summary>
    DisableDynamicDispatch = 41,
    /// <summary>bool</summary>
    DisableSpecialization = 42,
    /// <summary>intValue0: FloatingPointMode</summary>
    FloatingPointMode = 43,
    /// <summary>intValue0: DebugInfoLevel</summary>
    DebugInformation = 44,
    LineDirectiveMode = 45,
    /// <summary>intValue0: OptimizationLevel</summary>
    Optimization = 46,
    /// <summary>bool</summary>
    Obfuscate = 47,
    /// <summary>intValue0 (higher 8 bits): kind; intValue0(lower bits): set;</summary>
    /// <remarks>
    /// <para>intValue1: shift</para>
    /// </remarks>
    VulkanBindShift = 48,
    /// <summary>intValue0: index; intValue1: set</summary>
    VulkanBindGlobals = 49,
    /// <summary>bool</summary>
    VulkanInvertY = 50,
    /// <summary>bool</summary>
    VulkanUseDxPositionW = 51,
    /// <summary>bool</summary>
    VulkanUseEntryPointName = 52,
    /// <summary>bool</summary>
    VulkanUseGLLayout = 53,
    /// <summary>bool</summary>
    VulkanEmitReflection = 54,
    /// <summary>bool</summary>
    GLSLForceScalarLayout = 55,
    /// <summary>bool</summary>
    EnableEffectAnnotations = 56,
    /// <summary>bool (will be deprecated)</summary>
    EmitSpirvViaGLSL = 57,
    /// <summary>bool (will be deprecated)</summary>
    EmitSpirvDirectly = 58,
    /// <summary>stringValue0: json path</summary>
    SPIRVCoreGrammarJSON = 59,
    /// <summary>bool, when set, will not issue an error when the linked program</summary>
    /// <remarks>
    /// <para>has unresolved extern function symbols.</para>
    /// </remarks>
    IncompleteLibrary = 60,
    /// <summary>Downstream</summary>
    CompilerPath = 61,
    DefaultDownstreamCompiler = 62,
    /// <summary>stringValue0: downstream compiler name. stringValue1: argument list,</summary>
    /// <remarks>
    /// <para>one per line.</para>
    /// </remarks>
    DownstreamArgs = 63,
    PassThrough = 64,
    /// <summary>Repro</summary>
    DumpRepro = 65,
    DumpReproOnError = 66,
    ExtractRepro = 67,
    LoadRepro = 68,
    LoadReproDirectory = 69,
    ReproFallbackDirectory = 70,
    /// <summary>Debugging</summary>
    DumpAst = 71,
    DumpIntermediatePrefix = 72,
    /// <summary>bool</summary>
    DumpIntermediates = 73,
    /// <summary>bool</summary>
    DumpIr = 74,
    DumpIrIds = 75,
    PreprocessorOutput = 76,
    OutputIncludes = 77,
    ReproFileSystem = 78,
    /// <summary>deprecated and removed; value must never be reused</summary>
    REMOVED_SerialIR = 79,
    /// <summary>bool</summary>
    SkipCodeGen = 80,
    /// <summary>bool</summary>
    ValidateIr = 81,
    VerbosePaths = 82,
    VerifyDebugSerialIr = 83,
    /// <summary>Not used.</summary>
    NoCodeGen = 84,
    /// <summary>Experimental</summary>
    FileSystem = 85,
    Heterogeneous = 86,
    NoMangle = 87,
    NoHLSLBinding = 88,
    NoHLSLPackConstantBufferElements = 89,
    ValidateUniformity = 90,
    AllowGLSL = 91,
    EnableExperimentalPasses = 92,
    /// <summary>int</summary>
    BindlessSpaceIndex = 93,
    /// <summary>int: byte stride for SPIRV resource descriptor heap</summary>
    SPIRVResourceHeapStride = 94,
    /// <summary>int: byte stride for SPIRV sampler descriptor heap</summary>
    SPIRVSamplerHeapStride = 95,
    /// <summary>Internal</summary>
    ArchiveType = 96,
    CompileCoreModule = 97,
    Doc = 98,
    /// <summary>deprecated; value must never be reused</summary>
    IrCompression = 99,
    LoadCoreModule = 100,
    ReferenceModule = 101,
    SaveCoreModule = 102,
    SaveCoreModuleBinSource = 103,
    TrackLiveness = 104,
    /// <summary>bool, enable loop inversion optimization</summary>
    LoopInversion = 105,
    /// <summary>Deprecated; value must never be reused</summary>
    ParameterBlocksUseRegisterSpaces = 106,
    /// <summary>intValue0: SlangLanguageVersion</summary>
    LanguageVersion = 107,
    /// <summary>stringValue0: type conformance to link; format:</summary>
    /// <remarks>
    /// <para>"&lt;TypeName&gt;:&lt;IInterfaceName&gt;[=&lt;sequentialId&gt;]",</para>
    /// <para>e.g. "Impl:IFoo=3" or "Impl:IFoo".</para>
    /// </remarks>
    TypeConformance = 108,
    /// <summary>bool, experimental</summary>
    EnableExperimentalDynamicDispatch = 109,
    /// <summary>bool</summary>
    EmitReflectionJSON = 110,
    /// <summary>historical sentinel; value must not be reused</summary>
    CountOfParsableOptions = 111,
    /// <summary>Options added after the original set. Most have CLI flags; a few are</summary>
    /// <remarks>
    /// <para>API-only (marked below). All future additions belong after DiagnosticColor,</para>
    /// <para>immediately before CountOf.</para>
    /// <para>intValue0: DebugInfoFormat (derived from -g; no direct CLI</para>
    /// <para>flag)</para>
    /// </remarks>
    DebugInformationFormat = 112,
    /// <summary>intValue0: kind; intValue1: shift (derived from</summary>
    /// <remarks>
    /// <para>-fvk-x-shift; no direct CLI flag)</para>
    /// </remarks>
    VulkanBindShiftAll = 113,
    /// <summary>bool</summary>
    GenerateWholeProgram = 114,
    /// <summary>bool, when set, will only load precompiled modules</summary>
    /// <remarks>
    /// <para>if up-to-date with source. (API-only; no direct CLI flag)</para>
    /// </remarks>
    UseUpToDateBinaryModule = 115,
    /// <summary>bool</summary>
    EmbedDownstreamIR = 116,
    /// <summary>bool</summary>
    ForceDXLayout = 117,
    /// <summary>Setting of EmitSpirvDirectly or EmitSpirvViaGLSL will turn into this option internally.</summary>
    /// <remarks>
    /// <para>enum SlangEmitSpirvMethod (derived; no direct CLI flag)</para>
    /// </remarks>
    EmitSpirvMethod = 118,
    SaveGLSLModuleBinSource = 119,
    /// <summary>bool, experimental (API-only; no direct CLI flag)</summary>
    SkipDownstreamLinking = 120,
    DumpModule = 121,
    /// <summary>Print serialized module version and name</summary>
    GetModuleInfo = 122,
    /// <summary>Print the min and max module versions this compiler</summary>
    /// <remarks>
    /// <para>supports</para>
    /// </remarks>
    GetSupportedModuleVersions = 123,
    /// <summary>bool</summary>
    EmitSeparateDebug = 124,
    /// <summary>Floating point denormal handling modes</summary>
    DenormalModeFp16 = 125,
    DenormalModeFp32 = 126,
    DenormalModeFp64 = 127,
    /// <summary>Deprecated. When no BitfieldPackingRules value is supplied, true selects MSB-first</summary>
    /// <remarks>
    /// <para>packing and starts new storage on underlying type-size changes.</para>
    /// <para>bool</para>
    /// </remarks>
    UseMSVCStyleBitfieldPacking = 128,
    /// <summary>bool</summary>
    ForceCLayout = 129,
    /// <summary>bool, enable experimental features</summary>
    ExperimentalFeature = 130,
    /// <summary>bool, reports detailed compiler performance benchmark</summary>
    /// <remarks>
    /// <para>results</para>
    /// </remarks>
    ReportDetailedPerfBenchmark = 131,
    /// <summary>bool, enable detailed IR validation</summary>
    ValidateIRDetailed = 132,
    /// <summary>string, pass name to dump IR before</summary>
    DumpIRBefore = 133,
    /// <summary>string, pass name to dump IR after</summary>
    DumpIRAfter = 134,
    /// <summary>enum SlangEmitCPUMethod (derived; no direct CLI flag)</summary>
    EmitCPUMethod = 135,
    /// <summary>bool</summary>
    EmitCPUViaCPP = 136,
    /// <summary>bool</summary>
    EmitCPUViaLLVM = 137,
    /// <summary>string</summary>
    LLVMTargetTriple = 138,
    /// <summary>string</summary>
    LLVMCPU = 139,
    /// <summary>string</summary>
    LLVMFeatures = 140,
    /// <summary>bool, enable the experimental rich diagnostics</summary>
    EnableRichDiagnostics = 141,
    /// <summary>bool</summary>
    ReportDynamicDispatchSites = 142,
    /// <summary>bool, enable machine-readable diagnostic output</summary>
    /// <remarks>
    /// <para>(implies EnableRichDiagnostics)</para>
    /// </remarks>
    EnableMachineReadableDiagnostics = 143,
    /// <summary>intValue0: SlangDiagnosticColor (always, never, auto)</summary>
    DiagnosticColor = 144,
    /// <summary>bool: insert per-statement line coverage counters</summary>
    TraceCoverage = 145,
    /// <summary>intValue0: register index; intValue1: register space - explicit</summary>
    /// <remarks>
    /// <para>binding for the synthesized __slang_coverage buffer. Consumed</para>
    /// <para>only when any coverage mode is enabled; the slangc CLI spelling</para>
    /// <para>also enables TraceCoverage.</para>
    /// </remarks>
    TraceCoverageBinding = 146,
    /// <summary>intValue0: descriptor/register space reserved by the host when</summary>
    /// <remarks>
    /// <para>auto-allocating the synthesized __slang_coverage buffer. This is</para>
    /// <para>a repeatable hint consumed only when any coverage mode is enabled.</para>
    /// </remarks>
    TraceCoverageReservedSpace = 147,
    /// <summary>bool: insert per-function-entry coverage counters</summary>
    TraceFunctionCoverage = 148,
    /// <summary>bool: insert per-branch-arm coverage counters</summary>
    TraceBranchCoverage = 149,
    /// <summary>stringValue0: explicit path for the slangc coverage manifest sidecar.</summary>
    /// <remarks>
    /// <para>When unset, slangc writes &lt;output&gt;.coverage-manifest.json next to</para>
    /// <para>file outputs that carry coverage metadata. This option is output</para>
    /// <para>policy only and is excluded from compiler cache keys. It requires</para>
    /// <para>at least one coverage tracing mode, is rejected for container</para>
    /// <para>outputs, and errors if the selected outputs produce no coverage</para>
    /// <para>metadata. Explicit paths are valid only when exactly one compiled</para>
    /// <para>artifact carries coverage metadata and must not overlap any emitted</para>
    /// <para>artifact path. Query/set with the string option APIs.</para>
    /// </remarks>
    CoverageManifestOutput = 150,
    /// <summary>intValue0: per-slot byte width of the synthesized __slang_coverage</summary>
    /// <remarks>
    /// <para>buffer. Accepts 4 (uint32) or 8 (uint64). Omitting the option</para>
    /// <para>yields 8 when any coverage mode is enabled. Use 4 to opt down to</para>
    /// <para>uint32 when the runtime driver lacks 64-bit shader atomic support</para>
    /// <para>(notably MoltenVK on Apple Silicon, where Vulkan exposes</para>
    /// <para>shaderBufferInt64Atomics = false). uint32 counters wrap silently</para>
    /// <para>at 2^32 hits per slot; uint64 counters effectively do not wrap</para>
    /// <para>within any practical run. The corresponding CLI flag</para>
    /// <para><c>-trace-coverage-counter-width &lt;bits&gt;</c> takes a bit count (32/64)</para>
    /// <para>and stores the matching byte width here.</para>
    /// </remarks>
    TraceCoverageCounterByteWidth = 151,
    /// <summary>bool: record boolean coverage (CoverageCounterMode::Boolean) instead of exact</summary>
    /// <remarks>
    /// <para>execution counts. Each counter is written with a plain non-atomic store</para>
    /// <para>of <c>1</c>, eliminating atomic contention (much faster, and avoids the GPU</para>
    /// <para>watchdog timeouts heavy coverage can trigger) at the cost of exact</para>
    /// <para>counts. Off by default.</para>
    /// </remarks>
    TraceCoverageBoolean = 152,
    /// <summary>bool: when set, emit each SPIRV resource descriptor-heap runtime array's</summary>
    /// <remarks>
    /// <para>ArrayStride as the maximum of image and buffer descriptor sizes, so a</para>
    /// <para>single heap shared by buffers and images is indexed at the device's unified</para>
    /// <para>stride. Opt-in; mutually exclusive with a non-zero</para>
    /// <para><c>-spirv-resource-heap-stride</c> (combining the two is an error).</para>
    /// </remarks>
    SPIRVUnifiedDescriptorHeapStride = 154,
    /// <summary>intValue0: a SlangWarningLevel group to enable (e.g. SLANG_WARNING_LEVEL_PEDANTIC).</summary>
    /// <remarks>
    /// <para>Repeatable: enabling multiple groups is additive, matching how -Wall/-Wextra/-Wpedantic</para>
    /// <para>combine on the command line. CLI spellings: -Wall, -Wextra, -Wpedantic.</para>
    /// </remarks>
    WarningLevel = 155,
    /// <summary>stringValue0: explicit path for the slangc separate-debug-info sidecar.</summary>
    /// <remarks>
    /// <para>When unset, slangc derives the sidecar path from the main artifact path.</para>
    /// <para>This option is output policy only and is excluded from compiler cache keys.</para>
    /// <para>It requires EmitSeparateDebug and permits the main artifact to be written to</para>
    /// <para>stdout. A value of "-" writes the separate debug information to stdout when</para>
    /// <para>the main artifact is written to a file. Query/set with the string option APIs.</para>
    /// </remarks>
    SeparateDebugInfoOutput = 156,
    /// <summary>bool: embed the shader source text into the debug information independently of</summary>
    /// <remarks>
    /// <para>the overall <c>-g</c> debug level. At <c>-g1</c> (Minimal) the source is embedded via</para>
    /// <para>the core <c>OpSource</c> File+Source operands (no NonSemantic extension); at</para>
    /// <para><c>-g2</c>/<c>-g3</c> source is already embedded so the option is a no-op. Requires</para>
    /// <para>debug information: using it with <c>-g0</c>, or without any <c>-g</c> option (both</para>
    /// <para>resolve to no debug info), is an error. Only affects SPIR-V output.</para>
    /// </remarks>
    DebugInfoIncludeSource = 157,
    /// <summary>int: Synthesize <c>__slang_coverage</c> as an unbounded</summary>
    /// <remarks>
    /// <para>descriptor array of structured buffers rather than a single</para>
    /// <para>buffer, and index it with this value:</para>
    /// <para><c>__slang_coverage[N][slot]</c>. Many separately compiled</para>
    /// <para>shaders sharing one pipeline then occupy a single</para>
    /// <para>descriptor binding instead of one binding each, and each</para>
    /// <para>shader's buffer is sized independently by the host.</para>
    /// <para>Where the array itself lives is a separate decision, made</para>
    /// <para>with <c>TraceCoverageBinding</c> (or left to auto-allocation).</para>
    /// <para>Note for hosts: if the descriptor array is declared with</para>
    /// <para><c>VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT</c>,</para>
    /// <para>Vulkan requires it to be the highest-numbered binding in</para>
    /// <para>its set. A fixed <c>descriptorCount</c> carries no such</para>
    /// <para>restriction. Either way it is the host's layout to satisfy</para>
    /// <para>and the compiler cannot see it.</para>
    /// <para>The index is a compile-time constant and therefore part of</para>
    /// <para>the compiled artifact: a host that keys a shader cache on</para>
    /// <para>the compiled output must derive it from a stable shader</para>
    /// <para>identity rather than from load order, or an unchanged</para>
    /// <para>shader recompiles whenever that order shifts. Supplying it</para>
    /// <para>at pipeline creation instead would remove that constraint;</para>
    /// <para>see issue #12541. SPIR-V and GLSL only.</para>
    /// </remarks>
    TraceCoverageBindlessIndex = 158,
    /// <summary>CLI-only query option <c>-get-&lt;compiler&gt;-path</c>: prints the resolved on-disk path of the</summary>
    /// <remarks>
    /// <para>downstream &lt;compiler&gt; Slang would load for that pass-through (via</para>
    /// <para>IGlobalSession::getDownstreamCompilerPath), then continues. It takes no value and is</para>
    /// <para>never stored on an option set; it only drives the print-and-continue handler in the</para>
    /// <para>command-line parser.</para>
    /// </remarks>
    GetCompilerPath = 159,
    /// <summary>intValue0: slang::BitfieldPackingRules</summary>
    BitfieldPackingRules = 160,
    /// <summary>stringValue0: comma-separated note codes or names.</summary>
    DisableNotes = 161,
    /// <summary>intValue0: SlangDiagnosticFormat (default, vs)</summary>
    DiagnosticFormat = 162,
    /// <summary><c>bool</c>: Enables additional backwards-compatibility features for legacy HLSL inputs,</summary>
    /// <remarks>
    /// <para>such as using uniform parameters as mutable temporaries. Has no effect on other dialects.</para>
    /// </remarks>
    EnableExtendedHLSLBackwardsCompatibility = 163,
    /// <summary>Do not assign an explicit value to CountOf. It must remain one past the last option,</summary>
    /// <remarks>
    /// <para>which it derives implicitly from the preceding (highest-valued) enumerator.</para>
    /// </remarks>
    CountOf,
}
