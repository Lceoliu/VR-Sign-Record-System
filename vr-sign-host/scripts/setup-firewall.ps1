param(
    [switch]$EnableUnityEditorSimulation,
    [string]$UnityEditorPath,

    [ValidateRange(0, 65535)]
    [int]$RequestedHttpPort = 0,

    [ValidateRange(0, 65535)]
    [int]$RequestedUdpPort = 0,

    [string]$ErrorLogPath
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

trap {
    $message = $_ | Out-String
    if ($ErrorLogPath) {
        try {
            [IO.File]::WriteAllText(
                $ErrorLogPath,
                $message,
                [Text.UTF8Encoding]::new($false)
            )
        } catch {
            # Preserve the original firewall error if logging also fails.
        }
    }
    [Console]::Error.WriteLine($message)
    exit 1
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as Administrator.'
}

$hostRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $hostRoot 'config\station.json'
$httpPort = if ($RequestedHttpPort -gt 0) { $RequestedHttpPort } else { 8011 }
$udpPort = if ($RequestedUdpPort -gt 0) { $RequestedUdpPort } else { 5011 }

if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($RequestedHttpPort -eq 0) {
        $httpPort = [int]$config.http_port
    }
    if ($RequestedUdpPort -eq 0) {
        $udpPort = [int]$config.udp_port
    }
}
function Add-SignVrFirewallRule {
    param(
        [Parameter(Mandatory)] [string]$DisplayName,
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
        -Protocol $Protocol `
        -LocalPort $LocalPort `
        -RemoteAddress LocalSubnet `
        -Profile Private,Public | Out-Null
}

Add-SignVrFirewallRule `
    -DisplayName "SignVR Host HTTP $httpPort" `
    -Protocol TCP `
    -LocalPort $httpPort

Add-SignVrFirewallRule `
    -DisplayName "SignVR Host UDP $udpPort" `
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
        -DisplayName 'SignVR Pointing Quest Control UDP 5012' `
        -Protocol UDP `
        -LocalPort 5012
}

Write-Host 'SignVR local-network firewall rules are configured.'
