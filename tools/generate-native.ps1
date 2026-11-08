<#
.SYNOPSIS
  Regenerate the SharpSlang L0 raw interop layer from the Slang headers.

.DESCRIPTION
  Runs ClangSharpPInvokeGenerator over slang.h + slang-deprecated.h and applies the post-processing
  that ClangSharp cannot do itself, then carries Slang's header comments into it as XML docs
  (tools/generate-native-docs.ps1). Validated end to end against Slang v2026.21 with ClangSharp
  21.1.8.4: the counts are printed at the end and recorded in AGENTS.md#generator-and-abi.

  C++ mode, of necessity: slang.h does not parse as C (bool at :621, constexpr at :825, namespace at
  :1040, measured with --language c at v2026.21). In C++ mode no enum is erased: every parameter,
  field and return the header declares with an enum type is that enum in L0, and the check below
  fails a regeneration where one is an integer.

  See AGENTS.md#generator-and-abi for why each workaround below exists, and for every declaration
  this script removes. The short version:

    * `struct Attribute` shadows System.Attribute and breaks every generated helper attribute.
    * The 12 C++ reflection "classes" are opaque types whose methods are inline forwarders. C++
      `this` is a pointer, C# struct `this` is a value, so ClangSharp emits `(T*)(this)`, which
      does not compile. They are excluded and hand-declared as empty opaque structs; the ~173 flat
      spReflection* functions they forward to are bound regardless, and those are the real ABI.
    * SLANG_COM_INTERFACE's constexpr GUID cannot be expressed as a C# fixed-buffer initializer.
    * Callback function-pointer typedefs are mis-inferred as Thiscall. This one matters: it lands
      in ISlangFileSystemExt::enumeratePathContents. Left unfixed it would be an ABI mismatch.
    * The lint findings in the generated layer (AGENTS.md#lint) are fixed here: no
      [assembly: GeneratedCode] (it turned the analyzers off for the whole assembly), no
      case-only twins of the vtable methods, a message on every [Obsolete].

  Everything above L0 (lifetime, reflection projection, host callbacks) is hand-written by design.
  Do not point this script at those directories.

.PARAMETER SlangVersion
  Slang release tag to fetch headers from, e.g. "v2026.21". Must match the pinned native payload
  fetched by fetch-native.ps1 — Slang's ABI is explicitly not stable across versions.

.PARAMETER Output
  Destination for the generated sources. Defaults to dotnet/Native.
#>
param(
    [string]$SlangVersion,
    [string]$Output,
    [string]$Namespace = "SharpSlang.Native",
    [string]$Library   = "slang-compiler"   # NOT "slang" — renamed in v2025.21, alias dies end of 2026
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

# Defaults to the pin in Directory.Build.props, like fetch-native.ps1. Requiring it explicitly
# meant CI could not just run the script, and it invited the two tools drifting to different
# versions — the one thing the single-source-of-truth pin exists to prevent.
if (-not $SlangVersion) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    if ($props -notmatch '<SlangVersion>([^<]+)</SlangVersion>') {
        throw "SlangVersion not found in Directory.Build.props"
    }
    $SlangVersion = $Matches[1].Trim()
}
if (-not $Output) { $Output = Join-Path $root "dotnet" "Native" }

# Multi-segment Join-Path, not "obj\...": a literal backslash is a filename character on Linux,
# not a separator, so the string form silently creates a directory called `obj\slang-headers\...`.
$inc = Join-Path $root "obj" "slang-headers" $SlangVersion

# Prefer PATH — `dotnet tool install --global` puts the shim there on every platform. The explicit
# fallbacks cover a shell whose PATH has not picked up the tools directory yet, which is common
# right after installing in the same CI job.
$gen = $null
$onPath = Get-Command ClangSharpPInvokeGenerator -ErrorAction SilentlyContinue
if ($onPath) { $gen = $onPath.Source }
if (-not $gen) {
    foreach ($candidate in @(
        (Join-Path $HOME ".dotnet" "tools" "ClangSharpPInvokeGenerator.cmd"),
        (Join-Path $HOME ".dotnet" "tools" "ClangSharpPInvokeGenerator.exe"),
        (Join-Path $HOME ".dotnet" "tools" "ClangSharpPInvokeGenerator")
    )) {
        if (Test-Path $candidate) { $gen = $candidate; break }
    }
}
if (-not $gen) {
    throw "ClangSharpPInvokeGenerator not found on PATH or in ~/.dotnet/tools. Install it with:`n  dotnet tool install --global ClangSharpPInvokeGenerator"
}
Write-Host "==> generator: $gen"
Write-Host "==> slang:     $SlangVersion"

# ---------------------------------------------------------------- fetch pinned headers
New-Item -ItemType Directory -Force -Path $inc | Out-Null
$base = "https://raw.githubusercontent.com/shader-slang/slang/$SlangVersion/include"
foreach ($h in @("slang.h", "slang-deprecated.h", "slang-image-format-defs.h")) {
    $dest = Join-Path $inc $h
    if (-not (Test-Path $dest)) {
        Write-Host "==> fetching $h @ $SlangVersion"
        Invoke-WebRequest -Uri "$base/$h" -OutFile $dest
    }
}
$headerFiles = @("slang.h", "slang-deprecated.h", "slang-image-format-defs.h") | ForEach-Object { Join-Path $inc $_ }

# ---------------------------------------------------------------- generate
# The 12 C++ reflection veneer types. Excluded, then re-declared as opaque structs below.
$veneer = @(
    'Attribute', 'TypeReflection', 'TypeLayoutReflection', 'Modifier',
    'VariableReflection', 'VariableLayoutReflection', 'FunctionReflection',
    'GenericReflection', 'EntryPointReflection', 'TypeParameterReflection',
    'ShaderReflection', 'DeclReflection'
)

$exclude = @()
foreach ($t in $veneer) { $exclude += @('--exclude', $t) }
$exclude += @(
    '--exclude', 'getTypeGuid',          # SLANG_COM_INTERFACE constexpr GUID -> fixed buffer
    '--exclude', 'createGlobalSession'   # C++ default member initializers; hand-written in L1

    # The two functions in slang.h that are not extern "C". ClangSharp binds them by their MSVC
    # mangling — ?spReflection_GetSession@@YAPEAUISession@slang@@PEAUSlangProgramLayout@@@Z — which
    # compiles on every platform and can only ever resolve on Windows; on linux-x64 the symbol is
    # Itanium-mangled and the DllImport throws EntryPointNotFoundException at the first call.
    # Neither is used, and a binding that is silently Windows-only is worse than no binding.
    # NativeExportTests catches a regeneration that brings them back.
    '--exclude', 'spReflection_GetSession',
    '--exclude', 'slang_getEmbeddedCoreModule'
)

# Clear the previous output by deleting the .cs files rather than the directory itself: an IDE or
# a lingering MSBuild node commonly holds a handle on the folder, and that must not fail a rebuild.
New-Item -ItemType Directory -Force -Path $Output | Out-Null
Get-ChildItem (Join-Path $Output "*.cs") -ErrorAction SilentlyContinue | Remove-Item -Force -Confirm:$false

Write-Host "==> generating -> $Output"
# Only slang.h is passed as --file: it #includes slang-deprecated.h itself, so listing that as a
# second --file parses it twice and libclang reports redefinition errors. It still needs to appear
# in --traverse for its ~182 reflection declarations to be emitted.
& $gen `
    --file (Join-Path $inc "slang.h") `
    --file-directory $inc --include-directory $inc `
    --traverse (Join-Path $inc "slang.h") (Join-Path $inc "slang-deprecated.h") (Join-Path $inc "slang-image-format-defs.h") `
    --language c++ --std c++17 `
    --namespace $Namespace --method-class-name SlangApi --library-path $Library `
    --output $Output `
    --remap-type Attribute=SlangAttribute `
    @exclude `
    --config codegen=latest file=multi `
    --generate file-scoped-namespaces helper-types vtbl-index-attribute generated-code=none

# ClangSharp exits non-zero on macro-translation warnings even when generation succeeded, so
# verify by output rather than by exit code.
$produced = @(Get-ChildItem (Join-Path $Output "*.cs"))
if ($produced.Count -lt 100) { throw "generation produced only $($produced.Count) files - expected ~126" }

# ---------------------------------------------------------------- post-process
Write-Host "==> post-processing"

# Callback function-pointer typedefs mis-inferred as Thiscall. These are plain C callbacks.
# ABI-critical: ISlangFileSystemExt::enumeratePathContents is in this set.
$thiscall = 0
foreach ($p in $produced) {
    $t = Get-Content $p.FullName -Raw
    if ($t -match 'unmanaged\[Thiscall\]') {
        $thiscall += ([regex]::Matches($t, 'unmanaged\[Thiscall\]')).Count
        ($t -replace 'unmanaged\[Thiscall\]', 'unmanaged[Cdecl]') | Set-Content $p.FullName -NoNewline
    }
}
Write-Host "    rewrote $thiscall Thiscall -> Cdecl"

# Three C++ inline helpers doing pointer arithmetic on `this`.
$patches = @(
    @{ File = "ISlangSharedLibrary.cs"
       From = 'public delegate* unmanaged[Cdecl]<ISlangSharedLibrary*, void> findFuncByName'
       To   = 'public delegate* unmanaged[Cdecl]<void> findFuncByName' },
    @{ File = "SlangTerminatedChars.cs"
       From = '        return chars;'
       To   = '        return (sbyte*)Unsafe.AsPointer(ref Unsafe.AsRef(in chars));' },
    @{ File = "VMExecInstHeader.cs"
       From = 'return (VMExecInstHeader*)((VMExecOperand*)(this + 1) + operandCount);'
       To   = 'return (VMExecInstHeader*)((VMExecOperand*)((VMExecInstHeader*)Unsafe.AsPointer(ref this) + 1) + operandCount);' },
    @{ File = "VMExecInstHeader.cs"
       From = 'return ((VMExecOperand*)(this + 1) + index);'
       To   = 'return ((VMExecOperand*)((VMExecInstHeader*)Unsafe.AsPointer(ref Unsafe.AsRef(in this)) + 1) + index);' },
    # SpecializationArg::Kind has a member named `Type`. The generated `using static ...Kind;`
    # makes it ambiguous with System.Type as soon as the consuming project enables ImplicitUsings.
    # Qualify the uses and drop the using-static rather than forcing ImplicitUsings off repo-wide.
    @{ File = "SpecializationArg.cs"
       From = "using static $Namespace.SpecializationArg.Kind;"
       To   = '' },
    @{ File = "SpecializationArg.cs"
       From = 'rs.kind = Type;'
       To   = 'rs.kind = SpecializationArg.Kind.Type;' },
    @{ File = "SpecializationArg.cs"
       From = 'rs.kind = Expr;'
       To   = 'rs.kind = SpecializationArg.Kind.Expr;' }
)
foreach ($patch in $patches) {
    $p = Join-Path $Output $patch.File
    if (-not (Test-Path $p)) { Write-Warning "patch target missing: $($patch.File)"; continue }
    $t = Get-Content $p -Raw
    # .Contains, NOT -like: the patch strings contain [] which -like reads as a wildcard char class.
    if (-not $t.Contains($patch.From)) { Write-Warning "patch no longer applies in $($patch.File) - upstream changed, review manually"; continue }
    ($t -replace [regex]::Escape($patch.From), $patch.To) | Set-Content $p -NoNewline
}

# VMExecInstHeader.cs has no usings of its own.
$vm = Join-Path $Output "VMExecInstHeader.cs"
if ((Test-Path $vm) -and ((Get-Content $vm -Raw) -notmatch 'using System\.Runtime\.CompilerServices;')) {
    "using System.Runtime.CompilerServices;`r`n" + (Get-Content $vm -Raw) | Set-Content $vm -NoNewline
}

# ---------------------------------------------------------------- opaque reflection handles
@"
// <auto-generated-support/> Emitted by tools/generate-native.ps1. Do not edit by hand.
//
// These 12 types are opaque C++ structs in slang.h: no fields, no vtable, and every method an
// inline forwarder to a flat spReflection* C function. There is nothing for a generator to bind,
// and ClangSharp's attempt does not compile (C++ ``this`` is a pointer; C# struct ``this`` is not).
// They are excluded from generation and declared here only so the COM interfaces returning T*
// have a type to point at. The managed object model over them is hand-written in L3 against the
// flat SlangApi functions. See AGENTS.md#reflection-snapshot.
//
// NOTE: SlangAttribute, not Attribute — a type named ``Attribute`` in this namespace shadows
// System.Attribute and breaks every generated [NativeTypeName]/[VtblIndex] helper attribute.

namespace $Namespace;

$($veneer | ForEach-Object { "public partial struct $(if ($_ -eq 'Attribute') { 'SlangAttribute' } else { $_ }) { }" } | Out-String)
"@ | Set-Content (Join-Path $Output "_OpaqueReflectionHandles.cs")

# ---------------------------------------------------------------- interface GUIDs
# SLANG_COM_INTERFACE(a, b, c, {d0..d7}) is a constexpr returning a SlangUUID. ClangSharp renders
# it as `new byte[8]{...}` into a fixed buffer, which does not compile, so getTypeGuid is excluded
# above. The GUIDs are pure data, so we parse them out of the header instead. Without these,
# queryInterface / castAs are unusable.
Write-Host "==> extracting interface GUIDs"
$hdr = Get-Content (Join-Path $inc "slang.h") -Raw
# The macro does not always sit immediately after the opening brace (IByteCodeRunner and
# IModulePrecompileService_Experimental have doc comments and typedefs first), so allow any
# brace-balanced run between the two.
$guidMatches = [regex]::Matches(
    $hdr,
    '(?ms)(?:struct|class)\s+(?<name>[A-Za-z_0-9]+)[^;{]*?\{(?:[^{}]|\{[^{}]*\})*?SLANG_COM_INTERFACE\s*\(\s*(?<a>0x[0-9a-fA-F]+)\s*,\s*(?<b>0x[0-9a-fA-F]+)\s*,\s*(?<c>0x[0-9a-fA-F]+)\s*,\s*\{(?<d>[^}]*)\}\s*\)')

$guidLines = foreach ($g in $guidMatches) {
    $d = ($g.Groups['d'].Value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($d.Count -ne 8) { Write-Warning "GUID for $($g.Groups['name'].Value) has $($d.Count) trailing bytes - skipped"; continue }
    $n = $g.Groups['name'].Value
    if ($n -eq 'Attribute') { $n = 'SlangAttribute' }
    "    /// <summary>IID for <c>$n</c>.</summary>`r`n" +
    "    public static ref readonly Guid $n => ref Unsafe.As<byte, Guid>(ref MemoryMarshal.GetReference(Iid_$n));`r`n" +
    "    private static ReadOnlySpan<byte> Iid_$n => GuidBytes($($g.Groups['a'].Value), $($g.Groups['b'].Value), $($g.Groups['c'].Value), $($d -join ', '));`r`n"
}
Write-Host "    extracted $($guidMatches.Count) interface GUIDs"

# Completeness check: every generated COM interface must have an IID, or queryInterface/castAs on
# it is dead code. A silently missing IID is exactly the failure this whole script exists to avoid.
$guidNames = $guidMatches | ForEach-Object { $_.Groups['name'].Value }

# ICompileRequest predates Slang's COM conversion: it is a plain vtable type with no
# SLANG_COM_INTERFACE and therefore no IID, by design. It belongs to the 83-function legacy
# compile-request API that SharpSlang does not bind (AGENTS.md#unbound-surface).
$noIidByDesign = @('ICompileRequest')

$declaredIfaces = @(Get-ChildItem (Join-Path $Output "I*.cs") | ForEach-Object { $_.BaseName })
$missingIid = @($declaredIfaces | Where-Object { $_ -notin $guidNames -and $_ -notin $noIidByDesign })
if ($missingIid.Count -gt 0) {
    throw "generated interfaces with no IID: $($missingIid -join ', '). The SLANG_COM_INTERFACE parse missed them."
}

@"
// <auto-generated-support/> Emitted by tools/generate-native.ps1. Do not edit by hand.
//
// SLANG_COM_INTERFACE's constexpr SlangUUID cannot be expressed as a C# fixed-buffer initializer,
// so getTypeGuid() is excluded from generation and the IIDs are parsed out of slang.h here.
// Required by queryInterface / castAs. See AGENTS.md#generator-and-abi.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace $Namespace;

public static class SlangIID
{
    // Slang lays SlangUUID out as {uint32 data1; uint16 data2; uint16 data3; uint8 data4[8]},
    // identical to Windows GUID, so the little-endian byte order below matches System.Guid.
    private static ReadOnlySpan<byte> GuidBytes(uint a, ushort b, ushort c, params byte[] d) => new byte[16]
    {
        (byte)(a & 0xFF), (byte)((a >> 8) & 0xFF), (byte)((a >> 16) & 0xFF), (byte)((a >> 24) & 0xFF),
        (byte)(b & 0xFF), (byte)((b >> 8) & 0xFF),
        (byte)(c & 0xFF), (byte)((c >> 8) & 0xFF),
        d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7],
    };

$($guidLines -join "`r`n")
}
"@ | Set-Content (Join-Path $Output "_InterfaceGuids.cs")

# ---------------------------------------------------------------- normalise
# Over every file, so that everything after it reads and writes LF.
#
# `using System;` goes: both projects that compile these files (descs/SharpSlang.Descs.csproj and,
# for SlangApi.cs, dotnet/SharpSlang.csproj; NativeSplit.props) have ImplicitUsings on, which makes
# it a global using, and the build's IDE0005 then reports it as unnecessary. Measured with
# ClangSharpPInvokeGenerator 21.1.8.4 on v2026.21: 33 files carry it, 32 of ClangSharp's and the
# IID table above. A finding in this layer is fixed here, never by hand.
#
# And LF, in every file. ClangSharp writes LF, but Set-Content writes the platform's newline, so on
# Windows the files this script touched came out CRLF while the rest stayed LF. .gitattributes
# normalises them on add either way; writing LF here means the working tree is what is committed.
function Write-Lf([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false)) }

