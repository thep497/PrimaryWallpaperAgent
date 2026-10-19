[CmdletBinding()]
param([ValidateRange(3, 300)][int]$Seconds = 120)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows\PrimaryWallpaperAgent.exe') event-probe $Seconds
    if ($LASTEXITCODE -ne 0) { throw ('Event probe failed. Exit code: ' + $LASTEXITCODE) }
}
finally { Pop-Location }
