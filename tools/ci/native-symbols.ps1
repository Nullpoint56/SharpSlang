<#
.SYNOPSIS
  The native symbols of a source-built payload: kept beside it after the build, then checked to
  belong to the staged libraries and zipped for the GitHub release.

.DESCRIPTION
  tools/build-native.ps1 configures Slang with SLANG_ENABLE_RELEASE_DEBUG_INFO and
  SLANG_ENABLE_SPLIT_DEBUG_INFO, so the link writes each library's debug information beside it in
  _build/<rid>/Release/ (slang/cmake/SlangTarget.cmake at v2026.21): a PDB on Windows
  (<target>.pdb in bin/), and on Linux and macOS <library file>.dwarf in lib/, the library stripped
  and, on Linux, given a .gnu_debuglink naming it. The payload stages the libraries only
  (AGENTS.md#native-packages); the symbols go on the release as zips (the template's rule).

  -Collect (after the build; the slang-payload action runs it): for each library staged in
  runtimes/<rid>/native/, the build-tree file with the same bytes and its debug file, copied to
  obj/native-symbols/<rid>/ with symbols.json naming the library, its SHA-256 and the debug file.
  The action caches that folder with the payload, so a cached payload keeps its symbols.

  -Zip <dir> -Tag <tag> (the release): checks every staged library against symbols.json (the same
  bytes) and against its debug file, then writes SharpSlang-symbols-<rid>-<tag>.zip:

    Windows   the CodeView record in the DLL (PDB name, GUID, age) equals the PDB's info stream
    Linux     the .gnu_debuglink in the library names the .dwarf file and carries its CRC32
    macOS     the LC_UUID of the dylib equals the LC_UUID of the .dwarf (dsymutil --flat output)

  -Native also zips runtimes/<rid>/native/ as SharpSlang-native-<rid>-<tag>.zip, for a platform no
  package carries (osx-arm64).

  The Windows check ran on a source build of the workstation (branch sharpslang-workflows): both
  libraries' CodeView GUID and age equal their PDBs'. The Linux and macOS checks have not run.

        pwsh tools/ci/native-symbols.ps1 -Rid win-x64 -Collect
        pwsh tools/ci/native-symbols.ps1 -Rid win-x64 -Zip dist -Tag v2026210.0.0-beta.1
#>
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-arm64')] [string]$Rid,
    [switch]$Collect,
    [string]$Zip,
    [string]$Tag,
    [switch]$Native,
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)

$ErrorActionPreference = 'Stop'
if (-not $Collect -and -not $Zip) { throw "say -Collect or -Zip <dir>" }
if ($Zip -and -not $Tag) { throw "-Zip needs -Tag" }

