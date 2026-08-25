param(
    [string]$OutputRoot,
    [string]$PythonVersion = '3.12.8'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$hostRoot = Split-Path -Parent $PSScriptRoot
$backendRoot = Join-Path $hostRoot 'backend'
$frontendDist = Join-Path $hostRoot 'frontend\dist'
$distRoot = if ($OutputRoot) {
    [IO.Path]::GetFullPath($OutputRoot)
} else {
    Join-Path $hostRoot 'dist'
}
$packageRoot = Join-Path $distRoot 'SignVR-Host-Portable'
$zipPath = Join-Path $distRoot 'SignVR-Host-Portable.zip'
$buildPython = Join-Path $backendRoot '.venv\Scripts\python.exe'

if (-not (Test-Path -LiteralPath $frontendDist)) {
    throw 'frontend/dist is missing. Build the frontend before packaging.'
}
if (-not (Test-Path -LiteralPath $buildPython)) {
    throw 'backend/.venv is missing. Create the development virtual environment first.'
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
$resolvedDistRoot = [IO.Path]::GetFullPath($distRoot).TrimEnd('\')
$resolvedPackageRoot = [IO.Path]::GetFullPath($packageRoot)
if (-not $resolvedPackageRoot.StartsWith($resolvedDistRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace a package outside the output directory: $resolvedPackageRoot"
}
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$directories = @(
    $packageRoot,
    (Join-Path $packageRoot 'backend'),
    (Join-Path $packageRoot 'frontend'),
    (Join-Path $packageRoot 'config'),
    (Join-Path $packageRoot 'data'),
    (Join-Path $packageRoot 'scripts'),
    (Join-Path $packageRoot 'runtime\python\Lib\site-packages')
)
foreach ($directory in $directories) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Copy-Item -LiteralPath (Join-Path $backendRoot 'app') `
    -Destination (Join-Path $packageRoot 'backend\app') -Recurse
Copy-Item -LiteralPath $frontendDist `
    -Destination (Join-Path $packageRoot 'frontend\dist') -Recurse
Copy-Item -LiteralPath (Join-Path $backendRoot 'requirements-runtime-lock.txt') `
    -Destination (Join-Path $packageRoot 'backend\requirements-runtime-lock.txt')
Copy-Item -LiteralPath (Join-Path $hostRoot 'config\station.example.json') `
    -Destination (Join-Path $packageRoot 'config\station.example.json')
Copy-Item -LiteralPath (Join-Path $hostRoot 'config\pointing-station.json') `
    -Destination (Join-Path $packageRoot 'config\station.json')
Copy-Item -LiteralPath (Join-Path $hostRoot 'Start-SignVR-Host.bat') `
    -Destination (Join-Path $packageRoot 'Start-SignVR-Host.bat')
Copy-Item -LiteralPath (Join-Path $hostRoot 'PORTABLE-README.md') `
    -Destination (Join-Path $packageRoot 'README.md')
foreach ($scriptName in @('setup-firewall.ps1', 'start-local.ps1', 'start-portable.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) `
        -Destination (Join-Path $packageRoot "scripts\$scriptName")
}

$pythonArchive = Join-Path ([IO.Path]::GetTempPath()) (
    "signvr-python-$PythonVersion-embed-amd64.zip"
)
$pythonUrl = (
    "https://www.python.org/ftp/python/$PythonVersion/" +
    "python-$PythonVersion-embed-amd64.zip"
)
if (-not (Test-Path -LiteralPath $pythonArchive)) {
    Write-Host "Downloading portable Python $PythonVersion..."
    Invoke-WebRequest -Uri $pythonUrl -OutFile $pythonArchive
}

$runtimeRoot = Join-Path $packageRoot 'runtime\python'
Expand-Archive -LiteralPath $pythonArchive -DestinationPath $runtimeRoot -Force
$sitePackages = Join-Path $runtimeRoot 'Lib\site-packages'
Write-Host 'Installing locked runtime dependencies...'
& $buildPython -m pip install `
    --disable-pip-version-check `
    --no-compile `
    --no-warn-script-location `
    --target $sitePackages `
    -r (Join-Path $backendRoot 'requirements-runtime-lock.txt')
if ($LASTEXITCODE -ne 0) {
    throw "Runtime dependency installation failed with exit code $LASTEXITCODE."
}

$pthFile = Get-ChildItem -LiteralPath $runtimeRoot -Filter 'python*._pth' |
    Select-Object -First 1
if (-not $pthFile) {
    throw 'Portable Python path configuration file was not found.'
}
$pth = @(
    "python$($PythonVersion.Replace('.', '').Substring(0, 3)).zip",
    '.',
    'Lib\site-packages',
    '..\..\backend',
    'import site'
)
[IO.File]::WriteAllLines(
    $pthFile.FullName,
    $pth,
    [Text.UTF8Encoding]::new($false)
)

Get-ChildItem -LiteralPath $packageRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -eq '__pycache__' } |
    Remove-Item -Recurse -Force

Write-Host 'Validating portable runtime imports...'
& (Join-Path $runtimeRoot 'python.exe') -c `
    'import app.main, fastapi, uvicorn, websockets; print(1)'
if ($LASTEXITCODE -ne 0) {
    throw "Portable runtime validation failed with exit code $LASTEXITCODE."
}

# Import validation recreates bytecode caches; they are not needed at runtime
# and make Windows paths unnecessarily deep when the package is cached locally.
Get-ChildItem -LiteralPath $packageRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -eq '__pycache__' } |
    Remove-Item -Recurse -Force

# The launcher uses this content hash as the local cache directory name. A new
# package therefore gets a fresh cache without relying on its NAS drive letter.
$payloadRoots = @(
    (Join-Path $packageRoot 'backend\app'),
    (Join-Path $packageRoot 'frontend\dist'),
    (Join-Path $packageRoot 'runtime\python')
)
$payloadFiles = foreach ($payloadRoot in $payloadRoots) {
    Get-ChildItem -LiteralPath $payloadRoot -Recurse -File
}
$payloadFiles += Get-Item -LiteralPath (
    Join-Path $packageRoot 'scripts\start-local.ps1'
)
$fingerprintLines = foreach ($payloadFile in (
    $payloadFiles | Sort-Object -Property FullName
)) {
    $relativePath = $payloadFile.FullName.Substring(
        $packageRoot.Length + 1
    ).Replace('\', '/')
    $fileHash = Get-FileHash -LiteralPath $payloadFile.FullName `
        -Algorithm SHA256
    "$relativePath`t$($fileHash.Hash.ToLowerInvariant())"
}
$fingerprintBytes = [Text.Encoding]::UTF8.GetBytes(
    $fingerprintLines -join "`n"
)
$fingerprintAlgorithm = [Security.Cryptography.SHA256]::Create()
try {
    $fingerprint = [BitConverter]::ToString(
        $fingerprintAlgorithm.ComputeHash($fingerprintBytes)
    ).Replace('-', '').ToLowerInvariant()
} finally {
    $fingerprintAlgorithm.Dispose()
}
[IO.File]::WriteAllText(
    (Join-Path $packageRoot 'portable-runtime.version'),
    "$fingerprint`r`n",
    [Text.UTF8Encoding]::new($false)
)

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath `
    -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
[IO.File]::WriteAllText(
    "$zipPath.sha256",
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zipPath))`r`n",
    [Text.UTF8Encoding]::new($false)
)

$packageBytes = (
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Measure-Object -Property Length -Sum
).Sum
Write-Host "Portable folder: $packageRoot"
Write-Host "Portable ZIP:    $zipPath"
Write-Host ("Folder size:     {0:N1} MB" -f ($packageBytes / 1MB))
Write-Host "SHA256:          $($hash.Hash.ToLowerInvariant())"
