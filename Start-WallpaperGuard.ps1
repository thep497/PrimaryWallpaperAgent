[CmdletBinding()]
param([ValidateRange(10, 300)][int]$Seconds = 120)
$ErrorActionPreference = 'Stop'
$planPath = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'snapshots\phase2-current-plan.txt')).Trim()
Push-Location -LiteralPath $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\PrimaryWallpaperAgent.exe') trial-guard $planPath $Seconds
    if ($LASTEXITCODE -ne 0) { throw ('Guard stopped with an error; inspect its evidence directory. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
