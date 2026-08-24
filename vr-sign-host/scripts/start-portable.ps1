param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$hostRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $hostRoot 'config\station.json'
$runtimePython = Join-Path $hostRoot 'runtime\python\python.exe'
$startScript = Join-Path $PSScriptRoot 'start-local.ps1'
$firewallScript = Join-Path $PSScriptRoot 'setup-firewall.ps1'
$httpPort = 8000
$udpPort = 5005
$dataRoot = Join-Path $hostRoot 'data'

if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $httpPort = [int]$config.http_port
    $udpPort = [int]$config.udp_port
    if ($config.data_root) {
        $dataRoot = [string]$config.data_root
    }
}

if (-not (Test-Path -LiteralPath $runtimePython)) {
    throw (
        "Portable Python is missing: $runtimePython. " +
        'Use the generated SignVR-Host-Portable package, not the source folder.'
    )
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
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $firewallScript + '"')
    )
    try {
        $elevated = Start-Process powershell.exe -Verb RunAs -Wait -PassThru `
            -ArgumentList $arguments
    } catch {
        throw 'Firewall setup was cancelled. Quest cannot connect until it is allowed.'
    }
    if ($elevated.ExitCode -ne 0) {
        throw "Firewall setup failed with exit code $($elevated.ExitCode)."
    }
}

New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
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
    & $startScript -NoBrowser
} else {
    & $startScript
}
