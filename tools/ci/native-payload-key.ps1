<#
.SYNOPSIS
  The cache key of a staged native payload, from git object ids.

.DESCRIPTION
  Everything the payload is built or staged from, each named by its git object id at the commit, so
  the key is the same on every OS, where a hash of the checked-out bytes is not (Windows checks out
  some files with CRLF). The template's key: the submodule, the patches, the build scripts, the
  props, the notices and the workflow (AGENTS.md#actions-budget). "The workflow" is the composite
  action that stages the payload, .github/actions/slang-payload/action.yml: it installs the
  toolchain and runs the script, and it is the one file every workflow that stages a payload uses,
  so an edit to ci.yml's test steps does not rebuild Slang.

    -Source Build     what tools/build-native.ps1 builds from the slang/ submodule: the submodule
                      pointer (which fixes Slang's own submodules and what its configure downloads,
                      DXC by SHA-256), patches/, the build script, the props (the pin), the notices
                      and the action.
    -Source Release   what tools/fetch-native.ps1 stages from Slang's release archive, until CI
                      builds from source: the fetch script, the props (the pin), the notices and the
                      action. No submodule: the archive is the pin's.

  Prints "key=<20 hex digits>" for $GITHUB_OUTPUT. The action prefixes the source and the RID.

.PARAMETER Sha
  The commit. HEAD by default.
#>
param(
    [ValidateSet('Build', 'Release')]
    [string]$Source = 'Build',
    [string]$Sha = "HEAD"
)

$ErrorActionPreference = 'Stop'

$common = 'Directory.Build.props', 'THIRD-PARTY-NOTICES.md', '.github/actions/slang-payload/action.yml'
$inputs = switch ($Source) {
    'Build'   { @('slang', 'patches', 'tools/build-native.ps1') + $common }
    'Release' { @('tools/fetch-native.ps1') + $common }
}
$ids = foreach ($path in $inputs) {
    $id = git rev-parse --verify --quiet "${Sha}:$path"
    if ($LASTEXITCODE -ne 0 -or -not $id) { throw "native payload input '$path' not found in $Sha" }
    "$path $id"
}
$key = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes(($ids + "source $Source") -join "`n"))).Substring(0, 20).ToLowerInvariant()
$ids | ForEach-Object { Write-Host "    $_" }
"key=$key"
