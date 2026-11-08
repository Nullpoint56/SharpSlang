<#
.SYNOPSIS
  Checks the patch registry (docs/upstream.json) against patches/, the tests and docs/upstream.md,
  and that every patch applies to Slang at the pin. -Bump reports, after a Slang bump, which patches
  still apply and which the new tag has made unnecessary.

.DESCRIPTION
  docs/upstream.json is the registry: one entry per Slang defect this repository has found, with
  the patch that carries its fix and the test that holds it (AGENTS.md#patches has the fields and
  the states). docs/upstream.md's index is generated from it, between the patch-registry markers.

  The default run, which lint runs (tools/check.ps1 -Lint), fails when:
    - an entry is malformed: a missing field, an unknown kind or state, a duplicate id, a Slang
      version that is not one, or the registry's own "slang" differing from Directory.Build.props;
    - a patch file has no entry, an entry names a patch that is not there, or two entries name one;
    - an entry that carries a patch (patched, filed, upstream PR from fork) has no test, names a
      test that is not in tests/, or was last verified against another Slang than the pin; an entry
      in any other state names a patch;
    - a patch has no prose header above its first diff;
    - docs/upstream.md's index is not what the registry generates (-Write regenerates it);
    - the patches do not apply cleanly to the pinned tag in name order, or each alone, or one of
      them is not recognised as applied by build-native.ps1's `--reverse -C0` check once all are.

  The apply checks need the files the patches touch, as the tag has them: from slang/ when its
  object store has the pinned commit, otherwise from raw.githubusercontent.com, cached under
  obj/slang-patch-base/<commit>/. So lint needs no submodule checkout.

  -Bump is for a Slang bump, run locally on a host with the toolchain (AGENTS.md#patches): after the
  pin has moved and slang/ is at the new tag, for each patch it reports whether the patch still
  applies (alone and in order), then builds Slang without the patches (build-native.ps1
  -WithoutPatches, into _build/<rid>-unpatched/), builds the tests, and runs each carried entry's test
  against that payload. A test that is red there still needs its patch; one that is green means the
  tag carries the fix, and the patch should be dropped and the entry set to "fixed upstream in
  <tag>". It leaves the unpatched payload staged: run build-native.ps1 again afterwards.

.PARAMETER Write
  Regenerate docs/upstream.md's index from the registry instead of failing on a difference.

.PARAMETER Index
  The registry, patches/, the tests and the index only; no apply checks and no network. What
  tools/ci/check-docs.sh runs, because a change to docs/ alone does not run lint.

.PARAMETER Bump
  The Slang-bump report described above. Needs slang/ checked out with its submodules.

.PARAMETER SkipBuild
  With -Bump: report the apply status only.
#>
[CmdletBinding()]
param(
    [switch]$Write,
    [switch]$Index,
    [switch]$Bump,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 3.0
$PSNativeCommandUseErrorActionPreference = $false

$root = (Resolve-Path (Join-Path $PSScriptRoot ".." "..")).Path
$registryPath = Join-Path $root "docs" "upstream.json"
$markdownPath = Join-Path $root "docs" "upstream.md"
$patchDir = Join-Path $root "patches"
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$Message) { $script:failures.Add($Message) }

$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -notmatch '<SlangVersion>([^<]+)</SlangVersion>') { throw "SlangVersion not found in Directory.Build.props" }
$pin = $Matches[1].Trim()

# ------------------------------------------------------------------------------------------ registry
$registry = Get-Content $registryPath -Raw | ConvertFrom-Json
$kinds = 'Slang defect', 'Slang documentation defect', 'not a defect'
$version = 'v\d{4}\.\d+(\.\d+)?'
$states = [ordered]@{
    patched     = '^patched$'
    fixed       = "^fixed upstream in $version$"
    filed       = '^filed: https://github\.com/shader-slang/slang/(issues|pull)/\d+$'
    fork        = '^upstream PR from fork: https://github\.com/shader-slang/slang/pull/\d+$'
    unpatchable = '^not patchable: \S.*$'
    unpatched   = '^unpatched$'
}
# The states in which our build carries the entry's patch.
$carrying = 'patched', 'filed', 'fork'

function Get-StateName([string]$State) {
    foreach ($kv in $states.GetEnumerator()) { if ($State -cmatch $kv.Value) { return $kv.Key } }
    return $null
}

if ($registry.slang -ne $pin) { Fail "docs/upstream.json is for Slang $($registry.slang); Directory.Build.props pins $pin" }

