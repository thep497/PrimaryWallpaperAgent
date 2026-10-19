param([switch]$Config)
$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'artifacts\PrimaryWallpaperAgent-v0.2.0\PrimaryWallpaperAgent.Tray.exe'
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw 'Tray package is missing. Run Publish-Tray.ps1 first.'
}
if ($Config) {
    Start-Process -FilePath $appPath -ArgumentList '--config' -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Hidden
} else {
    Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Hidden
}
