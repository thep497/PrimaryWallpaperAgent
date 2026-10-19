[CmdletBinding()]
param([switch]$SkipBuild)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Debug }
$testPath = Join-Path $script:ProjectRoot 'artifacts\bin\PrimaryWallpaperAgent.Tests\debug\PrimaryWallpaperAgent.Tests.exe'
if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) { throw 'Build the solution before running tests.' }
foreach ($suite in @('policy','config')) {
    & $testPath $suite
    if ($LASTEXITCODE -ne 0) { throw "Test suite failed: $suite ($LASTEXITCODE)" }
}
# Hidden startup was the native Config regression. This test intentionally reveals
# only our fixture form and closes it; no Windows settings are modified.
$testProcess = Start-Process -FilePath $testPath -ArgumentList 'window' -WindowStyle Hidden -PassThru
if (-not $testProcess.WaitForExit(15000)) { throw "Window test timed out; PID=$($testProcess.Id)" }
if ($testProcess.ExitCode -ne 0) { throw "Window test failed: $($testProcess.ExitCode)" }
Write-Host 'Policy, configuration and native window visibility tests passed.'