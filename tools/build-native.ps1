<#
.SYNOPSIS
  Build Slang from the pinned submodule and stage the native payload into runtimes/<rid>/.

.DESCRIPTION
  1. Checks that slang/ is checked out at the tag Directory.Build.props pins (SlangVersion), and
     that its own submodules (external/) are initialised.
  2. Applies patches/*.patch to slang/ in name order. A patch that is already applied (its reverse
     applies with -C0) is skipped, so the script is re-runnable. An empty patches/ is valid.
  3. Configures and builds Slang with CMake and Ninja into _build/<rid>/. Nothing is written into
     slang/. Every upstream option is passed explicitly (table below); after the configure, every
     SLANG_* cache entry CMake holds must be one this script passed or one it lists as derived, so
     a new upstream option fails the build instead of taking its default silently.
  4. Checks the load-time imports of each staged library against an exact list.
  5. Stages, through an allowlist, the layout the binding loads (dotnet/Interop/CompanionLibraries.cs):

       runtimes/<rid>/native/   slang-compiler, slang-glslang (spirv-opt lives there), the standard
                                modules directory (slang-standard-module-<version>/)
       runtimes/<rid>/          LICENSE.txt, LICENSES/, third-party-notices/  - outside native/,
                                because NuGet copies native/ flat into the application's output

     and prints what it staged. tests/SharpSlang.Tests/NativePayloadTests.cs holds the same lists.

  Options, all passed on every platform where Slang declares them (CMakeLists.txt at v2026.21).
  "Official" is what Slang's release workflow (.github/workflows/release.yml) builds with: the
  "default" preset plus -DSLANG_ENABLE_EXAMPLES=OFF -DSLANG_ENABLE_RELEASE_LTO=ON
  -DSLANG_STANDARD_MODULE_DEVELOP_BUILD=OFF, slang-llvm built from LLVM on x86_64.

    Option                                   Value    Why
    ---------------------------------------  -------  ----------------------------------------------
    SLANG_LIB_TYPE                           SHARED   the binding P/Invokes slang-compiler
    SLANG_EMBED_CORE_MODULE                  ON       official; the core module is inside the library
    SLANG_EMBED_CORE_MODULE_SOURCE           ON       official
    SLANG_ENABLE_SLANG_GLSLANG               ON       slang-glslang carries spirv-opt (docs/design.md §6)
    SLANG_ENABLE_DXIL                        ON       DXIL through a dxc the application brings
                                                      (SharpSlang.Native.Dxc). Compiles the DXC loader
                                                      in; FetchDXC.cmake also downloads DXC (below)
    SLANG_DXC_BUILD_FROM_SOURCE              OFF      never a 30-minute DXC source build; unset, macOS
                                                      builds DXC from source and Linux x86_64 does when
                                                      the host glibc is older than DXC's prebuilt needs
    SLANG_SLANG_LLVM_FLAVOR                  DISABLE  slang-llvm (CPU targets) is not staged; the default
                                                      FETCH_BINARY_IF_POSSIBLE downloads the whole
                                                      official release archive to take it from there
    SLANG_EXCLUDE_DAWN                       ON       its Windows default (OFF) downloads Dawn for the
                                                      test tools; ON everywhere else already
    SLANG_EXCLUDE_TINT                       ON       its default (OFF) downloads slang-tint on Windows
    SLANG_ENABLE_SLANG_RHI                   OFF      slang-rhi is for the tests and examples
    SLANG_ENABLE_GFX                         OFF      Slang's graphics layer; not staged
    SLANG_ENABLE_SLANGC / SLANGD / SLANGI    OFF      tools; not staged. The standard modules are
                                                      compiled by slang-bootstrap, not slangc
    SLANG_ENABLE_SLANGRT                     OFF      slang-rt is for CPU/host targets; not staged
    SLANG_ENABLE_SLANG_PROXY                 OFF      the legacy slang.dll; the binding binds slang-compiler
    SLANG_ENABLE_TESTS / EXAMPLES / REPLAYER OFF      not staged (TESTS requires SLANG_RHI)
    SLANG_ENABLE_RECORD_REPLAY               ON       official: OFF compiles the slang_*RecordLayer exports
                                                      as stubs, a different library under the same names
    SLANG_ENABLE_CUDA / OPTIX / NVAPI /      OFF      auto_option()s: AUTO enables whatever the build
      AFTERMATH                                       machine has installed. Only the tools use them
    SLANG_ENABLE_XLIB (Linux only)           OFF      auto_option() for gfx windows; declared on Linux only
    SLANG_ENABLE_DX_ON_VK                    OFF      test configuration
    SLANG_ENABLE_MIMALLOC                    ON win,  Slang's own default per platform (MSVC + SHARED
                                             OFF else -> ON), which the official build takes
    SLANG_ENABLE_SPIRV_TOOLS_MIMALLOC        ON win,  likewise
                                             OFF else
    SLANG_ENABLE_RELEASE_LTO                 ON       official
    SLANG_STANDARD_MODULE_DEVELOP_BUILD      OFF      official ("Disable for release builds")
    SLANG_BUILD_MONOLITHIC_STANDARD_MODULE_  OFF      loose .slang-module files, the layout the official
      LIBRARY                                         release and the binding use
    SLANG_ENABLE_RELEASE_DEBUG_INFO          ON       PDB / DWARF for the symbol zips; not staged
    SLANG_ENABLE_SPLIT_DEBUG_INFO            ON       debug info beside the library, the library stripped
    SLANG_ENABLE_PCH                         ON       build speed only
    SLANG_ENABLE_VALIDATION_VM_BYTECODE      ON       upstream default, official
    SLANG_ENABLE_VALIDATION_IR / _FOSSIL,    OFF      debugging aids
      SLANG_ENABLE_IR_BREAK_ALLOC
    SLANG_ENABLE_ASAN / TSAN / COVERAGE /    OFF      instrumentation
      TIME_TRACE
    SLANG_IGNORE_ABORT_MSG                   OFF      upstream default, official
    SLANG_USE_SCCACHE                        OFF      and $env:SLANG_USE_SCCACHE is cleared: Slang turns
                                                      it on from the environment
    SLANG_INSTALL_USER_SKILLS                OFF      install-time only
    SLANG_USE_SYSTEM_* (8)                   OFF      every dependency from slang/external/ at the pin
    SLANG_OVERRIDE_*_PATH (15)               OFF      likewise
    SLANG_SPIRV_HEADERS_INCLUDE_DIR          OFF      likewise
    SLANG_ENABLE_EXTERNAL_COMPILER_WARNINGS  OFF      upstream default (read in external/, not declared)
    SLANG_DISABLE_NOTE_IDS                   ""       upstream default
    SLANG_GENERATORS_PATH                    ""       native build, no cross-compiled generators
    SLANG_VERSION_FULL / _NUMERIC            <pin>    Slang reads its version from `git describe`, which
                                                      gives 0.0.0 in a checkout without tags (CI's
                                                      submodule fetch) and changes the library and module
                                                      directory names. Passed from the checked pin.
    CMAKE_MSVC_RUNTIME_LIBRARY (Windows)     static   MultiThreaded, as Slang's own "default" preset
    CMAKE_OSX_DEPLOYMENT_TARGET (macOS)      15.0     .NET 11's oldest macOS, as SharpNRI
    CMAKE_OSX_ARCHITECTURES (macOS)          arm64
    CMAKE_BUILD_WITH_INSTALL_RPATH (not      ON       the staged libraries carry the release's RPATH;
      Windows)                                        the build tree's has an empty entry (see $opt)

  Configure-time downloads with these options, measured on win-x64 and linux-x64: only DXC, by
  slang/cmake/FetchDXC.cmake through FetchContent into _build/<rid>/_deps/dxc-src, SHA-256 pinned
  there (Windows dxc_<date>.zip, Linux x86_64 linux_dxc_<date>.x86_64.tar.gz; none on macOS or Linux
  arm64, which have no prebuilt). DXC is not staged: Slang only copies it beside slangc, which is off.
  The offline-dependency key (_build/<rid>/_deps/.sharpslang-deps) is the Slang commit, every nested
  submodule commit, the patch set, the option set, the compiler and the DXC pin. While it matches,
  the configure runs with FETCHCONTENT_FULLY_DISCONNECTED=ON and touches no network; when it moves,
  the whole build tree is deleted, downloads included.

.PARAMETER Rid
  win-x64 (default on Windows), linux-x64 / linux-arm64 (default on Linux), osx-arm64 (default on
  macOS). Must match the host OS; this script does not cross-compile.

.PARAMETER StandardModules
  All (default): every standard module Slang builds, the same set fetch-native.ps1 stages from the
  official release. WithoutNeuralNumerics: drops the slang.neural and slang.numerics modules (31 MB
  of the 37 MB directory at v2026.21; `import slang.neural` / `import slang.numerics` then fails),
  keeping experimental/workgraph and slang/functional. Staging only: the build is the same. The
  payload contract in NativePayloadTests is All's; a different set must be reconciled there.

.PARAMETER WithGlslModule
  Also stage slang-glsl-module (GLSL source input), as fetch-native.ps1 -Profile Glsl did. Off by
  default, like the payload CI and the packages carry.

.PARAMETER AllowNewerGlibc
  Linux: stage a build whose libraries need a newer GLIBC_ or GLIBCXX_ than Directory.Build.props
  pins (LinuxGlibcFloor, LinuxGlibcxxFloor) instead of failing. A build on Ubuntu 24.04 needs
  GLIBC_2.38 and GLIBCXX_3.4.32 where the pin, Slang's release, is 2.34 and 3.4.29. For local test
  builds only; NativePayloadTests fails on such a payload unless SHARPSLANG_ALLOW_NEWER_GLIBC=1.

.PARAMETER Configuration
  CMake configuration. Release by default.

.PARAMETER Clean
  Delete the build tree (including downloads) before configuring.
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "linux-arm64", "osx-arm64")]
    [string]$Rid,
    [ValidateSet("All", "WithoutNeuralNumerics")]
    [string]$StandardModules = "All",
    [switch]$WithGlslModule,
    [switch]$AllowNewerGlibc,
    [string]$Configuration = "Release",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 3.0
$PSNativeCommandUseErrorActionPreference = $false

$root  = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$slang = Join-Path $root "slang"

function Invoke-Native {
    param([string]$What, [scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE" }
}

$started = [System.Diagnostics.Stopwatch]::StartNew()

# ------------------------------------------------------------------------------------------ target
if (-not $Rid) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $a = if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { "arm64" } else { "x64" }
    $Rid = if ($IsWindows) { "win-$a" } elseif ($IsLinux) { "linux-$a" } elseif ($IsMacOS) { "osx-$a" } else { throw "Unsupported host OS" }
}
$targetWindows = $Rid.StartsWith("win-")
$targetLinux   = $Rid.StartsWith("linux-")
$targetMac     = $Rid.StartsWith("osx-")
$targetArm64   = $Rid.EndsWith("-arm64")
if ($targetWindows -and -not $IsWindows) { throw "$Rid must be built on Windows" }
if ($targetLinux -and -not $IsLinux) { throw "$Rid must be built on Linux" }
if ($targetMac -and -not $IsMacOS) { throw "$Rid must be built on macOS" }
$hostArm64 = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64
if ($hostArm64 -ne $targetArm64) { throw "$Rid must be built on a host of that architecture" }

# ------------------------------------------------------------------------------------------ 1. pin
$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -notmatch '<SlangVersion>([^<]+)</SlangVersion>') { throw "SlangVersion not found in Directory.Build.props" }
$SlangVersion = $Matches[1].Trim()                  # v2026.21
$versionNumeric = $SlangVersion.TrimStart('v')      # 2026.21

if (-not (Test-Path (Join-Path $slang "CMakeLists.txt"))) {
    throw "slang/ is not checked out. Run: git submodule update --init --recursive slang"
}
$head = (git -C $slang rev-parse HEAD) -join ""
# The tag, not just the commit the superproject records: the commit is what moves when someone runs
# `git -C slang checkout` by hand. A checkout made by `git submodule update` may have no tags, so
# fetch the one tag (refs only, the working tree is not touched) when it is missing.
$tagCommit = (git -C $slang rev-parse --verify --quiet "refs/tags/$SlangVersion^{commit}" 2>$null) -join ""
if (-not $tagCommit) {
    $url = (git -C $root config -f .gitmodules submodule.slang.url) -join ""
    Write-Host "==> fetching tag $SlangVersion from $url"
    Invoke-Native "git fetch tag $SlangVersion" { git -C $slang fetch --quiet --no-tags $url "refs/tags/${SlangVersion}:refs/tags/$SlangVersion" }
    $tagCommit = (git -C $slang rev-parse --verify "refs/tags/$SlangVersion^{commit}") -join ""
}
if ($head -ne $tagCommit) {
    throw "slang/ is at $head but Directory.Build.props pins $SlangVersion ($tagCommit). Run: git -C slang checkout $SlangVersion; git submodule update --init --recursive slang"
}
# Slang's own submodules (external/). An uninitialised one is an empty directory, and CMake's error
# for it names a target, not the cause.
$nested = @(git -C $slang submodule status --recursive)
$uninitialised = @($nested | Where-Object { $_ -match '^-' })
if ($uninitialised) { throw "slang/ has uninitialised submodules:`n$($uninitialised -join "`n")`nRun: git -C slang submodule update --init --recursive" }
$moved = @($nested | Where-Object { $_ -match '^\+' })
if ($moved) { throw "slang/ submodules are not at the commits $SlangVersion records:`n$($moved -join "`n")`nRun: git -C slang submodule update --init --recursive" }
Write-Host "==> Slang $SlangVersion ($head), $Rid, $Configuration"

# ------------------------------------------------------------------------------------------ 2. patches
$patches = @(Get-ChildItem (Join-Path $root "patches") -Filter "*.patch" -File -ErrorAction SilentlyContinue | Sort-Object Name)
$sha = [System.Security.Cryptography.SHA256]::Create()
function Get-Hash([string]$Text) { [Convert]::ToHexString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Text))).Substring(0, 16) }
$patchHashInput = [System.Text.StringBuilder]::new()
foreach ($p in $patches) {
    [void]$patchHashInput.Append($p.Name).Append(':').Append([Convert]::ToHexString($sha.ComputeHash([System.IO.File]::ReadAllBytes($p.FullName)))).Append(';')
}
$patchHash = Get-Hash $patchHashInput.ToString()

Write-Host "==> patches: $($patches.Count)"
foreach ($p in $patches) {
    git -C $slang apply --check --reverse -C0 $p.FullName 2>$null  # -C0: a later patch may change a line within this one's context
    if ($LASTEXITCODE -eq 0) { Write-Host "    $($p.Name): already applied, skipped"; continue }
    git -C $slang apply --check $p.FullName
    if ($LASTEXITCODE -ne 0) { throw "patch $($p.Name) does not apply to slang/ at $SlangVersion" }
    Invoke-Native "git apply $($p.Name)" { git -C $slang apply $p.FullName }
    Write-Host "    $($p.Name): applied"
}

# ------------------------------------------------------------------------------------------ 3. options
# Ordered: name -> value. Each exists on the target (slang/CMakeLists.txt); the header says why.
$platformDefault = if ($targetWindows) { "ON" } else { "OFF" }
$opt = [ordered]@{
    SLANG_LIB_TYPE                                 = "SHARED"
    SLANG_EMBED_CORE_MODULE                        = "ON"
    SLANG_EMBED_CORE_MODULE_SOURCE                 = "ON"
    SLANG_ENABLE_SLANG_GLSLANG                     = "ON"
    SLANG_ENABLE_DXIL                              = "ON"
    SLANG_DXC_BUILD_FROM_SOURCE                    = "OFF"
    SLANG_SLANG_LLVM_FLAVOR                        = "DISABLE"
    SLANG_EXCLUDE_DAWN                             = "ON"
    SLANG_EXCLUDE_TINT                             = "ON"
    SLANG_ENABLE_SLANG_RHI                         = "OFF"
    SLANG_ENABLE_GFX                               = "OFF"
    SLANG_ENABLE_SLANGC                            = "OFF"
    SLANG_ENABLE_SLANGD                            = "OFF"
    SLANG_ENABLE_SLANGI                            = "OFF"
    SLANG_ENABLE_SLANGRT                           = "OFF"
    SLANG_ENABLE_SLANG_PROXY                       = "OFF"
    SLANG_ENABLE_TESTS                             = "OFF"
    SLANG_ENABLE_EXAMPLES                          = "OFF"
    SLANG_ENABLE_REPLAYER                          = "OFF"
    SLANG_ENABLE_RECORD_REPLAY                     = "ON"
    SLANG_ENABLE_CUDA                              = "OFF"
    SLANG_ENABLE_OPTIX                             = "OFF"
    SLANG_ENABLE_NVAPI                             = "OFF"
    SLANG_ENABLE_AFTERMATH                         = "OFF"
    SLANG_ENABLE_DX_ON_VK                          = "OFF"
    SLANG_ENABLE_MIMALLOC                          = $platformDefault
    SLANG_ENABLE_SPIRV_TOOLS_MIMALLOC              = $platformDefault
    SLANG_ENABLE_RELEASE_LTO                       = "ON"
    SLANG_STANDARD_MODULE_DEVELOP_BUILD            = "OFF"
    SLANG_BUILD_MONOLITHIC_STANDARD_MODULE_LIBRARY = "OFF"
    SLANG_ENABLE_RELEASE_DEBUG_INFO                = "ON"
    SLANG_ENABLE_SPLIT_DEBUG_INFO                  = "ON"
    SLANG_ENABLE_PCH                               = "ON"
    SLANG_ENABLE_VALIDATION_VM_BYTECODE            = "ON"
    SLANG_ENABLE_VALIDATION_IR                     = "OFF"
    SLANG_ENABLE_VALIDATION_FOSSIL                 = "OFF"
    SLANG_ENABLE_IR_BREAK_ALLOC                    = "OFF"
    SLANG_ENABLE_ASAN                              = "OFF"
    SLANG_ENABLE_TSAN                              = "OFF"
    SLANG_ENABLE_COVERAGE                          = "OFF"
    SLANG_ENABLE_TIME_TRACE                        = "OFF"
    SLANG_IGNORE_ABORT_MSG                         = "OFF"
    SLANG_USE_SCCACHE                              = "OFF"
    SLANG_INSTALL_USER_SKILLS                      = "OFF"
    SLANG_USE_SYSTEM_MINIZ                         = "OFF"
    SLANG_USE_SYSTEM_LZ4                           = "OFF"
    SLANG_USE_SYSTEM_LUA                           = "OFF"
    SLANG_USE_SYSTEM_VULKAN_HEADERS                = "OFF"
    SLANG_USE_SYSTEM_SPIRV_HEADERS                 = "OFF"
    SLANG_USE_SYSTEM_UNORDERED_DENSE               = "OFF"
    SLANG_USE_SYSTEM_SPIRV_TOOLS                   = "OFF"
    SLANG_USE_SYSTEM_GLSLANG                       = "OFF"
    SLANG_SPIRV_HEADERS_INCLUDE_DIR                = "OFF"
    SLANG_ENABLE_EXTERNAL_COMPILER_WARNINGS        = "OFF"
    SLANG_DISABLE_NOTE_IDS                         = ""
    SLANG_GENERATORS_PATH                          = ""
    SLANG_VERSION_FULL                             = $versionNumeric
    SLANG_VERSION_NUMERIC                          = $versionNumeric
}
foreach ($dep in "LZ4", "MINIZ", "UNORDERED_DENSE", "VULKAN_HEADERS", "SPIRV_HEADERS", "SPIRV_TOOLS", "GLSLANG",
                 "GLM", "IMGUI", "SLANG_RHI", "TINYOBJLOADER", "LUA", "MIMALLOC", "CMARK", "FAST_FLOAT") {
    $opt["SLANG_OVERRIDE_$($dep)_PATH"] = "OFF"
}
if ($targetLinux) { $opt.SLANG_ENABLE_XLIB = "OFF" }   # auto_option() on Linux only; a plain variable elsewhere
# The libraries are staged from the build tree, and CMake's build-tree RPATH is Slang's
# "$ORIGIN/../lib;$ORIGIN" plus an empty entry (measured: RUNPATH "$ORIGIN/../lib:$ORIGIN:" on
# linux-x64), which glibc expands to paths relative to the working directory, so a library there
# would be found first. The install RPATH, which the release carries, is exactly Slang's two entries.
if (-not $targetWindows) { $opt.CMAKE_BUILD_WITH_INSTALL_RPATH = "ON" }
$on = { param($n) $opt.Contains($n) -and $opt[$n] -eq "ON" }

# Cache entries Slang derives itself, or that its dependencies declare: accounted for, not passed.
# Anything SLANG_* in the cache outside $opt and this list is a new upstream option, and the script
# stops so that it is decided rather than defaulted.
$derived = @(
    'SLANG_STANDARD_MODULE_DIR_NAME', 'SLANG_NEURAL_MODULE_FILE_NAME', 'SLANG_WORKGRAPH_MODULE_FILE_NAME',
    'SLANG_STANDARD_MODULE_INSTALL_DIR', 'SLANG_STANDARD_MODULE_BUILD_SUBDIR', 'SLANG_STANDARD_MODULE_FILES',
    'SLANG_STANDARD_MODULE_TARGETS'
)

# ------------------------------------------------------------------------------------------ toolchain
# Slang turns sccache on from the environment whatever SLANG_USE_SCCACHE says (CMakeLists.txt).
Remove-Item Env:SLANG_USE_SCCACHE -ErrorAction SilentlyContinue

if ($targetWindows -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    # MSVC and the Ninja that Visual Studio ships, through Visual Studio's own developer shell.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio" "Installer" "vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found: install Visual Studio 2022 (or its Build Tools) with the C++ workload" }
    $vs = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath) -join ""
    if (-not $vs) { throw "no Visual Studio installation with the C++ x64 tools (Microsoft.VisualStudio.Component.VC.Tools.x86.x64)" }
    $env:PATH = "$(Split-Path $vswhere);$env:PATH"   # VsDevCmd's own scripts call vswhere by name
    Import-Module (Join-Path $vs "Common7" "Tools" "Microsoft.VisualStudio.DevShell.dll")
    Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=x64" | Out-Null
}
if (-not (Get-Command ninja -ErrorAction SilentlyContinue)) { throw "ninja not found on PATH (Windows: Visual Studio's CMake component ships it; Linux: apt install ninja-build; macOS: brew install ninja)" }

# SPIRV-Tools and glslang generate sources with Python at build time. Found here rather than left to
# CMake, which takes the first python on PATH even when it is a broken virtual environment.
$python = $null
foreach ($candidate in @(@("python3"), @("python"), @("py", "-3"))) {
    if (-not (Get-Command $candidate[0] -ErrorAction SilentlyContinue)) { continue }
    # Windows' python3.exe / python.exe app-execution aliases fail to start rather than exit non-zero.
    try { $exe = (& $candidate[0] @($candidate | Select-Object -Skip 1) -c "import sys; print(sys.executable)" 2>$null) -join "" } catch { continue }
    if ($LASTEXITCODE -eq 0 -and $exe -and (Test-Path $exe)) { $python = $exe; break }
}
if (-not $python) { throw "no working Python 3 found (python3, python, py -3); SPIRV-Tools and glslang need one to build" }

$compiler = @()
if ($targetLinux) {
    # What Slang's own Linux release builds with. CC/CXX override it, and say so.
    $cc  = if ($env:CC)  { $env:CC }  else { "gcc" }
    $cxx = if ($env:CXX) { $env:CXX } else { "g++" }
    $compiler = @("-DCMAKE_C_COMPILER=$cc", "-DCMAKE_CXX_COMPILER=$cxx")
}
$compilerVersion = if ($targetWindows) { ((& cl.exe 2>&1) | Select-Object -First 1) -join "" }
                   elseif ($targetLinux) { ((& $cxx --version) | Select-Object -First 1) -join "" }
                   else { ((& c++ --version) | Select-Object -First 1) -join "" }
Write-Host "==> compiler: $compilerVersion"
Write-Host "    cmake:    $(((cmake --version) | Select-Object -First 1) -join '')"
Write-Host "    ninja:    $((ninja --version) -join '')"
Write-Host "    python:   $python"

# ------------------------------------------------------------------------------------------ configure
$build = Join-Path $root "_build" $Rid
$deps  = Join-Path $build "_deps"
if ($Clean -and (Test-Path $build)) { Remove-Item $build -Recurse -Force -Confirm:$false }

$fwd = { param($p) $p -replace '\\', '/' }
$crt = if ($targetWindows) { 'MultiThreaded$<$<CONFIG:Debug>:Debug>' }

# The DXC pin FetchDXC.cmake downloads. Inside the Slang commit and the patches already; read out so
# the key names it.
$fetchDxc = Get-Content (Join-Path $slang "cmake" "FetchDXC.cmake") -Raw
$dxcPin = @('_dxc_version_tag', '_dxc_windows_sha256', '_dxc_linux_sha256' | ForEach-Object {
    if ($fetchDxc -match "set\($_\s+`"([^`"]+)`"") { "$_=$($Matches[1])" } else { throw "$_ not found in slang/cmake/FetchDXC.cmake" }
}) -join ","

$optString = (@($opt.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) + @(if ($crt) { "crt=$crt" })) -join ";"
# "<sha> <path>" only: the describe text git appends in parentheses depends on which tags are fetched.
$submoduleCommits = @($nested | ForEach-Object { ($_.Trim() -split '\s+')[0..1] -join ' ' }) -join "`n"
$depsKey = "slang=$SlangVersion;commit=$head;submodules=$(Get-Hash $submoduleCommits);patches=$patchHash;rid=$Rid;config=$Configuration;compiler=$compilerVersion;dxc=$dxcPin;$optString"
$depsMarker = Join-Path $deps ".sharpslang-deps"
$offline = (Test-Path $depsMarker) -and ((Get-Content $depsMarker -Raw).Trim() -eq $depsKey)

# A new Slang commit, patch set or option set starts the tree from scratch, downloads included: a
# cache entry outlives the default that created it, and FetchContent keeps an extracted archive in
# _deps across a pin change (SharpNRI measured both, AGENTS.md there).
if (-not $offline -and (Test-Path $build)) {
    Write-Host "==> Slang commit, patches or options changed since the last configure: fresh build tree, downloads again"
    Remove-Item -Recurse -Force $build -Confirm:$false
}
New-Item -ItemType Directory -Force -Path $build | Out-Null

$cmakeArgs = @("-S", $slang, "-B", $build, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=$Configuration") + $compiler
$cmakeArgs += @(
    "-DFETCHCONTENT_BASE_DIR=$(& $fwd $deps)"
    "-DFETCHCONTENT_FULLY_DISCONNECTED=$(if ($offline) { 'ON' } else { 'OFF' })"
    "-DPython3_EXECUTABLE=$(& $fwd $python)"
)
if ($crt) { $cmakeArgs += "-DCMAKE_MSVC_RUNTIME_LIBRARY=$crt" }   # Slang sets CMP0091 NEW itself
if ($targetMac) { $cmakeArgs += @("-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0", "-DCMAKE_OSX_ARCHITECTURES=arm64") }
foreach ($kv in $opt.GetEnumerator()) { $cmakeArgs += "-D$($kv.Key)=$($kv.Value)" }

Write-Host "==> configure -> $build$(if ($offline) { ' (deps cached, offline)' })"
foreach ($kv in $opt.GetEnumerator()) { Write-Host ("    {0,-48} {1}" -f $kv.Key, $kv.Value) }
if ($crt) { Write-Host ("    {0,-48} {1}" -f "CMAKE_MSVC_RUNTIME_LIBRARY", $crt) }
$configureTime = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-Native "cmake configure" { cmake @cmakeArgs }
$configureTime.Stop()
New-Item -ItemType Directory -Force -Path $deps | Out-Null
Set-Content -Path $depsMarker -Value $depsKey -NoNewline

# What CMake decided: every passed option must have stuck, and every SLANG_* entry must be accounted for.
$cache = @(Get-Content (Join-Path $build "CMakeCache.txt"))
$normal = { param($v) switch -Regex ($v) { '^(ON|TRUE|YES|1)$' { "ON" } '^(OFF|FALSE|NO|0)$' { "OFF" } default { $v } } }
$entries = [ordered]@{}
foreach ($line in $cache) {
    if ($line -cmatch '^(SLANG_[A-Za-z0-9_]+):([A-Z]+)=(.*)$' -and $Matches[2] -ne 'INTERNAL') { $entries[$Matches[1]] = $Matches[3] }
}
foreach ($kv in $opt.GetEnumerator()) {
    if ($entries.Contains($kv.Key) -and (& $normal $entries[$kv.Key]) -ne (& $normal $kv.Value)) {
        throw "$($kv.Key): requested '$($kv.Value)' but CMake has '$($entries[$kv.Key])'"
    }
}
$unknown = @($entries.Keys | Where-Object { -not $opt.Contains($_) -and $derived -notcontains $_ })
if ($unknown) { throw "slang/ declares options this script does not decide: $($unknown -join ', '). Add each to `$opt (and the header table) with a reason." }
# What was downloaded: only DXC is expected (header). slang-llvm, Dawn and slang-tint would land here
# or in the build tree's top level if an option above stopped holding.
$downloaded = @(Get-ChildItem $deps -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*-src' } | ForEach-Object { $_.Name })
Write-Host "    downloaded (_deps): $(if ($downloaded) { $downloaded -join ', ' } else { 'nothing' })"
$expectedDownloads = @(if ((& $on SLANG_ENABLE_DXIL) -and ($targetWindows -or ($targetLinux -and -not $targetArm64))) { "dxc-src" })
$extraDownloads = @($downloaded | Where-Object { $expectedDownloads -notcontains $_ })
if ($extraDownloads) { throw "the configure downloaded something no option asked for: $($extraDownloads -join ', ')" }

# ------------------------------------------------------------------------------------------ build
Write-Host "==> build ($Configuration, $([Environment]::ProcessorCount) logical processors)"
$buildTime = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-Native "cmake build" { cmake --build $build --config $Configuration --parallel }
$buildTime.Stop()

# Slang writes its outputs under <build>/<config>/{bin,lib}: shared libraries in bin/ on Windows and
# lib/ elsewhere, beside the standard modules.
$out = Join-Path $build $Configuration $(if ($targetWindows) { "bin" } else { "lib" })
$moduleDirName = "slang-standard-module-$versionNumeric"
$libs = if ($targetWindows) {
    [ordered]@{ "slang-compiler.dll" = "slang-compiler.dll"; "slang-glslang.dll" = "slang-glslang.dll" }
} elseif ($targetLinux) {
    # Staged under the names the binding and Slang ask for (tools/fetch-native.ps1 measured them):
    # the DllImport resolves libslang-compiler.so by its plain name, and Slang dlopens
    # libslang-glslang-<version>.so by the versioned one.
    [ordered]@{ "libslang-compiler.so" = "libslang-compiler.so.0.$versionNumeric"; "libslang-glslang-$versionNumeric.so" = "libslang-glslang-$versionNumeric.so" }
} else {
    [ordered]@{ "libslang-compiler.dylib" = "libslang-compiler.0.$versionNumeric.dylib"; "libslang-glslang-$versionNumeric.dylib" = "libslang-glslang-$versionNumeric.dylib" }
}
if ($WithGlslModule) {
    $glslModule = if ($targetWindows) { "slang-glsl-module.dll" } elseif ($targetLinux) { "libslang-glsl-module-$versionNumeric.so" } else { "libslang-glsl-module-$versionNumeric.dylib" }
    $libs[$glslModule] = $glslModule
}
foreach ($kv in $libs.GetEnumerator()) {
    if (-not (Test-Path (Join-Path $out $kv.Value) -PathType Leaf)) { throw "expected build output missing: $(Join-Path $out $kv.Value)" }
}

# ------------------------------------------------------------------------------------------ 4. imports
# Load-time dependencies of each staged library, exactly. Slang loads its companions (slang-glslang,
# dxcompiler, slang-llvm) itself at run time by name, so none may be a load-time import, and with the
# C runtime static none of the VC++ / UCRT DLLs may be either. The same lists, measured on the
# official v2026.21 release, are in NativePayloadTests.
if ($targetWindows) {
    $expectedImports = @{
        "slang-compiler.dll"    = @("ADVAPI32.dll", "KERNEL32.dll", "ole32.dll", "SHELL32.dll")
        "slang-glslang.dll"     = @("ADVAPI32.dll", "KERNEL32.dll")
        "slang-glsl-module.dll" = @("KERNEL32.dll")
    }
    $dumpbin = (Get-Command dumpbin.exe -ErrorAction SilentlyContinue).Source
    if (-not $dumpbin) { throw "dumpbin.exe not found; it comes with the MSVC tools this build used" }
    foreach ($kv in $libs.GetEnumerator()) {
        $section = ""; $loadTime = @(); $delayed = @()
        foreach ($l in & $dumpbin /nologo /dependents (Join-Path $out $kv.Value)) {
            if ($l -match 'following dependencies:') { $section = "load" }
            elseif ($l -match 'following delay load dependencies:') { $section = "delay" }
            elseif ($l -match '^\s+(\S+\.dll)\s*$') { if ($section -eq "load") { $loadTime += $Matches[1] } elseif ($section -eq "delay") { $delayed += $Matches[1] } }
            elseif ($l -match '^\s+Summary') { $section = "" }
        }
        Write-Host "==> $($kv.Key) imports: $($loadTime -join ', ')$(if ($delayed) { "; delay-loaded: $($delayed -join ', ')" })"
        $want = $expectedImports[$kv.Key]
        if ($want -and (Compare-Object ($loadTime | Sort-Object) ($want | Sort-Object))) { throw "$($kv.Key) load-time imports are not exactly $($want -join ', ')" }
        if ($delayed) { throw "$($kv.Key) delay-loads $($delayed -join ', '); the contract has none" }
    }
} elseif ($targetLinux) {
    $loader = if ($targetArm64) { "ld-linux-aarch64.so.1" } else { "ld-linux-x86-64.so.2" }
    $expectedNeeded = @("libstdc++.so.6", "libm.so.6", "libgcc_s.so.1", "libc.so.6", $loader)
    # The newest GLIBC_ / GLIBCXX_ version a library's version-needs section names: the oldest glibc
    # and libstdc++ it loads with. NativePayloadTests reads the same section.
    function Get-Newest([string[]]$VersionNeeds, [string]$Prefix) {
        $VersionNeeds | ForEach-Object { if ($_ -match "Name: $($Prefix)_(\d+(\.\d+)+)\b") { [version]$Matches[1] } } | Sort-Object | Select-Object -Last 1
    }
    $floor = [ordered]@{ GLIBC = [version]"0.0"; GLIBCXX = [version]"0.0" }
    foreach ($kv in $libs.GetEnumerator()) {
        $file = Join-Path $out $kv.Value
        $dyn = @(readelf -d $file)
        $needed = @($dyn | ForEach-Object { if ($_ -match '\(NEEDED\)\s+Shared library: \[(.+)\]') { $Matches[1] } })
        $runpath = @($dyn | ForEach-Object { if ($_ -match '\(RUNPATH\)\s+Library runpath: \[(.+)\]') { $Matches[1] } }) -join ""
        $versionNeeds = @(readelf -V --wide $file)
        $glibc = Get-Newest $versionNeeds "GLIBC"; $glibcxx = Get-Newest $versionNeeds "GLIBCXX"
        if ($glibc -gt $floor.GLIBC) { $floor.GLIBC = $glibc }
        if ($glibcxx -gt $floor.GLIBCXX) { $floor.GLIBCXX = $glibcxx }
        Write-Host "==> $($kv.Key) NEEDED: $($needed -join ', '); RUNPATH $runpath; needs GLIBC_$glibc, GLIBCXX_$glibcxx"
        if (Compare-Object ($needed | Sort-Object) ($expectedNeeded | Sort-Object)) { throw "$($kv.Key) NEEDED is not exactly $($expectedNeeded -join ', ')" }
        # dotnet/Interop/CompanionLibraries.cs relies on it: glibc's dlopen of a bare name searches the
        # calling object's DT_RUNPATH, which is how Slang finds slang-glslang beside itself.
        # Exactly Slang's install RPATH: an empty entry would search the working directory.
        if ($runpath -cne '$ORIGIN/../lib:$ORIGIN') { throw "$($kv.Key) RUNPATH is '$runpath', not exactly `$ORIGIN/../lib:`$ORIGIN" }
    }
    # The floor this build produced, against the one Directory.Build.props pins (the release's).
    $pinned = [ordered]@{}
    foreach ($p in @(@("GLIBC", "LinuxGlibcFloor"), @("GLIBCXX", "LinuxGlibcxxFloor"))) {
        if ($props -notmatch "<$($p[1])>([^<]+)</$($p[1])>") { throw "$($p[1]) not found in Directory.Build.props" }
        $pinned[$p[0]] = [version]$Matches[1].Trim()
    }
    Write-Host "==> glibc floor of this build: GLIBC_$($floor.GLIBC), GLIBCXX_$($floor.GLIBCXX) (pinned: GLIBC_$($pinned.GLIBC), GLIBCXX_$($pinned.GLIBCXX))"
    $above = @($floor.Keys | Where-Object { $floor[$_] -gt $pinned[$_] } | ForEach-Object { "$($_)_$($floor[$_]) > $($_)_$($pinned[$_])" })
    if ($above) {
        $why = "the payload needs $($above -join ' and '), newer than Directory.Build.props pins (LinuxGlibcFloor, LinuxGlibcxxFloor), so it would not load on the distributions the pin promises. The build host's glibc and libstdc++ set it: build on Ubuntu 22.04 (glibc 2.35, GCC 11), as CI does"
        if (-not $AllowNewerGlibc) { throw "$why. For a local test build only, pass -AllowNewerGlibc; NativePayloadTests then fails on it unless SHARPSLANG_ALLOW_NEWER_GLIBC=1." }
        Write-Warning "$why. Staged anyway (-AllowNewerGlibc): a local test build, not one to pack."
    }
} else {
    # UNVERIFIED: written without a Mac. The system only: libc++ and libSystem, plus the library's own
    # install name; Homebrew or build-machine paths would not exist on a user's Mac.
    foreach ($kv in $libs.GetEnumerator()) {
        $file = Join-Path $out $kv.Value
        $linked = @(otool -L $file | Select-Object -Skip 1 | ForEach-Object { ($_.Trim() -split ' \(')[0] })
        Write-Host "==> $($kv.Key) links: $($linked -join ', ')"
        $foreign = @($linked | Where-Object { $_ -notmatch '^(/usr/lib/libc\+\+\.1\.dylib|/usr/lib/libSystem\.B\.dylib|@rpath/libslang-[a-z-]+[0-9.]*\.dylib)$' })
        if ($foreign) { throw "$($kv.Key) links something other than the system: $($foreign -join ', ')" }
        $buildVersion = @(otool -l $file | Select-String -Pattern '^\s*(minos|sdk)\s' | ForEach-Object { $_.Line.Trim() })
        Write-Host "    build version: $($buildVersion -join ', ')"
        if ($buildVersion -notcontains "minos 15.0") { throw "$($kv.Key) is not built for macOS 15.0 ($($buildVersion -join ', '))" }
        # The release's LC_RPATHs (read from its v2026.21 archive): how Slang's dlopen of a leaf name
        # finds slang-glslang beside the compiler.
        $loadCommands = @(otool -l $file)
        $rpaths = @(for ($i = 0; $i -lt $loadCommands.Count; $i++) {
            if ($loadCommands[$i] -match '^\s*cmd LC_RPATH$' -and $loadCommands[$i + 2] -match '^\s*path (\S+) \(offset') { $Matches[1] }
        })
        Write-Host "    rpaths: $($rpaths -join ', ')"
        if (($rpaths -join ',') -cne '@loader_path/../lib,@loader_path') { throw "$($kv.Key) LC_RPATHs are not exactly @loader_path/../lib, @loader_path" }
    }
}

# ------------------------------------------------------------------------------------------ 5. stage
$stageRoot = Join-Path $root "runtimes" $Rid
$native = Join-Path $stageRoot "native"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force -Path $native | Out-Null
$staged = [System.Collections.Generic.List[string]]::new()
function Stage([string]$Source, [string]$RelDest) {
    if (-not (Test-Path $Source -PathType Leaf)) { throw "expected file missing: $Source" }
    $dest = Join-Path $stageRoot $RelDest
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item $Source $dest -Force   # follows a symlink: the staged file is the real library
    $script:staged.Add(($RelDest -replace '\\', '/'))
}

foreach ($kv in $libs.GetEnumerator()) { Stage (Join-Path $out $kv.Value) "native/$($kv.Key)" }

# The standard modules, as Slang's build lays them out. -StandardModules WithoutNeuralNumerics leaves
# out the slang.neural module (its .slang-module and the .slang sources it is built from, which Slang
# ships beside it) and slang.numerics (numerics.slang-module and numerics/).
$modules = Join-Path $out $moduleDirName
if (-not (Test-Path $modules -PathType Container)) { throw "standard modules missing: $modules" }
$neuralSources = @(Get-ChildItem (Join-Path $slang "source" "standard-modules" "neural") -Filter "*.slang" -File | ForEach-Object { "slang/$($_.Name)" })
$dropped = [System.Collections.Generic.List[string]]::new()
foreach ($f in Get-ChildItem $modules -Recurse -File | Sort-Object FullName) {
    $rel = [System.IO.Path]::GetRelativePath($modules, $f.FullName) -replace '\\', '/'
    $isNeural   = $rel -eq "slang/neural.slang-module" -or $neuralSources -contains $rel
    $isNumerics = $rel -eq "slang/numerics.slang-module" -or $rel.StartsWith("slang/numerics/")
    if ($StandardModules -eq "WithoutNeuralNumerics" -and ($isNeural -or $isNumerics)) { $dropped.Add($rel); continue }
    Stage $f.FullName "native/$moduleDirName/$rel"
}

# Licences, beside native/ (NuGet copies native/ flat into the application). Slang's own LICENSE and
# LICENSES/, as fetch-native.ps1 stages from the release, and the third-party notices Slang's install
# step ships for what it links statically (slang/CMakeLists.txt, "third-party-notices"), under the
# same conditions. The notices get a .txt extension: NuGet packs an extensionless file as a directory
# (fetch-native.ps1 measured it with LICENSE).
Stage (Join-Path $slang "LICENSE") "LICENSE.txt"
foreach ($f in Get-ChildItem (Join-Path $slang "LICENSES") -File | Sort-Object Name) { Stage $f.FullName "LICENSES/$($f.Name)" }
$notices = [ordered]@{
    "cmark-COPYING.txt"         = "external/cmark/COPYING"
    "lz4-LICENSE.txt"           = "external/lz4/lib/LICENSE"
    "miniz-LICENSE.txt"         = "external/miniz/LICENSE"
    "unordered_dense-LICENSE.txt" = "external/unordered_dense/LICENSE"
}
if (& $on SLANG_ENABLE_SLANG_GLSLANG) {
    $notices["glslang-LICENSE.txt"] = "external/glslang/LICENSE.txt"
    $notices["spirv-tools-LICENSE.txt"] = "external/spirv-tools/LICENSE"
}
if ((& $on SLANG_ENABLE_MIMALLOC) -or (& $on SLANG_ENABLE_SPIRV_TOOLS_MIMALLOC)) { $notices["mimalloc-LICENSE.txt"] = "external/mimalloc/LICENSE" }
foreach ($kv in $notices.GetEnumerator()) { Stage (Join-Path $slang $kv.Value) "third-party-notices/$($kv.Key)" }

# Everything else the build wrote beside the libraries: debug info (the symbol zips' input), import
# libraries, the unstaged GLSL module, DXC, which Slang fetched but which the application brings,
# the plain-named symlinks of the Unix compiler, and slang.exe: source/slang-dispatcher, a launcher
# Slang builds whatever the options (it has none), written beside the DLLs on Windows.
$ignored = '\.(pdb|lib|exp|ilk|a|dwarf)$|^(lib)?slang-glsl-module|^(lib)?(dxcompiler|dxil)\.|^libslang-compiler\.so(\.0)?$|^libslang-compiler(\.0)?\.dylib$|^slang\.exe$|^' + [regex]::Escape($moduleDirName) + '/'
$stagedSources = @($libs.Values) + @(Get-ChildItem $modules -Recurse -File | ForEach-Object { "$moduleDirName/" + ([System.IO.Path]::GetRelativePath($modules, $_.FullName) -replace '\\', '/') })
foreach ($f in Get-ChildItem $out -Recurse -File) {
    $rel = [System.IO.Path]::GetRelativePath($out, $f.FullName) -replace '\\', '/'
    if ($stagedSources -contains $rel -or $rel -match $ignored) { continue }
    Write-Warning "not staged: $rel (review whether the binding needs it at run time)"
}

$total = 0L
$rows = foreach ($rel in $staged) {
    $len = (Get-Item (Join-Path $stageRoot $rel)).Length
    $total += $len
    [pscustomobject]@{ File = $rel; Bytes = $len }
}
Write-Host ""
Write-Host "==> staged into $stageRoot"
$rows | Where-Object { $_.File -notmatch "^native/$([regex]::Escape($moduleDirName))/" } |
    Format-Table -AutoSize File, @{ n = "MB"; e = { "{0:N2}" -f ($_.Bytes / 1MB) }; a = "right" }, @{ n = "Bytes"; e = { $_.Bytes }; a = "right" } | Out-String | Write-Host
$moduleBytes = ($rows | Where-Object { $_.File -match "^native/$([regex]::Escape($moduleDirName))/" } | Measure-Object Bytes -Sum)
Write-Host ("    native/{0}/: {1} files, {2:N1} MB ({3})" -f $moduleDirName, $moduleBytes.Count, ($moduleBytes.Sum / 1MB), $StandardModules)
if ($dropped.Count) { Write-Host "    left out by -StandardModules ${StandardModules}: $($dropped.Count) files" }
$nativeBytes = ($rows | Where-Object { $_.File -like "native/*" } | Measure-Object Bytes -Sum).Sum
Write-Host ("    {0} files; native/ {1:N1} MB; everything {2:N1} MB" -f $staged.Count, ($nativeBytes / 1MB), ($total / 1MB))

$started.Stop()
Write-Host ("==> configure {0:mm\:ss}, build {1:mm\:ss}, total {2:mm\:ss} on {3} logical processors" -f $configureTime.Elapsed, $buildTime.Elapsed, $started.Elapsed, [Environment]::ProcessorCount)
