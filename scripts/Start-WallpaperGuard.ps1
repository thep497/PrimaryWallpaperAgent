[CmdletBinding()]
param([ValidateRange(10, 300)][int]$Seconds = 120)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$planPath = [IO.File]::ReadAllText((Join-Path $script:ProjectRoot 'snapshots\phase2-current-plan.txt')).Trim()
Push-Location -LiteralPath $script:ProjectRoot
try {
    & (Get-ProbePath) trial-guard $planPath $Seconds
    if ($LASTEXITCODE -ne 0) { throw ('Guard stopped with an error; inspect its evidence directory. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
