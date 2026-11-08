# Shared by the package scripts (check-packages.ps1, descs-version.ps1, release-body.ps1,
# api-changes.ps1, tools/consume/consume.ps1): reads tools/ci/packages.psd1, resolves its pins from
# Directory.Build.props, expands its placeholders, and opens .nupkg files.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-RepoRoot { Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }

# A map parameter given as a hashtable (from PowerShell) or as "key=value" strings (pwsh -File)
function ConvertTo-Map($Value) {
    if ($null -eq $Value) { return @{} }
    if ($Value -is [hashtable]) { return $Value }
    $map = @{}
    foreach ($item in @($Value | ForEach-Object { "$_" -split ',' })) {
        if (-not $item) { continue }
        $k, $v = $item -split '=', 2
        if ($null -eq $v) { throw "'$item' is not key=value" }
        $map[$k.Trim()] = $v.Trim()
    }
    $map
}

# The data file, with defaults filled in, so the scripts can read every field without a check.
function Import-PackageSpec([string]$Path) {
    if (-not $Path) { $Path = Join-Path $PSScriptRoot "packages.psd1" }
    $spec = Import-PowerShellDataFile -Path $Path
    foreach ($p in $spec.Packages) {
        foreach ($k in 'Files', 'PerRid', 'Forbidden', 'Sources') { if (-not $p.ContainsKey($k)) { $p[$k] = @() } }
        foreach ($k in 'Dependencies', 'DependenciesWhenPacked', 'Props', 'Contains') { if (-not $p.ContainsKey($k)) { $p[$k] = @{} } }
        if (-not $p.ContainsKey('Commit')) { $p.Commit = $true }
        if (-not $p.ContainsKey('Optional')) { $p.Optional = $false }
        if (-not $p.ContainsKey('Assemblies')) { $p.Assemblies = $true }
        if (-not $p.ContainsKey('Description')) { $p.Description = $null }
        if (-not $p.ContainsKey('SourcesFromProject')) { $p.SourcesFromProject = $false }
    }
    $spec
}

# The value of an MSBuild property in a file, or $null
function Get-MSBuildProperty([string]$File, [string]$Name) {
    if (-not (Test-Path $File)) { return $null }
    $text = Get-Content $File -Raw
    if ($text -match "<$([regex]::Escape($Name))(\s[^>]*)?>([^<]+)</$([regex]::Escape($Name))>") { return $Matches[2].Trim() }
    $null
}

# Every pin as token -> value: -Overrides first, then Directory.Build.props, then (for a package
# version pin) the <Version> of a package's project that uses it. Unresolved pins are left out.
function Get-Pins($Spec, [string]$Root, [hashtable]$Overrides = @{}) {
    $props = Join-Path $Root "Directory.Build.props"
    $pins = @{}
    foreach ($token in $Spec.Pins.Keys) {
        if ($Overrides.ContainsKey($token) -and $Overrides[$token]) { $pins[$token] = $Overrides[$token]; continue }
        $value = Get-MSBuildProperty $props $Spec.Pins[$token]
        if (-not $value) {
            foreach ($p in @($Spec.Packages | Where-Object { $_.Version -eq $token })) {
                $value = Get-MSBuildProperty (Join-Path $Root $p.Project) 'Version'
                if ($value) { break }
            }
        }
        if ($value) { $pins[$token] = $value }
    }
    foreach ($k in $Overrides.Keys) { if (-not $pins.ContainsKey($k) -and $Overrides[$k]) { $pins[$k] = $Overrides[$k] } }
    $pins
}

# "{Version}" and friends; {rid} {lib} {ext} {platform} from a Rids entry. An unknown pin throws,
# naming the property that would hold it.
function Expand-Template([string]$Text, [hashtable]$Pins, $Rid = $null) {
    [regex]::Replace($Text, '\{(\w+)\}', {
        param($m)
        $name = $m.Groups[1].Value
        if ($Rid) {
            switch ($name) {
                'rid' { return $Rid.Rid }
                'lib' { return $Rid.Lib }
                'ext' { return $Rid.Ext }
                'platform' { return $Rid.Platform }
            }
        }
        if ($Pins.ContainsKey($name)) { return $Pins[$name] }
        throw "no value for {$name} in '$Text': set it in Directory.Build.props or pass it with -Pins @{ $name = '...' }"
    })
}

function Get-PackageVersion($Package, [hashtable]$Pins) {
    if (-not $Pins.ContainsKey($Package.Version)) { throw "no version for $($Package.Id): pin {$($Package.Version)} is not set" }
    $Pins[$Package.Version]
}

# A .nupkg's entries, nuspec (text and XML), and a reader for one entry's text
function Read-Nupkg([string]$Path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path).Path)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName })
        $texts = @{}
        foreach ($e in $zip.Entries) {
            if ($e.FullName -match '\.(nuspec|props|targets|json|md|txt|xml)$' -and $e.Length -lt 4MB) {
                $reader = [System.IO.StreamReader]::new($e.Open())
                try { $texts[$e.FullName] = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
        }
        $sizes = @{}; foreach ($e in $zip.Entries) { $sizes[$e.FullName] = $e.Length }
    } finally { $zip.Dispose() }
    $nuspecName = @($entries | Where-Object { $_ -like '*.nuspec' -and $_ -notlike '*/*' })[0]
    $nuspecText = $texts[$nuspecName]
    [xml]$nuspec = $nuspecText
    [pscustomobject]@{
        Path = $Path; Entries = $entries; Sizes = $sizes; Texts = $texts
        NuspecText = $nuspecText; Nuspec = $nuspec
        Id = $nuspec.package.metadata.id; Version = $nuspec.package.metadata.version
    }
}

# Every dependency of a nuspec as @{ Id; Version; Group }, across its groups
function Get-NuspecDependencies($Nuspec) {
    $deps = $Nuspec.package.metadata.SelectNodes("//*[local-name()='dependency']")
    foreach ($d in $deps) {
        $group = if ($d.ParentNode.LocalName -eq 'group') { $d.ParentNode.GetAttribute('targetFramework') } else { '' }
        [pscustomobject]@{ Id = $d.GetAttribute('id'); Version = $d.GetAttribute('version'); Group = $group }
    }
}

# The .nupkg files of a folder by id (newest version when several)
function Get-PackedPackages([string]$Folder) {
    $found = @{}
    foreach ($f in Get-ChildItem $Folder -Filter *.nupkg | Where-Object { $_.Name -notlike '*.snupkg' }) {
        $p = Read-Nupkg $f.FullName
        if (-not $found.ContainsKey($p.Id)) { $found[$p.Id] = @() }
        $found[$p.Id] += $p
    }
    $found
}

Export-ModuleMember -Function Get-RepoRoot, ConvertTo-Map,Import-PackageSpec, Get-MSBuildProperty, Get-Pins, Expand-Template, Get-PackageVersion, Read-Nupkg, Get-NuspecDependencies, Get-PackedPackages
