[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
. (Join-Path $PSScriptRoot 'Common.ps1')
Set-LocalBuildEnvironment
& dotnet build (Join-Path $script:ProjectRoot 'PrimaryWallpaperAgent.slnx') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }