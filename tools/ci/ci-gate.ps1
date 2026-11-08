<#
.SYNOPSIS
  Decides what each job of ci.yml runs, so nothing is paid for twice.

.DESCRIPTION
  The repository is private, on the Free plan, with $12 of Actions a month. The owner's rules
  (2026-11-07):

  - A push to a pull request starts nothing. A branch is tested when someone starts ci.yml by
    hand on it.
  - A branch must be up to date with master and have a CI run before it merges. GitHub enforces
    neither on a private Free repository (branch protection and rulesets answer "Upgrade to GitHub
    Pro or make this repository public"), so this script refuses a stale branch and the report job
    posts the result as a commit status named "ci". A ruleset can require that status once the
    plan allows one.
  - Nothing re-runs on master unless the code differs from what a branch run tested.

  What decides it is the tree. Two commits with the same tree are the same code, whatever their
  history. A job counts as done for a tree when a ci.yml run of a commit with that tree has the
  job succeeded. Commits looked at: this one, and the head of every pull request it belongs to.
  For the merge of an up-to-date branch, that head has the merged tree.

  Outputs, written to $GITHUB_OUTPUT on a runner and printed otherwise:

    legs       JSON list of the test legs to run, [] for none
    count      how many legs that is, for the digests comparison
    digests    run | skip
    bindings   run | skip    the generated-bindings check, Windows
    docs       run | skip    links and section references, Linux
    lint       run | skip    the analyzers and the formatter (tools/check.ps1), Linux

  Decisions:

    branch, behind master   fails here, before anything is paid for. Merge or rebase, then run again.
    branch, docs only     the test legs, digests, bindings and lint skip. The docs check runs.
    tests and digests     skip when every requested leg passed for this tree, and the digests
                          comparison passed for at least that many legs. Otherwise all requested
                          legs run, because the digests job compares legs of one run.
    bindings              skip when it passed for this tree. On a branch it runs only when its
                          inputs differ from master, and on a push only when the push changed them:
                          Directory.Build.props, tools/generate-native.ps1, dotnet/Native.
    docs                  skip when it passed for this tree.
    lint                  skip when it passed for this tree.
    release               publish.yml's call. Bindings, docs and lint skip, and the legs follow the
                          rule above. macOS normally has not run, so a release normally runs every leg.
    force                 the manual input: everything requested runs.

  Any API failure makes every job run. Paying twice beats skipping something untested.

  Run it locally against the repository's history, read-only:

    pwsh tools/ci/ci-gate.ps1 -EventName push -Sha <commit> -Ref refs/heads/master -Repository owner/name -Token <token>
#>
param(
    [string]$EventName  = $env:GITHUB_EVENT_NAME,
    [string]$Sha        = $env:GITHUB_SHA,
    [string]$Ref        = $env:GITHUB_REF,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Token      = $env:GH_TOKEN,
    [string]$Before     = $env:GATE_BEFORE,     # github.event.before, on a push
    [string]$MacOS      = $env:GATE_MACOS,      # inputs.macos
    [string]$Release    = $env:GATE_RELEASE,    # inputs.release, from publish.yml
    [string]$Force      = $env:GATE_FORCE       # inputs.force, a manual run
)

$ErrorActionPreference = 'Stop'

function Get-Api([string]$Path) {
    try {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/$Path" -Headers @{
            Authorization          = "Bearer $Token"
            Accept                 = 'application/vnd.github+json'
            'X-GitHub-Api-Version' = '2022-11-28'
        }
    }
    catch {
        throw "GET $Path : $($_.Exception.Message)"
    }
}

function Test-Like([string[]]$Files, [string[]]$Patterns) {
    @($Files | Where-Object { $f = $_; $Patterns | Where-Object { $f -like $_ } }).Count -gt 0
}

$requested = @('win-x64', 'linux-x64', 'linux-arm64')
if ($MacOS -eq 'true') { $requested += 'osx-arm64' }

$bindingInputs = 'Directory.Build.props', 'tools/generate-native.ps1', 'dotnet/Native/*'
$noResult      = '*.md', 'docs/*'
$isRelease     = $Release -eq 'true'
$onBranch      = $Ref -like 'refs/heads/*' -and $Ref -ne 'refs/heads/master'
$short         = $Sha.Substring(0, [Math]::Min(7, $Sha.Length))
$notes         = [System.Collections.Generic.List[string]]::new()

$decision = [ordered]@{ legs = $requested; digests = 'run'; bindings = 'run'; docs = 'run'; lint = 'run' }
if ($isRelease) {
    $decision.bindings = 'skip'
    $decision.docs = 'skip'
    $decision.lint = 'skip'
    $notes.Add('Release: the bindings, docs and lint checks skip; the packages do not depend on them.')
}

function Write-Decision([string]$Headline) {
    $legs = @($decision.legs)
    $lines = @(
        "legs=$(ConvertTo-Json -Compress -InputObject $legs)",
        "count=$($legs.Count)",
        "digests=$($decision.digests)",
        "bindings=$($decision.bindings)",
        "docs=$($decision.docs)",
        "lint=$($decision.lint)"
    )
    $summary = @("### CI gate: $short", '', $Headline, '') + @($notes | ForEach-Object { "- $_" }) + @(
        '', '| Job | Decision |', '|---|---|',
        "| test legs | $(if ($legs.Count) { $legs -join ', ' } else { 'none' }) |",
        "| digests | $($decision.digests) |",
        "| bindings | $($decision.bindings) |",
        "| docs | $($decision.docs) |",
        "| lint | $($decision.lint) |")
    if ($env:GITHUB_OUTPUT) { $lines | Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8 } else { $lines | Write-Output }
    if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 } else { $summary | Write-Host }
}

