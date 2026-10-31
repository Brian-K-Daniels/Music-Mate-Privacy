# Android-only build for Music Mate (agent / CLI use).
#
# Pitfalls that cause long hangs or failures:
# - Wrong TFM: use net10.0-android (not net9).
# - Omitting -f builds/restores ALL TargetFrameworks (incl. Windows App SDK) and can hang on NuGet.
# - MAUI Android packaging can take 5–15 minutes on a cold build.
# - CLI build fails with XALNS7024 if Visual Studio is deploying (DLL locked) — stop the VS debug session first.
#
# Usage:
#   .\scripts\build-android.ps1           # restore (if needed) + build Android TFM only
#   .\scripts\build-android.ps1 -Quick    # skip restore (~20s when packages are already restored)

param(
    [switch]$Quick
)

$ErrorActionPreference = "Stop"
$Project = Join-Path $PSScriptRoot "..\musicmate.csproj"
$Tfm = "net10.0-android"

$args = @("build", $Project, "-f", $Tfm, "-v", "minimal")
if ($Quick) { $args += "--no-restore" }

dotnet @args
exit $LASTEXITCODE
