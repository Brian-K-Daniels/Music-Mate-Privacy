# Pull / inspect Music Mate note_attempts.db3 from the DEV Android app via adb.
#
# Targets the currently connected device (prefers wireless IPv4:port from
# `adb devices`). Does not hard-code USB serials or IP addresses.
#
# Usage (from repo root):
#   .\scripts\pull-note-attempts.ps1
#   .\scripts\pull-note-attempts.ps1 -QueryLatest
#   .\scripts\pull-note-attempts.ps1 -OutFile .\.tmp\note_attempts.db3
#   .\scripts\pull-note-attempts.ps1 -Serial 192.168.42.18:42353   # override only if needed
#
# Requires: adb on PATH, sqlite3 on PATH for -QueryLatest / -Sql.

param(
    [string]$Serial,
    [string]$Package = "com.bkdaniels.musicmate.dev",
    [string]$RemoteFile = "files/note_attempts.db3",
    [string]$OutFile,
    [switch]$QueryLatest,
    [string]$Sql
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "adb-device.ps1")

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $tmpDir = Join-Path $RepoRoot ".tmp"
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    $OutFile = Join-Path $tmpDir "note_attempts.db3"
}
else {
    $outParent = Split-Path -Parent $OutFile
    if ($outParent) {
        New-Item -ItemType Directory -Force -Path $outParent | Out-Null
    }
}

$device = Get-MusicMateAdbSerial -Serial $Serial
Write-Host "Using adb device: $device" -ForegroundColor Cyan
Write-Host "Pulling $Package/$RemoteFile -> $OutFile"

# cmd redirection keeps the SQLite binary intact (PowerShell pipeline can corrupt it).
$cmd = "adb -s `"$device`" exec-out run-as $Package cat $RemoteFile > `"$OutFile`""
cmd /c $cmd
if ($LASTEXITCODE -ne 0) {
    throw "adb pull via run-as failed (exit $LASTEXITCODE). Is the DEV app installed and debuggable on $device?"
}
if (-not (Test-Path $OutFile) -or (Get-Item $OutFile).Length -lt 100) {
    throw "Failed to write database file to $OutFile"
}

$len = (Get-Item $OutFile).Length
Write-Host "Wrote $len bytes." -ForegroundColor Green

$fs = [System.IO.File]::OpenRead($OutFile)
try {
    $buf = New-Object byte[] 16
    $null = $fs.Read($buf, 0, 16)
    $ascii = -join ($buf | ForEach-Object {
        if ($_ -ge 32 -and $_ -le 126) { [char]$_ } else { '.' }
    })
}
finally {
    $fs.Dispose()
}
if ($ascii -notlike "SQLite format 3*") {
    Write-Host "Warning: file does not look like SQLite (header='$ascii')." -ForegroundColor Yellow
}

function Invoke-Sqlite([string]$Database, [string]$Statement) {
    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if (-not $sqlite) {
        throw "sqlite3 not found on PATH (needed for -QueryLatest / -Sql)."
    }
    & sqlite3 -header -column $Database $Statement
    if ($LASTEXITCODE -ne 0) {
        throw "sqlite3 failed (exit $LASTEXITCODE)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($Sql)) {
    Invoke-Sqlite $OutFile $Sql
    exit 0
}

if ($QueryLatest) {
    Write-Host ""
    Write-Host "==> Sessions (newest first)" -ForegroundColor Cyan
    Invoke-Sqlite $OutFile @"
SELECT substr(SessionId,1,8) AS sess,
       COUNT(*) AS n,
       SUM(CASE WHEN OvrOk=0 THEN 1 ELSE 0 END) AS wrong,
       SUM(CASE WHEN WngRs IN ('hadEarlyCandidate','wasEarly') THEN 1 ELSE 0 END) AS hadEarly,
       MAX(Level) AS lvl
FROM NoteAttempts
GROUP BY SessionId
ORDER BY MAX(AttemptId) DESC
LIMIT 5;
"@

    $sess = & sqlite3 $OutFile "SELECT SessionId FROM NoteAttempts ORDER BY AttemptId DESC LIMIT 1;"
    if ([string]::IsNullOrWhiteSpace($sess)) {
        Write-Host "No rows in NoteAttempts." -ForegroundColor Yellow
        exit 0
    }

    Write-Host ""
    Write-Host "==> Latest session $sess" -ForegroundColor Cyan
    Invoke-Sqlite $OutFile @"
SELECT IFNULL(NULLIF(WngRs,''),'(ok)') AS reason, COUNT(*) AS n, SUM(OvrOk) AS ovrOk
FROM NoteAttempts
WHERE SessionId='$sess'
GROUP BY WngRs
ORDER BY n DESC;
"@

    Write-Host ""
    Invoke-Sqlite $OutFile @"
SELECT AttemptId, ExpNm, ActNm, PchOk, TmgOk, OvrOk,
       IFNULL(NULLIF(WngRs,''),'-') AS reason,
       printf('%.0f',TmgErr) AS dMs,
       PitchErrorCents AS cents,
       printf('%.0f',ExpMs) AS expMs,
       printf('%.0f',ActMs) AS actMs
FROM NoteAttempts
WHERE SessionId='$sess'
ORDER BY COALESCE(ExpMs, ActMs, AttemptId), AttemptId;
"@
}

Write-Host ""
Write-Host "Done. Database: $OutFile" -ForegroundColor Green