if ($Force -eq 'true') {
    Write-Decision "Forced by the manual input: every requested job runs."
    exit 0
}

try {
    # ---------------------------------------------------------------- a branch: up to date, and what changed
    $changed = $null
    if ($onBranch) {
        $compare = Get-Api "compare/master...$Sha"
        if ($compare.behind_by -gt 0) {
            $message = "$($Ref -replace '^refs/heads/', '') is $($compare.behind_by) commit(s) behind master. Merge or rebase it onto master, then run CI again: a run of a stale branch tests a tree master will not have."
            Write-Host "::error::$message"
            if ($env:GITHUB_STEP_SUMMARY) { "### CI gate: $short", '', $message | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
            exit 1
        }
        # The compare API lists at most 300 files; a longer list is treated as touching everything.
        if ($compare.files.Count -lt 300) { $changed = @($compare.files | ForEach-Object filename) }
    }
    elseif ($EventName -eq 'push' -and $Before -and $Before -notmatch '^0+$') {
        $compare = Get-Api "compare/$Before...$Sha"
        if ($compare.files.Count -lt 300) { $changed = @($compare.files | ForEach-Object filename) }
    }

    if ($null -ne $changed) {
        $results = @($changed | Where-Object { $f = $_; -not ($noResult | Where-Object { $f -like $_ }) })
        if ($onBranch -and $changed.Count -gt 0 -and $results.Count -eq 0) {
            $decision.legs = @()
            $decision.digests = 'skip'
            $decision.bindings = 'skip'
            $decision.lint = 'skip'
            $notes.Add("Only documentation differs from master ($($changed.Count) files): the test legs, digests, bindings and lint skip.")
        }
        if ($decision.bindings -eq 'run' -and -not (Test-Like $changed $bindingInputs)) {
            $decision.bindings = 'skip'
            $notes.Add('The bindings check skips: Directory.Build.props, tools/generate-native.ps1 and dotnet/Native are unchanged.')
        }
    }

    # ---------------------------------------------------------------- what already passed for this tree
    $tree = (Get-Api "git/commits/$Sha").tree.sha
    $heads = [System.Collections.Generic.List[string]]::new()
    $heads.Add($Sha)
    # Piped, so the list is enumerated: Invoke-RestMethod hands a JSON array back as one object, and
    # @() around it made a one-element list whose .head.sha was every head at once.
    foreach ($pr in (Get-Api "commits/$Sha/pulls" | ForEach-Object { $_ })) {
        if ($pr.head.sha -and -not $heads.Contains($pr.head.sha)) { $heads.Add($pr.head.sha) }
    }

    $passed = [System.Collections.Generic.HashSet[string]]::new()
    $digestLegs = 0
    foreach ($head in $heads) {
        if ($head -ne $Sha -and (Get-Api "git/commits/$head").tree.sha -ne $tree) { continue }
        $runs = Get-Api "actions/workflows/ci.yml/runs?head_sha=$head&per_page=20"
        foreach ($run in @($runs.workflow_runs)) {
            if ($run.id -eq [long]$env:GITHUB_RUN_ID) { continue }
            foreach ($job in @((Get-Api "actions/runs/$($run.id)/jobs?per_page=50").jobs)) {
                if ($job.conclusion -ne 'success') { continue }
                [void]$passed.Add($job.name)
                if ($job.name -match '^every platform cooks the same asset \((\d+) legs\)$') {
                    $digestLegs = [Math]::Max($digestLegs, [int]$Matches[1])
                }
            }
        }
    }

    $legsDone = @($decision.legs | Where-Object { $passed.Contains("test ($_)") })
    if (@($decision.legs).Count -gt 0 -and $legsDone.Count -eq @($decision.legs).Count -and $digestLegs -ge @($decision.legs).Count) {
        $notes.Add("Every requested leg and the digests comparison already passed for tree $($tree.Substring(0, 7)): the test legs skip.")
        $decision.legs = @()
        $decision.digests = 'skip'
    }
    elseif ($legsDone.Count -gt 0) {
        $notes.Add("Passed before for this tree: $($legsDone -join ', '). All requested legs run anyway, because the digests job compares legs of one run.")
    }
    if (@($decision.legs).Count -eq 0) { $decision.digests = 'skip' }

    if ($decision.bindings -eq 'run' -and $passed.Contains('generated bindings are current')) {
        $decision.bindings = 'skip'
        $notes.Add('The bindings check already passed for this tree.')
    }
    if ($decision.docs -eq 'run' -and $passed.Contains('links and section references')) {
        $decision.docs = 'skip'
        $notes.Add('The docs check already passed for this tree.')
    }
    if ($decision.lint -eq 'run' -and $passed.Contains('lint')) {
        $decision.lint = 'skip'
        $notes.Add('The lint check already passed for this tree.')
    }

    $headline = "$EventName on $Ref, tree $($tree.Substring(0, 7)), looked up through $($heads.Count) commit(s)."
}
catch {
    Write-Host "::warning::could not establish what this tree already passed ($($_.Exception.Message)); every requested job runs"
    $decision = [ordered]@{
        legs = $requested; digests = 'run'
        bindings = $(if ($isRelease) { 'skip' } else { 'run' })
        docs = $(if ($isRelease) { 'skip' } else { 'run' })
        lint = $(if ($isRelease) { 'skip' } else { 'run' })
    }
    $headline = 'The GitHub API did not answer: every requested job runs.'
}

Write-Decision $headline
