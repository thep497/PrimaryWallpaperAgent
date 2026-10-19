[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$')]
    [string]$Label
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Capture-DesktopSnapshot.ps1') -Label $Label
$planPath = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'snapshots\phase2-current-plan.txt')).Trim()
Push-Location -LiteralPath $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\PrimaryWallpaperAgent.exe') trial-status $planPath
    if ($LASTEXITCODE -ne 0) { throw ('Trial inspection failed. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
