[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.4.3', [switch]$Stable)
. (Join-Path $PSScriptRoot 'Common.ps1')
Set-LocalBuildEnvironment
$project = Join-Path $script:ProjectRoot 'src\PrimaryWallpaperAgent.Tray\PrimaryWallpaperAgent.Tray.csproj'
$destination = Join-Path $script:ProjectRoot "artifacts\releases\v$Version\win-x64"
$buildRoot = Join-Path $script:ProjectRoot ".local\release-build-v$Version"
$packages = Join-Path $script:ProjectRoot '.local\release-nuget'
$privateFile = Join-Path $script:ProjectRoot '.local\release-signing\update-key.dpapi'
$publicFile = Join-Path $script:ProjectRoot 'assets\updates\update-public-key.pem'
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $buildRoot)) { throw 'Release destination/build already exists; inspect before rebuilding.' }
if (-not (Test-Path -LiteralPath $privateFile) -or -not (Test-Path -LiteralPath $publicFile)) { throw 'Update signing key is missing. Never replace the trusted key automatically.' }
$key = [Security.Cryptography.ECDsa]::Create()
$plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($privateFile), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
try { $read = 0; $key.ImportPkcs8PrivateKey($plain, [ref]$read) }
finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($plain) }
try {
    if ($key.ExportSubjectPublicKeyInfoPem().Trim() -ne [IO.File]::ReadAllText($publicFile).Trim()) { throw 'Private and embedded public keys do not match.' }
    [void][IO.Directory]::CreateDirectory($destination)
    $assets = @()
    foreach ($flavor in @('full','slim')) {
        $output = Join-Path $buildRoot $flavor
        $contained = if ($flavor -eq 'full') { 'true' } else { 'false' }
        & dotnet publish $project -c Release -r win-x64 --self-contained $contained --source https://api.nuget.org/v3/index.json --packages $packages -o $output "-p:Version=$Version" "-p:DistributionFlavor=$flavor" "-p:ArtifactsPivots=release_win-x64_$flavor" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $flavor ($LASTEXITCODE)" }
        $files = @(Get-ChildItem -LiteralPath $output -File)
        if ($files.Count -ne 1 -or $files[0].Name -ne 'PrimaryWallpaperAgent.Tray.exe' -or @(Get-ChildItem -LiteralPath $output -Directory).Count -ne 0) { throw "Single-file check failed: $flavor" }
        $name = if ($flavor -eq 'full') { 'PrimaryWallpaperAgent-Full-win-x64.exe' } else { 'PrimaryWallpaperAgent-Slim-win-x64.exe' }
        $path = Join-Path $destination $name
        Copy-Item -LiteralPath $files[0].FullName -Destination $path
        $assets += [ordered]@{Flavor=$flavor;FileName=$name;Size=(Get-Item -LiteralPath $path).Length;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $channel = if ($Stable) { 'stable' } else { 'preview' }
    $manifest = [ordered]@{SchemaVersion=1;Version=$Version;Channel=$channel;Assets=$assets} | ConvertTo-Json -Depth 5
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($manifest)
    [IO.File]::WriteAllBytes((Join-Path $destination 'update-manifest.json'), $bytes)
    $signature = $key.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256)
    [IO.File]::WriteAllText((Join-Path $destination 'update-manifest.sig'), [Convert]::ToBase64String($signature), [Text.UTF8Encoding]::new($false))
    $sums = @($assets | ForEach-Object { $_.Sha256 + '  ' + $_.FileName })
    foreach ($name in @('update-manifest.json','update-manifest.sig')) { $sums += (Get-FileHash -LiteralPath (Join-Path $destination $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name }
    [IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'), ($sums -join [Environment]::NewLine) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Host "Signed Full/Slim release ready for verification: $destination"
    $assets | ForEach-Object { Write-Host ($_.Flavor + ': ' + $_.Size + ' bytes') }
} finally { $key.Dispose() }
