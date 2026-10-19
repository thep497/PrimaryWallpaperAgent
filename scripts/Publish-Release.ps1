[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.0')
. (Join-Path $PSScriptRoot 'Common.ps1')
Set-LocalBuildEnvironment
$project = Join-Path $script:ProjectRoot 'src\PrimaryWallpaperAgent.Tray\PrimaryWallpaperAgent.Tray.csproj'
$destination = Join-Path $script:ProjectRoot "artifacts\releases\v$Version\win-x64"
$packages = Join-Path $script:ProjectRoot '.local\release-nuget'
if (Test-Path -LiteralPath $destination) { throw 'Release destination already exists; inspect it before rebuilding.' }
# Explicit Microsoft runtime pack restore; no global sources or PATH changes.
& dotnet publish $project -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json --packages $packages -o $destination "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
$original = Join-Path $destination 'PrimaryWallpaperAgent.Tray.exe'
$executable = Join-Path $destination 'PrimaryWallpaperAgent.exe'
Move-Item -LiteralPath $original -Destination $executable
$files = @(Get-ChildItem -LiteralPath $destination -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'PrimaryWallpaperAgent.exe' -or @(Get-ChildItem -LiteralPath $destination -Directory).Count -ne 0) {
    throw 'Single-file packaging check failed; do not distribute this directory.'
}
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'), "$hash  PrimaryWallpaperAgent.exe" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Release ready for verification: $destination"
