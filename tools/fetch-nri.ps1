<#
.SYNOPSIS
  Download the pinned PrismNRI packages from their GitHub release into the local folder feed.

.DESCRIPTION
  PrismSlang.NRI reads a reflection snapshot onto PrismNRI's descriptor types, and the tests drive
  it against PrismNRI itself. Both come as NuGet packages from PrismNRI's GitHub release, pinned
  exactly as PrismNriVersion in Directory.Build.props; the release tag is `v` + that value.

  They are consumed from nupkg-feed/, a folder feed registered in nuget.config, and not from GitHub
  Packages. PrismNRI is a private repository, so either route needs a token — but a package-feed
  route needs it on every restore everywhere, and this one needs it once per pin, here. What this
  script writes is gitignored and reproducible from the pin.

  Exactly three of the release's assets are wanted:

    PrismNRI          the managed binding, plus NRI's native library per RID. Depends on Descs.
    PrismNRI.Descs    NRI's descriptor structs and enums. No native payload, no dependencies.
                      The only one PrismSlang.NRI itself references.
    PrismNRI.Testing  test helpers over PrismNRI. Referenced by the test project only.

  The release also carries PrismNRI.Native.Upscalers, PrismNRI.Native.Agility and the per-RID
  native zips. Nothing here uses them, so nothing here downloads them.

  The assets are fetched through the REST API, which is GitHub's documented route to a private
  repository's release asset with a token: the tag resolves to a release, the release lists its
  assets by API url, and an asset url requested with `Accept: application/octet-stream` redirects
  to the file.

.PARAMETER PrismNriVersion
  The package version to fetch, without the leading `v`. Defaults to PrismNriVersion in
  Directory.Build.props, which is what the projects restore — overriding it fetches packages
  nothing will use until the pin moves too.

.PARAMETER Token
  A GitHub token that can read PrismNRI's contents: a fine-grained PAT with Contents: read on that
  repository, or a classic one with `repo`. Falls back to $env:PRISMNRI_TOKEN, then
  $env:GITHUB_TOKEN. In CI that is the PRISMNRI_READ_TOKEN secret; the job's own GITHUB_TOKEN is
  scoped to this repository and cannot see another one's releases.

  Only needed when something is missing. A feed that already holds all three packages makes no
  API call at all.

.PARAMETER Force
  Re-download packages already present in nupkg-feed/.
#>
param(
    [string]$PrismNriVersion,
    [string]$Token,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$repo = 'Nullpoint56/PrismNRI'
$packages = @('PrismNRI', 'PrismNRI.Descs', 'PrismNRI.Testing')

# ---------------------------------------------------------------- resolve version + token
if (-not $PrismNriVersion) {
    $props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
    if ($props -notmatch '<PrismNriVersion>([^<]+)</PrismNriVersion>') { throw "PrismNriVersion not found in Directory.Build.props" }
    $PrismNriVersion = $Matches[1].Trim()
}
$PrismNriVersion = $PrismNriVersion.TrimStart('v')
$tag = "v$PrismNriVersion"

if (-not $Token) { $Token = $env:PRISMNRI_TOKEN }
if (-not $Token) { $Token = $env:GITHUB_TOKEN }

# What to say whenever GitHub refuses. Written once because the refusal has the same causes
# wherever it happens, and GitHub reports a private repository the token cannot see as 404, not
# 403 — so a 404 here is more often a token problem than a missing release, and the message must
# not send anyone hunting for a typo in the tag first.
$howToAuthenticate = @"
  PrismNRI is a private repository, so this needs a token that can read it:
    pwsh tools/fetch-nri.ps1 -Token <token>      or set PRISMNRI_TOKEN (or GITHUB_TOKEN).
  A fine-grained PAT with 'Contents: read' on $repo is enough. In CI it is the
  PRISMNRI_READ_TOKEN secret on this repository; the job's own GITHUB_TOKEN cannot read
  another repository's releases.
  If the token is right, check that release $tag exists at https://github.com/$repo/releases.
"@

function Get-StatusCode($err) {
    $response = $err.Exception.Response
    if ($response) { return [int]$response.StatusCode }
    return $null
}

# ---------------------------------------------------------------- what is already here
$feed = Join-Path $root "nupkg-feed"
New-Item -ItemType Directory -Force -Path $feed | Out-Null

$wanted = foreach ($id in $packages) {
    $name = "$id.$PrismNriVersion.nupkg"
    $path = Join-Path $feed $name
    if ($Force -and (Test-Path $path)) { Remove-Item $path -Force -Confirm:$false }
    [pscustomobject]@{ Name = $name; Path = $path }
}

$missing = @($wanted | Where-Object { -not (Test-Path $_.Path) })

# ---------------------------------------------------------------- download
#
# Only when something is missing. A warm feed — every local run after the first, and every CI leg
# with a cache hit — makes no API call and needs no token.
if ($missing.Count -gt 0) {
    if (-not $Token) {
        throw "no GitHub token, and nupkg-feed/ is missing $(($missing | ForEach-Object Name) -join ', ').`n$howToAuthenticate"
    }

    $headers = @{
        Authorization          = "Bearer $Token"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    Write-Host "==> resolving release $tag of $repo"
    try { $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers }
    catch {
        $status = Get-StatusCode $_
        if ($status -in 401, 403, 404) { throw "GitHub answered $status for release $tag of $repo.`n$howToAuthenticate" }
        throw "failed to read release $tag of $repo`n  $_"
    }

    foreach ($pkg in $missing) {
        $asset = $release.assets | Where-Object { $_.name -eq $pkg.Name } | Select-Object -First 1
        if (-not $asset) {
            throw ("release $tag of $repo has no asset named '$($pkg.Name)'.`n" +
                   "  It carries: $(($release.assets | ForEach-Object name) -join ', ')`n" +
                   "  Either the pin in Directory.Build.props names a version that release does not publish, or the package was renamed.")
        }

        # Downloaded under a temporary name and moved into place, so an interrupted download never
        # leaves a truncated .nupkg behind for the next run to skip as already present.
        $partial = "$($pkg.Path).partial"
        Write-Host "==> downloading $($pkg.Name)"
        try {
            Invoke-WebRequest -Uri $asset.url -OutFile $partial -Headers @{
                Authorization          = "Bearer $Token"
                Accept                 = 'application/octet-stream'
                'X-GitHub-Api-Version' = '2022-11-28'
            }
        }
        catch {
            Remove-Item $partial -Force -Confirm:$false -ErrorAction SilentlyContinue
            $status = Get-StatusCode $_
            if ($status -in 401, 403, 404) { throw "GitHub answered $status for asset '$($pkg.Name)' of release $tag.`n$howToAuthenticate" }
            throw "failed to download $($pkg.Name) from release $tag of $repo`n  $_"
        }
        Move-Item $partial $pkg.Path -Force
    }
}

# ---------------------------------------------------------------- report
Write-Host "==> nupkg-feed ($tag)"
foreach ($pkg in $wanted) {
    $state = if ($missing.Path -contains $pkg.Path) { 'fetched' } else { 'present' }
    Write-Host ("    {0,-40} {1,8:N1} KB  {2}" -f $pkg.Name, ((Get-Item $pkg.Path).Length / 1KB), $state)
}
