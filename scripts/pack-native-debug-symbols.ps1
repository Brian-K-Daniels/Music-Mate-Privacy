# Packs a Play Console native-debug-symbols.zip for this exact .NET MAUI
# Release build.
#
# Play accepts only these three entries (ABI folder at the ZIP root):
#   arm64-v8a/libxamarin-app.so
#   armeabi-v7a/libxamarin-app.so
#   x86_64/libxamarin-app.so
#
# Each file is this build's unstripped libxamarin-app.dbg.so, renamed
# inside the ZIP. AOT libaot-*.dll.so images, assembly stores, manifests,
# stripped runtime libraries, libarc.bin.so, and R8 mapping.txt are excluded.
# The signed package is required so the ZIP is tied to that same build.
# This script does not change packaged libraries or signing.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputZip,

    [Parameter(Mandatory = $true)]
    [string]$AppSharedLibrariesDir
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Signed Android package not found: $PackagePath"
}
if ([string]::IsNullOrWhiteSpace($AppSharedLibrariesDir) -or -not (Test-Path -LiteralPath $AppSharedLibrariesDir)) {
    throw "App shared libraries directory not found: $AppSharedLibrariesDir"
}

$packageFull = (Resolve-Path -LiteralPath $PackagePath).Path
$sharedFull = (Resolve-Path -LiteralPath $AppSharedLibrariesDir).Path
$abis = @('arm64-v8a', 'armeabi-v7a', 'x86_64')

$packageAbis = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$package = [System.IO.Compression.ZipFile]::OpenRead($packageFull)
try {
    foreach ($entry in $package.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name -match '(?:^|/)(?:base/)?lib/(arm64-v8a|armeabi-v7a|x86_64)/libxamarin-app\.so$') {
            [void]$packageAbis.Add($Matches[1])
        }
    }
}
finally {
    $package.Dispose()
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("mm-native-debug-symbols-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    foreach ($abi in $abis) {
        if (-not $packageAbis.Contains($abi)) {
            $missingPackage = "Signed package is missing lib/$abi/libxamarin-app.so: $packageFull"
            Write-Host $missingPackage
            throw $missingPackage
        }

        $dbg = Join-Path $sharedFull (Join-Path $abi "libxamarin-app.dbg.so")
        if (-not (Test-Path -LiteralPath $dbg)) {
            $missingDbg = "Unstripped libxamarin-app.dbg.so not found for ${abi}: $dbg"
            Write-Host $missingDbg
            throw $missingDbg
        }

        $destDir = Join-Path $stage $abi
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        Copy-Item -LiteralPath $dbg -Destination (Join-Path $destDir "libxamarin-app.so") -Force
        Write-Host "  $abi/libxamarin-app.so <- libxamarin-app.dbg.so"
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
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

$zipItem = Get-Item -LiteralPath $OutputZip
Write-Host "Wrote native-debug-symbols.zip (3 libxamarin-app.so from this build's dbg.so)"
Write-Host "  $($zipItem.FullName)  $($zipItem.Length) bytes"
Write-Host "  source package: $packageFull"
Write-Host "  symbols dir: $sharedFull"
Write-Host "  Play Console: upload this ZIP separately (AAB does not embed native debug symbols)."