foreach ($p in @(Get-ChildItem (Join-Path $Output "*.cs"))) {
    $t = [System.IO.File]::ReadAllText($p.FullName)
    $n = ($t -replace "`r`n", "`n") -replace '(?m)^using System;\n', ''
    if (-not $n.EndsWith("`n")) { $n += "`n" }
    if ($n -ne $t) { Write-Lf $p.FullName $n }
}

# ---------------------------------------------------------------- lint findings, fixed here
# The generated layer is linted like the rest (AGENTS.md#lint). Measured at v2026.21 with the SDK's
# analyzers at latest-recommended, once ClangSharp's [assembly: GeneratedCode] was gone (which had
# made the analyzers treat SharpSlang.dll, hand-written layers included, as generated code and
# report nothing): the findings that are ClangSharp's choices are fixed below; the ones that are
# Slang's names and C layout are exempt in .editorconfig with their reasons.
#
# 1. ClangSharp's COM-name twins of the IUnknown slots. For every interface deriving
#    ISlangUnknown it writes QueryInterface(Guid*), AddRef() and Release() beside Slang's
#    queryInterface, addRef and release, which slang.h declares inline on ISlangUnknown "to allow
#    the above operations to be called using their traditional COM names". Two public methods
#    whose names differ only by case (CA1708, 85 findings), each forwarding to the slot; removed.
#    The slots stay, and the binding never called the twins.
$twin = '(?m)^(    \[return: NativeTypeName\("[^"]*"\)\]\n)?    public \w+ (QueryInterface|AddRef|Release)\([^\n]*\)\n    \{\n        return [^\n]*;\n    \}\n\n'
$twins = 0
foreach ($p in @(Get-ChildItem (Join-Path $Output "*.cs"))) {
    $t = [System.IO.File]::ReadAllText($p.FullName)
    $c = ([regex]::Matches($t, $twin)).Count
    if ($c) { $twins += $c; Write-Lf $p.FullName ([regex]::Replace($t, $twin, '')) }
}
if ($twins % 3 -ne 0 -or $twins -eq 0) { throw "removed $twins COM-name twins, not three per interface: ClangSharp's helper layout changed" }
Write-Host "    removed $twins COM-name twins ($($twins / 3) interfaces x QueryInterface, AddRef, Release)"

