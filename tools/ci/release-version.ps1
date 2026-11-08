<#
.SYNOPSIS
  release.yml's version job: the versions a release packs, read the way MSBuild reads them, and the
  refusals before anything is built.

.DESCRIPTION
  The versions live in Directory.Build.props (AGENTS.md#native-packages): SharpSlangVersion is the
  release (SharpSlang, SharpSlang.Dxc, SharpSlang.Pipeline.Bake, SharpSlang.Pipeline.NRI),
  SharpSlangDescsVersion and SharpSlangPipelineVersion move only when their sources change. Each
  project's <Version> is the text $(SharpSlangVersion) or the like, so reading the csproj text
  gives the property's name, not its value: publish.yml did that and would have refused every
  release (AGENTS.md#actions-budget). This asks MSBuild for each project's evaluated Version and
  requires it to equal the property it names in the props.

  Refuses: a run on another branch than master, a tag v<Version> that exists already (on origin),
  and missing notes docs/releases/v<Version>.md. Writes version, tag, descs_version and
  pipeline_version to $GITHUB_OUTPUT when it is set, and prints them.

        pwsh tools/ci/release-version.ps1 -Ref refs/heads/master
#>
param(
    [string]$Ref = $env:GITHUB_REF,
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [switch]$NoTagCheck
)

$ErrorActionPreference = 'Stop'

$props = Get-Content (Join-Path $Root 'Directory.Build.props') -Raw
function Get-Prop([string]$Name) {
    if ($props -notmatch "<$Name>([^<]+)</$Name>") { throw "$Name not found in Directory.Build.props" }
    $Matches[1].Trim()
}
$pins = [ordered]@{
    SharpSlangVersion         = Get-Prop 'SharpSlangVersion'
    SharpSlangDescsVersion    = Get-Prop 'SharpSlangDescsVersion'
    SharpSlangPipelineVersion = Get-Prop 'SharpSlangPipelineVersion'
}

# Each packed project and the property its version is.
$projects = [ordered]@{
    'dotnet/SharpSlang.csproj'                    = 'SharpSlangVersion'
    'dxc/SharpSlang.Dxc.csproj'                   = 'SharpSlangVersion'
    'bake/SharpSlang.Pipeline.Bake.csproj'        = 'SharpSlangVersion'
    'pipeline-nri/SharpSlang.Pipeline.NRI.csproj' = 'SharpSlangVersion'
    'descs/SharpSlang.Descs.csproj'               = 'SharpSlangDescsVersion'
    'pipeline/SharpSlang.Pipeline.csproj'         = 'SharpSlangPipelineVersion'
}
$rows = foreach ($p in $projects.Keys) {
    $evaluated = (dotnet msbuild (Join-Path $Root $p) -getProperty:Version -nologo -p:SharpSlangRequireNative=false 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild -getProperty:Version failed for ${p}: $evaluated" }
    $want = $pins[$projects[$p]]
    if ($evaluated -cne $want) { throw "$p evaluates Version to '$evaluated'; $($projects[$p]) in Directory.Build.props is '$want'" }
    [pscustomobject]@{ Project = $p; Property = $projects[$p]; Version = $evaluated }
}
$rows | Format-Table -AutoSize | Out-String | Write-Host

$version = $pins.SharpSlangVersion
$tag = "v$version"
$notes = Join-Path $Root "docs/releases/$tag.md"

if ($Ref -ne 'refs/heads/master') { throw "a release runs on master only, not '$Ref'" }
if (-not (Test-Path $notes)) { throw "release notes docs/releases/$tag.md missing: add them in the version-bump pull request (docs/releases/README.md)" }
if (-not $NoTagCheck) {
    git -C $Root ls-remote --exit-code --tags origin "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "$tag exists already: bump SharpSlangVersion for a new release" }
    if ($LASTEXITCODE -ne 2) { throw "git ls-remote failed ($LASTEXITCODE); cannot tell whether $tag exists" }
    $global:LASTEXITCODE = 0   # 2 is a missing tag, the good case
}

$out = [ordered]@{ version = $version; tag = $tag; descs_version = $pins.SharpSlangDescsVersion; pipeline_version = $pins.SharpSlangPipelineVersion }
$lines = $out.Keys | ForEach-Object { "$_=$($out[$_])" }
if ($env:GITHUB_OUTPUT) { $lines | Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8 }
$lines | Write-Output
