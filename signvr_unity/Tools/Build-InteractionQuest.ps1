param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe',
    [string]$BuildPath = 'Builds\Android\SignVRInteraction.apk'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$logPath = Join-Path $projectRoot 'Logs\InteractionAndroidBuild.log'

& (Join-Path $PSScriptRoot 'Apply-MetaXrUnity6000Patch.ps1')

$unityArguments = @(
    '-batchmode'
    '-quit'
    '-projectPath', $projectRoot
    '-executeMethod', 'SignVR.Editor.CommandLineBuild.BuildInteractionAndroid'
    '-buildPath', $BuildPath
    '-logFile', $logPath
)

$unityProcess = Start-Process `
    -FilePath $UnityPath `
    -ArgumentList $unityArguments `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
exit $unityProcess.ExitCode
