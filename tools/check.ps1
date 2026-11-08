<#
.SYNOPSIS
    The quick checks a merge requires besides the suite, locally, in the form CI runs them: lint
    (with the descs version) and zero allocation.

.DESCRIPTION
    The same commands as the lint and zero-alloc jobs of .github/workflows/ci.yml and checks.yml
    (AGENTS.md#lint, AGENTS.md#pipeline-baking-packages-allocation). Run them on the committed tree:
    descs-version.ps1 compares commits, so against uncommitted changes it passes while HEAD is still
    the old commit (AGENTS.md#actions-budget).

    lint: the SDK's own analyzers (AnalysisLevel latest-recommended and EnforceCodeStyleInBuild,
    set in Directory.Build.props and configured in .editorconfig) in a Release build with warnings
    as errors, the formatter's check, and tools/ci/descs-version.ps1: SharpSlang.Descs and the
    reader may not change their sources under a version already on nuget.org; and
    tools/ci/patch-registry.ps1: docs/upstream.json against patches/, the tests and
    docs/upstream.md, and every patch applying to the pinned tag (AGENTS.md#patches). Lint needs no native
    payload: SharpSlangRequireNative=false turns off the build's SHARPSLANG001 error about a
    missing one. descs-version needs the history back to the published commit and asks nuget.org.

    zero-alloc: the test project, then its allocation classes alone (AllocationTests,
    AllocationCoverageTests) through xunit's own runner. It needs the payload staged
    (tools/fetch-native.ps1), because the measured loops reflect and bake their shaders first, and
    SharpNRI's library for the NONE frame, which the test project restores. A class that skipped
    fails here: a skipped allocation test is not a measured zero.

    dotnet/Native is ClangSharp's output and is left out of the formatter, as .editorconfig leaves it
    out of IDE0055: its layout is the generator's, which regen.yml compares byte for byte. Every
    analyzer still runs over it.

        pwsh tools/check.ps1                  # both
        pwsh tools/check.ps1 -Lint            # lint only
        pwsh tools/check.ps1 -ZeroAlloc       # zero-alloc only
        pwsh tools/check.ps1 -Lint -Fix       # apply the formatter first, then check
#>
param(
    [switch]$Lint,
    [switch]$ZeroAlloc,
    [switch]$Fix
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Lint -and -not $ZeroAlloc) { $Lint = $true; $ZeroAlloc = $true }
$failed = @()

# The classes the zero-alloc check runs, and the TRX it counts them from.
$allocationClasses = 'SharpSlang.Tests.AllocationTests', 'SharpSlang.Tests.AllocationCoverageTests'

Push-Location $root
try {
    if ($Lint) {
        Write-Host "==> lint"
        if ($Fix) {
            dotnet format whitespace SharpSlang.slnx --exclude dotnet/Native
            if ($LASTEXITCODE -ne 0) { throw "dotnet format failed to apply the formatting" }
        }

        # --no-incremental: an up-to-date build skips the compiler, and with it every analyzer, so a
        # second run after a failing one reported "all checks passed" (measured 2026-11-08).
        dotnet build SharpSlang.slnx -c Release --no-incremental -warnaserror -p:SharpSlangRequireNative=false -nologo -v q
        if ($LASTEXITCODE -ne 0) { $failed += "lint: analyzers" }

        dotnet format whitespace SharpSlang.slnx --exclude dotnet/Native --verify-no-changes
        if ($LASTEXITCODE -ne 0) { $failed += "lint: formatting (pwsh tools/check.ps1 -Lint -Fix applies it)" }

        try { & (Join-Path $PSScriptRoot "ci/descs-version.ps1") }
        catch { $failed += "lint: descs version ($($_.Exception.Message))" }

        try { & (Join-Path $PSScriptRoot "ci/patch-registry.ps1") }
        catch { $failed += "lint: patch registry ($($_.Exception.Message))" }
    }

    if ($ZeroAlloc) {
        Write-Host "==> zero-alloc"
        dotnet build tests/SharpSlang.Tests -c Release -nologo -v q -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "the test project does not build" }

        $exe = Join-Path $root "tests/SharpSlang.Tests/bin/Release/net11.0/SharpSlang.Tests$(if ($IsWindows) { '.exe' })"
        $results = Join-Path $root "test-results"
        New-Item -ItemType Directory -Force $results | Out-Null
        $trx = Join-Path $results "zero-alloc.trx"
        $classArgs = @($allocationClasses | ForEach-Object { '-class', $_ })
        & $exe -noColor @classArgs -result-trx $trx
        if ($LASTEXITCODE -ne 0) { $failed += "zero-alloc" }
        elseif (Test-Path $trx) {
            # Counted from the TRX, as every "actually ran" gate is (tools/ci/count-tests.ps1).
            # 6>&1: count-tests.ps1 prints with Write-Host, which an assignment does not capture.
            $counts = @(& (Join-Path $PSScriptRoot "ci/count-tests.ps1") -Trx $trx -Class (($allocationClasses -replace '^SharpSlang\.Tests\.', '') -join ',') 6>&1 | ForEach-Object { "$_" })
            $last = $counts[-1]
            if ($last -notmatch 'skipped=0\b') { $failed += "zero-alloc: an allocation test skipped (the payload or SharpNRI's library did not load; the reason is above)" }
        }
        else { $failed += "zero-alloc: no TRX written to $trx" }
    }
}
finally {
    Pop-Location
}

if ($failed) { throw "failed: $($failed -join '; ')" }
Write-Host "==> all checks passed"