# 2. [Obsolete] with no message (CA1041): the two IGlobalSession slots slang.h marks
#    SLANG_DEPRECATED (createCompileRequest, addBuiltins). The message says where it comes from.
$obsolete = 0
foreach ($p in @(Get-ChildItem (Join-Path $Output "*.cs"))) {
    $t = [System.IO.File]::ReadAllText($p.FullName)
    $c = ([regex]::Matches($t, '(?m)^(\s*)\[Obsolete\]$')).Count
    if ($c) { $obsolete += $c; Write-Lf $p.FullName ($t -replace '(?m)^(\s*)\[Obsolete\]$', '$1[Obsolete("slang.h declares this SLANG_DEPRECATED.")]') }
}
Write-Host "    gave $obsolete [Obsolete] a message"

# 3. A fixed buffer's indexer as a block-bodied getter (IDE0026, SlangTerminatedChars): written
#    as the expression body the repository's style asks for.
$indexer = '(?m)^(\s*)(public ref \w+ this\[int index\])\n\s*\{\n\s*get\n\s*\{\n\s*return (ref [^\n]*);\n\s*\}\n\s*\}\n'
$indexers = 0
foreach ($p in @(Get-ChildItem (Join-Path $Output "*.cs"))) {
    $t = [System.IO.File]::ReadAllText($p.FullName)
    $c = ([regex]::Matches($t, $indexer)).Count
    if ($c) { $indexers += $c; Write-Lf $p.FullName ([regex]::Replace($t, $indexer, "`$1`$2 => `$3;`n")) }
}
Write-Host "    wrote $indexers fixed-buffer indexers as expression bodies"

