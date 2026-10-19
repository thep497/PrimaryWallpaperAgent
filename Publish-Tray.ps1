$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
$outputPath = Join-Path $projectPath 'artifacts\PrimaryWallpaperAgent-v0.1.0'
$planPath = (Get-Content -LiteralPath (Join-Path $projectPath 'snapshots\phase2-current-plan.txt') -Raw).Trim()
if (-not $planPath.StartsWith((Join-Path $projectPath 'snapshots\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Trial plan must be inside this project snapshots folder.'
}
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
foreach ($item in @(@{ Path = $plan.PhotoPath; Hash = $plan.PhotoSha256 }, @{ Path = $plan.BlackPath; Hash = $plan.BlackSha256 })) {
    if (-not $item.Path.StartsWith((Join-Path $projectPath '.local-assets\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Approved image copy must be inside this project .local-assets folder.'
    }
    if ((Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -ne $item.Hash) { throw 'Approved image checksum mismatch.' }
}
# Only local sources, the installed SDK and the approved local image copies are used.
& dotnet publish (Join-Path $projectPath 'Tray\PrimaryWallpaperAgent.Tray.csproj') -c Release --no-self-contained -o $outputPath
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
$assetPath = Join-Path $outputPath 'assets'
New-Item -ItemType Directory -Path $assetPath -Force | Out-Null
Copy-Item -LiteralPath $plan.PhotoPath -Destination (Join-Path $assetPath 'primary.jpg')
Copy-Item -LiteralPath $plan.BlackPath -Destination (Join-Path $assetPath 'black.bmp')
$settings = [ordered]@{
    SchemaVersion = 1
    PhotoFile = 'assets/primary.jpg'
    PhotoSha256 = $plan.PhotoSha256
    BlackFile = 'assets/black.bmp'
    BlackSha256 = $plan.BlackSha256
    AcceptedPaths = @($plan.SourceOriginalPath, $plan.PhotoPath, $plan.BlackPath)
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'settings.json') -Encoding utf8
foreach ($item in @(@{ Name = 'primary.jpg'; Hash = $plan.PhotoSha256 }, @{ Name = 'black.bmp'; Hash = $plan.BlackSha256 })) {
    if ((Get-FileHash -LiteralPath (Join-Path $assetPath $item.Name) -Algorithm SHA256).Hash -ne $item.Hash) { throw 'Packaged image checksum mismatch.' }
}
Write-Host "Manual tray app: $(Join-Path $outputPath 'PrimaryWallpaperAgent.Tray.exe')"
Write-Host 'No autostart or registry settings were added. Stop the tray app before republishing.'
