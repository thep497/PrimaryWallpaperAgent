[CmdletBinding()]
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$pointerPath = Join-Path $script:ProjectRoot 'snapshots\phase2-current-plan.txt'
$planPath = [IO.File]::ReadAllText($pointerPath).Trim()
$command = if ($CheckOnly) { 'trial-restore-check' } else { 'trial-restore' }
Push-Location -LiteralPath $script:ProjectRoot
try {
    & (Get-ProbePath) $command $planPath
    if ($LASTEXITCODE -ne 0) { throw ('Trial restore/check failed. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
