$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'artifacts\PrimaryWallpaperAgent-v0.1.0\PrimaryWallpaperAgent.Tray.exe'
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw 'Tray package is missing. Run Publish-Tray.ps1 first.'
}
Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath) -WindowStyle Hidden
