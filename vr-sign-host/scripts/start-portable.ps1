param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$hostRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $hostRoot 'config\station.json'
$runtimePython = Join-Path $hostRoot 'runtime\python\python.exe'
$runtimeVersionPath = Join-Path $hostRoot 'portable-runtime.version'
$firewallScript = Join-Path $PSScriptRoot 'setup-firewall.ps1'
$stationId = 'development'
$httpHost = '0.0.0.0'
$httpPort = 8011
$udpHost = '0.0.0.0'
$udpPort = 5005
$questControlPort = 5006
$discoveryBroadcast = '255.255.255.255'
$sentenceCatalog = $null
$dataRoot = Join-Path $hostRoot 'data'

if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    if ($config.station_id) {
        $stationId = [string]$config.station_id
    }
    if ($config.http_host) {
        $httpHost = [string]$config.http_host
    }
    if ($null -ne $config.http_port) {
        $httpPort = [int]$config.http_port
    }
    if ($config.udp_host) {
        $udpHost = [string]$config.udp_host
    }
    if ($null -ne $config.udp_port) {
        $udpPort = [int]$config.udp_port
    }
    if ($null -ne $config.quest_control_port) {
        $questControlPort = [int]$config.quest_control_port
    }
    if ($config.discovery_broadcast) {
        $discoveryBroadcast = [string]$config.discovery_broadcast
    }
    if ($config.data_root) {
        $dataRootValue = [string]$config.data_root
        $dataRootPath = if ([IO.Path]::IsPathRooted($dataRootValue)) {
            $dataRootValue
        } else {
            Join-Path $hostRoot $dataRootValue
        }
        $dataRoot = [IO.Path]::GetFullPath($dataRootPath)
    }
    if ($config.sentence_catalog) {
        $catalogValue = [string]$config.sentence_catalog
        $catalogPath = if ([IO.Path]::IsPathRooted($catalogValue)) {
            $catalogValue
        } else {
            Join-Path $hostRoot $catalogValue
        }
        $sentenceCatalog = [IO.Path]::GetFullPath($catalogPath)
    }
}

if (-not (Test-Path -LiteralPath $runtimePython)) {
    throw (
        "Portable Python is missing: $runtimePython. " +
        'Use the generated SignVR-Host-Portable package, not the source folder.'
    )
}
if (-not (Test-Path -LiteralPath $runtimeVersionPath)) {
    throw (
        "Portable runtime version is missing: $runtimeVersionPath. " +
        'Replace this package with the latest generated portable package.'
    )
}

$runtimeVersion = (
    Get-Content -LiteralPath $runtimeVersionPath -Raw -Encoding UTF8
).Trim()
if ($runtimeVersion -notmatch '^[a-f0-9]{64}$') {
    throw "Portable runtime version is invalid: $runtimeVersionPath"
}

$webUrl = "http://127.0.0.1:$httpPort/"
try {
    $health = Invoke-RestMethod -Uri "${webUrl}api/health" -TimeoutSec 2
    if ($health.status -eq 'ok') {
        Write-Host "SignVR Host is already running at $webUrl"
        if (-not $NoBrowser) {
            Start-Process $webUrl
        }
        exit 0
    }
} catch {
    # No compatible Host is listening. Continue with startup checks.
}

$portOwner = Get-NetTCPConnection -LocalPort $httpPort -State Listen `
    -ErrorAction SilentlyContinue | Select-Object -First 1
if ($portOwner) {
    throw (
        "TCP port $httpPort is already used by process " +
        "$($portOwner.OwningProcess). Close that program and run this file again."
    )
}

function Test-SignVrFirewallRule {
    param(
        [Parameter(Mandatory)] [string]$DisplayName,
        [Parameter(Mandatory)] [string]$Protocol,
        [Parameter(Mandatory)] [int]$LocalPort
    )

    try {
        $rules = Get-NetFirewallRule -DisplayName $DisplayName `
            -ErrorAction Stop | Where-Object {
                $_.Enabled -eq 'True' -and
                $_.Direction -eq 'Inbound' -and
                $_.Action -eq 'Allow'
            }
        foreach ($rule in $rules) {
            $portFilter = $rule | Get-NetFirewallPortFilter
            if (
                $portFilter.Protocol -eq $Protocol -and
                [string]$portFilter.LocalPort -eq [string]$LocalPort
            ) {
                return $true
            }
        }
    } catch {
        return $false
    }
    return $false
}

$httpRuleName = "SignVR Host HTTP $httpPort"
$udpRuleName = "SignVR Host UDP $udpPort"
$firewallReady =
    (Test-SignVrFirewallRule -DisplayName $httpRuleName -Protocol TCP -LocalPort $httpPort) -and
    (Test-SignVrFirewallRule -DisplayName $udpRuleName -Protocol UDP -LocalPort $udpPort)