$entries = @($registry.defects)
$ids = @{}
$byPatch = @{}
foreach ($e in $entries) {
    $label = if ($e.PSObject.Properties['id']) { $e.id } else { "(an entry without an id)" }
    foreach ($field in 'id', 'title', 'area', 'kind', 'patch', 'test', 'state', 'verified') {
        if (-not $e.PSObject.Properties[$field]) { Fail "${label}: no '$field' field"; continue }
    }
    if (-not $e.PSObject.Properties['state']) { continue }
    if ($ids.ContainsKey($e.id)) { Fail "$($e.id) is in the registry twice" }
    $ids[$e.id] = $e
    if ($e.id -cnotmatch '^U\d+$') { Fail "$($e.id): an id is U and a number" }
    if ($kinds -cnotcontains $e.kind) { Fail "$($e.id): kind '$($e.kind)' is not one of: $($kinds -join ', ')" }
    if ($e.verified -cnotmatch "^$version$") { Fail "$($e.id): verified '$($e.verified)' is not a Slang version" }
    $state = Get-StateName $e.state
    if (-not $state) { Fail "$($e.id): state '$($e.state)' is not one of: patched; fixed upstream in vYYYY.RR; filed: <issue or PR URL>; upstream PR from fork: <PR URL>; not patchable: <reason>; unpatched"; continue }

    if ($carrying -contains $state) {
        if (-not $e.patch) { Fail "$($e.id) is '$($e.state)' and names no patch" }
        if (-not $e.test) { Fail "$($e.id) is '$($e.state)' and names no test" }
        if ($e.verified -ne $pin) { Fail "$($e.id) carries a patch last verified against $($e.verified), not the pin $pin" }
    } elseif ($e.patch) {
        Fail "$($e.id) is '$($e.state)' and still names patch $($e.patch): a patch is carried only while the entry is patched, filed or upstreamed from a fork"
    }
    if ($e.patch) {
        if ($byPatch.ContainsKey($e.patch)) { Fail "$($e.patch) is named by $($byPatch[$e.patch]) and $($e.id)" }
        $byPatch[$e.patch] = $e.id
        if (-not (Test-Path (Join-Path $patchDir $e.patch) -PathType Leaf)) { Fail "$($e.id) names patches/$($e.patch), which does not exist" }
    }
}

# ------------------------------------------------------------------------------------------ patches
$patches = @(Get-ChildItem $patchDir -Filter "*.patch" -File | Sort-Object Name)
foreach ($p in $patches) {
    if (-not $byPatch.ContainsKey($p.Name)) { Fail "patches/$($p.Name) has no entry in docs/upstream.json" }
    if ($p.Name -cnotmatch '^\d{4}-[a-z0-9-]+\.patch$') { Fail "patches/$($p.Name): a patch is named NNNN-short-description.patch" }
    $text = [System.IO.File]::ReadAllText($p.FullName)
    $first = $text.IndexOf("diff --git ", [StringComparison]::Ordinal)
    if ($first -lt 0) { Fail "patches/$($p.Name) has no diff" }
    elseif (-not $text.Substring(0, $first).Trim()) { Fail "patches/$($p.Name) has no prose header above its first diff (patches/README.md)" }
    if ($text.Contains("`r`n")) { Fail "patches/$($p.Name) has CRLF line endings; a patch is LF, as git diff writes it" }
}

