# Build a signed Android App Bundle for Play Console (Closed Testing / production).
# Device F5/deploy should use Configuration=Release (APK) or LocalRelease — not this script.
#
# Usage (from repo root or scripts):
#   .\scripts\publish-play-aab.ps1
#   .\scripts\publish-play-aab.ps1 -Quick   # --no-restore

param(
    [switch]$Quick
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $RepoRoot "musicmate.csproj"
$Tfm = "net10.0-android"
$OutDir = Join-Path $RepoRoot "Publish"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$dotnetArgs = @(
    "publish", $Project,
    "-f", $Tfm,
    "-c", "Release",
    "-p:AndroidPackageFormat=aab",
    "-o", $OutDir,
    "-v", "minimal"
)
if ($Quick) { $dotnetArgs += "--no-restore" }

Write-Host "==> Publishing Play AAB to $OutDir" -ForegroundColor Cyan
dotnet @dotnetArgs
exit $LASTEXITCODE
