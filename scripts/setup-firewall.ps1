param(
    [switch]$EnableUnityEditorSimulation,
    [string]$UnityEditorPath
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as Administrator.'
}

$hostRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $hostRoot 'config\station.json'
$portablePython = Join-Path $hostRoot 'runtime\python\python.exe'
$venvPython = Join-Path $hostRoot 'backend\.venv\Scripts\python.exe'
$python = if (Test-Path -LiteralPath $portablePython) { $portablePython } else { $venvPython }
$httpPort = 8000
$udpPort = 5005

if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $httpPort = [int]$config.http_port
    $udpPort = [int]$config.udp_port
}
if (-not (Test-Path -LiteralPath $python)) {
    throw "SignVR host Python was not found: $python"
}

function Add-SignVrFirewallRule {
    param(
        [Parameter(Mandatory)] [string]$DisplayName,
        [Parameter(Mandatory)] [string]$Program,
        [Parameter(Mandatory)] [string]$Protocol,
        [Parameter(Mandatory)] [int]$LocalPort
    )

    $existing = Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue
    if ($existing) {
        $existing | Remove-NetFirewallRule
    }

    New-NetFirewallRule `
        -DisplayName $DisplayName `
        -Direction Inbound `
        -Action Allow `
        -Program $Program `
        -Protocol $Protocol `
        -LocalPort $LocalPort `
        -RemoteAddress LocalSubnet `
        -Profile Private,Public | Out-Null
}

Add-SignVrFirewallRule `
    -DisplayName "SignVR Host HTTP $httpPort" `
    -Program $python `
    -Protocol TCP `
    -LocalPort $httpPort

Add-SignVrFirewallRule `
    -DisplayName "SignVR Host UDP $udpPort" `
    -Program $python `
    -Protocol UDP `
    -LocalPort $udpPort

if ($EnableUnityEditorSimulation) {
    if (-not $UnityEditorPath) {
        $UnityEditorPath = Get-Process Unity -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1 -ExpandProperty Path
    }
    if (-not $UnityEditorPath -or -not (Test-Path -LiteralPath $UnityEditorPath)) {
        throw 'Unity Editor was not found. Pass -UnityEditorPath explicitly.'
    }

    $blockingRules = Get-NetFirewallRule -PolicyStore ActiveStore |
        Where-Object { $_.Direction -eq 'Inbound' -and $_.Action -eq 'Block' } |
        Where-Object {
            $filter = $_ | Get-NetFirewallApplicationFilter
            $filter.Program -eq $UnityEditorPath
        }

    $blockingRules | Disable-NetFirewallRule

    Add-SignVrFirewallRule `
        -DisplayName 'SignVR Unity Editor UDP 5006' `
        -Program $UnityEditorPath `
        -Protocol UDP `
        -LocalPort 5006
}

Write-Host 'SignVR local-network firewall rules are configured.'
