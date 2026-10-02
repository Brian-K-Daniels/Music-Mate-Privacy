# Recover from flaky MAUI Android debug attach (MonoVsDbg / exit code 0 / port 8890).
#
# Typical symptoms:
#   - "Start debugging Android application failed"
#   - "New MonoVsDbg debugger process created" (sometimes twice)
#   - musicmate.dll exited with code 0 before the app is usable under F5
#   - XAAADB0000 DELETE_FAILED_INTERNAL_ERROR (Release AAB/split deploy while app running,
#     or replacing an AAB split install). Stop the app, then:
#       adb uninstall com.bkdaniels.musicmate
#     Prefer LocalRelease (APK) for device testing; Play AAB via
#     .\scripts\publish-play-aab.ps1 or Archive Release.
#
# This script does NOT fix app logic bugs. It resets adb, clears stale debug state,
# optionally removes the DEV app, and optionally rebuilds the Android TFM.
#
# Usage (from repo root or scripts folder):
#   .\scripts\recover-android-debug.ps1
#   .\scripts\recover-android-debug.ps1 -Rebuild          # also dotnet build Android
#   .\scripts\recover-android-debug.ps1 -KeepApp          # skip uninstall
#   .\scripts\recover-android-debug.ps1 -KeepApp -Rebuild # lightest rebuild path
#
# Stop Visual Studio debugging (Shift+F5) before running if a build step is used.

param(
    [switch]$KeepApp,
    [switch]$SkipClean,
    [switch]$Rebuild,
    [switch]$QuickRebuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $RepoRoot "musicmate.csproj"
$Tfm = "net10.0-android"
$DevPackage = "com.bkdaniels.musicmate.dev"
$DebugPort = 8890

. (Join-Path $PSScriptRoot "adb-device.ps1")

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Stop-StaleDebugProcesses {
    Write-Step "Stopping stale MonoVsDbg processes"
    Get-Process -Name "MonoVsDbg" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  Stopping MonoVsDbg (PID $($_.Id))"
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 1
}

function Reset-Adb {
    Write-Step "Resetting adb"
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $adb) {
        Write-Host "  adb not found on PATH. Install Android SDK platform-tools or use VS Developer PowerShell." -ForegroundColor Yellow
        return $false
    }

    & adb kill-server 2>$null
    Start-Sleep -Milliseconds 500
    & adb start-server
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  adb start-server failed (exit $LASTEXITCODE)." -ForegroundColor Yellow
        return $false
    }

    Write-Host "  Connected devices:"
    & adb devices -l
    Write-Host "  Note: kill-server drops wireless connections; re-run adb connect host:port if needed." -ForegroundColor Yellow
    return $true
}

function Clear-DebugPortForwards([string]$DeviceSerial) {
    Write-Step "Clearing adb port forwards (including debug port $DebugPort)"
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { return }

    if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
        & adb forward --remove-all 2>$null
    }
    else {
        & adb -s $DeviceSerial forward --remove-all 2>$null
    }
    # Harmless if no device; VS will recreate forwards on next F5.
}

function Uninstall-DevApp([string]$DeviceSerial) {
    if ($KeepApp) {
        Write-Step "Skipping DEV app uninstall (-KeepApp)"
        return
    }

    Write-Step "Uninstalling DEV app ($DevPackage)"
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { return }
    if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
        Write-Host "  No device serial resolved; skip uninstall." -ForegroundColor Yellow
        return
    }

    $installed = & adb -s $DeviceSerial shell pm list packages $DevPackage 2>$null
    if ($installed -match [regex]::Escape($DevPackage)) {
        & adb -s $DeviceSerial uninstall $DevPackage
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Uninstalled on $DeviceSerial."
        } else {
            Write-Host "  uninstall returned exit $LASTEXITCODE (device may be offline)." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Not installed on $DeviceSerial (OK)."
    }
}

function Clear-AndroidDebugArtifacts {
    if ($SkipClean) {
        Write-Step "Skipping obj/bin clean (-SkipClean)"
        return
    }

    Write-Step "Removing Android Debug build artifacts"
    $paths = @(
        (Join-Path $RepoRoot "obj\Debug\net10.0-android"),
        (Join-Path $RepoRoot "bin\Debug\net10.0-android")
    )

    foreach ($path in $paths) {
        if (Test-Path $path) {
            Write-Host "  Removing $path"
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-AndroidBuild {
    Write-Step "Building $Tfm (stop VS debug first if DLL lock errors appear)"
    $args = @("build", $Project, "-f", $Tfm, "-v", "minimal")
    if ($QuickRebuild) { $args += "--no-restore" }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Music Mate — Android debug recovery" -ForegroundColor Green
Write-Host "Repo: $RepoRoot"

Stop-StaleDebugProcesses
$adbOk = Reset-Adb

$deviceSerial = $null
if ($adbOk) {
    try {
        $deviceSerial = Get-MusicMateAdbSerial
        Write-Host "Using adb device: $deviceSerial" -ForegroundColor Cyan
    }
    catch {
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  Continuing without a targeted device serial." -ForegroundColor Yellow
    }
}

Clear-DebugPortForwards -DeviceSerial $deviceSerial
if ($adbOk) { Uninstall-DevApp -DeviceSerial $deviceSerial }
Clear-AndroidDebugArtifacts

if ($Rebuild) {
    Invoke-AndroidBuild
}

Write-Host ""
Write-Host "Recovery complete." -ForegroundColor Green
Write-Host "Next steps:"
Write-Host "  1. Open Visual Studio (or leave it open if already open)."
Write-Host "  2. Confirm one Android device/emulator is selected (wireless OK)."
Write-Host "  3. Optional sanity check: Ctrl+F5 (Run Without Debugging)."
Write-Host "  4. Then F5 once — do not double-click Start."
if (-not $Rebuild) {
    Write-Host ""
    Write-Host "Tip: if F5 still fails, rerun with -Rebuild (or -Rebuild -QuickRebuild)." -ForegroundColor Yellow
}
