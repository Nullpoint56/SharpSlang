#!/usr/bin/env pwsh
<#
.SYNOPSIS
    The packages as an outside project sees them: restores the packed .nupkg files from a folder
    into a packages folder of its own, then builds and runs tools/consume portable, with `run -r`,
    `publish -r` and Native AOT, and each optional package alone, and checks what each run loads
    and lays out.

.DESCRIPTION
    Every check ends PASS, FAIL or NOT YET. NOT YET is a check whose prerequisite the packages do
    not have yet, named ("not yet: <what is missing>"), so a check that cannot pass is neither a
    silent failure nor a skip; the summary lists them. The exit code is 1 when any check FAILs.

      core, portable     the loaded slang-compiler is the one under runtimes/<rid>/native/ of the
                         output; SPIR-V with the optimiser required; Slang's standard modules found
                         and imported from beside the loaded compiler; signed DXIL on win-x64; a
                         baked package with its NRI projection written for the reader runs; started
                         as the apphost and as `dotnet <dll>`, from another directory; the layout:
                         every packed RID's licences under licenses/<binding>/<rid>/ and standard
                         modules under runtimes/<rid>/native/, nothing of them flat beside it
      core, run -r       the same through `dotnet run -r <rid>`, flat: the run's RID's licences
                         and the module folder beside the application
      licences off       a RID-specific build with <binding>CopyLicenses=false: no licenses/
      core, publish -r   the same from a RID-specific publish directory
      core, AOT          the same from a Native AOT publish, which warns nothing (IL2xxx, IL3xxx);
                         every assembly of ours restored declares IsAotCompatible
      dxc alone          the dxc package with nothing else of ours: dxcompiler.dll and dxil.dll beside
                         the application (win-x64), no compiler
      reader alone       the reader with nothing else of ours: reads the baked package, and its
                         output holds no native library and the process loads no compiler
      NRI projection     the same with the NRI projection alone, which reads the projection stored
      alone              in the package
      wrong dxc          an explicit Microsoft.Direct3D.DXC of another version fails the build with
                         SHARPSLANG011, both with the pinned version already in the package cache
                         and with an empty one

    What each run references comes from tools/ci/packages.psd1 (Consume), at the versions the folder
    holds. Each run is a copy of tools/consume under _build/consume/<run>/.

        pwsh tools/consume/consume.ps1 -Feed nupkg
        pwsh tools/consume/consume.ps1 -Feed nupkg -Skip aot

.PARAMETER Feed
    The folder with the packed .nupkg files (default: nupkg/).

.PARAMETER WrongDxc
    The Microsoft.Direct3D.DXC version for the wrong-dxc run (default: the newest release on
    nuget.org below the pin).

.PARAMETER Skip
    Runs to leave out (portable, run, nolicences, publish, aot, dxc, reader, nri, wrongdxc); each is reported as
    skipped by request.
