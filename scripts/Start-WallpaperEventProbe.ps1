[CmdletBinding()]
param([ValidateRange(3, 300)][int]$Seconds = 120)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
Push-Location -LiteralPath $script:ProjectRoot
try {
    & (Get-ProbePath) event-probe $Seconds
    if ($LASTEXITCODE -ne 0) { throw ('Event probe failed. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