if (-not $firewallReady) {
    Write-Host 'Windows will ask for administrator permission once to allow Quest access.'
    $firewallBootstrapRoot = Join-Path $env:LOCALAPPDATA 'SignVR\Firewall'
    $localFirewallScript = Join-Path $firewallBootstrapRoot 'setup-firewall.ps1'
    $firewallErrorLog = Join-Path $firewallBootstrapRoot 'setup-firewall-error.log'
    New-Item -ItemType Directory -Force -Path $firewallBootstrapRoot |
        Out-Null
    Copy-Item -LiteralPath $firewallScript -Destination $localFirewallScript `
        -Force
    if (Test-Path -LiteralPath $firewallErrorLog) {
        Remove-Item -LiteralPath $firewallErrorLog -Force
    }

    $escapedScript = $localFirewallScript.Replace("'", "''")
    $escapedLog = $firewallErrorLog.Replace("'", "''")
    $elevatedCommand = (
        "& '$escapedScript' " +
        "-RequestedHttpPort $httpPort " +
        "-RequestedUdpPort $udpPort " +
        "-ErrorLogPath '$escapedLog'"
    )
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($elevatedCommand)
    )
    $arguments = (
        '-NoLogo -NoProfile -ExecutionPolicy Bypass ' +
        "-EncodedCommand $encodedCommand"
    )
    $elevatedPowerShell = Join-Path $PSHOME 'powershell.exe'
    try {
        $elevated = Start-Process $elevatedPowerShell `
            -Verb RunAs -Wait -PassThru `
            -ArgumentList $arguments
    } catch {
        throw 'Firewall setup was cancelled. Quest cannot connect until it is allowed.'
    }
    if ($elevated.ExitCode -ne 0) {
        $detail = if (Test-Path -LiteralPath $firewallErrorLog) {
            (Get-Content -LiteralPath $firewallErrorLog -Raw -Encoding UTF8).Trim()
        } else {
            "Elevated PowerShell exit code: $($elevated.ExitCode)"
        }
        throw "Firewall setup failed. $detail"
    }
}

New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
$localCacheParent = Join-Path $env:LOCALAPPDATA 'SignVR\HostRuntime'
$runtimeCacheKey = $runtimeVersion.Substring(0, 16)
$localHostRoot = Join-Path $localCacheParent $runtimeCacheKey
$localReadyFile = Join-Path $localHostRoot '.ready'
$localPython = Join-Path $localHostRoot 'runtime\python\python.exe'
$localStartScript = Join-Path $localHostRoot 'scripts\start-local.ps1'
$localFrontend = Join-Path $localHostRoot 'frontend\dist\index.html'
$localBackend = Join-Path $localHostRoot 'backend\app\main.py'
$cachedVersion = if (Test-Path -LiteralPath $localReadyFile) {
    (Get-Content -LiteralPath $localReadyFile -Raw -Encoding UTF8).Trim()
} else {
    $null
}
$localCacheReady =
    $cachedVersion -eq $runtimeVersion -and
    (Test-Path -LiteralPath $localReadyFile) -and
    (Test-Path -LiteralPath $localPython) -and
    (Test-Path -LiteralPath $localStartScript) -and
    (Test-Path -LiteralPath $localFrontend) -and
    (Test-Path -LiteralPath $localBackend)

if (-not $localCacheReady) {
    Write-Host 'Preparing the local SignVR runtime...'
    Write-Host 'The first launch from a NAS can take up to a minute.'
    foreach ($directory in @(
        $localHostRoot,
        (Join-Path $localHostRoot 'backend'),
        (Join-Path $localHostRoot 'frontend'),
        (Join-Path $localHostRoot 'runtime'),
        (Join-Path $localHostRoot 'scripts')
    )) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $hostRoot 'backend\app') `
        -Destination (Join-Path $localHostRoot 'backend\app') `
        -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $hostRoot 'frontend\dist') `
        -Destination (Join-Path $localHostRoot 'frontend\dist') `
        -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $hostRoot 'runtime\python') `
        -Destination (Join-Path $localHostRoot 'runtime\python') `
        -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $hostRoot 'scripts\start-local.ps1') `
        -Destination $localStartScript -Force
    [IO.File]::WriteAllText(
        $localReadyFile,
        "$runtimeVersion`r`n",
        [Text.UTF8Encoding]::new($false)
    )
    Write-Host 'Local runtime is ready.'
} else {
    Write-Host 'Using the cached local SignVR runtime.'
}

$env:SIGNVR_STATION_ID = $stationId
$env:SIGNVR_DATA_ROOT = [IO.Path]::GetFullPath($dataRoot)
$env:SIGNVR_HTTP_HOST = $httpHost
$env:SIGNVR_HTTP_PORT = [string]$httpPort
$env:SIGNVR_UDP_HOST = $udpHost
$env:SIGNVR_UDP_PORT = [string]$udpPort
$env:SIGNVR_QUEST_CONTROL_PORT = [string]$questControlPort
$env:SIGNVR_DISCOVERY_BROADCAST = $discoveryBroadcast
if ($sentenceCatalog) {
    $env:SIGNVR_SENTENCE_CATALOG = $sentenceCatalog
} else {
    $env:SIGNVR_SENTENCE_CATALOG = Join-Path `
        $localHostRoot 'backend\app\pointing_sentence_catalog.json'
}

$addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -ne '127.0.0.1' -and
        $_.IPAddress -notlike '169.254.*'
    } |
    Select-Object -ExpandProperty IPAddress -Unique

Write-Host ''
Write-Host 'SignVR Recording Host'
Write-Host "  Console: $webUrl"
Write-Host "  Data:    $dataRoot"
if ($addresses) {
    Write-Host "  LAN IP:  $($addresses -join ', ')"
}
Write-Host 'Keep this window open while recording. Press Ctrl+C to stop.'
Write-Host ''

if ($NoBrowser) {
    & $localStartScript -NoBrowser
} else {
    & $localStartScript
}
