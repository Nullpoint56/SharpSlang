<#
.SYNOPSIS
    Assembles a GitHub release body: Bound libraries, the notes, API changes, Packages.

.DESCRIPTION
    Bound libraries lists every library tools/ci/packages.psd1 names (Slang, dxc, SharpNRI.Descs),
    each at its pin from Directory.Build.props, linked, with the package that brings it. The middle
    is docs/releases/<tag>.md (Added, Fixed, Breaking changes, Verified: compact lists of what
    happened, no why or how), then API changes from -ApiChanges (tools/ci/api-changes.ps1: the public
    surface against the previous release, mechanical), then Packages: a nuget.org link per package
    and version, a reused one (-Reused, descs-version.ps1's publish=false) marked so. release.yml
    runs it; run it locally to preview or to rewrite a published body.

        pwsh tools/ci/release-body.ps1 -Tag v2026210.0.0-beta.1 -ApiChanges api-changes.md -Out body.md
        pwsh tools/ci/release-body.ps1 -Tag v2026210.0.0-beta.2 -Reused SharpSlang.Descs,SharpSlang.Pipeline -ApiChanges api-changes.md -Out body.md

    The tag's version is the release pin ({Version}); the other pins come from Directory.Build.props
    unless -Pins names them.
#>
param(
    [Parameter(Mandatory)] [string]$Tag,
    [Parameter(Mandatory)] [string]$Out,
    [string]$ApiChanges,
    [string[]]$Reused = @(),
    [object]$Pins = @{},             # hashtable, or "Name=value" strings
    [string]$Notes,                  # default docs/releases/<Tag>.md
    [string]$Gallery = "https://www.nuget.org/packages",
    [string]$Spec
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Packages.psm1") -Force
$root = Get-RepoRoot
$data = Import-PackageSpec $Spec
$overrides = ConvertTo-Map $Pins
$overrides['Version'] = $Tag.TrimStart('v')
$pinValues = Get-Pins $data $root $overrides
$Reused = @($Reused | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

if (-not $Notes) { $Notes = Join-Path $root "docs/releases/$Tag.md" }
if (-not (Test-Path $Notes)) { throw "release notes $Notes missing" }
$noteLines = @(Get-Content $Notes)
if ($noteLines[0] -notmatch '^## ') { throw "$Notes must start with a section (## Added, ## Fixed, ## Breaking changes, ## Verified); Bound libraries is generated" }
$allowed = 'Added', 'Fixed', 'Breaking changes', 'Verified'
$sections = @($noteLines | Where-Object { $_ -match '^## ' } | ForEach-Object { $_.Substring(3).Trim() })
foreach ($s in $sections) { if ($allowed -notcontains $s) { throw "$Notes has the section '$s'; the notes hold only $($allowed -join ', '), in that order" } }
$order = @($sections | ForEach-Object { [array]::IndexOf($allowed, $_) })
if (($order -join ',') -ne (($order | Sort-Object) -join ',')) { throw "$Notes sections are out of order: $($sections -join ', '); the order is $($allowed -join ', ')" }

# Bound libraries, from the pins
$bound = @("## Bound libraries")
$patchCount = @(Get-ChildItem (Join-Path $root "patches") -Filter *.patch -ErrorAction SilentlyContinue).Count
foreach ($l in $data.Libraries) {
    $v = $pinValues[$l.Pin]
    if (-not $v) { throw "no value for the $($l.Name) pin {$($l.Pin)}" }
    $note = if ($l.ContainsKey('Via')) { " (restored by the application through $($l.Via))" } elseif ($patchCount) { " ($patchCount patches)" } else { "" }
    $bound += "- $($l.Name): [$v]($(Expand-Template $l.Url $pinValues))$note"
}

$api = if ($ApiChanges) { @("") + @((Get-Content $ApiChanges -Raw).TrimEnd() -split "`r?`n") } else { @() }

# Packages: every package this tree packs, at its version
$packages = @("", "## Packages")
foreach ($p in $data.Packages) {
    if ($p.Optional -and -not ($p.Project -and (Test-Path (Join-Path $root $p.Project)))) { continue }
    $v = Get-PackageVersion $p $pinValues
    $link = "[$($p.Id) $v]($Gallery/$($p.Id)/$v)"
    $packages += "- $link$(if ($Reused -contains $p.Id) { ' (reused: unchanged, the published package)' })"
}

$body = $bound + @("") + $noteLines + $api + $packages
$body | Set-Content $Out -Encoding utf8NoBOM
Get-Content $Out
