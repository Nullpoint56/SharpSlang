<#
.SYNOPSIS
    Checks each independently versioned package's version against nuget.org: a new version is
    published, a published one must still describe the same sources.

.DESCRIPTION
    SharpSlang.Descs is versioned apart from the release (Directory.Build.props), so a binding-only
    release leaves everything built on the descs untouched; SharpSlang.Pipeline, the reader, is
    versioned the same way, so a game that ships only the reader takes no new package for a change
    to the compiler side. That holds only while a published version keeps meaning one assembly.
    For every package tools/ci/packages.psd1 gives Sources:

      - its version not on the feed yet: this release publishes it ("publish=true").
      - published: the package names the commit it was built from (<repository commit>), and its
        sources must be unchanged since; the release then reuses the published package
        ("publish=false"). Changed: fails, naming the paths, until its version moves. A
        documentation comment counts as a change.

    Sources are git pathspecs (SharpSlang.Descs: dotnet/Native without SlangApi.cs, descs/,
    NativeSplit.props); with SourcesFromProject, also the project file and every file it compiles,
    so a file it links from elsewhere (the reader enums under dotnet/Native) counts. A package whose
    project does not exist yet (not split out) is skipped and said so.

    Needs the history back to the published commit (actions/checkout fetch-depth: 0). Writes
    <key>_version and <key>_publish per package to GITHUB_OUTPUT when it is set (key: the id,
    lower case, dots as underscores). Lint runs it. Locally:

        pwsh tools/ci/descs-version.ps1
        pwsh tools/ci/descs-version.ps1 -PublishedFrom SharpSlang.Pipeline=<sha>   # no feed: state the commit

    -PublishedFrom names, per id, the commit a published version was built from instead of asking
    the feed (a version published elsewhere, or a check without network).
#>
param(
    [string]$Feed = "https://api.nuget.org/v3-flatcontainer",
    [object]$Pins = @{},             # hashtable, or "Name=value" strings
    [object]$PublishedFrom = @{},    # hashtable, or "Id=commit" strings
    [string]$Spec
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Packages.psm1") -Force
$root = Get-RepoRoot
$data = Import-PackageSpec $Spec
$pinValues = Get-Pins $data $root (ConvertTo-Map $Pins)
$PublishedFrom = ConvertTo-Map $PublishedFrom

# The paths of a package's sources, as pathspecs relative to the root
function Get-SourcePaths($p) {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($s in $p.Sources) { $paths.Add($s) }
    if ($p.SourcesFromProject) {
        $project = Join-Path $root $p.Project
        $paths.Add($p.Project)
        $json = dotnet msbuild $project -getItem:Compile -nologo 2>$null
        if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild -getItem:Compile failed for $($p.Project)" }
        foreach ($item in (($json -join "`n") | ConvertFrom-Json).Items.Compile) {
            $rel = [IO.Path]::GetRelativePath($root, $item.FullPath) -replace '\\', '/'
            if (-not $rel.StartsWith('..') -and -not $rel.StartsWith('obj/') -and $rel -notmatch '/(obj|bin)/') { $paths.Add($rel) }
        }
    }
    @($paths | Select-Object -Unique)
}

$failed = @()
foreach ($p in $data.Packages | Where-Object { $_.Sources.Count -or $_.SourcesFromProject }) {
    $id = $p.Id
    $key = $id.ToLowerInvariant().Replace('.', '_')
    if (-not (Test-Path (Join-Path $root $p.Project))) {
        Write-Host "$id`: no $($p.Project) in this tree (not split out yet): nothing to check"
        continue
    }
    $version = Get-PackageVersion $p $pinValues
    $lower = $id.ToLowerInvariant()
    $commit = $PublishedFrom[$id]
    $publish = $false
    if (-not $commit) {
        $published = @()
        try { $published = @((Invoke-RestMethod "$Feed/$lower/index.json").versions) }
        catch { if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw } }   # 404: never published
        $publish = $published -notcontains $version.ToLowerInvariant()
        if (-not $publish) {
            [xml]$nuspec = (Invoke-WebRequest "$Feed/$lower/$($version.ToLowerInvariant())/$lower.nuspec").Content
            $commit = $nuspec.package.metadata.repository.commit
            if (-not $commit) { $failed += "the published $id $version names no commit"; continue }
        }
    }
    if ($publish) {
        Write-Host "$id $version is not on $Feed`: this release publishes it"
    }
    else {
        git -C $root cat-file -e "$commit^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) { $failed += "commit $commit ($id $version) is not in this clone: fetch the full history"; continue }
        $commit = (git -C $root rev-parse "$commit^{commit}").Trim()
        $paths = Get-SourcePaths $p
        $changed = @(git -C $root diff --name-only $commit HEAD -- @paths)
        if ($LASTEXITCODE -ne 0) { throw "git diff failed" }
        if ($changed) {
            $failed += "$id $version was published from $($commit.Substring(0, 12)), and its sources changed since:`n    $($changed -join "`n    ")`n  Move $($data.Pins[$p.Version]) in Directory.Build.props."
        }
        else { Write-Host "$id $version is published (from $($commit.Substring(0, 12))) and its sources are unchanged: this release reuses it" }
    }
    if ($env:GITHUB_OUTPUT) {
        "${key}_version=$version" >> $env:GITHUB_OUTPUT
        "${key}_publish=$($publish.ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
    }
}
if ($failed) {
    foreach ($f in $failed) {
        if ($env:GITHUB_ACTIONS -eq "true") { Write-Host "::error title=Package version::$($f -replace "`n", ' ')" }
        Write-Host "FAIL: $f"
    }
    exit 1
}
