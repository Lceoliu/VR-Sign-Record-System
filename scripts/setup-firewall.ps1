param(
    [switch]$EnableUnityEditorSimulation,
    [string]$UnityEditorPath = 'D:\Softwares\Unityhub\Editor\6000.5.6f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as Administrator.'
}

$hostRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $hostRoot 'backend\.venv\Scripts\python.exe'
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
    -DisplayName 'SignVR Host HTTP 8000' `
    -Program $python `
    -Protocol TCP `
    -LocalPort 8000

Add-SignVrFirewallRule `
    -DisplayName 'SignVR Host UDP 5005' `
    -Program $python `
    -Protocol UDP `
    -LocalPort 5005

if ($EnableUnityEditorSimulation) {
    if (-not (Test-Path -LiteralPath $UnityEditorPath)) {
        throw "Unity Editor was not found: $UnityEditorPath"
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