$windows = $Rid.StartsWith('win')
$ext = if ($windows) { '.dll' } elseif ($Rid.StartsWith('osx')) { '.dylib' } else { '.so' }
$nativeDir = Join-Path $Root "runtimes" $Rid "native"
$kept = Join-Path $Root "obj" "native-symbols" $Rid
$manifestPath = Join-Path $kept "symbols.json"

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public static class NativeSymbols
{
    // ---- PDB (MSF 7.00): the info stream (stream 1) starts with version, signature, age, GUID.
    public static string PdbIdentity(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        string magic = Encoding.ASCII.GetString(f, 0, 24);
        if (magic != "Microsoft C/C++ MSF 7.00") throw new InvalidDataException(path + " is not an MSF 7.00 PDB");
        int blockSize = BitConverter.ToInt32(f, 32);
        int directoryBytes = BitConverter.ToInt32(f, 44);
        int blockMapAddr = BitConverter.ToInt32(f, 52);
        int directoryBlocks = (directoryBytes + blockSize - 1) / blockSize;
        byte[] dir = new byte[directoryBlocks * blockSize];
        for (int i = 0; i < directoryBlocks; i++)
        {
            int block = BitConverter.ToInt32(f, blockMapAddr * blockSize + i * 4);
            Array.Copy(f, (long)block * blockSize, dir, i * blockSize, blockSize);
        }
        int streams = BitConverter.ToInt32(dir, 0);
        if (streams < 2) throw new InvalidDataException(path + " has no info stream");
        int size0 = BitConverter.ToInt32(dir, 4);
        int blocks0 = size0 <= 0 ? 0 : (size0 + blockSize - 1) / blockSize;
        int firstBlockOfStream1 = BitConverter.ToInt32(dir, 4 + streams * 4 + blocks0 * 4);
        long info = (long)firstBlockOfStream1 * blockSize;
        uint age = BitConverter.ToUInt32(f, (int)info + 8);
        var guid = new Guid(new ReadOnlySpan<byte>(f, (int)info + 12, 16));
        return guid.ToString("N").ToUpperInvariant() + "/" + age;
    }

    // ---- ELF: the .gnu_debuglink section, a NUL-terminated name padded to 4 bytes, then a CRC32.
    public static string DebugLink(string path, out uint crc)
    {
        byte[] f = File.ReadAllBytes(path);
        if (f[0] != 0x7F || f[1] != (byte)'E' || f[4] != 2) throw new InvalidDataException(path + " is not a 64-bit ELF file");
        long shoff = BitConverter.ToInt64(f, 0x28);
        int shentsize = BitConverter.ToUInt16(f, 0x3A), shnum = BitConverter.ToUInt16(f, 0x3C), shstrndx = BitConverter.ToUInt16(f, 0x3E);
        long strtab = BitConverter.ToInt64(f, (int)(shoff + shstrndx * shentsize + 24));
        for (int i = 0; i < shnum; i++)
        {
            int h = (int)(shoff + i * shentsize);
            int nameOff = BitConverter.ToInt32(f, h);
            int end = (int)strtab + nameOff; while (f[end] != 0) end++;
            string name = Encoding.ASCII.GetString(f, (int)strtab + nameOff, end - (int)strtab - nameOff);
            if (name != ".gnu_debuglink") continue;
            long offset = BitConverter.ToInt64(f, h + 24), size = BitConverter.ToInt64(f, h + 32);
            int z = (int)offset; while (f[z] != 0) z++;
            string file = Encoding.UTF8.GetString(f, (int)offset, z - (int)offset);
            int crcAt = (int)offset + ((file.Length + 1 + 3) & ~3);
            if (crcAt + 4 > offset + size) throw new InvalidDataException(path + ": malformed .gnu_debuglink");
            crc = BitConverter.ToUInt32(f, crcAt);
            return file;
        }
        throw new InvalidDataException(path + " has no .gnu_debuglink");
    }

    // The CRC .gnu_debuglink carries: CRC-32 (IEEE 802.3, reflected, 0xEDB88320), as gdb computes it.
    public static uint Crc32(string path)
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; }
        uint crc = 0xFFFFFFFFu;
        using var s = File.OpenRead(path);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) crc = table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    // ---- Mach-O (64-bit, thin): LC_UUID (0x1B).
    public static string MachUuid(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        if (BitConverter.ToUInt32(f, 0) != 0xFEEDFACFu) throw new InvalidDataException(path + " is not a thin 64-bit Mach-O file");
        int ncmds = BitConverter.ToInt32(f, 16);
        int at = 32;
        for (int i = 0; i < ncmds; i++)
        {
            uint cmd = BitConverter.ToUInt32(f, at), size = BitConverter.ToUInt32(f, at + 4);
            if (cmd == 0x1B) return BitConverter.ToString(f, at + 8, 16).Replace("-", "");
            at += (int)size;
        }
        throw new InvalidDataException(path + " has no LC_UUID");
    }
}
'@

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 $Path).Hash }

$libraries = @(Get-ChildItem $nativeDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -eq $ext } | Sort-Object Name)
if (-not $libraries) { throw "no staged library in $nativeDir" }

# ---------------------------------------------------------------- collect, after the build
if ($Collect) {
    $out = Join-Path $Root "_build" $Rid $Configuration $(if ($windows) { 'bin' } else { 'lib' })
    if (-not (Test-Path $out)) { throw "no build tree at $out" }
    if (Test-Path $kept) { Remove-Item $kept -Recurse -Force -Confirm:$false }
    New-Item -ItemType Directory -Force $kept | Out-Null
    # Not the plain-named symlinks of the Unix compiler: the debug file is named after the real file.
    $candidates = @(Get-ChildItem $out -File | Where-Object { -not $_.LinkType -and $_.Name -like "*$ext*" -and $_.Extension -notin '.pdb', '.dwarf', '.lib', '.exp' })
    $entries = foreach ($lib in $libraries) {
        $hash = Get-Sha256 $lib.FullName
        $built = $candidates | Where-Object { $_.Length -eq $lib.Length -and (Get-Sha256 $_.FullName) -eq $hash } | Select-Object -First 1
        if (-not $built) { throw "the staged $($lib.Name) matches no library in $out" }
        $debug = if ($windows) { Join-Path $out ([IO.Path]::GetFileNameWithoutExtension($built.Name) + '.pdb') } else { "$($built.FullName).dwarf" }
        if (-not (Test-Path $debug)) { throw "$($built.Name) has no debug file beside it ($debug)" }
        Copy-Item $debug $kept
        Write-Host ("    {0,-36} <- {1}" -f $lib.Name, (Split-Path $debug -Leaf))
        [ordered]@{ library = $lib.Name; sha256 = $hash; debug = (Split-Path $debug -Leaf) }
    }
    ConvertTo-Json -InputObject @($entries) | Set-Content $manifestPath -Encoding utf8
    Write-Host "symbols of $($libraries.Count) libraries kept in $kept"
}