#>
param(
    [string]$Feed,
    [string]$Rid,
    [string]$WrongDxc,
    [string[]]$Skip = @(),
    [object]$Pins = @{},
    [string]$Spec
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
Import-Module (Join-Path $PSScriptRoot ".." "ci" "Packages.psm1") -Force
$root = Get-RepoRoot
$data = Import-PackageSpec $Spec
$pinValues = Get-Pins $data $root (ConvertTo-Map $Pins)
$c = $data.Consume
$Skip = @($Skip | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if (-not $Feed) { $Feed = Join-Path $root "nupkg" }
$Feed = (Resolve-Path $Feed).Path
if (-not $Rid) { $Rid = "$($IsWindows ? 'win' : ($IsMacOS ? 'osx' : 'linux'))-$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())" }
$exe = $IsWindows ? ".exe" : ""
$work = Join-Path $root "_build" "consume"
if (Test-Path $work) { dotnet build-server shutdown *> $null; Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null
$packagesPath = Join-Path $work "packages"
$packed = Get-PackedPackages $Feed
Write-Host "feed $Feed`: $(($packed.Keys | Sort-Object | ForEach-Object { "$_ $($packed[$_][0].Version)" }) -join ', '); RID $Rid"

# ------------------------------------------------------------------------------------------ results
$results = [System.Collections.Generic.List[object]]::new()
function Result([string]$run, [string]$outcome, [string]$check, [string]$detail) {
    $results.Add([pscustomobject]@{ Run = $run; Outcome = $outcome; Check = $check; Detail = $detail })
    $label = @{ PASS = "PASS   "; FAIL = "FAIL   "; NOTYET = "NOT YET"; SKIP = "SKIP   " }[$outcome]
    Write-Host "  $label $check$(if ($detail) { ": $detail" })"
}
# The program's own PASS/FAIL/NOTYET lines, folded into the results
function Absorb([string]$run, [string[]]$lines, [int]$exit, [string]$what) {
    $own = 0
    foreach ($l in $lines) {
        if ($l -match '^(PASS|FAIL|NOTYET) ([^:]+): (.*)$') { Result $run $Matches[1] $Matches[2] $Matches[3]; $own++ }
    }
    if ($exit -ne 0 -and -not @($lines -match '^FAIL ').Count) { Result $run "FAIL" $what "exit code ${exit}: $(($lines | Select-Object -Last 5) -join ' / ')" }
    elseif (-not $own) { Result $run "FAIL" $what "the program reported nothing (exit $exit)" }
}

# A package entry prefix the packed package carries, by id
function Carries([string]$id, [string]$pattern) {
    $p = @($packed[$id])[0]
    $p -and @($p.Entries | Where-Object { $_ -like $pattern }).Count -gt 0
}

# ------------------------------------------------------------------------------------------ runs
# A copy of tools/consume with the generated references for one kind of run
function New-Run([string]$name, [string]$kind, [string[]]$extraProps = @(), [string]$packages = $packagesPath) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($f in "SharpSlang.Consume.csproj", "Program.cs", "nuget.config", "Directory.Build.props", "Directory.Build.targets") { Copy-Item (Join-Path $PSScriptRoot $f) $dir }
    # the tools' analyzer policy, which tools/.editorconfig gives the source folder
    Copy-Item (Join-Path $PSScriptRoot ".." ".editorconfig") (Join-Path $dir ".editorconfig")
    $run = $c.Runs[$kind]
    $items = foreach ($id in @($run.Packages) + @($run.Optional | Where-Object { $_ -and $packed.ContainsKey($_) })) {
        if (-not $packed.ContainsKey($id)) { throw "$id is not in $Feed (tools/ci/packages.psd1, Consume.Runs.$kind)" }
        "    <PackageReference Include=`"$id`" Version=`"[$(@($packed[$id])[0].Version)]`" />"
    }
    $usings = foreach ($u in $run.Usings) { "    <Using Include=`"$u`" />" }
    $props = @"
<Project>
  <!-- Generated by tools/consume/consume.ps1 for the $name run ($kind). -->
  <PropertyGroup>
    <DefineConstants>`$(DefineConstants);$(@($run.Defines) -join ';')</DefineConstants>
    <RestoreSources>$Feed;https://api.nuget.org/v3/index.json</RestoreSources>
  </PropertyGroup>
  <ItemGroup>
$($items -join "`n")
$($usings -join "`n")
  </ItemGroup>
</Project>
"@
    $propsPath = Join-Path $dir "consume.g.props"
    Set-Content $propsPath $props -Encoding utf8NoBOM
    [pscustomobject]@{
        Name = $name; Dir = $dir; Project = (Join-Path $dir "SharpSlang.Consume.csproj")
        # No build servers: a node that outlives the run keeps the packages folder's task assemblies locked
        Props = @("-p:SharpSlangConsumeProps=$propsPath", "-p:RestorePackagesPath=$packages", "--disable-build-servers") + $extraProps
    }
}

function Invoke-Logged([string]$log, [scriptblock]$command) {
    $lines = @(& $command 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $lines | Set-Content $log
    [pscustomobject]@{ Lines = $lines; Exit = $code }
}

# What the output must hold, read from the package: for each RID it carries (the run's own when
# flat, every packed one when portable), its licences under licenses/<Binding>/<rid>/ and its
# standard modules beside where the compiler lands; nothing of them flat beside the application.
# -NoLicences: the run set <Binding>CopyLicenses=false, so licenses/ must be absent.
function Test-Layout([string]$run, [string]$out, [bool]$flat, [switch]$NoLicences) {
    $binding = $c.Runs.core.Packages[0]
    $pkg = @($packed[$binding])[0]
    $rids = $flat ? @($Rid) : @($pkg.Entries | Where-Object { $_ -match '^runtimes/([^/]+)/native/' } | ForEach-Object { $_.Split('/')[1] } | Select-Object -Unique)
    $licences = Join-Path $out "licenses"
    if ($NoLicences) {
        if (Test-Path $licences) { Result $run "FAIL" "licences opted out" "$out has licenses/ with $($c.CopyLicenses)=false" }
        else { Result $run "PASS" "licences opted out" "no licenses/ with $($c.CopyLicenses)=false" }
    }
    elseif (-not (Carries $binding "licenses/*")) {
        Result $run "NOTYET" "licences beside the application" "not yet: $binding carries no licenses/<rid>/ to copy"
    }
    else {
        $missing = foreach ($r in $rids) {
            foreach ($e in @($pkg.Entries | Where-Object { $_ -like "licenses/$r/*" })) {
                $dest = Join-Path $licences $binding $r ($e.Substring("licenses/$r/".Length))
                if (-not (Test-Path $dest)) { $dest }
            }
        }
        $count = @(Get-ChildItem $licences -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($missing) { Result $run "FAIL" "licences beside the application" "$(@($missing).Count) of the package's licence files missing, e.g. $(@($missing)[0])" }
        else { Result $run "PASS" "licences beside the application" "$count files under licenses/$binding/ for $($rids -join ', ')" }
    }
    $flatTexts = @(Get-ChildItem $out -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|NOTICE|COPYING|THIRD-PARTY)' })
    # Slang's LICENSES/ and third-party-notices/ copied flat; licenses/ is ours only when it holds
    # nothing but licenses/<binding>/ (the names compare without case, as Windows' file system does)
    $flatDirs = @(Get-ChildItem $out -Directory | Where-Object {
        $_.Name -eq 'third-party-notices' -or ($_.Name -in 'LICENSES', 'LICENCES' -and @(Get-ChildItem $_.FullName | Where-Object Name -ne $binding).Count) })
    if ($flatTexts -or $flatDirs) { Result $run "FAIL" "no licence text flat beside the application" (($flatTexts + $flatDirs | ForEach-Object Name) -join ', ') }
    else { Result $run "PASS" "no licence text flat beside the application" "" }

    # The standard modules, folders kept, where the compiler lands
    $modules = @(); $absent = @()
    foreach ($r in $rids) {
        $base = $flat ? $out : (Join-Path $out "runtimes" $r "native")
        foreach ($e in @($pkg.Entries | Where-Object { $_ -like "standard-modules/$r/*" })) {
            $modules += $e
            $dest = Join-Path $base ($e.Substring("standard-modules/$r/".Length))
            if (-not (Test-Path $dest)) { $absent += $dest }
        }
    }
    if (-not $modules) { Result $run "NOTYET" "standard modules beside the compiler" "not yet: $binding carries no standard-modules/<rid>/" }
    elseif ($absent) { Result $run "FAIL" "standard modules beside the compiler" "$($absent.Count) of $($modules.Count) files missing, e.g. $($absent[0])" }
    else { Result $run "PASS" "standard modules beside the compiler" "$($modules.Count) files for $($rids -join ', ')" }

    # Files of a subfolder of native/ (or of the module folder) that landed flat beside the application
    $nested = @($pkg.Entries | Where-Object { $_ -match "^runtimes/[^/]+/native/[^/]+/" -or $_ -match '^standard-modules/' } | ForEach-Object { Split-Path $_ -Leaf } | Select-Object -Unique)
    $flattened = @($nested | Where-Object { Test-Path (Join-Path $out $_) })
    if ($flattened) { Result $run "FAIL" "no native/ subfolder flattened" "$($flattened.Count) files of a native/ subfolder are flat beside the application (e.g. $($flattened[0]))" }
    else { Result $run "PASS" "no native/ subfolder flattened" "" }
}

$baked = Join-Path $work "baked.slangpkg"
$core = New-Run "core" "core"
$coreBuilt = $false

# core, portable
if ($Skip -notcontains 'portable') {
    Write-Host "`n==> core, portable (dotnet build)"
    $b = Invoke-Logged (Join-Path $work "core-build.log") { dotnet build $core.Project -c Release --nologo @($core.Props) }
    if ($b.Exit -ne 0) { Result "portable" "FAIL" "build" "exit $($b.Exit): $(($b.Lines | Select-String ': error ' | Select-Object -First 3) -join ' / ')" }
    else {
        $coreBuilt = $true
        $out = Join-Path $core.Dir "bin" "Release" "net11.0"
        $nativeDir = Join-Path $out "runtimes" $Rid "native"
        if (@(Get-ChildItem $nativeDir -Filter "*$($c.Compiler)*" -ErrorAction SilentlyContinue).Count) { Result "portable" "PASS" "portable layout" "the payload is under runtimes/$Rid/native/" }
        else { Result "portable" "FAIL" "portable layout" "no $($c.Compiler) under runtimes/$Rid/native/ of $out" }
        Push-Location $work
        try {
            $r = Invoke-Logged (Join-Path $work "core-apphost.log") { & (Join-Path $out "SharpSlang.Consume$exe") $baked }
            Absorb "portable (apphost)" $r.Lines $r.Exit "run"
            $r = Invoke-Logged (Join-Path $work "core-dll.log") { dotnet (Join-Path $out "SharpSlang.Consume.dll") }
            Absorb "portable (dotnet dll)" $r.Lines $r.Exit "run"
        } finally { Pop-Location }
        Test-Layout "portable" $out $false
    }
} else { Result "portable" "SKIP" "run" "skipped by request" }

# core, run -r
if ($Skip -notcontains 'run') {
    Write-Host "`n==> core, dotnet run -r $Rid"
    $r = Invoke-Logged (Join-Path $work "core-run.log") { dotnet run --project $core.Project -c Release -r $Rid @($core.Props) -- (Join-Path $work "baked-run.slangpkg") }
    Absorb "run -r" $r.Lines $r.Exit "run"
    if ($r.Exit -eq 0) { Test-Layout "run -r" (Join-Path $core.Dir "bin" "Release" "net11.0" $Rid) $true }
} else { Result "run -r" "SKIP" "run" "skipped by request" }

# core, RID-specific build with the notices opted out
if ($Skip -notcontains 'nolicences') {
    Write-Host "`n==> core, dotnet build -r $Rid -p:$($c.CopyLicenses)=false"
    $optOut = New-Run "nolicences" "core" @("-p:$($c.CopyLicenses)=false")
    $b = Invoke-Logged (Join-Path $work "nolicences-build.log") { dotnet build $optOut.Project -c Release -r $Rid @($optOut.Props) }
    if ($b.Exit -ne 0) { Result "licences off" "FAIL" "build" "exit $($b.Exit): $(($b.Lines | Select-String ': error ' | Select-Object -First 3) -join ' / ')" }
    else { Test-Layout "licences off" (Join-Path $optOut.Dir "bin" "Release" "net11.0" $Rid) $true -NoLicences }
} else { Result "licences off" "SKIP" "run" "skipped by request" }

# core, publish -r
if ($Skip -notcontains 'publish') {
    Write-Host "`n==> core, dotnet publish -r $Rid"
    $pub = Join-Path $work "publish-$Rid"
    $b = Invoke-Logged (Join-Path $work "core-publish.log") { dotnet publish $core.Project -c Release -r $Rid -o $pub @($core.Props) }
    if ($b.Exit -ne 0) { Result "publish -r" "FAIL" "publish" "exit $($b.Exit): $(($b.Lines | Select-String ': error ' | Select-Object -First 3) -join ' / ')" }
    else {
        Push-Location $work
        try { $r = Invoke-Logged (Join-Path $work "core-publish-run.log") { & (Join-Path $pub "SharpSlang.Consume$exe") } } finally { Pop-Location }
        Absorb "publish -r" $r.Lines $r.Exit "run"
        Test-Layout "publish -r" $pub $true
    }
} else { Result "publish -r" "SKIP" "run" "skipped by request" }

# core, Native AOT
if ($Skip -notcontains 'aot') {
    Write-Host "`n==> core, dotnet publish -r $Rid -p:PublishAot=true"
    $aot = Join-Path $work "aot-$Rid"
    $b = Invoke-Logged (Join-Path $work "core-aot.log") { dotnet publish $core.Project -c Release -r $Rid -o $aot -p:PublishAot=true @($core.Props) }
    $aotWarnings = @($b.Lines | Select-String -Pattern 'warning IL(2|3)\d{3}' | ForEach-Object { if ($_ -match 'warning (IL\d{4})') { $Matches[1] } } | Select-Object -Unique)
    if ($b.Exit -ne 0) { Result "AOT" "FAIL" "publish" "exit $($b.Exit): $(($b.Lines | Select-String ': error ' | Select-Object -First 3) -join ' / ')" }
    else {
        Push-Location $work
        try { $r = Invoke-Logged (Join-Path $work "core-aot-run.log") { & (Join-Path $aot "SharpSlang.Consume$exe") } } finally { Pop-Location }
        Absorb "AOT" $r.Lines $r.Exit "run"
        Test-Layout "AOT" $aot $true
    }
    # No trim, AOT or single-file warning from the publish: each names a construct of ours that
    # Native AOT cannot keep, or an assembly that never ran the analyzers
    if ($aotWarnings) { Result "AOT" "FAIL" "no trim or AOT warnings" "the AOT publish warned $($aotWarnings -join ', ') (core-aot.log)" }
    elseif ($b.Exit -eq 0) { Result "AOT" "PASS" "no trim or AOT warnings" "" }
    # IsAotCompatible sets IsTrimmable, which the SDK writes into the assembly as metadata: every
    # assembly of ours the core run restored
    $ours = @($data.Packages | Where-Object { $_.Assemblies } | ForEach-Object { $_.Id })
    $dlls = @(foreach ($id in $ours) { Get-ChildItem (Join-Path $packagesPath $id.ToLowerInvariant()) -Recurse -Filter "$id.dll" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '[\\/]lib[\\/]' } })
    $undeclared = @($dlls | Where-Object { -not [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($_.FullName)).Contains("IsTrimmable") } | ForEach-Object Name)
    if (-not @($dlls | Where-Object Name -eq "$($c.Binding).dll")) { Result "AOT" "FAIL" "declares IsAotCompatible" "no $($c.Binding).dll restored under $packagesPath" }
    elseif ($undeclared) { Result "AOT" "FAIL" "declares IsAotCompatible" "$($undeclared -join ', ') declare no IsAotCompatible/IsTrimmable" }
    else { Result "AOT" "PASS" "declares IsAotCompatible" "$(($dlls | ForEach-Object Name | Sort-Object -Unique) -join ', ') carry IsTrimmable" }
} else { Result "AOT" "SKIP" "run" "skipped by request" }

# the dxc package alone
if ($Skip -notcontains 'dxc') {
    Write-Host "`n==> the dxc package alone"
    $dxcRun = New-Run "dxc" "dxc"
    $b = Invoke-Logged (Join-Path $work "dxc-build.log") { dotnet build $dxcRun.Project -c Release @($dxcRun.Props) }
    if ($b.Exit -ne 0) { Result "dxc alone" "FAIL" "build" "exit $($b.Exit): $(($b.Lines | Select-String ': error ' | Select-Object -First 3) -join ' / ')" }
    else {
        $out = Join-Path $dxcRun.Dir "bin" "Release" "net11.0"
        if ($Rid -eq 'win-x64') {
            $missing = @($c.DxcFiles | Where-Object { -not (Test-Path (Join-Path $out $_)) })
            if ($missing) { Result "dxc alone" "FAIL" "dxc copied" "missing $($missing -join ', ') in $out" } else { Result "dxc alone" "PASS" "dxc copied" ($c.DxcFiles -join ', ') }
        }
        $compiler = @(Get-ChildItem $out -Recurse -File | Where-Object { $_.Name -like "*$($c.Compiler)*" })
        if ($compiler) { Result "dxc alone" "FAIL" "no compiler" "$($compiler[0].Name) is in the output" } else { Result "dxc alone" "PASS" "no compiler" "" }
        $r = Invoke-Logged (Join-Path $work "dxc-run.log") { dotnet (Join-Path $out "SharpSlang.Consume.dll") }
        if ($Rid -eq 'win-x64') { Absorb "dxc alone" $r.Lines $r.Exit "run" }
    }
} else { Result "dxc alone" "SKIP" "run" "skipped by request" }

# the reader alone, and the NRI projection alone: no compiler in the output or the process, and no
# NRI library either (the projection references SharpNRI.Descs only)
foreach ($alone in @(@{ Kind = "reader"; Label = "reader alone" }, @{ Kind = "nri"; Label = "NRI projection alone" })) {
    $label = $alone.Label
    if ($Skip -contains $alone.Kind) { Result $label "SKIP" "run" "skipped by request"; continue }
    Write-Host "`n==> $label"
    $reader = New-Run $alone.Kind $alone.Kind
    $b = Invoke-Logged (Join-Path $work "$($alone.Kind)-build.log") { dotnet build $reader.Project -c Release @($reader.Props) }
    if ($b.Exit -ne 0) { Result $label "FAIL" "build" "exit $($b.Exit): $(($b.Lines | Select-String ': error ' | Select-Object -First 3) -join ' / ')"; continue }
    $out = Join-Path $reader.Dir "bin" "Release" "net11.0"
    $native = @(Get-ChildItem $out -Recurse -File | Where-Object { $_.Name -like "*$($c.Compiler)*" -or $_.Name -like "*glslang*" -or $_.Name -match '^(lib)?NRI\.(dll|so|dylib)$' })
    if ($native) { Result $label "FAIL" "no native library in the output" "$(($native | ForEach-Object Name) -join ', ')" } else { Result $label "PASS" "no native library in the output" "" }
    if (-not (Test-Path $baked)) { Result $label "FAIL" "read" "no baked package: the core portable run did not write $baked"; continue }
    $r = Invoke-Logged (Join-Path $work "$($alone.Kind)-run.log") { dotnet (Join-Path $out "SharpSlang.Consume.dll") $baked }
    Absorb $label $r.Lines $r.Exit "run"
}
# a wrong dxc version fails the build
if ($Skip -notcontains 'wrongdxc') {
    $pinned = $pinValues[$c.DxcPin]
    if (-not $WrongDxc) {
        $versions = @((Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/$($c.DxcPackage.ToLowerInvariant())/index.json").versions | Where-Object { $_ -notmatch '-' })
        $WrongDxc = @($versions | Where-Object { [version]$_ -lt [version]$pinned })[-1]
    }
    # Whether the dxc package's targets compare the resolved version at all: with no error code in
    # them, the build can only fail by not finding the pinned version's files, which a package
    # cache that holds them (any machine that restored the pin once) defeats
    $dxcId = @($c.Runs.dxc.Packages)[0]
    $guarded = @(@($packed[$dxcId])[0].Texts.GetEnumerator() | Where-Object { $_.Key -like '*.targets' -and $_.Value -match $c.ErrorCode }).Count -gt 0
    # Twice: with the shared packages folder, which holds the pinned version from the runs above,
    # and with an empty one, as on a machine that never restored it
    foreach ($variant in @(@{ Name = "wrongdxc"; Label = "wrong dxc, pin in the cache"; Packages = $packagesPath }, @{ Name = "wrongdxc-fresh"; Label = "wrong dxc, empty cache"; Packages = (Join-Path $work "packages-fresh") })) {
        Write-Host "`n==> $($c.DxcPackage) $WrongDxc instead of $pinned ($($variant.Label))"
        $wrong = New-Run $variant.Name "core" @("-p:SharpSlangConsumeDxcOverride=$WrongDxc") $variant.Packages
        $b = Invoke-Logged (Join-Path $work "$($variant.Name)-build.log") { dotnet build $wrong.Project -c Release @($wrong.Props) }
        $errors = @($b.Lines | Select-String ': error ' | ForEach-Object { $_.Line -replace '\s*\[[^\]]+\]$', '' } | Select-Object -Unique)
        $run = $variant.Label
        if ($b.Exit -eq 0) {
            if ($guarded) { Result $run "FAIL" "fails the build" "the build with $($c.DxcPackage) $WrongDxc succeeded" }
            else { Result $run "NOTYET" "fails the build" "not yet: the build with $($c.DxcPackage) $WrongDxc succeeded; $dxcId's targets do not compare the resolved version (no $($c.ErrorCode) error)" }
        }
        elseif (@($errors | Where-Object { $_ -match ': error NU1\d{3}' }).Count) { Result $run "FAIL" "fails the build" "the restore failed, before the check applies: $($errors[0])" }
        else {
            Result $run "PASS" "fails the build" (($errors | Select-Object -First 1) -replace '^.*?: error ', 'error ')
            if (@($errors | Where-Object { $_ -match $c.ErrorCode }).Count) { Result $run "PASS" "error code" (@($errors | Where-Object { $_ -match $c.ErrorCode })[0] -replace '^.*?error ', '') }
            elseif ($guarded) { Result $run "FAIL" "error code" "$dxcId's targets carry a $($c.ErrorCode) error, and the build failed without it" }
            else { Result $run "NOTYET" "error code" "not yet: the build fails without a $($c.ErrorCode) code (exact-version error codes in $dxcId's targets)" }
        }
    }
} else { Result "wrong dxc" "SKIP" "run" "skipped by request" }

# ------------------------------------------------------------------------------------------ summary
Write-Host ""
$results | Format-Table Run, Outcome, Check, Detail -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$notYet = @($results | Where-Object Outcome -eq 'NOTYET')
$fail = @($results | Where-Object Outcome -eq 'FAIL')
Write-Host ("consume: {0} passed, {1} failed, {2} not yet, {3} skipped; logs in {4}" -f @($results | Where-Object Outcome -eq 'PASS').Count, $fail.Count, $notYet.Count, @($results | Where-Object Outcome -eq 'SKIP').Count, $work)
if ($notYet) { Write-Host "not yet:"; foreach ($n in $notYet) { Write-Host "  $($n.Run): $($n.Check): $($n.Detail -replace '^not yet: ', '')" } }
if ($fail) { if ($env:GITHUB_ACTIONS -eq "true") { foreach ($f in $fail) { Write-Host "::error title=Consume $($f.Run)::$($f.Check): $($f.Detail)" } }; exit 1 }
