<#
.SYNOPSIS
  Count the outcomes of some test classes in a TRX file, and print why each skipped one skipped.

.DESCRIPTION
  The CI gates ("the NRI device tests actually ran" and its siblings) exist because a skipped test
  is green: each one asserts that a class of oracle really executed on a leg. They used to re-run
  their classes with `dotnet test --filter` only to read a count, which cost a test-host start and
  a second execution of the same tests per gate, 64 seconds on win-x64's three gates in the
  October 2026 runs. The full run's TRX
  already records every result, so the gates read it instead.

  A class matches the way `--filter "FullyQualifiedName~<Class>"` matched: a test whose name
  contains `.<Class>.`. xunit writes the fully qualified method name, with theory arguments, as
  the TRX testName, and reports a skipped theory as one row, exactly as the filtered runs did.

.PARAMETER Trx
  The TRX file of the full run.

.PARAMETER Class
  One or more test class names, as an array or as one comma-separated string. The second form is
  what a bash step can pass: `pwsh -File` hands every argument over as a plain string, so
  "A,B" arrives as one value rather than two.

  An entry with a dot in it is `Class.Method`, and the method may carry wildcards
  (`TargetValidationTests.Spirv_*`): it matches that method's results, a theory's rows included,
  rather than the whole class.

.OUTPUTS
  Prints the skipped tests and their reasons, then one line `passed=N skipped=N failed=N total=N`
  for the gate to parse. Exits 2 when the TRX is missing or matches nothing, which a gate must
  treat as a failure: no results is not zero skips.
#>
param(
    [Parameter(Mandatory)] [string] $Trx,
    [Parameter(Mandatory)] [string[]] $Class
)
$ErrorActionPreference = 'Stop'

$Class = @($Class -split '[,\s]+' | Where-Object { $_ })

if (-not (Test-Path $Trx)) {
    Write-Host "::error::no TRX at $Trx; the test step did not get far enough to write one"
    exit 2
}

[xml]$doc = Get-Content -Raw $Trx
$ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
$results = Select-Xml -Xml $doc -XPath '//t:UnitTestResult' -Namespace $ns | ForEach-Object Node

$matched = @($results | Where-Object {
    $name = $_.testName
    $Class | Where-Object {
        if ($_.Contains('.')) { $name -like "*.$_" -or $name -like "*.$_(*" }
        else { $name -like "*.$_.*" }
    }
})

if ($matched.Count -eq 0) {
    Write-Host "::error::no result in $Trx belongs to $($Class -join ', ')"
    exit 2
}

$passed  = @($matched | Where-Object outcome -eq 'Passed').Count
$skipped = @($matched | Where-Object outcome -eq 'NotExecuted')
$failed  = @($matched | Where-Object { $_.outcome -notin 'Passed', 'NotExecuted' }).Count

foreach ($s in $skipped) {
    $reason = $s.Output.ErrorInfo.Message
    if (-not $reason) { $reason = $s.Output.StdOut }
    Write-Host "skipped: $($s.testName)"
    if ($reason) { Write-Host "  $($reason.Trim())" }
}

Write-Host "passed=$passed skipped=$($skipped.Count) failed=$failed total=$($matched.Count)"
