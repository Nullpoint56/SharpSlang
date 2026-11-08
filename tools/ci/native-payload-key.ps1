<#
.SYNOPSIS
  The cache key of the native payload tools/build-native.ps1 stages, from git object ids.

.DESCRIPTION
  Everything the payload is built or staged from, each named by its git object id at the commit:
  the slang/ submodule pointer (which fixes Slang's own submodules and what its configure downloads,
  DXC by SHA-256), patches/, the build script, Directory.Build.props (the pin), the notices, and
  ci.yml (the toolchain the job installs before the build). Object ids are the same on every OS,
  where a hash of the checked-out bytes is not: Windows checks out some files with CRLF.

  Prints "key=<20 hex digits>" for $GITHUB_OUTPUT. The workflow prefixes the RID.

.PARAMETER Sha
  The commit. HEAD by default.
#>
param([string]$Sha = "HEAD")

$ErrorActionPreference = 'Stop'

$inputs = 'slang', 'patches', 'tools/build-native.ps1', 'Directory.Build.props', 'THIRD-PARTY-NOTICES.md', '.github/workflows/ci.yml'
$ids = foreach ($path in $inputs) {
    $id = git rev-parse --verify --quiet "${Sha}:$path"
    if ($LASTEXITCODE -ne 0 -or -not $id) { throw "native payload input '$path' not found in $Sha" }
    "$path $id"
}
$key = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($ids -join "`n"))).Substring(0, 20).ToLowerInvariant()
$ids | ForEach-Object { Write-Host "    $_" }
"key=$key"
