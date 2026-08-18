$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$hostRoot = Split-Path -Parent $PSScriptRoot
$backendRoot = Join-Path $hostRoot 'backend'
$python = Join-Path $backendRoot '.venv\Scripts\python.exe'
$frontend = Join-Path $hostRoot 'frontend\dist\index.html'

if (-not (Test-Path -LiteralPath $python)) {
    throw 'Backend virtual environment is missing. Follow README.md first-install steps.'
}

if (-not (Test-Path -LiteralPath $frontend)) {
    throw 'Frontend production build is missing. Run pnpm build in the frontend folder.'
}

Set-Location -LiteralPath $backendRoot
& $python -m uvicorn app.main:app --host 0.0.0.0 --port 8000 --no-access-log
