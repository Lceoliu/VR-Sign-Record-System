[CmdletBinding()]
param(
    [string]$DeviceSerial = "",
    [string]$Destination = "",
    [switch]$AllSessions,
    [switch]$DeleteRemoteAfterPull,
    [switch]$KeepRemote,
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"
$deleteAfterPull = -not $KeepRemote
if ($DeleteRemoteAfterPull) {
    $deleteAfterPull = $true
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$unityVersionFile = Join-Path $repositoryRoot `
    "signvr_unity\ProjectSettings\ProjectVersion.txt"
$unityVersion = (
    Select-String -Path $unityVersionFile -Pattern '^m_EditorVersion: (.+)$'
).Matches[0].Groups[1].Value.Trim()
$adb = Join-Path ${env:ProgramFiles} `
    "Unity\Hub\Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"

if (-not (Test-Path -LiteralPath $adb)) {
    throw "Unity Android SDK adb was not found: $adb"
}

$devices = @(
    & $adb devices |
        Select-Object -Skip 1 |
        ForEach-Object {
            if ($_ -match '^(\S+)\s+device$') { $Matches[1] }
        }
)
if ($DeviceSerial) {
    if ($devices -notcontains $DeviceSerial) {
        throw "Requested Quest is not connected: $DeviceSerial"
    }
} elseif ($devices.Count -eq 1) {
    $DeviceSerial = $devices[0]
} elseif ($devices.Count -eq 0) {
    throw "No authorized Quest device is connected."
} else {
    throw "Multiple Quest devices are connected; pass -DeviceSerial."
}

if (-not $Destination) {
    $Destination = Join-Path $repositoryRoot `
        "Recordings\FirstPersonVideos"
}
$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
$destinationPrefix = $resolvedDestination.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Path $resolvedDestination -Force | Out-Null

$remoteRoot = "/sdcard/Android/data/com.signvr.interaction/files/FirstPersonVideos"
$remoteFiles = @(
    & $adb -s $DeviceSerial shell find $remoteRoot -type f |
        ForEach-Object { $_.Trim() } |
        Where-Object {
            $_ -and
            $_.StartsWith("$remoteRoot/") -and
            $_ -match '\.(mp4|avi)$'
        }
)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to list Quest recordings (adb exit $LASTEXITCODE)."
}
if ($remoteFiles.Count -eq 0) {
    Write-Host "No first-person videos were found on $DeviceSerial."
    exit 0
}

if (-not $AllSessions) {
    $newestRemoteFile = $null
    [long]$newestUnixTime = [long]::MinValue
    foreach ($remoteFile in $remoteFiles) {
        $unixTimeText = (& $adb -s $DeviceSerial shell `
            stat -c '%Y' $remoteFile).Trim()
        [long]$unixTime = 0
        if ($LASTEXITCODE -ne 0 -or
            -not [long]::TryParse($unixTimeText, [ref]$unixTime)) {
            throw "Unable to read the timestamp of $remoteFile."
        }
        if ($unixTime -gt $newestUnixTime) {
            $newestUnixTime = $unixTime
            $newestRemoteFile = $remoteFile
        }
    }
    $latestSession = $newestRemoteFile.Substring(
        $remoteRoot.Length + 1
    ).Split('/')[0]
    $remoteFiles = @(
        $remoteFiles | Where-Object {
            $_.StartsWith(
                "$remoteRoot/$latestSession/",
                [System.StringComparison]::Ordinal
            )
        }
    )
}

$copied = 0
$skipped = 0
$deleted = 0
$remoteSessions = [System.Collections.Generic.HashSet[string]]::new()

function Get-RemoteSha256([string]$remotePath) {
    $hashOutput = (& $adb -s $DeviceSerial shell sha256sum -- $remotePath)
    if ($LASTEXITCODE -ne 0 -or
        $hashOutput -notmatch '^([0-9a-fA-F]{64})\s') {
        throw "Unable to hash Quest recording: $remotePath"
    }
    return $Matches[1].ToLowerInvariant()
}

