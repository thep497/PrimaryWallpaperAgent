[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.4.0')
. (Join-Path $PSScriptRoot 'Common.ps1')
$release = Join-Path $script:ProjectRoot "artifacts\releases\v$Version\win-x64"
$root = Join-Path $script:ProjectRoot ('.local\release-verification-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[void][IO.Directory]::CreateDirectory($root)
& (Join-Path $script:ProjectRoot 'artifacts\bin\PrimaryWallpaperAgent.Tests\debug\PrimaryWallpaperAgent.Tests.exe') verify-release $release
if ($LASTEXITCODE -ne 0) { throw 'Production signature verification failed.' }
$manifestBytes = [IO.File]::ReadAllBytes((Join-Path $release 'update-manifest.json'))
$manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
$key = [Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem([IO.File]::ReadAllText((Join-Path $script:ProjectRoot 'assets\updates\update-public-key.pem')))
    $signature = [Convert]::FromBase64String([IO.File]::ReadAllText((Join-Path $release 'update-manifest.sig')))
    if (-not $key.VerifyData($manifestBytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256)) { throw 'Release signature verification failed.' }
} finally { $key.Dispose() }
foreach ($asset in $manifest.Assets) {
    $copy = Join-Path $root ($asset.Flavor + ' standalone ภาษาไทย')
    [void][IO.Directory]::CreateDirectory($copy)
    $source = Join-Path $release $asset.FileName
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $asset.Sha256 -or (Get-Item -LiteralPath $source).Length -ne $asset.Size) { throw 'Signed asset hash/size mismatch.' }
    $exe = Join-Path $copy 'Renamed Wallpaper Agent.exe'
    Copy-Item -LiteralPath $source -Destination $exe
    $data = Join-Path $root ($asset.Flavor + ' data')
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$exe; $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['DOTNET_DISABLE_GUI_ERRORS']='1'
    $start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $root 'bundle-cache'
    if ($asset.Flavor -eq 'full') {
        $start.Environment['DOTNET_ROOT']=Join-Path $root 'no-shared-runtime'
        $start.Environment['DOTNET_ROOT_X64']=Join-Path $root 'no-shared-runtime'
        $start.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
    }
    foreach ($run in 1..2) {
        $start.ArgumentList.Clear();$start.ArgumentList.Add('--package-check');$start.ArgumentList.Add($data)
        $process=[Diagnostics.Process]::Start($start)
        if(-not $process.WaitForExit(30000)){throw 'Package check timed out.'}
        if($process.ExitCode -ne 0){throw "Package check failed: $($asset.Flavor)"}
        $report=Get-Content -LiteralPath (Join-Path $data 'runtime\package-check.json') -Raw | ConvertFrom-Json
        if(-not $report.Passed -or -not $report.SettingsUnchanged -or $report.Flavor -ne $asset.Flavor -or $report.NativeWallpaperCalls -ne 0 -or $report.RegistryWrites -ne 0){throw 'Package identity/read-only check failed.'}
    }
    $preflight=Join-Path $root ($asset.Flavor+'-preflight.json')
    $start.ArgumentList.Clear();$start.ArgumentList.Add('--update-preflight');$start.ArgumentList.Add($preflight)
    $process=[Diagnostics.Process]::Start($start)
    if(-not $process.WaitForExit(30000) -or $process.ExitCode -ne 0){throw 'Update preflight failed.'}
    $report=Get-Content -LiteralPath $preflight -Raw | ConvertFrom-Json
    if($report.Version -ne $Version -or $report.Flavor -ne $asset.Flavor){throw 'Preflight identity mismatch.'}
    Write-Host "SINGLE-FILE $($asset.Flavor) PASSED: renamed EXE, fresh/reopened data, preflight, languages/icons, no wallpaper/registry writes."
}
foreach($line in Get-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt')){
    if($line -match '^([a-f0-9]{64})  ([A-Za-z0-9._-]+)$'){
        if((Get-FileHash -LiteralPath (Join-Path $release $matches[2]) -Algorithm SHA256).Hash -ne $matches[1]){throw 'Checksum list mismatch.'}
    }else{throw 'Invalid checksum entry.'}
}
Write-Host "Signed release verification passed. Evidence: $root"