# ---------------------------------------------------------------- check and zip, for the release
if ($Zip) {
    if (-not (Test-Path $manifestPath)) { throw "no ${manifestPath}: the payload was not built from source with its symbols kept" }
    $manifest = @(Get-Content $manifestPath -Raw | ConvertFrom-Json)
    foreach ($lib in $libraries) {
        $entry = $manifest | Where-Object library -eq $lib.Name
        if (-not $entry) { throw "symbols.json names no $($lib.Name)" }
        if ((Get-Sha256 $lib.FullName) -ne $entry.sha256) { throw "the staged $($lib.Name) is not the library whose symbols were kept" }
        $debug = Join-Path $kept $entry.debug
        if ($windows) {
            $pe = [System.Reflection.PortableExecutable.PEReader]::new([System.IO.File]::OpenRead($lib.FullName))
            try {
                $codeView = $pe.ReadDebugDirectory() | Where-Object Type -eq CodeView | Select-Object -First 1
                if (-not $codeView) { throw "$($lib.Name) has no CodeView record" }
                $cv = $pe.ReadCodeViewDebugDirectoryData($codeView)
            } finally { $pe.Dispose() }
            if ((Split-Path $cv.Path -Leaf) -ne $entry.debug) { throw "$($lib.Name) names $($cv.Path), not $($entry.debug)" }
            $want = "$($cv.Guid.ToString('N').ToUpperInvariant())/$($cv.Age)"
            $have = [NativeSymbols]::PdbIdentity($debug)
            if ($want -ne $have) { throw "$($lib.Name) wants PDB $want; $($entry.debug) is $have" }
            Write-Host "    $($lib.Name): CodeView $want = $($entry.debug)"
        }
        elseif ($Rid.StartsWith('linux')) {
            [uint32]$crc = 0
            $link = [NativeSymbols]::DebugLink($lib.FullName, [ref]$crc)
            if ($link -ne $entry.debug) { throw "$($lib.Name) links $link, not $($entry.debug)" }
            $actual = [NativeSymbols]::Crc32($debug)
            if ($actual -ne $crc) { throw ("$($lib.Name) links $link with CRC32 {0:X8}; the file's is {1:X8}" -f $crc, $actual) }
            Write-Host ("    {0}: .gnu_debuglink {1}, CRC32 {2:X8}" -f $lib.Name, $link, $crc)
        }
        else {
            $want = [NativeSymbols]::MachUuid($lib.FullName)
            $have = [NativeSymbols]::MachUuid($debug)
            if ($want -ne $have) { throw "$($lib.Name) has LC_UUID $want; $($entry.debug) has $have" }
            Write-Host "    $($lib.Name): LC_UUID $want = $($entry.debug)"
        }
    }

    New-Item -ItemType Directory -Force $Zip | Out-Null
    $symbols = Join-Path (Resolve-Path $Zip) "SharpSlang-symbols-$Rid-$Tag.zip"
    if (Test-Path $symbols) { Remove-Item $symbols -Force -Confirm:$false }
    $archive = [System.IO.Compression.ZipFile]::Open($symbols, 'Create')
    try {
        foreach ($entry in $manifest) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $kept $entry.debug), $entry.debug) | Out-Null }
    } finally { $archive.Dispose() }
    Write-Host "wrote $symbols"
    if ($Native) {
        $payload = Join-Path (Resolve-Path $Zip) "SharpSlang-native-$Rid-$Tag.zip"
        if (Test-Path $payload) { Remove-Item $payload -Force -Confirm:$false }
        # ZipFile writes '/' separators; the zip unpacks the same on every OS.
        [System.IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path $nativeDir).Path, $payload)
        Write-Host "wrote $payload"
    }
}
