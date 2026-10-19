param([switch]$Config)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$appPath = Join-Path $script:ProjectRoot 'artifacts\PrimaryWallpaperAgent-v0.2.0\PrimaryWallpaperAgent.Tray.exe'
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw 'Tray package is missing. Run Publish-Tray.ps1 first.'
}
if ($Config) {
    Start-Process -FilePath $appPath -ArgumentList '--config' -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Normal
} else {
    Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Hidden
}
