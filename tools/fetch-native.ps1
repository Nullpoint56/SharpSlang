<#
.SYNOPSIS
  Download the pinned Slang release for a RID and stage the runtime subset.

.DESCRIPTION
  PrismSlang vendors no native source. It consumes Slang's own prebuilt release, pinned exactly,
  because Slang's public ABI is not stable (docs/design.md §3.5).

  The published archives are large (54 MB win-x64, 76 MB linux-x64) and most of that is irrelevant
  to a Vulkan renderer embedding the compiler:

    bin/slang-llvm.dll        82 MB   CPU/host codegen. Not used for SPIR-V.
    bin/slang-compiler.dll    25 MB   the compiler itself. Required.
    bin/slang-glslang.dll      6 MB   glslang + spirv-opt.
    bin/slang-glsl-module.dll  1.5 MB GLSL *input* support.
    bin/gfx.dll                1 MB   Slang's own graphics layer. We use Vortice.Vulkan.
    bin/slang*.exe                    tooling.
    bin/slang.dll                     legacy proxy; upstream removes it end of 2026.

  Hence -Profile:
    Minimal  slang-compiler only. Direct SPIR-V emission.
    Spirv    + slang-glslang, which also carries spirv-opt (needed above -O0).   [default]
    Glsl     + slang-glsl-module, if you ever feed GLSL source to Slang.

.PARAMETER Rid
  win-x64 | linux-x64 | linux-arm64 | osx-x64 | osx-arm64. Defaults to the host.
#>
param(
    [string]$SlangVersion,
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid,
    [ValidateSet('Minimal', 'Spirv', 'Glsl')]
    [string]$Profile = 'Spirv',
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

# ---------------------------------------------------------------- resolve version + RID
if (-not $SlangVersion) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    if ($props -notmatch '<SlangVersion>([^<]+)</SlangVersion>') { throw "SlangVersion not found in Directory.Build.props" }
    $SlangVersion = $Matches[1].Trim()
}
if (-not $Rid) {
    $Rid = if ($IsLinux) { if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'linux-arm64' } else { 'linux-x64' } }
           elseif ($IsMacOS) { if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' } }
           else { 'win-x64' }
}

$bare = $SlangVersion.TrimStart('v')
$asset = switch ($Rid) {
    'win-x64'     { "slang-$bare-windows-x86_64.zip" }
    'linux-x64'   { "slang-$bare-linux-x86_64.zip" }
    'linux-arm64' { "slang-$bare-linux-aarch64.zip" }
    'osx-x64'     { "slang-$bare-macos-x86_64.zip" }
    'osx-arm64'   { "slang-$bare-macos-aarch64.zip" }
}
$ext = if ($Rid.StartsWith('win')) { 'dll' } elseif ($Rid.StartsWith('osx')) { 'dylib' } else { 'so' }
$pre = if ($Rid.StartsWith('win')) { '' } else { 'lib' }

# ---------------------------------------------------------------- download (cached)
$cache = Join-Path $root "obj" "slang-dist" $SlangVersion
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$archive = Join-Path $cache $asset

if ($Force -and (Test-Path $archive)) { Remove-Item $archive -Force -Confirm:$false }
if (-not (Test-Path $archive)) {
    $url = "https://github.com/shader-slang/slang/releases/download/$SlangVersion/$asset"
    Write-Host "==> downloading $asset"
    try { Invoke-WebRequest -Uri $url -OutFile $archive }
    catch { throw "failed to download $url`n  Check that release $SlangVersion publishes an asset named '$asset'.`n  $_" }
}
Write-Host ("    archive {0:N1} MB" -f ((Get-Item $archive).Length / 1MB))

# ---------------------------------------------------------------- extract
$staged = Join-Path $cache $Rid
if (-not (Test-Path (Join-Path $staged "bin"))) {
    Write-Host "==> extracting"
    Expand-Archive -Path $archive -DestinationPath $staged -Force
}

# ---------------------------------------------------------------- select the runtime subset
#
# Layout differs per platform in three ways, all discovered by actually looking in the archives:
#   * Windows keeps shared libraries in bin/; Linux and macOS keep them in lib/.
#   * Unix libraries carry versioned sonames (libslang-compiler.so.0.2026.14.1) with a plain-named
#     symlink beside them, and glslang embeds the version mid-name
#     (libslang-glslang-2026.14.1.so). So match by glob, not by exact filename.
#   * Expand-Archive on Windows cannot create symlinks: it writes them as ~32-byte text stubs.
#     Staging a cross-RID payload from Windows therefore has to copy the real versioned file and
#     synthesise the plain name as a real copy.
$libDir = if ($Rid.StartsWith('win')) { Join-Path $staged 'bin' } else { Join-Path $staged 'lib' }

