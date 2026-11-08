<#
.SYNOPSIS
    Checks a folder of packed .nupkg files against tools/ci/packages.psd1: the files each package
    must carry, per package and per RID; nothing it must not; no `//` in an entry; nothing in a
    subfolder of runtimes/<rid>/native/; exact dependencies; the commit in <repository>; the values
    its packed props set; and the generated description.

.DESCRIPTION
    Run on every pack: the release's pack job before anything is published, and locally on a pack
    of the same tree, on Linux (WSL, a clone on the Linux filesystem) as well as Windows, because
    pack paths differ between the two (SharpNRI 1.0.0-beta.4: PackagePath "lib\net11.0\" packed as
    lib/net11.0//_._ on the Linux runner, which only this check showed).

    It carries publish.yml's inline checks: every RID of the core package carries the compiler, the
    SPIR-V optimiser (slang-glslang, which holds spirv-opt) and Slang's licence texts; the dxc
    package carries its targets, depends on Microsoft.Direct3D.DXC at exactly the pinned version,
    names that version in its targets, and carries no binary.

        pwsh tools/ci/check-packages.ps1 -Folder nupkg
        pwsh tools/ci/check-packages.ps1 -Folder nupkg -Pins Version=2026.21.0-beta.1 -Commit <sha> -Rids win-x64,linux-x64

    Versions and pins come from Directory.Build.props (packages.psd1 names the properties); -Pins
    overrides them. -Commit defaults to HEAD. -Rids names the platforms the pack had native
    libraries for (default: every RID packages.psd1 lists). -Reused names packages taken from
    nuget.org rather than built (a reused SharpSlang.Descs), whose <repository commit> is the one
    they were published from.
#>
param(
    [Parameter(Mandatory)] [string]$Folder,
    [object]$Pins = @{},             # hashtable, or "Name=value" strings
    [string]$Commit,
    [string[]]$Rids,
    [string[]]$Reused = @(),
    [string]$Spec,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "Packages.psm1") -Force
$root = Get-RepoRoot
$data = Import-PackageSpec $Spec
$pinValues = Get-Pins $data $root (ConvertTo-Map $Pins)
if (-not $Commit) { $Commit = (git -C $root rev-parse HEAD).Trim() }
$allRids = @($data.Rids | ForEach-Object { $_.Rid })
if (-not $Rids) { $Rids = $allRids }
$Rids = @($Rids | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($r in $Rids) { if ($allRids -notcontains $r) { throw "unknown RID $r (packages.psd1 lists $($allRids -join ', '))" } }
$ridEntries = @($data.Rids | Where-Object { $Rids -contains $_.Rid })
$ourIds = @($data.Packages | ForEach-Object { $_.Id })

Write-Host "pins: $(($pinValues.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')"
Write-Host "commit $Commit; RIDs $($Rids -join ', ')"

$packed = Get-PackedPackages $Folder
$failed = [System.Collections.Generic.List[string]]::new()
$checked = 0
$expectedFiles = @()
foreach ($p in $data.Packages) {
    $id = $p.Id
    $version = if ($pinValues.ContainsKey($p.Version)) { $pinValues[$p.Version] } else { $null }
    $candidates = @($packed[$id] | Where-Object { $_ })
    $pkg = @($candidates | Where-Object { $version -and $_.Version -eq $version })[0]
    if (-not $pkg) {
        $seen = if ($candidates) { " (packed: $(($candidates | ForEach-Object Version) -join ', '))" } else { "" }
        if ($p.Optional -and -not $candidates) { Write-Host "`n==> $id not packed (optional: not split out yet)"; continue }
        if (-not $version) { $failed.Add("$id has no version: pin {$($p.Version)} ($($data.Pins[$p.Version])) is not set$seen"); continue }
        $failed.Add("$id $version not produced in $Folder$seen"); continue
    }
    $checked++
    $name = Split-Path $pkg.Path -Leaf
    $expectedFiles += $name
    $entries = $pkg.Entries
    Write-Host "`n==> $name ($([math]::Round((Get-Item $pkg.Path).Length / 1KB)) KB)"
    if (-not $Quiet) { $entries | Sort-Object | ForEach-Object { Write-Host ("    {0,12}  {1}" -f $pkg.Sizes[$_], $_) } }

    $must = @($p.Files | ForEach-Object { Expand-Template $_ $pinValues })
    foreach ($r in $ridEntries) { $must += @($p.PerRid | ForEach-Object { Expand-Template $_ $pinValues $r }) }
    foreach ($pattern in $must) {
        if (-not @($entries | Where-Object { $_ -like $pattern }).Count) { $failed.Add("$name lacks $pattern") }
    }
    # Entries of one finding are named one by one up to three, then counted per folder
    function Report([string]$what, [string[]]$hits) {
        if (-not $hits) { return }
        if ($hits.Count -le 3) { foreach ($h in $hits) { $failed.Add("$name $what $h") }; return }
        foreach ($g in $hits | Group-Object { $_ -replace '/[^/]*$', '/' }) {
            $failed.Add("$name $what $($g.Count -eq 1 ? $g.Group[0] : "$($g.Name)* ($($g.Count) entries, e.g. $(Split-Path $g.Group[0] -Leaf))")")
        }
    }
    foreach ($rx in $p.Forbidden) { Report "carries, against /$rx/," @($entries | Where-Object { $_ -match $rx }) }
    # no empty path segment: a separator NuGet did not take as one
    Report "has the malformed entry" @($entries | Where-Object { $_ -match '//' -or $_ -match '\\' })
    # nothing but libraries under runtimes/<rid>/native/: NuGet copies those flat
    $nested = @($entries | Where-Object { $_ -match '^runtimes/[^/]+/native/[^/]+/' })
    foreach ($g in $nested | Group-Object { if ($_ -match '^(runtimes/[^/]+/native/[^/]+/)') { $Matches[1] } }) {
        $failed.Add("$name has $($g.Count) entr$($g.Count -eq 1 ? 'y' : 'ies') in a subfolder of native/: $($g.Name)")
    }

    # Source Link: the package names the commit it was built from (PublishRepositoryUrl)
    if ($p.Commit -and $Reused -notcontains $id) {
        $repo = $pkg.Nuspec.package.metadata.repository
        $named = if ($repo) { $repo.GetAttribute('commit') } else { '' }
        if ($named -ne $Commit) { $failed.Add("$name does not name commit $Commit in <repository>$(if ($named) { " (it names $named)" } else { ' (no commit)' })") }

        # Each portable PDB beside its DLL carries a Source Link map onto that commit
        foreach ($pdb in @($entries | Where-Object { $_ -match '^lib/[^/]+/[^/]+\.pdb$' })) {
            $map = Get-SourceLink $pkg.Path $pdb
            if (-not $map) { $failed.Add("$name $pdb has no Source Link map (not a portable PDB, or no Source Link)") }
            elseif ($map -notmatch "/$([regex]::Escape($Commit))/") { $failed.Add("$name $pdb maps its sources elsewhere than commit ${Commit}: $map") }
            elseif (-not $Quiet) { Write-Host "    source link ($pdb): $map" }
        }
    }

    # Exact dependencies: the listed ones at their values, and any on a package of ours as [v]
    $deps = @(Get-NuspecDependencies $pkg.Nuspec)
    $wanted = @{}
    foreach ($k in $p.Dependencies.Keys) { $wanted[$k] = Expand-Template $p.Dependencies[$k] $pinValues }
    foreach ($k in $p.DependenciesWhenPacked.Keys) { if ($packed.ContainsKey($k)) { $wanted[$k] = Expand-Template $p.DependenciesWhenPacked[$k] $pinValues } }
    foreach ($k in $wanted.Keys) {
        $found = @($deps | Where-Object Id -eq $k)
        if (-not $found) { $failed.Add("$name does not depend on $k"); continue }
        foreach ($f in $found) { if ($f.Version -ne $wanted[$k]) { $failed.Add("$name depends on $k $($f.Version)$(if ($f.Group) { " ($($f.Group))" }), expected $($wanted[$k])") } }
    }
    foreach ($d in $deps | Where-Object { $ourIds -contains $_.Id -and -not $wanted.ContainsKey($_.Id) }) {
        if ($d.Version -notmatch '^\[[^,\]]+\]$') { $failed.Add("$name depends on $($d.Id) $($d.Version): not an exact range") }
    }
    if ($deps.Count) { Write-Host "    dependencies: $(($deps | ForEach-Object { "$($_.Id) $($_.Version)" } | Sort-Object -Unique) -join ', ')" }

    # The values the packed props set (the restore guards compare against them)
    foreach ($file in $p.Props.Keys) {
        $text = $pkg.Texts[$file]
        if ($null -eq $text) { $failed.Add("$name has no $file to hold $(($p.Props[$file].Keys | Sort-Object) -join ', ')"); continue }
        foreach ($prop in $p.Props[$file].Keys) {
            $value = Expand-Template $p.Props[$file][$prop] $pinValues
            if ($text -notmatch "<$prop(\s[^>]*)?>$([regex]::Escape($value))</$prop>") { $failed.Add("$name $file does not set $prop to $value") }
        }
    }
    foreach ($file in $p.Contains.Keys) {
        $text = $pkg.Texts[$file]
        if ($null -eq $text) { continue } # its absence is a Files finding
        foreach ($needle in $p.Contains[$file]) {
            $value = Expand-Template $needle $pinValues
            if (-not $text.Contains($value)) { $failed.Add("$name $file does not contain '$value'") }
        }
    }

    # The description names exactly the platforms whose library is inside, generated at pack time
    $description = $pkg.Nuspec.package.metadata.description
    if ($p.Description -eq 'Native') {
        $expected = "Native library inside for: " + (($ridEntries | ForEach-Object { $_.Platform }) -join ", ") + "."
        Write-Host "    description: $description"
        if (-not "$description".TrimEnd().EndsWith($expected)) { $failed.Add("$name description does not end with '$expected': $description") }
    }
}
foreach ($x in @(Get-ChildItem $Folder -Filter *.nupkg | Where-Object { $_.Name -notin $expectedFiles })) { Write-Warning "unexpected package $($x.Name)" }

if ($failed.Count) {
    Write-Host "`npackage check failed ($($failed.Count)):"
    foreach ($f in $failed) { Write-Host "  $f" }
    if ($env:GITHUB_ACTIONS -eq "true") { foreach ($f in $failed) { Write-Host "::error title=Package check::$f" } }
    exit 1
}
Write-Host "`npackage check passed: $checked packages, platforms $($Rids -join ', ')"
