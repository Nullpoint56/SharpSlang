<#
.SYNOPSIS
    The release body's "API changes": this pack's packages against a baseline's, through tools/apidiff.

.DESCRIPTION
    The baseline is one of:

      -Baseline <dir>        a folder of .nupkg files (a previous release's assets, a local pack)
      -BaselineRef <ref>     a git ref: its tree is exported (git archive, no checkout touched) and
                             its solution packed into a temporary folder; no native payload is needed
                             for the API
      (neither)              nuget.org: the SharpSlang version just before -Version, with the
                             versions its nuspec depends on for the independently versioned packages
                             and the same version for the rest (tools/ci/packages.psd1)

    tools/apidiff compares every package's public types and members (signatures and values) and
    writes Markdown for release-body.ps1. -Package Old=New and -Rename Old=New carry a rename or a
    split across the comparison (PrismSlang.Runtime=SharpSlang.Pipeline, PrismSlang=SharpSlang).

        pwsh tools/ci/api-changes.ps1 -New nupkg -Out api-changes.md -BaselineRef v2026.21.0-beta.1
        pwsh tools/ci/api-changes.ps1 -New nupkg -Out api-changes.md -Version 2026.21.0-beta.2
#>
param(
    [Parameter(Mandatory)] [string]$New,
    [Parameter(Mandatory)] [string]$Out,
    [string]$Baseline,
    [string]$BaselineRef,
    [string]$Version,
    [string[]]$Package = @(),
    [string[]]$Rename = @(),
    [string]$Feed = "https://api.nuget.org/v3-flatcontainer",
    [string]$Spec
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Packages.psm1") -Force
$root = Get-RepoRoot
$data = Import-PackageSpec $Spec
$temp = Join-Path ([System.IO.Path]::GetTempPath()) "apidiff-baseline-$PID"

if ($BaselineRef) {
    $sha = (git -C $root rev-parse --verify "$BaselineRef^{commit}").Trim()
    $tree = Join-Path $temp "tree"
    New-Item -ItemType Directory -Force $tree | Out-Null
    $tar = Join-Path $temp "tree.tar"
    git -C $root archive --format=tar -o $tar $sha
    if ($LASTEXITCODE -ne 0) { throw "git archive $BaselineRef failed" }
    tar -xf $tar -C $tree
    if ($LASTEXITCODE -ne 0) { throw "extracting $BaselineRef failed" }
    $solution = @(Get-ChildItem $tree -Filter *.slnx) + @(Get-ChildItem $tree -Filter *.sln) | Select-Object -First 1
    if (-not $solution) { throw "$BaselineRef has no solution at its root" }
    $Baseline = Join-Path $temp "nupkg"
    Write-Host "baseline: $BaselineRef ($($sha.Substring(0, 12))), packing $($solution.Name)"
    dotnet pack $solution.FullName -c Release -o $Baseline --nologo -v q "-p:SourceRevisionId=$sha" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "packing $BaselineRef failed" }
    $label = $sha.StartsWith($BaselineRef, [StringComparison]::OrdinalIgnoreCase) ? $sha.Substring(0, 7) : "$BaselineRef ($($sha.Substring(0, 7)))"
}
elseif ($Baseline) {
    $label = Split-Path (Resolve-Path $Baseline) -Leaf
}
else {
    if (-not $Version) { throw "give -Baseline, -BaselineRef, or -Version (the release being made, to find the one before it on $Feed)" }
    $main = @($data.Packages | Where-Object { $_.Version -eq 'Version' -and $_.Assemblies })[0].Id
    $lower = $main.ToLowerInvariant()
    $published = @((Invoke-RestMethod "$Feed/$lower/index.json").versions)
    $earlier = @($published | Where-Object { $_ -ne $Version.ToLowerInvariant() })
    if (-not $earlier) { throw "no earlier $main on $Feed to compare with" }
    $previous = $earlier[-1]
    $Baseline = Join-Path $temp "nupkg"
    New-Item -ItemType Directory -Force $Baseline | Out-Null
    function Get-Package([string]$id, [string]$v) {
        $l = $id.ToLowerInvariant(); $lv = $v.ToLowerInvariant()
        $file = Join-Path $Baseline "$id.$v.nupkg"
        try { Invoke-WebRequest "$Feed/$l/$lv/$l.$lv.nupkg" -OutFile $file; $file } catch { Write-Warning "$id $v is not on $Feed"; $null }
    }
    $mainFile = Get-Package $main $previous
    $deps = @(Get-NuspecDependencies (Read-Nupkg $mainFile).Nuspec)
    foreach ($p in $data.Packages | Where-Object { $_.Id -ne $main -and $_.Assemblies }) {
        $dep = @($deps | Where-Object Id -eq $p.Id)[0]
        $v = if ($p.Version -ne 'Version' -and $dep) { $dep.Version.Trim('[', ']').Split(',')[0].Trim() } else { $previous }
        Get-Package $p.Id $v | Out-Null
    }
    $label = "v$previous"
}

# "A=B,C=D" from pwsh -File, or an array
$Package = @($Package | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Rename = @($Rename | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$diffArgs = @($Baseline, $New, $Out, $label)
foreach ($p in $Package) { $diffArgs += "--package", $p }
foreach ($r in $Rename) { $diffArgs += "--rename", $r }
dotnet run --project (Join-Path $root "tools" "apidiff") -c Release -- @diffArgs
$code = $LASTEXITCODE
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
if ($code -ne 0) { throw "tools/apidiff failed ($code)" }