function Remove-RemoteFileAfterVerification([string]$remotePath) {
    & $adb -s $DeviceSerial shell rm -f -- $remotePath
    if ($LASTEXITCODE -ne 0) {
        throw "Copied but could not delete remote file: $remotePath"
    }
    & $adb -s $DeviceSerial shell test -e $remotePath
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -eq 0) {
        throw "Copied but Quest still reports the remote file: $remotePath"
    }
    $script:deleted++
}

foreach ($remoteFile in ($remoteFiles | Sort-Object)) {
    $relativePath = $remoteFile.Substring($remoteRoot.Length + 1)
    $sessionName = $relativePath.Split('/')[0]
    [void]$remoteSessions.Add($sessionName)
    $localRelativePath = $relativePath.Replace(
        '/',
        [System.IO.Path]::DirectorySeparatorChar
    )
    $localPath = [System.IO.Path]::GetFullPath(
        (Join-Path $resolvedDestination $localRelativePath)
    )
    if (-not $localPath.StartsWith(
        $destinationPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing a recording path outside the destination: $localPath"
    }

    $remoteSizeText = (& $adb -s $DeviceSerial shell `
        stat -c '%s' $remoteFile).Trim()
    [long]$remoteSize = 0
    if ($LASTEXITCODE -ne 0 -or
        -not [long]::TryParse($remoteSizeText, [ref]$remoteSize)) {
        throw "Unable to read the size of $remoteFile."
    }

    if ($ListOnly) {
        Write-Host "$remoteSize`t$relativePath"
        continue
    }

    if ((Test-Path -LiteralPath $localPath) -and
        (Get-Item -LiteralPath $localPath).Length -eq $remoteSize) {
        if ($deleteAfterPull) {
            $localHash = (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $remoteHash = Get-RemoteSha256 $remoteFile
            if ($localHash -ne $remoteHash) {
                Write-Host "Local file has the same size but a different hash; recopying $relativePath"
            } else {
                $skipped++
                Remove-RemoteFileAfterVerification $remoteFile
                continue
            }
        } else {
            $skipped++
            continue
        }
    }

    $localParent = Split-Path -Parent $localPath
    New-Item -ItemType Directory -Path $localParent -Force | Out-Null
    $partialPath = "$localPath.partial"
    if (Test-Path -LiteralPath $partialPath) {
        Remove-Item -LiteralPath $partialPath -Force
    }

    & $adb -s $DeviceSerial pull $remoteFile $partialPath
    if ($LASTEXITCODE -ne 0) {
        throw "adb pull failed with exit code $LASTEXITCODE."
    }

    $remoteSizeAfterText = (& $adb -s $DeviceSerial shell `
        stat -c '%s' $remoteFile).Trim()
    [long]$remoteSizeAfter = 0
    if (-not [long]::TryParse(
            $remoteSizeAfterText,
            [ref]$remoteSizeAfter
        ) -or
        $remoteSizeAfter -ne $remoteSize -or
        (Get-Item -LiteralPath $partialPath).Length -ne $remoteSize) {
        throw "Recording changed while being copied; retry later: $remoteFile"
    }

    $localHash = (Get-FileHash -LiteralPath $partialPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $remoteHash = Get-RemoteSha256 $remoteFile
    if ($localHash -ne $remoteHash) {
        Remove-Item -LiteralPath $partialPath -Force
        throw "Recording hash mismatch after pull; retry later: $remoteFile"
    }

    Move-Item -LiteralPath $partialPath -Destination $localPath -Force
    $copied++

    if ($deleteAfterPull) {
        Remove-RemoteFileAfterVerification $remoteFile
    }
}

if ($deleteAfterPull -and -not $ListOnly) {
    foreach ($sessionName in $remoteSessions) {
        & $adb -s $DeviceSerial shell rmdir `
            "$remoteRoot/$sessionName" 2>$null
    }
}

if ($ListOnly) {
    Write-Host "Listed $($remoteFiles.Count) file(s); no files were copied."
} else {
    Write-Host (
        "First-person videos synchronized to: $resolvedDestination " +
        "(copied=$copied, skipped=$skipped, deleted=$deleted, " +
        "remoteCleanup=$deleteAfterPull)"
    )
}