$components = @(@{ Match = 'slang-compiler'; Canonical = "$($pre)slang-compiler.$ext" })
if ($Profile -in 'Spirv', 'Glsl') { $components += @{ Match = 'slang-glslang';    Canonical = "$($pre)slang-glslang.$ext" } }
if ($Profile -eq 'Glsl')          { $components += @{ Match = 'slang-glsl-module'; Canonical = "$($pre)slang-glsl-module.$ext" } }

$dest = Join-Path $root "runtimes" $Rid "native"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Get-ChildItem (Join-Path $dest "*") -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -Confirm:$false

Write-Host "==> staging profile '$Profile' -> runtimes/$Rid/native"
foreach ($c in $components) {
    # Ignore extracted symlink stubs; the real library is always the largest match.
    $real = Get-ChildItem $libDir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "*$($c.Match)*" -and $_.Name -notlike '*.lib' -and $_.Length -gt 4096 } |
            Sort-Object Length -Descending | Select-Object -First 1

    if (-not $real) { throw "no '$($c.Match)' library found in $libDir - the $Rid release layout changed" }

    Copy-Item $real.FullName (Join-Path $dest $real.Name)
    Write-Host ("    {0,-40} {1,8:N1} MB" -f $real.Name, ($real.Length / 1MB))

    # The loader resolves DllImport("slang-compiler") through the plain name, and Slang loads its
    # downstream compilers by plain name too, so both have to exist.
    if ($real.Name -ne $c.Canonical) {
        Copy-Item $real.FullName (Join-Path $dest $c.Canonical)
        Write-Host ("    {0,-40} {1,8}" -f $c.Canonical, "(alias)")
    }
}

# The standard modules ship as loose .slang-module files beside the compiler. The core module is
# embedded in slang-compiler, but `import` of anything outside it resolves through this directory.
$stdModules = Get-ChildItem $libDir, (Join-Path $staged 'bin') -Directory -Filter "slang-standard-module-*" -ErrorAction SilentlyContinue |
              Select-Object -First 1
if ($stdModules) {
    Copy-Item $stdModules.FullName (Join-Path $dest $stdModules.Name) -Recurse
    Write-Host ("    {0,-40} {1,8:N1} MB" -f $stdModules.Name, ((Get-ChildItem $stdModules.FullName -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
}

# Slang's licence texts, staged beside the payload rather than inside it.
#
# The PrismSlang package redistributes these binaries, so it has to carry the terms they come under
# - Apache-2.0 WITH LLVM-exception for Slang, plus the LICENSES directory covering what Slang itself
# vendors. THIRD-PARTY-NOTICES.md describes them and this is what makes the description true.
#
# runtimes/<rid>/ and not runtimes/<rid>/native/: NuGet copies everything under native/ into the
# consumer's output directory, and licence files do not belong next to a shipped game's binaries.
# One level up they travel with the package and stop there. The csproj packs runtimes/** already,
# so nothing needs to know these exist.
#
# Staged as LICENSE.txt because NuGet treats an extensionless PackagePath as a *directory*: the
# first attempt packed Slang's LICENSE to runtimes/win-x64/LICENSE/win-x64/LICENSE. The sibling
# LICENSES/*.txt files were fine, which is what made the one bad path easy to miss.
$licenceRoot = Join-Path $root "runtimes" $Rid

# Everything beside native/ is cleared first, so staging is idempotent across a change to this
# script and not only across a change to the payload. Renaming LICENSE to LICENSE.txt left the old
# name behind on the machine that made the change, and `dotnet pack` shipped both.
Get-ChildItem $licenceRoot -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'native' } |
    Remove-Item -Recurse -Force -Confirm:$false

foreach ($item in @(@{ From = 'LICENSE'; To = 'LICENSE.txt' }, @{ From = 'LICENSES'; To = 'LICENSES' })) {
    $source = Join-Path $staged $item.From
    if (-not (Test-Path $source)) {
        Write-Warning "no $($item.From) in the $Rid distribution - the release layout changed, and the package would ship the binaries without their terms"
        continue
    }

    Copy-Item $source (Join-Path $licenceRoot $item.To) -Recurse
    Write-Host ("    {0,-40} {1,8}" -f $item.To, "(licence)")
}

$total = (Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ""
Write-Host ("==> done. {0} payload: {1:N1} MB  (archive was {2:N1} MB)" -f $Rid, ($total / 1MB), ((Get-Item $archive).Length / 1MB))