# ---------------------------------------------------------------- enum types are kept
# A parameter, field or return slang.h declares with an enum type must be that enum in L0, not an
# integer: the bytes would line up and the type would be gone. ClangSharp writes the C++ type it
# translated into [NativeTypeName] wherever the C# spelling differs, so an enum name there on an
# integer is an erased enum. 0 at v2026.21; the integer typedefs slang.h declares for flags
# (SlangTargetFlags, SlangCompileFlags, SlangDiagnosticFlags, CompileCoreModuleFlags, SessionFlags)
# are integers in the header too.
$enums = @{}
foreach ($m in (Select-String -Path (Join-Path $Output "*.cs") -Pattern '^\s*public enum (\w+)')) { $enums[$m.Matches[0].Groups[1].Value] = $true }
$erased = @()
foreach ($p in @(Get-ChildItem (Join-Path $Output "*.cs"))) {
    $t = [System.IO.File]::ReadAllText($p.FullName)
    foreach ($m in [regex]::Matches($t, 'NativeTypeName\("(?:const )?(?:slang::)?(\w+)"\)\]\s*(?:\[[^\]]*\]\s*)*(?:public )?(?:static )?(?:extern )?(\w+)\b')) {
        if (-not $enums.ContainsKey($m.Groups[1].Value)) { continue }
        if ($m.Groups[2].Value -in 'byte', 'sbyte', 'short', 'ushort', 'int', 'uint', 'long', 'ulong', 'nint', 'nuint') { $erased += "$($p.Name): $($m.Groups[1].Value) as $($m.Groups[2].Value)" }
    }
}
if ($erased) { throw "enum types erased to integers: $($erased -join ', ')" }
Write-Host "    enum types kept: $($enums.Count) enums, 0 erased to an integer"

