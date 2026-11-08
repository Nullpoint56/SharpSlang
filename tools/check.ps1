<#
.SYNOPSIS
    The quick checks a merge requires besides the suite, locally, in the form CI runs them.

.DESCRIPTION
    Lint: the SDK's own analyzers (AnalysisLevel latest-recommended and EnforceCodeStyleInBuild,
    set in Directory.Build.props and configured in .editorconfig) in a Release build with warnings
    as errors, then the formatter's check. The same commands as the lint job of
    .github/workflows/ci.yml. Lint needs no native payload: SharpSlangRequireNative=false turns off
    the build's SHARPSLANG001 error about a missing one.

    dotnet/Native is ClangSharp's output and is left out of the formatter, as .editorconfig leaves it
    out of IDE0055: its layout is the generator's, which the bindings job compares byte for byte.
    Every analyzer still runs over it.

        pwsh tools/check.ps1          # check
        pwsh tools/check.ps1 -Fix     # apply the formatter first, then check
#>
param(
    [switch]$Fix
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$failed = @()

Push-Location $root
try {
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
    if ($LASTEXITCODE -ne 0) { $failed += "lint: formatting (pwsh tools/check.ps1 -Fix applies it)" }
}
finally {
    Pop-Location
}

if ($failed) { throw "failed: $($failed -join '; ')" }
Write-Host "==> all checks passed"
