param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$')]
    [string]$StationId,

    [Parameter(Mandatory)]
    [string]$DataRoot,

    [ValidateRange(1, 65535)]
    [int]$HttpPort = 8011,

    [ValidateRange(1, 65535)]
    [int]$UdpPort = 5005,

    [ValidateRange(1, 65535)]
    [int]$QuestControlPort = 5006,

    [string]$DiscoveryBroadcast = '255.255.255.255'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$hostRoot = Split-Path -Parent $PSScriptRoot
$configRoot = Join-Path $hostRoot 'config'
$configPath = Join-Path $configRoot 'station.json'
$dataRootIsAbsolute = [IO.Path]::IsPathRooted($DataRoot)
$resolvedDataRoot = [IO.Path]::GetFullPath(
    $(if ($dataRootIsAbsolute) { $DataRoot } else { Join-Path $hostRoot $DataRoot })
)
$configuredDataRoot = if ($dataRootIsAbsolute) { $resolvedDataRoot } else { $DataRoot }

New-Item -ItemType Directory -Force -Path $configRoot | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedDataRoot | Out-Null

$config = [ordered]@{
    station_id = $StationId
    data_root = $configuredDataRoot
    http_host = '0.0.0.0'
    http_port = $HttpPort
    udp_host = '0.0.0.0'
    udp_port = $UdpPort
    quest_control_port = $QuestControlPort
    discovery_broadcast = $DiscoveryBroadcast
}

$json = $config | ConvertTo-Json
[IO.File]::WriteAllText(
    $configPath,
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
)

Write-Host "Configured SignVR workstation '$StationId'."
Write-Host "Recording data: $resolvedDataRoot"
Write-Host "Configuration: $configPath"
