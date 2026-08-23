$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$hostRoot = Split-Path -Parent $PSScriptRoot
$backendRoot = Join-Path $hostRoot 'backend'
$frontend = Join-Path $hostRoot 'frontend\dist\index.html'
$configPath = Join-Path $hostRoot 'config\station.json'
$pointingCatalog = Join-Path $backendRoot 'app\pointing_sentence_catalog.json'
$portablePython = Join-Path $hostRoot 'runtime\python\python.exe'
$venvPython = Join-Path $backendRoot '.venv\Scripts\python.exe'
$python = if (Test-Path -LiteralPath $portablePython) { $portablePython } else { $venvPython }

if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $env:SIGNVR_STATION_ID = [string]$config.station_id
    $env:SIGNVR_DATA_ROOT = [string]$config.data_root
    $env:SIGNVR_HTTP_HOST = [string]$config.http_host
    $env:SIGNVR_HTTP_PORT = [string]$config.http_port
    $env:SIGNVR_UDP_HOST = [string]$config.udp_host
    $env:SIGNVR_UDP_PORT = [string]$config.udp_port
    $env:SIGNVR_QUEST_CONTROL_PORT = [string]$config.quest_control_port
    $env:SIGNVR_DISCOVERY_BROADCAST = [string]$config.discovery_broadcast
    if ($config.sentence_catalog) {
        $catalogValue = [string]$config.sentence_catalog
        $catalogPath = if ([IO.Path]::IsPathRooted($catalogValue)) {
            $catalogValue
        } else {
            Join-Path $hostRoot $catalogValue
        }
        $env:SIGNVR_SENTENCE_CATALOG = [IO.Path]::GetFullPath(
            $catalogPath
        )
    }
} elseif (-not $env:SIGNVR_STATION_ID) {
    $env:SIGNVR_STATION_ID = 'development'
    Write-Warning 'No config/station.json was found; using the development data directory.'
}

if (-not $env:SIGNVR_SENTENCE_CATALOG) {
    $env:SIGNVR_SENTENCE_CATALOG = $pointingCatalog
}
if (-not (Test-Path -LiteralPath $env:SIGNVR_SENTENCE_CATALOG)) {
    throw "Sentence catalog is missing: $env:SIGNVR_SENTENCE_CATALOG"
}

$httpHost = if ($env:SIGNVR_HTTP_HOST) { $env:SIGNVR_HTTP_HOST } else { '0.0.0.0' }
$httpPort = if ($env:SIGNVR_HTTP_PORT) { [int]$env:SIGNVR_HTTP_PORT } else { 8000 }

if (-not (Test-Path -LiteralPath $python)) {
    throw 'Backend virtual environment is missing. Follow README.md first-install steps.'
}

if (-not (Test-Path -LiteralPath $frontend)) {
    throw 'Frontend production build is missing. Run pnpm build in the frontend folder.'
}

Set-Location -LiteralPath $backendRoot
& $python -m uvicorn app.main:app --host $httpHost --port $httpPort --no-access-log
