<#
.SYNOPSIS
  Decides what each job of ci.yml, checks.yml and regen.yml runs, so nothing is paid for twice, and
  whether release.yml's commit has passed what a release needs.

.DESCRIPTION
  The repository is private, on the Free plan, with $12 of Actions a month. The owner's rules
  (2026-11-07; AGENTS.md#actions-budget):

  - A push to a pull request starts nothing. A branch is tested when someone starts ci.yml by
    hand on it (checks.yml and regen.yml too, for one check alone).
  - A branch must be up to date with master and have a green "ci" status before it merges. GitHub
    enforces neither on a private Free repository (branch protection and rulesets answer "Upgrade
    to GitHub Pro or make this repository public"), so this script refuses a stale branch in ci.yml
    and ci.yml's report job posts the result as the commit status "ci".
  - Nothing re-runs on master unless the code differs from what a branch run tested.
  - Never two overlapping runs on one head: checks.yml and ci.yml each refuse while the other runs.

  What decides it is the tree. Two commits with the same tree are the same code, whatever their
  history. A job counts as done for a tree when a run of ci.yml, checks.yml or regen.yml of a commit
  with that tree has the job succeeded, under the job's own name: "lint" in checks.yml and
  "checks / lint" in ci.yml, which calls checks.yml, are the same job. Commits looked at: this one,
  and the head of every pull request it belongs to; for the merge of an up-to-date branch, that head
  has the merged tree. A release looks further, down the first-parent history while only
  documentation differs, because a documentation-only push to master starts nothing.

  -Workflow ci       legs, count, digests, docs, lint, zero_alloc, regen
  -Workflow checks   lint, zero_alloc (each also off when the manual run's input turned it off)
  -Workflow regen    regen
  -Workflow release  nothing to decide: fails unless the three test legs, the digests comparison
                     of at least as many legs, docs, lint and zero-alloc passed for this tree.
                     macOS is not required: nothing in a release waits for it.

  Each output is "run" or "skip", except legs (a JSON list, [] for none) and count. Written to
  $GITHUB_OUTPUT on a runner and printed otherwise; the summary says what was decided and why.

  Decisions:

    branch behind master (ci)   fails here, before anything is paid for. Merge or rebase, then run again.
    the other workflow running  fails here (ci against checks, checks against ci): one run per head.
    branch, docs only           the test legs, digests, regen, lint and zero-alloc skip; docs runs.
    tests and digests           skip when every requested leg passed for this tree and the digests
                                comparison passed for at least that many legs. Otherwise all requested
                                legs run, because the digests job compares legs of one run.
    regen                       skip when it passed for this tree. On a branch it runs only when its
                                inputs differ from master, on a push only when the push changed them
                                (regen.yml's paths); ci.yml runs it on a branch only, since master's
                                push starts regen.yml itself.
    docs, lint, zero-alloc      skip when passed for this tree.
    schedule (ci)               the weekly macOS run on master: the osx-arm64 leg alone, skipped when
                                it passed for this tree; no digests (one leg), docs, lint or zero-alloc.
    force                       the manual input: everything requested runs.

  Any API failure makes every job run, and fails a release check. Paying twice beats skipping
  something untested.

  Run it locally against the repository's history, read-only:

    pwsh tools/ci/ci-gate.ps1 -Workflow ci -EventName push -Sha <commit> -Ref refs/heads/master -Repository owner/name -Token <token>
#>
param(
    [ValidateSet('ci', 'checks', 'regen', 'release')]
    [string]$Workflow   = $(if ($env:GATE_WORKFLOW) { $env:GATE_WORKFLOW } else { 'ci' }),
    [string]$EventName  = $env:GITHUB_EVENT_NAME,
    [string]$Sha        = $env:GITHUB_SHA,
    [string]$Ref        = $env:GITHUB_REF,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Token      = $env:GH_TOKEN,
    [string]$RunId      = $env:GITHUB_RUN_ID,
    [string]$Before     = $env:GATE_BEFORE,     # github.event.before, on a push
    [string]$MacOS      = $env:GATE_MACOS,      # inputs.macos, ci.yml
    [string]$Force      = $env:GATE_FORCE,      # inputs.force, a manual run
    [string]$Lint       = $env:GATE_LINT,       # inputs.lint, checks.yml ('false' turns it off)
    [string]$ZeroAlloc  = $env:GATE_ZERO_ALLOC  # inputs.zero_alloc, checks.yml
)

$ErrorActionPreference = 'Stop'

function Get-Api([string]$Path) {
    try {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/$Path" -Headers @{
            Authorization          = "Bearer $Token"
            Accept                 = 'application/vnd.github+json'
            'X-GitHub-Api-Version' = '2022-11-28'
            'User-Agent'           = 'sharpslang-ci-gate'
        }
    }
    catch {
        throw "GET $Path : $($_.Exception.Message)"
    }
}

function Test-Like([string[]]$Files, [string[]]$Patterns) {
    @($Files | Where-Object { $f = $_; $Patterns | Where-Object { $f -like $_ } }).Count -gt 0
}

# The workflows whose jobs count for a tree, and the one each gate must not overlap with.
$countedWorkflows = '.github/workflows/ci.yml', '.github/workflows/checks.yml', '.github/workflows/regen.yml'
$exclusive = @{ ci = '.github/workflows/checks.yml'; checks = '.github/workflows/ci.yml' }

# The job names, as the workflows spell them. A called workflow's job is "<caller job> / <name>".
$jobName = @{
    docs = 'links and section references'; lint = 'lint'; zero_alloc = 'zero-alloc'; regen = 'generated bindings are current'
}
$regenInputs = 'Directory.Build.props', 'tools/generate-native.ps1', 'tools/generate-native-docs.*', 'dotnet/Native/*', '.github/workflows/regen.yml'
$noResult    = '*.md', 'docs/*'
$onBranch    = $Ref -like 'refs/heads/*' -and $Ref -ne 'refs/heads/master'
$isSchedule  = $EventName -eq 'schedule'
$short       = $Sha.Substring(0, [Math]::Min(7, $Sha.Length))
$notes       = [System.Collections.Generic.List[string]]::new()

$required = @('win-x64', 'linux-x64', 'linux-arm64')
$requested = if ($isSchedule) { @('osx-arm64') } elseif ($MacOS -eq 'true') { $required + 'osx-arm64' } else { $required }

$decision = [ordered]@{}
switch ($Workflow) {
    'ci' {
        $decision.legs = @($requested); $decision.digests = 'run'; $decision.docs = 'run'
        $decision.lint = 'run'; $decision.zero_alloc = 'run'; $decision.regen = $(if ($onBranch) { 'run' } else { 'skip' })
        if (-not $onBranch -and -not $isSchedule) { $notes.Add("regen skips here: a push to master starts regen.yml itself when its inputs changed.") }
        if ($isSchedule) {
            $decision.digests = 'skip'; $decision.docs = 'skip'; $decision.lint = 'skip'; $decision.zero_alloc = 'skip'
            $notes.Add('The weekly macOS run: the osx-arm64 leg alone; digests compare legs of one run, so they skip, and so do docs, lint and zero-alloc.')
        }
    }
    'checks' {
        $decision.lint = $(if ($Lint -eq 'false') { 'skip' } else { 'run' })
        $decision.zero_alloc = $(if ($ZeroAlloc -eq 'false') { 'skip' } else { 'run' })
    }
    'regen' { $decision.regen = 'run' }
    'release' { }
}

function Write-Decision([string]$Headline) {
    $lines = foreach ($k in $decision.Keys) {
        if ($k -eq 'legs') {
            $legs = @($decision.legs)
            "legs=$(ConvertTo-Json -Compress -InputObject $legs)"
            "count=$($legs.Count)"
        }
        else { "$k=$($decision[$k])" }
    }
    $rows = foreach ($k in $decision.Keys) {
        $v = if ($k -eq 'legs') { $(if (@($decision.legs).Count) { @($decision.legs) -join ', ' } else { 'none' }) } else { $decision[$k] }
        "| $k | $v |"
    }
    $summary = @("### CI gate ($Workflow): $short", '', $Headline, '') + @($notes | ForEach-Object { "- $_" }) + @('', '| Job | Decision |', '|---|---|') + @($rows)
    if ($env:GITHUB_OUTPUT) { $lines | Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8 } else { $lines | Write-Output }
    if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 } else { $summary | Write-Host }
}

function Stop-Gate([string]$Message) {
    Write-Host "::error::$Message"
    if ($env:GITHUB_STEP_SUMMARY) { "### CI gate ($Workflow): $short", '', $Message | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8 }
    exit 1
}

# ---------------------------------------------------------------- one run per head
# Before force: a forced run overlapping another is still two runs on one head.
if ($exclusive.ContainsKey($Workflow)) {
    try {
        $active = foreach ($status in 'queued', 'in_progress', 'waiting', 'pending', 'requested') {
            @((Get-Api "actions/runs?head_sha=$Sha&status=$status&per_page=50").workflow_runs) |
                Where-Object { $_.path -eq $exclusive[$Workflow] -and "$($_.id)" -ne $RunId }
        }
        if ($active) {
            $run = @($active)[0]
            Stop-Gate "$($run.name) run $($run.id) is $($run.status) on this commit ($short). checks.yml and ci.yml never run on one head at once (AGENTS.md#actions-budget): wait for it, or cancel it, then run again. $($run.html_url)"
        }
    }
    catch { $notes.Add("Could not list the runs on this commit ($($_.Exception.Message)); the overlap check was not made.") }
}

if ($Force -eq 'true' -and $Workflow -ne 'release') {
    Write-Decision "Forced by the manual input: every requested job runs."
    exit 0
}

try {
    # ------------------------------------------------------------ a branch: up to date, and what changed
    $changed = $null
    if ($onBranch -and $Workflow -ne 'release') {
        $compare = Get-Api "compare/master...$Sha"
        if ($compare.behind_by -gt 0 -and $Workflow -eq 'ci') {
            Stop-Gate "$($Ref -replace '^refs/heads/', '') is $($compare.behind_by) commit(s) behind master. Merge or rebase it onto master, then run CI again: a run of a stale branch tests a tree master will not have."
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
            foreach ($k in @($decision.Keys)) {
                if ($k -eq 'legs') { $decision.legs = @() }
                elseif ($k -ne 'docs') { $decision[$k] = 'skip' }
            }
            $notes.Add("Only documentation differs from master ($($changed.Count) files): everything but the docs check skips.")
        }
        if ($decision.Contains('regen') -and $decision.regen -eq 'run' -and -not (Test-Like $changed $regenInputs)) {
            $decision.regen = 'skip'
            $notes.Add("regen skips: its inputs ($($regenInputs -join ', ')) are unchanged.")
        }
    }

    # ------------------------------------------------------------ what already passed for this tree
    $tree = (Get-Api "git/commits/$Sha").tree.sha
    $heads = [System.Collections.Generic.List[string]]::new()
    $heads.Add($Sha)
    # Piped, so the list is enumerated: Invoke-RestMethod hands a JSON array back as one object, and
    # @() around it made a one-element list whose .head.sha was every head at once.
    foreach ($pr in (Get-Api "commits/$Sha/pulls" | ForEach-Object { $_ })) {
        if ($pr.head.sha -and -not $heads.Contains($pr.head.sha)) { $heads.Add($pr.head.sha) }
    }
    # A release: also the first-parent commits whose difference from this one is documentation only,
    # read from the local history (release.yml checks out with fetch-depth 0). Their trees differ, so
    # they are taken whatever their tree.
    $docsEquivalent = [System.Collections.Generic.HashSet[string]]::new()
    if ($Workflow -eq 'release') {
        foreach ($c in @(git rev-list --first-parent --max-count=30 "$Sha" 2>$null | Select-Object -Skip 1)) {
            $diff = @(git diff --name-only $c $Sha)
            if (@($diff | Where-Object { $f = $_; -not ($noResult | Where-Object { $f -like $_ }) }).Count -gt 0) { break }
            [void]$docsEquivalent.Add($c)
            if (-not $heads.Contains($c)) { $heads.Add($c) }
        }
        if ($docsEquivalent.Count) { $notes.Add("Documentation alone differs from $($docsEquivalent.Count) earlier first-parent commit(s); their runs count.") }
    }

    $passed = [System.Collections.Generic.HashSet[string]]::new()
    $digestLegs = 0
    foreach ($head in $heads) {
        if ($head -ne $Sha -and -not $docsEquivalent.Contains($head) -and (Get-Api "git/commits/$head").tree.sha -ne $tree) { continue }
        $runs = @((Get-Api "actions/runs?head_sha=$head&per_page=50").workflow_runs) | Where-Object { $countedWorkflows -contains $_.path }
        foreach ($run in $runs) {
            if ("$($run.id)" -eq $RunId) { continue }
            foreach ($job in @((Get-Api "actions/runs/$($run.id)/jobs?per_page=100").jobs)) {
                if ($job.conclusion -ne 'success') { continue }
                $name = ($job.name -split ' / ')[-1]   # a called workflow's job: "<caller job> / <name>"
                [void]$passed.Add($name)
                if ($name -match '^every platform cooks the same asset \((\d+) legs\)$') {
                    $digestLegs = [Math]::Max($digestLegs, [int]$Matches[1])
                }
            }
        }
    }

    if ($Workflow -eq 'release') {
        $missing = @($required | Where-Object { -not $passed.Contains("test ($_)") } | ForEach-Object { "test ($_)" })
        if ($digestLegs -lt $required.Count) { $missing += "the digests comparison of $($required.Count) legs" }
        foreach ($k in 'docs', 'lint', 'zero_alloc') { if (-not $passed.Contains($jobName[$k])) { $missing += $jobName[$k] } }
        if ($missing) {
            Stop-Gate "CI has not passed on tree $($tree.Substring(0, 7)) of $short for: $($missing -join ', '). Run CI on master (Actions > CI > Run workflow), then release again."
        }
        $notes.Add("Passed for tree $($tree.Substring(0, 7)): the three test legs, the digests comparison of $digestLegs legs, docs, lint and zero-alloc.")
        Write-Decision "$EventName on $Ref, tree $($tree.Substring(0, 7)), looked up through $($heads.Count) commit(s): CI has passed what a release needs."
        exit 0
    }

    if ($decision.Contains('legs')) {
        $legsDone = @($decision.legs | Where-Object { $passed.Contains("test ($_)") })
        $needsDigests = $decision.digests -eq 'run'
        if (@($decision.legs).Count -gt 0 -and $legsDone.Count -eq @($decision.legs).Count -and (-not $needsDigests -or $digestLegs -ge @($decision.legs).Count)) {
            $notes.Add("Every requested leg$(if ($needsDigests) { ' and the digests comparison' }) already passed for tree $($tree.Substring(0, 7)): the test legs skip.")
            $decision.legs = @()
        }
        elseif ($legsDone.Count -gt 0) {
            $notes.Add("Passed before for this tree: $($legsDone -join ', '). All requested legs run anyway, because the digests job compares legs of one run.")
        }
        if (@($decision.legs).Count -eq 0) { $decision.digests = 'skip' }
    }

    foreach ($k in 'docs', 'lint', 'zero_alloc', 'regen') {
        if ($decision.Contains($k) -and $decision[$k] -eq 'run' -and $passed.Contains($jobName[$k])) {
            $decision[$k] = 'skip'
            $notes.Add("$($jobName[$k]) already passed for this tree.")
        }
    }

    $headline = "$EventName on $Ref, tree $($tree.Substring(0, 7)), looked up through $($heads.Count) commit(s)."
}
catch {
    if ($Workflow -eq 'release') { Stop-Gate "could not establish what this tree passed ($($_.Exception.Message)); a release does not start on an unknown." }
    Write-Host "::warning::could not establish what this tree already passed ($($_.Exception.Message)); every requested job runs"
    switch ($Workflow) {
        'ci' {
            $decision.legs = @($requested)
            $decision.digests = $(if ($isSchedule) { 'skip' } else { 'run' })
            foreach ($k in 'docs', 'lint', 'zero_alloc') { $decision[$k] = $(if ($isSchedule) { 'skip' } else { 'run' }) }
            $decision.regen = $(if ($onBranch) { 'run' } else { 'skip' })
        }
        'checks' {
            $decision.lint = $(if ($Lint -eq 'false') { 'skip' } else { 'run' })
            $decision.zero_alloc = $(if ($ZeroAlloc -eq 'false') { 'skip' } else { 'run' })
        }
        'regen' { $decision.regen = 'run' }
    }
    $headline = 'The GitHub API did not answer: every requested job runs.'
}

Write-Decision $headline
