# Packs a Play Console native-debug-symbols.zip from the signed Android
# package produced by this exact .NET MAUI Release build.
#
# Layout required by Play:
#   arm64-v8a/*.so
#   armeabi-v7a/*.so
#   x86_64/*.so
#
# R8 mapping.txt is Java/Kotlin deobfuscation and is intentionally excluded.
# This script does not change packaged libraries or signing.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputZip,

    [string]$AppSharedLibrariesDir = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Signed Android package not found: $PackagePath"
}

$packageFull = (Resolve-Path -LiteralPath $PackagePath).Path
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("mm-native-debug-symbols-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Get-AbiFromEntry([string]$entryName) {
    if ($entryName -match '(?:^|/)(?:base/)?lib/(arm64-v8a|armeabi-v7a|x86_64)/([^/]+\.so)$') {
        return [pscustomobject]@{
            Abi      = $Matches[1]
            FileName = $Matches[2]
        }
    }
    return $null
}

$copied = 0
$replaced = 0
$package = [System.IO.Compression.ZipFile]::OpenRead($packageFull)
try {
    foreach ($entry in $package.Entries) {
        if (-not $entry.FullName.EndsWith(".so", [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($entry.FullName.EndsWith(".so.manifest", [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $parsed = Get-AbiFromEntry $entry.FullName
        if ($null -eq $parsed) {
            continue
        }
        if ($parsed.FileName.EndsWith(".dbg.so", [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $destDir = Join-Path $stage $parsed.Abi
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        $dest = Join-Path $destDir $parsed.FileName

        $dbgCandidate = $null
        if ($parsed.FileName -eq "libxamarin-app.so" -and $AppSharedLibrariesDir) {
            $dbgPath = Join-Path $AppSharedLibrariesDir (Join-Path $parsed.Abi "libxamarin-app.dbg.so")
            if (Test-Path -LiteralPath $dbgPath) {
                $dbgCandidate = $dbgPath
            }
        }

        if ($dbgCandidate) {
            Copy-Item -LiteralPath $dbgCandidate -Destination $dest -Force
            $replaced++
            Write-Host "  $($parsed.Abi)/$($parsed.FileName) <- libxamarin-app.dbg.so"
        }
        else {
            $out = [System.IO.File]::Create($dest)
            try {
                $src = $entry.Open()
                try { $src.CopyTo($out) }
                finally { $src.Dispose() }
            }
            finally { $out.Dispose() }
        }
        $copied++
    }
}
finally {
    $package.Dispose()
}

if ($copied -eq 0) {
    Remove-Item -LiteralPath $stage -Recurse -Force
    throw "No native .so libraries found in $packageFull"
}

$outDir = Split-Path -Parent $OutputZip
if ($outDir) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
if (Test-Path -LiteralPath $OutputZip) {
    Remove-Item -LiteralPath $OutputZip -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage,
    $OutputZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

Remove-Item -LiteralPath $stage -Recurse -Force

$zipItem = Get-Item -LiteralPath $OutputZip
Write-Host "Wrote native-debug-symbols.zip ($copied .so, $replaced xamarin-app dbg replacements)"
Write-Host "  $(($zipItem.FullName))  $($zipItem.Length) bytes"
Write-Host "  source package: $packageFull"
Write-Host "  Play Console: upload this ZIP separately (AAB does not embed native debug symbols)."
