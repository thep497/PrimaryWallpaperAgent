$ErrorActionPreference = 'Stop'
$script:ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'PrimaryWallpaperAgent.slnx') -PathType Leaf)) {
    throw 'Scripts must be kept in the scripts folder beside the solution root.'
}
function Get-ProbePath {
    $path = Join-Path $script:ProjectRoot 'artifacts\bin\PrimaryWallpaperAgent.Probe\debug\PrimaryWallpaperAgent.Probe.exe'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Run scripts\Build.ps1 before using diagnostics.' }
    return $path
}
function Set-LocalBuildEnvironment {
    $env:DOTNET_CLI_HOME = Join-Path $script:ProjectRoot '.local\dotnet-cli'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $env:DOTNET_NOLOGO = '1'
    $env:NUGET_PACKAGES = Join-Path $script:ProjectRoot '.local\release-nuget'
}