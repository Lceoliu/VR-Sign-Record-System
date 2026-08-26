[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Apply', 'Status', 'Watch')]
    [string]$Mode = 'Status',

    [string]$DeviceId,

    [string]$AdbPath,

    [ValidateRange(10, 3600)]
    [int]$PollSeconds = 30,

    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$taskName = 'SignVR Quest Always On'
$stateRoot = Join-Path $env:LOCALAPPDATA 'SignVR'
$statePath = Join-Path $stateRoot 'quest-always-on.json'
$logPath = Join-Path $stateRoot 'quest-always-on.log'
$script:ResolvedAdbPath = $null
$script:ResolvedDeviceId = $null

function Write-OperatorMessage {
    param([string]$Message)

    if (-not $Quiet) {
        Write-Host $Message
    }
}

function Write-WatcherLog {
    param([string]$Message)

    New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    $timestamp = [DateTimeOffset]::UtcNow.ToString('o')
    Add-Content -LiteralPath $logPath -Value "$timestamp $Message" -Encoding UTF8
}

function Get-SavedState {
    if (-not (Test-Path -LiteralPath $statePath)) {
        return $null
    }

    return Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 |
        ConvertFrom-Json
}

function Resolve-AdbPath {
    param([object]$SavedState)

    $candidates = [Collections.Generic.List[string]]::new()
    if ($AdbPath) {
        $candidates.Add($AdbPath)
    }
    if ($null -ne $SavedState -and $SavedState.adb_path) {
        $candidates.Add([string]$SavedState.adb_path)
    }
    if ($env:ANDROID_SDK_ROOT) {
        $candidates.Add((Join-Path $env:ANDROID_SDK_ROOT 'platform-tools\adb.exe'))
    }
    if ($env:ANDROID_HOME) {
        $candidates.Add((Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'))
    }
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'))

    $unityEditors = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path -LiteralPath $unityEditors) {
        Get-ChildItem -LiteralPath $unityEditors -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                $candidates.Add((Join-Path $_.FullName 'Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'))
            }
    }

    $adbCommand = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($adbCommand) {
        $candidates.Add($adbCommand.Source)
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw 'adb.exe was not found. Pass -AdbPath with an Android platform-tools adb.exe.'
}

function Invoke-Adb {
    param(
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = @(& $script:ResolvedAdbPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "adb failed with exit code $exitCode`: $text"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $text.Trim()
    }
}

function Test-DeviceOnline {
    $result = Invoke-Adb -Arguments @(
        '-s', $script:ResolvedDeviceId, 'get-state'
    ) -AllowFailure
    return $result.ExitCode -eq 0 -and $result.Text -eq 'device'
}

function Resolve-DeviceId {
    param([object]$SavedState)

    $requested = $DeviceId
    if (-not $requested -and $null -ne $SavedState -and $SavedState.device_id) {
        $requested = [string]$SavedState.device_id
    }
    if ($requested) {
        return $requested.Trim()
    }

    $devicesResult = Invoke-Adb -Arguments @('devices')
    $onlineDevices = @(
        $devicesResult.Text -split "`r?`n" |
            ForEach-Object {
                if ($_ -match '^\s*(\S+)\s+device(?:\s|$)') {
                    $Matches[1]
                }
            }
    )
    if ($onlineDevices.Count -ne 1) {
        throw "Expected exactly one online ADB device; found $($onlineDevices.Count). Pass -DeviceId explicitly."
    }

    return $onlineDevices[0]
}

function Get-AndroidSetting {
    param(
        [string]$Namespace,
        [string]$Key
    )

    return (Invoke-Adb -Arguments @(
        '-s', $script:ResolvedDeviceId,
        'shell', 'settings', 'get', $Namespace, $Key
    )).Text
}

function Set-AndroidSetting {
    param(
        [string]$Namespace,
        [string]$Key,
        [string]$Value
    )

    Invoke-Adb -Arguments @(
        '-s', $script:ResolvedDeviceId,
        'shell', 'settings', 'put', $Namespace, $Key, $Value
    ) | Out-Null
}

function Restore-AndroidSetting {
    param(
        [string]$Namespace,
        [string]$Key,
        [string]$Value
    )

    if (-not $Value -or $Value -eq 'null') {
        Invoke-Adb -Arguments @(
            '-s', $script:ResolvedDeviceId,
            'shell', 'settings', 'delete', $Namespace, $Key
        ) | Out-Null
        return
    }

    Set-AndroidSetting -Namespace $Namespace -Key $Key -Value $Value
}

function Get-QuestPowerState {
    $dump = (Invoke-Adb -Arguments @(
        '-s', $script:ResolvedDeviceId,
        'shell', 'dumpsys', 'vrpowermanager'
    )).Text
    $virtualMatch = [regex]::Match(
        $dump,
        '(?m)^Virtual proximity state:\s*(\S+)\s*$'
    )
    $powerMatch = [regex]::Match($dump, '(?m)^State:\s*(\S+)\s*$')

    return [pscustomobject]@{
        VirtualProximity = if ($virtualMatch.Success) {
            $virtualMatch.Groups[1].Value
        } else {
            'UNKNOWN'
        }
        PowerState = if ($powerMatch.Success) {
            $powerMatch.Groups[1].Value
        } else {
            'UNKNOWN'
        }
        StayOnWhilePluggedIn = Get-AndroidSetting `
            -Namespace 'global' `
            -Key 'stay_on_while_plugged_in'
    }
}

function Set-QuestAlwaysOn {
    param([switch]$WatcherInvocation)

    if (-not (Test-DeviceOnline)) {
        if ($WatcherInvocation) {
            return $false
        }
        throw "Quest $($script:ResolvedDeviceId) is not online over ADB."
    }

    $before = Get-QuestPowerState
    $changed = $false
    if ($before.StayOnWhilePluggedIn -ne '7') {
        Set-AndroidSetting `
            -Namespace 'global' `
            -Key 'stay_on_while_plugged_in' `
            -Value '7'
        $changed = $true
    }

    if ($before.VirtualProximity -ne 'CLOSE') {
        Invoke-Adb -Arguments @(
            '-s', $script:ResolvedDeviceId,
            'shell', 'am', 'broadcast',
            '-a', 'com.oculus.vrpowermanager.prox_close'
        ) | Out-Null
        $changed = $true
    }

    if ($before.PowerState -ne 'HEADSET_MOUNTED') {
        Invoke-Adb -Arguments @(
            '-s', $script:ResolvedDeviceId,
            'shell', 'input', 'keyevent', '224'
        ) | Out-Null
        $changed = $true
    }

    $after = Get-QuestPowerState
    if ($after.VirtualProximity -ne 'CLOSE') {
        throw "Quest rejected the proximity override (state: $($after.VirtualProximity))."
    }
    if ($after.StayOnWhilePluggedIn -ne '7') {
        throw "Quest rejected stay_on_while_plugged_in=7 (value: $($after.StayOnWhilePluggedIn))."
    }

    if ($changed) {
        Write-OperatorMessage (
            "Quest $($script:ResolvedDeviceId) always-on applied: " +
            "proximity=$($after.VirtualProximity), " +
            "power=$($after.PowerState), " +
            "plugged-in=$($after.StayOnWhilePluggedIn)."
        )
    }
    return $changed
}

function Restore-QuestPower {
    param([object]$SavedState)

    if (-not (Test-DeviceOnline)) {
        throw "Quest $($script:ResolvedDeviceId) must be connected before uninstalling the always-on task."
    }

    Invoke-Adb -Arguments @(
        '-s', $script:ResolvedDeviceId,
        'shell', 'am', 'broadcast',
        '-a', 'com.oculus.vrpowermanager.automation_disable'
    ) | Out-Null
    Restore-AndroidSetting `
        -Namespace 'global' `
        -Key 'stay_on_while_plugged_in' `
        -Value ([string]$SavedState.original_stay_on_while_plugged_in)

    $restored = Get-QuestPowerState
    if ($restored.VirtualProximity -eq 'CLOSE') {
        throw 'Quest proximity override remained active after the restore command.'
    }
    Write-OperatorMessage (
        "Quest $($script:ResolvedDeviceId) restored: " +
        "proximity=$($restored.VirtualProximity), " +
        "plugged-in=$($restored.StayOnWhilePluggedIn)."
    )
}

function Save-InstallState {
    param([object]$ExistingState)

    if ($null -ne $ExistingState) {
        if ([string]$ExistingState.device_id -ne $script:ResolvedDeviceId) {
            throw "An always-on state already exists for device $($ExistingState.device_id). Uninstall it before changing devices."
        }
        return $ExistingState
    }

    New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    $saved = [ordered]@{
        schema_version = 1
        device_id = $script:ResolvedDeviceId
        adb_path = $script:ResolvedAdbPath
        original_stay_on_while_plugged_in = Get-AndroidSetting `
            -Namespace 'global' `
            -Key 'stay_on_while_plugged_in'
        installed_utc = [DateTimeOffset]::UtcNow.ToString('o')
        task_name = $taskName
    }
    $saved | ConvertTo-Json | Set-Content `
        -LiteralPath $statePath `
        -Encoding UTF8
    return [pscustomobject]$saved
}

function Register-AlwaysOnTask {
    $scriptPath = [IO.Path]::GetFullPath($PSCommandPath)
    $windowsPowerShell = Join-Path $env:SystemRoot `
        'System32\WindowsPowerShell\v1.0\powershell.exe'
    $actionArguments = @(
        '-NoLogo'
        '-NoProfile'
        '-NonInteractive'
        '-WindowStyle Hidden'
        '-ExecutionPolicy Bypass'
        "-File `"$scriptPath`""
        '-Mode Watch'
        "-DeviceId `"$($script:ResolvedDeviceId)`""
        "-AdbPath `"$($script:ResolvedAdbPath)`""
        "-PollSeconds $PollSeconds"
        '-Quiet'
    ) -join ' '

    $userId = "$env:USERDOMAIN\$env:USERNAME"
    $action = New-ScheduledTaskAction `
        -Execute $windowsPowerShell `
        -Argument $actionArguments
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
    $principal = New-ScheduledTaskPrincipal `
        -UserId $userId `
        -LogonType Interactive `
        -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -MultipleInstances IgnoreNew `
        -RestartCount 5 `
        -RestartInterval (New-TimeSpan -Minutes 1)
    $task = New-ScheduledTask `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description 'Keeps the configured SignVR Quest awake while it is connected to this PC.'

    $existingTask = Get-ScheduledTask `
        -TaskName $taskName `
        -ErrorAction SilentlyContinue
    if ($existingTask) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    Register-ScheduledTask `
        -TaskName $taskName `
        -InputObject $task `
        -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
}

function Show-Status {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $taskState = if ($task) { [string]$task.State } else { 'NotInstalled' }
    if (-not (Test-DeviceOnline)) {
        Write-OperatorMessage (
            "Task=$taskState; Quest $($script:ResolvedDeviceId) is offline."
        )
        return
    }

    $power = Get-QuestPowerState
    Write-OperatorMessage (
        "Task=$taskState; Quest=$($script:ResolvedDeviceId); " +
        "proximity=$($power.VirtualProximity); " +
        "power=$($power.PowerState); " +
        "plugged-in=$($power.StayOnWhilePluggedIn)."
    )
}

$savedState = Get-SavedState
$script:ResolvedAdbPath = Resolve-AdbPath -SavedState $savedState
$script:ResolvedDeviceId = Resolve-DeviceId -SavedState $savedState

switch ($Mode) {
    'Install' {
        if (-not (Test-DeviceOnline)) {
            throw "Quest $($script:ResolvedDeviceId) is not online over ADB."
        }
        $savedState = Save-InstallState -ExistingState $savedState
        Set-QuestAlwaysOn | Out-Null
        Register-AlwaysOnTask
        Show-Status
        Write-OperatorMessage "Restore with: & `"$PSCommandPath`" -Mode Uninstall"
    }
    'Uninstall' {
        if ($null -eq $savedState) {
            throw "No saved always-on state exists at $statePath."
        }
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($task) {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        }
        Restore-QuestPower -SavedState $savedState
        if ($task) {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
        Remove-Item -LiteralPath $statePath -Force
        Write-OperatorMessage 'The SignVR Quest always-on task was removed.'
    }
    'Apply' {
        Set-QuestAlwaysOn | Out-Null
        Show-Status
    }
    'Status' {
        Show-Status
    }
    'Watch' {
        $lastOnline = $false
        while ($true) {
            try {
                $online = Test-DeviceOnline
                if ($online) {
                    $changed = Set-QuestAlwaysOn -WatcherInvocation
                    if ($changed -or -not $lastOnline) {
                        $power = Get-QuestPowerState
                        Write-WatcherLog (
                            "Quest $($script:ResolvedDeviceId) online: " +
                            "proximity=$($power.VirtualProximity), " +
                            "power=$($power.PowerState), " +
                            "plugged-in=$($power.StayOnWhilePluggedIn)."
                        )
                    }
                } elseif ($lastOnline) {
                    Write-WatcherLog "Quest $($script:ResolvedDeviceId) disconnected."
                }
                $lastOnline = $online
            } catch {
                Write-WatcherLog "Watcher error: $($_.Exception.Message)"
            }
            Start-Sleep -Seconds $PollSeconds
        }
    }
}