# ------------------------------------------------------------------------------------------ tests
# "Class.Method": a class of that name in tests/ declaring a method of that name.
$testSources = @(Get-ChildItem (Join-Path $root "tests") -Recurse -Filter "*.cs" -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$sourceText = @{}
foreach ($f in $testSources) { $sourceText[$f.FullName] = [System.IO.File]::ReadAllText($f.FullName) }
function Test-TestExists([string]$Name) {
    if ($Name -cnotmatch '^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)$') { return $false }
    $class = $Matches[1]; $method = $Matches[2]
    foreach ($text in $sourceText.Values) {
        if ($text -cmatch "\bclass $class\b" -and $text -cmatch "\bvoid $method\(") { return $true }
    }
    return $false
}
foreach ($e in $entries) {
    if (-not $e.PSObject.Properties['test'] -or -not $e.test) { continue }
    if (-not (Test-TestExists $e.test)) { Fail "$($e.id) names test $($e.test), which is not in tests/" }
}

# ------------------------------------------------------------------------------------------ index
function Format-Cell([string]$Text) { $Text.Replace('|', '\|') }
$rows = [System.Collections.Generic.List[string]]::new()
$rows.Add("| # | Defect | Area | Kind | Patch | Test | State | Verified |")
$rows.Add("|---|---|---|---|---|---|---|---|")
foreach ($e in ($entries | Sort-Object { [int]($_.id.Substring(1)) })) {
    $patchCell = if ($e.patch) { "``$($e.patch)``" } else { "" }
    $testCell = if ($e.test) { "``$($e.test)``" } else { "" }
    $rows.Add("| $($e.id) | $(Format-Cell $e.title) | $($e.area) | $($e.kind) | $patchCell | $testCell | $(Format-Cell $e.state) | $($e.verified) |")
}
$begin = "<!-- patch-registry:begin (generated from docs/upstream.json by tools/ci/patch-registry.ps1 -Write; do not edit) -->"
$end = "<!-- patch-registry:end -->"
$generated = (@($begin) + $rows + @($end)) -join "`n"
$markdown = [System.IO.File]::ReadAllText($markdownPath)
$at = $markdown.IndexOf("<!-- patch-registry:begin", [StringComparison]::Ordinal)
$to = $markdown.IndexOf($end, [StringComparison]::Ordinal)
if ($at -lt 0 -or $to -lt $at) { Fail "docs/upstream.md has no patch-registry markers around its index" }
else {
    $current = $markdown.Substring($at, $to + $end.Length - $at)
    if ($current -cne $generated) {
        if ($Write) {
            [System.IO.File]::WriteAllText($markdownPath, $markdown.Substring(0, $at) + $generated + $markdown.Substring($to + $end.Length))
            Write-Host "==> docs/upstream.md: index regenerated"
        } else { Fail "docs/upstream.md's index is not what docs/upstream.json generates: run pwsh tools/ci/patch-registry.ps1 -Write" }
    }
}

# ------------------------------------------------------------------------------------------ apply
# The files the patches touch, as the given commit has them, in a scratch directory outside any
# repository, where `git apply` works on plain files.
function New-BaseTree([string]$Commit, [string]$From) {
    $paths = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($p in $patches) {
        foreach ($line in [System.IO.File]::ReadAllLines($p.FullName)) {
            if ($line -cmatch '^--- a/(.+)$') { [void]$paths.Add($Matches[1]) }
        }
    }
    $tree = Join-Path ([System.IO.Path]::GetTempPath()) "sharpslang-patch-base-$([guid]::NewGuid().ToString('n').Substring(0, 8))"
    New-Item -ItemType Directory -Force $tree | Out-Null
    $cache = Join-Path $root "obj" "slang-patch-base" $Commit
    $haveObjects = $From -and (Test-Path (Join-Path $From ".git")) -and ((git -C $From cat-file -t $Commit 2>$null) -join "") -eq "commit"
    foreach ($path in $paths) {
        $dest = Join-Path $tree $path
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        if ($haveObjects) {
            # Bytes as committed: git show through the shell would re-encode them.
            $blob = (git -C $From rev-parse "${Commit}:$path" 2>$null) -join ""
            if (-not $blob) { throw "$path is not in Slang at $Commit, but a patch changes it" }
            $info = [System.Diagnostics.ProcessStartInfo]::new("git", @("-C", $From, "cat-file", "blob", $blob))
            $info.RedirectStandardOutput = $true
            $proc = [System.Diagnostics.Process]::Start($info)
            $stream = [System.IO.File]::Create($dest)
            try { $proc.StandardOutput.BaseStream.CopyTo($stream) } finally { $stream.Dispose() }
            $proc.WaitForExit()
            if ($proc.ExitCode -ne 0) { throw "git cat-file of $path at $Commit failed" }
        } else {
            $cached = Join-Path $cache $path
            if (-not (Test-Path $cached)) {
                New-Item -ItemType Directory -Force (Split-Path $cached) | Out-Null
                Invoke-WebRequest -Uri "https://raw.githubusercontent.com/shader-slang/slang/$Commit/$path" -OutFile "$cached.partial" -TimeoutSec 120
                Move-Item "$cached.partial" $cached -Force
            }
            Copy-Item $cached $dest
        }
    }
    return $tree
}

function Test-Apply([string]$Tree, [string[]]$Arguments) {
    Push-Location $Tree
    try { $out = @(git apply @Arguments 2>&1); return [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Output = ($out -join "`n") } }
    finally { Pop-Location }
}

# Alone onto the tag, then in name order onto each other, then every one recognised as applied the
# way build-native.ps1 recognises it. Returns one line per finding, empty when all is well.
function Test-PatchSet([string]$Commit, [string]$From, [switch]$Report) {
    $findings = [System.Collections.Generic.List[string]]::new()
    if (-not $patches) { return $findings }
    $tree = New-BaseTree $Commit $From
    try {
        foreach ($p in $patches) {
            $r = Test-Apply $tree @('--check', $p.FullName)
            if ($Report) { Write-Host ("    {0,-60} alone: {1}" -f $p.Name, $(if ($r.Ok) { 'applies' } else { 'DOES NOT APPLY' })) }
            if (-not $r.Ok) { $findings.Add("$($p.Name) does not apply alone to Slang at $($Commit.Substring(0, 8)):`n$($r.Output)") }
        }
        foreach ($p in $patches) {
            $r = Test-Apply $tree @($p.FullName)
            if ($Report) { Write-Host ("    {0,-60} in order: {1}" -f $p.Name, $(if ($r.Ok) { 'applies' } else { 'DOES NOT APPLY' })) }
            if (-not $r.Ok) { $findings.Add("$($p.Name) does not apply in name order after the patches before it:`n$($r.Output)"); return $findings }
        }
        foreach ($p in $patches) {
            $r = Test-Apply $tree @('--check', '--reverse', '-C0', $p.FullName)
            if (-not $r.Ok) { $findings.Add("$($p.Name) is not recognised as applied (git apply --check --reverse -C0) once every patch is; build-native.ps1 would try to apply it twice:`n$($r.Output)") }
        }
    } finally { Remove-Item -Recurse -Force $tree -ErrorAction SilentlyContinue }
    return $findings
}

$slang = Join-Path $root "slang"
if ($Bump) {
    if (-not (Test-Path (Join-Path $slang "CMakeLists.txt"))) { throw "-Bump needs slang/ checked out at the new tag, with its submodules (AGENTS.md#native-build)" }
    $head = (git -C $slang rev-parse HEAD) -join ""
    Write-Host "==> patches against slang/ at $head (pin $pin)"
    # The tag's own files, not slang/'s working tree, which normally carries the patches.
    $applyFindings = @(Test-PatchSet $head $slang -Report)
    $applyFindings | ForEach-Object { Write-Host $_ }
    if ($SkipBuild) { return }

    Write-Host "==> Slang without the patches"
    & (Join-Path $root "tools" "build-native.ps1") -WithoutPatches -AllowNewerGlibc
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "build-native.ps1 -WithoutPatches failed" }
    dotnet build (Join-Path $root "tests" "SharpSlang.Tests") -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "the test project does not build" }
    $exe = Join-Path $root "tests/SharpSlang.Tests/bin/Release/net11.0/SharpSlang.Tests$(if ($IsWindows) { '.exe' })"

    Write-Host "==> each carried patch's test against the unpatched build"
    foreach ($e in ($entries | Where-Object { $_.patch -and $carrying -contains (Get-StateName $_.state) })) {
        $out = @(& $exe -noColor -method "SharpSlang.Tests.$($e.test)" 2>&1 | ForEach-Object { "$_" })
        $summary = @($out | Where-Object { $_ -match 'Total: \d+' }) | Select-Object -Last 1
        $verdict = if ($summary -match 'Total: 1, Errors: 0, Failed: 0, Skipped: 0') { "GREEN without the patch: the tag carries the fix; drop $($e.patch) and set the state to 'fixed upstream in <tag>'" }
                   elseif ($summary -match 'Failed: 1') { "red without the patch: keep $($e.patch)" }
                   else { "no verdict (skipped or did not run): $summary" }
        Write-Host ("    {0,-4} {1}" -f $e.id, $verdict)
    }
    Write-Host "==> the unpatched payload is staged; run pwsh tools/build-native.ps1 to stage the patched one"
    return
}

if (-not $Index) {
    $commit = (git -C $root rev-parse "HEAD:slang" 2>$null) -join ""
    if (-not $commit) { throw "the superproject records no slang/ commit at HEAD" }
    foreach ($f in Test-PatchSet $commit $slang) { Fail $f }
}

if ($failures.Count) {
    $failures | ForEach-Object { Write-Host "    $_" }
    throw "patch registry: $($failures.Count) problem(s)"
}
$carried = @($entries | Where-Object { $_.patch }).Count
Write-Host "==> patch registry: $($entries.Count) entries, $($patches.Count) patch(es), $carried carried$(if (-not $Index) { ', every patch applies alone and in order' })"
