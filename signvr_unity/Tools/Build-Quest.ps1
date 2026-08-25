param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe',
    [string]$BuildPath = 'Builds\Android\SignVRRecording.apk'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$logPath = Join-Path $projectRoot 'Logs\AndroidBuild.log'

& (Join-Path $PSScriptRoot 'Apply-MetaXrUnity6000Patch.ps1')

& $UnityPath `
    -batchmode `
    -quit `
    -projectPath $projectRoot `
    -executeMethod SignVR.Editor.CommandLineBuild.BuildAndroid `
    -buildPath $BuildPath `
    -logFile $logPath
exit $LASTEXITCODE
