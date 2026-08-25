param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$packageCache = Join-Path $projectRoot 'Library\PackageCache'
$captureFiles = @(Get-ChildItem -LiteralPath $packageCache -Directory -Filter 'com.meta.xr.sdk.core@*' |
    ForEach-Object {
        Join-Path $_.FullName 'Editor\RuntimeOptimizer\PerformanceInsight\CaptureTool.cs'
    } |
    Where-Object { Test-Path -LiteralPath $_ })

if ($captureFiles.Count -ne 1) {
    throw "Expected one Meta XR CaptureTool.cs, found $($captureFiles.Count)."
}

$captureFile = $captureFiles[0]
$source = [System.IO.File]::ReadAllText($captureFile)
if ($source.Contains('GetAndroidSdkRootPath()')) {
    Write-Host 'Meta XR Unity 6000.5 compatibility patch is already applied.'
    return
}

$oldConstructor = 'new OVRADBTool(AndroidExternalToolsSettings.sdkRootPath);'
if (-not $source.Contains($oldConstructor)) {
    throw 'The Meta XR source layout changed; refusing to patch an unknown version.'
}

$source = $source.Replace(
    $oldConstructor,
    'new OVRADBTool(GetAndroidSdkRootPath());'
)

$anchor = '        static private Thread captureThread = null;'
$helper = @'

#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
        private static string GetAndroidSdkRootPath()
        {
            try
            {
                var settingsType = Type.GetType(
                    "UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
                var sdkProperty = settingsType?.GetProperty(
                    "sdkRootPath",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var configuredPath = sdkProperty?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(configuredPath))
                {
                    return configuredPath;
                }
            }
            catch
            {
                // Fall back to Unity's bundled Android SDK.
            }

            return Path.Combine(
                UnityEditor.EditorApplication.applicationContentsPath,
                "PlaybackEngines",
                "AndroidPlayer",
                "SDK");
        }
#endif

'@

if (-not $source.Contains($anchor)) {
    throw 'The Meta XR source anchor was not found; no file was changed.'
}

$source = $source.Replace($anchor, $helper + $anchor)
[System.IO.File]::WriteAllText(
    $captureFile,
    $source,
    [System.Text.UTF8Encoding]::new($false)
)
Write-Host "Patched $captureFile"
