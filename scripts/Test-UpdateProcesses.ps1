[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
Set-LocalBuildEnvironment
$payloads = Join-Path $script:ProjectRoot ('.local\update-process-payloads-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$project = Join-Path $script:ProjectRoot 'tests\PrimaryWallpaperAgent.Tests\PrimaryWallpaperAgent.Tests.csproj'
$packages = Join-Path $script:ProjectRoot '.local\release-nuget'
foreach ($version in @('0.3.9','0.4.0')) {
    foreach ($flavor in @('full','slim')) {
        $output = Join-Path $payloads "$version\$flavor"
        $contained = if ($flavor -eq 'full') { 'true' } else { 'false' }
        $pivot = 'fixture_' + $version.Replace('.','_') + '_' + $flavor
        & dotnet publish $project -c Release -r win-x64 --self-contained $contained --source https://api.nuget.org/v3/index.json --packages $packages -o $output "-p:Version=$version" "-p:DistributionFlavor=$flavor" "-p:ArtifactsPivots=$pivot" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "Update fixture publish failed: $version/$flavor" }
        if (-not (Test-Path -LiteralPath (Join-Path $output 'PrimaryWallpaperAgent.Tests.exe'))) { throw 'Fixture EXE is missing.' }
    }
}
$tests = Join-Path $script:ProjectRoot 'artifacts\bin\PrimaryWallpaperAgent.Tests\debug\PrimaryWallpaperAgent.Tests.exe'
& $tests update-process $payloads
if ($LASTEXITCODE -ne 0) { throw 'Real-process updater tests failed.' }
