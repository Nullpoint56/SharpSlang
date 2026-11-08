<#
.SYNOPSIS
  Regenerate the SharpSlang L0 raw interop layer from the Slang headers.

.DESCRIPTION
  Runs ClangSharpPInvokeGenerator over slang.h + slang-deprecated.h and applies the post-processing
  that ClangSharp cannot do itself. Validated end to end against Slang v2026.21 with
  ClangSharp 21.1.8.4: 129 files, ~5,100 lines, 279 DllImports, 383 vtable call sites, 0 errors,
  0 warnings on net11.0.

  See docs/design.md §4.3 for why each workaround below exists. The short version:

    * `struct Attribute` shadows System.Attribute and breaks every generated helper attribute.
    * The 12 C++ reflection "classes" are opaque types whose methods are inline forwarders. C++
      `this` is a pointer, C# struct `this` is a value, so ClangSharp emits `(T*)(this)`, which
      does not compile. They are excluded and hand-declared as empty opaque structs; the ~173 flat
      spReflection* functions they forward to are bound regardless, and those are the real ABI.
    * SLANG_COM_INTERFACE's constexpr GUID cannot be expressed as a C# fixed-buffer initializer.
    * Nested C++ enum mirrors default to int backing but initialize from uint-backed C constants.
    * Callback function-pointer typedefs are mis-inferred as Thiscall. This one matters: it lands
      in ISlangFileSystemExt::enumeratePathContents. Left unfixed it would be an ABI mismatch.

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
    --generate file-scoped-namespaces helper-types vtbl-index-attribute

# ClangSharp exits non-zero on macro-translation warnings even when generation succeeded, so
# verify by output rather than by exit code.
$produced = @(Get-ChildItem (Join-Path $Output "*.cs"))
if ($produced.Count -lt 100) { throw "generation produced only $($produced.Count) files - expected ~126" }

# ---------------------------------------------------------------- post-process
Write-Host "==> post-processing"

# Nested C++ enum mirrors: int-backed by default, but initialized from uint-backed C enum members.
foreach ($f in @("TypeReflection.cs", "DeclReflection.cs")) {
    $p = Join-Path $Output $f
    if (Test-Path $p) {
        (Get-Content $p) -replace '^(\s*public enum (?:Kind|ScalarType))$', '$1 : uint' | Set-Content $p
    }
}

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
// flat SlangApi functions. See docs/design.md §3.2 and §4.4.
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
# compile-request API that SharpSlang does not bind (docs/design.md §3.3, Tier 4).
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
// Required by queryInterface / castAs. See docs/design.md §4.3.

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
# Last, over every file, so nothing after it can undo it.
#
# `using System;` goes: both projects that compile these files (dotnet/SharpSlang.csproj and,
# for the reader enums, pipeline/SharpSlang.Pipeline.csproj) have ImplicitUsings on, which makes
# it a global using, and the build's IDE0005 then reports it as unnecessary. Measured with
# ClangSharpPInvokeGenerator 21.1.8.4 on v2026.21: 33 files carry it, 32 of ClangSharp's and the
# IID table above. A finding in this layer is fixed here, never by hand.
#
# And LF, in every file. ClangSharp writes LF, but Set-Content writes the platform's newline, so on
# Windows the files this script touched came out CRLF while the rest stayed LF. .gitattributes
# normalises them on add either way; writing LF here means the working tree is what is committed.
foreach ($p in @(Get-ChildItem (Join-Path $Output "*.cs"))) {
    $t = [System.IO.File]::ReadAllText($p.FullName)
    $n = ($t -replace "`r`n", "`n") -replace '(?m)^using System;\n', ''
    if (-not $n.EndsWith("`n")) { $n += "`n" }
    if ($n -ne $t) { [System.IO.File]::WriteAllText($p.FullName, $n, [System.Text.UTF8Encoding]::new($false)) }
}

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
