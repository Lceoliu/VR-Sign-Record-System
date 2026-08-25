param(
    [int]$MaxTextureSize = 1024
)

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "optimize-glb-textures.mjs"
& "E:\nodejs\node.exe" $scriptPath $MaxTextureSize
exit $LASTEXITCODE