# ---------------------------------------------------------------- header comments as XML docs
# Last: it reads the layout every step above produced.
& (Join-Path $PSScriptRoot "generate-native-docs.ps1") -Headers @($headerFiles) -Output $Output

# ---------------------------------------------------------------- report
$files = @(Get-ChildItem (Join-Path $Output "*.cs"))
$lines = ($files | Get-Content | Measure-Object -Line).Lines
$imports = (Select-String -Path (Join-Path $Output "*.cs") -Pattern 'DllImport').Count
$vtbl = (Select-String -Path (Join-Path $Output "*.cs") -Pattern 'delegate\* unmanaged').Count
$refl = ((Select-String -Path (Join-Path $Output "SlangApi.cs") -Pattern 'spReflection[A-Za-z_0-9]*\(' -AllMatches).Matches.Value | Sort-Object -Unique).Count

Write-Host ""
Write-Host "==> done  ($SlangVersion)"
Write-Host "    files              : $($files.Count)"
Write-Host "    lines              : $lines"
Write-Host "    DllImport bindings : $imports"
Write-Host "    vtable call sites  : $vtbl"
Write-Host "    spReflection* bound: $refl"
Write-Host ""
Write-Host "Review the diff before committing. Vtable ORDER changes are silent ABI breaks --"
Write-Host "pay particular attention to reordered [VtblIndex] values."
