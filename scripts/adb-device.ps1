# Shared adb device selection for Music Mate scripts (wireless-friendly).
#
# Prefer a single device in state "device". If several are connected, prefer a
# wireless serial matching IPv4:port (e.g. 192.168.42.18:42353) over USB ids.
# Never hard-code a USB serial or IP — pass -Serial only as an explicit override.
#
# Dot-source from other scripts:
#   . (Join-Path $PSScriptRoot "adb-device.ps1")
#   $serial = Get-MusicMateAdbSerial

function Get-MusicMateAdbSerial {
    [CmdletBinding()]
    param(
        [string]$Serial
    )

    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $adb) {
        throw "adb not found on PATH. Install Android SDK platform-tools."
    }

    if (-not [string]::IsNullOrWhiteSpace($Serial)) {
        return $Serial.Trim()
    }

    $raw = & adb devices 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "adb devices failed: $raw"
    }

    $ready = @()
    foreach ($line in $raw) {
        $text = "$line".Trim()
        if ($text.Length -eq 0 -or $text -like "List of devices*") {
            continue
        }
        # serial<TAB>state  (state: device | offline | unauthorized | ...)
        if ($text -match '^(\S+)\s+(\S+)') {
            $ready += [pscustomobject]@{
                Serial = $Matches[1]
                State  = $Matches[2]
            }
        }
    }

    $online = @($ready | Where-Object { $_.State -eq "device" })
    if ($online.Count -eq 0) {
        $listed = ($ready | ForEach-Object { "  $($_.Serial)  $($_.State)" }) -join "`n"
        if ([string]::IsNullOrWhiteSpace($listed)) {
            $listed = "  (none)"
        }
        throw @"
No adb device in 'device' state.
adb devices:
$listed

For wireless debugging: ensure the phone is connected (adb connect host:port), then retry.
"@
    }

    $wirelessPattern = '^\d{1,3}(\.\d{1,3}){3}:\d+$'
    $wireless = @($online | Where-Object { $_.Serial -match $wirelessPattern })

    if ($wireless.Count -eq 1) {
        return $wireless[0].Serial
    }
    if ($wireless.Count -gt 1) {
        $list = ($wireless | ForEach-Object { "  $($_.Serial)" }) -join "`n"
        throw @"
Multiple wireless adb devices are online. Disconnect extras or pass -Serial.
$list
"@
    }

    if ($online.Count -eq 1) {
        return $online[0].Serial
    }

    $list = ($online | ForEach-Object { "  $($_.Serial)  $($_.State)" }) -join "`n"
    throw @"
Multiple adb devices are online and none look like wireless IP:port.
Disconnect extras, use wireless only, or pass -Serial.
$list
"@
}

function Invoke-MusicMateAdb {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$AdbArgs
    )

    & adb -s $Serial @AdbArgs
    return $LASTEXITCODE
}
