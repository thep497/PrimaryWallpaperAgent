[CmdletBinding()]
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$pointerPath = Join-Path $PSScriptRoot 'snapshots\phase2-current-plan.txt'
$planPath = [IO.File]::ReadAllText($pointerPath).Trim()
$command = if ($CheckOnly) { 'trial-restore-check' } else { 'trial-restore' }
Push-Location -LiteralPath $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\PrimaryWallpaperAgent.exe') $command $planPath
    if ($LASTEXITCODE -ne 0) { throw ('Trial restore/check failed. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
